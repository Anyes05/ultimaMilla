using System.Diagnostics;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;

namespace Plataforma.Application;

public sealed class SincronizarEntregas
{
    private readonly IRutaRepositorio _rutas;
    private readonly IEnvioRepositorio _envios;
    private readonly IBultoRepositorio _bultos;
    private readonly IPruebaDeEntregaRepositorio _pruebas;
    private readonly IEventoSincronizacionRepositorio _eventosProcesados;
    private readonly IOutboxRepositorio _mensajesOutbox;
    private readonly RegistrarOutboxEstado _outbox;
    private readonly PublicarOutboxPendientes _publicar;
    private readonly ConfiguracionOperador _operadores;
    private readonly IReglaOperativaRepositorio _reglas;
    private readonly IBanderasOperador _banderas;
    private readonly ICacheLectura _cache;
    private readonly IIntentoEntregaRepositorio _intentos;

    public SincronizarEntregas(
        IRutaRepositorio rutas,
        IEnvioRepositorio envios,
        IBultoRepositorio bultos,
        IPruebaDeEntregaRepositorio pruebas,
        IEventoSincronizacionRepositorio eventosProcesados,
        IOutboxRepositorio mensajesOutbox,
        RegistrarOutboxEstado outbox,
        PublicarOutboxPendientes publicar,
        ConfiguracionOperador operadores,
        IReglaOperativaRepositorio reglas,
        IBanderasOperador banderas,
        ICacheLectura cache,
        IIntentoEntregaRepositorio intentos)
    {
        _rutas = rutas;
        _envios = envios;
        _bultos = bultos;
        _pruebas = pruebas;
        _eventosProcesados = eventosProcesados;
        _mensajesOutbox = mensajesOutbox;
        _outbox = outbox;
        _publicar = publicar;
        _operadores = operadores;
        _reglas = reglas;
        _banderas = banderas;
        _cache = cache;
        _intentos = intentos;
    }

    public async Task<ResultadoSincronizacion> Ejecutar(
        Guid operadorId,
        Guid repartidorId,
        DateOnly fecha,
        IReadOnlyList<ComprobanteEntrega> eventos,
        string usuario,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(usuario))
            throw new ArgumentException("El usuario del sincronizado es requerido.", nameof(usuario));

        if (eventos is null || eventos.Count > 100)
            throw new ArgumentException("El lote admite hasta 100 eventos.", nameof(eventos));

        if (eventos.Any(evento => evento is null))
            throw new ArgumentException("El lote contiene comprobantes nulos.", nameof(eventos));

        if (eventos.Select(evento => evento.EventoId).Distinct().Count() != eventos.Count)
            throw new ArgumentException("Hay eventoId duplicados en el lote.", nameof(eventos));

        if (eventos.Select(evento => evento.EnvioId).Distinct().Count() != eventos.Count)
            throw new ArgumentException("Hay envíos duplicados en el lote.", nameof(eventos));

        var operador = await _operadores.ObtenerEntidadAsync(operadorId, cancellationToken);
        var rutas = await _rutas.ObtenerDeHoyAsync(operadorId, repartidorId, fecha, cancellationToken);
        var rutasId = rutas.Select(ruta => ruta.Id).ToArray();
        var aplicados = new List<EventoAplicado>();
        var rechazados = new List<EventoRechazado>();
        var enviosAplicados = new List<Envio>();
        var eventIdsOutbox = new List<Guid>();

        foreach (var evento in eventos)
        {
            if (evento.EventoId == Guid.Empty || evento.EnvioId == Guid.Empty)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "El comprobante no tiene identificadores válidos."));
                continue;
            }

            // Idempotencia durable: mismo eventoId entre requests → mismo resultado aplicado.
            var yaProcesado = await _eventosProcesados.ObtenerAsync(evento.EventoId, cancellationToken);
            if (yaProcesado is not null)
            {
                if (yaProcesado.OperadorId != operadorId || yaProcesado.EnvioId != evento.EnvioId ||
                    yaProcesado.Estado != evento.Estado)
                {
                    rechazados.Add(new EventoRechazado(evento.EnvioId, "El eventoId ya fue utilizado por otro comprobante."));
                    continue;
                }
                aplicados.Add(new EventoAplicado(
                    yaProcesado.EnvioId,
                    yaProcesado.Estado,
                    yaProcesado.IntentosRealizados));
                continue;
            }

            if (evento.NombreReceptor is null || evento.Observaciones is null ||
                evento.NombreReceptor.Length > 100 || evento.Observaciones.Length > 500)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "El comprobante supera los largos permitidos."));
                continue;
            }

            if (evento.Bultos is not { Count: > 0 } &&
                evento.Estado == EstadoEnvio.NoEntregado && string.IsNullOrWhiteSpace(evento.Observaciones))
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "El intento fallido requiere un motivo en observaciones."));
                continue;
            }

            var errorEvidencia = ValidarEvidencia(evento);
            if (errorEvidencia is not null)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, errorEvidencia));
                continue;
            }

            if ((evento.Estado is EstadoEnvio.Entregado or EstadoEnvio.EntregadoParcialmente) &&
                (evento.Estado == EstadoEnvio.Entregado || evento.Bultos?.Any(bulto => bulto.Estado == EstadoBulto.Entregado) == true) &&
                string.IsNullOrWhiteSpace(evento.NombreReceptor))
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "La entrega requiere el nombre del receptor."));
                continue;
            }

            var envio = await _envios.ObtenerEnRutaAsync(evento.EnvioId, operadorId, rutasId, cancellationToken);
            if (envio is null)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "El envío no está en tu ruta de hoy."));
                continue;
            }

            var rutaAntes = envio.RutaId;

            if (evento.CodigoEscaneado is not null &&
                (evento.Estado != EstadoEnvio.EnTransito ||
                 !string.Equals(evento.CodigoEscaneado.Trim(), envio.NumeroSeguimiento, StringComparison.OrdinalIgnoreCase)))
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, "El código de carga no corresponde al envío o al estado EnTransito.", envio.Estado));
                continue;
            }

            var reglaSync = await _reglas.ObtenerVigenteAsync(operadorId, DateTime.UtcNow, cancellationToken);
            var errorPod = ValidarPodSegunRegla(evento, reglaSync);
            if (errorPod is not null)
            {
                rechazados.Add(new EventoRechazado(evento.EnvioId, errorPod, envio.Estado));
                continue;
            }

            var usaBultos = evento.Bultos is { Count: > 0 };

            if (!usaBultos)
            {
                // Transición 10 (Pedir otra visita / Reprogramado): solo si quedan intentos.
                if (evento.Estado == EstadoEnvio.Reprogramado && envio.IntentosRealizados >= operador.MaximoIntentos)
                {
                    rechazados.Add(new EventoRechazado(
                        evento.EnvioId,
                        $"El operador admite como máximo {operador.MaximoIntentos} intentos.",
                        envio.Estado));
                    continue;
                }

                if (!TransicionesEnvio.EsValida(envio.Estado, evento.Estado))
                {
                    rechazados.Add(new EventoRechazado(
                        evento.EnvioId,
                        $"Transición inválida: {envio.Estado} -> {evento.Estado}",
                        envio.Estado));
                    continue;
                }

                if (evento.Estado == EstadoEnvio.NoEntregado)
                {
                    envio.RegistrarIntentoFallido(operador.MaximoIntentos);
                    await _intentos.AgregarAsync(IntentoEntrega.Registrar(
                        Guid.NewGuid(), operadorId, envio.Id, envio.IntentosRealizados,
                        RelojOperacion.AUtc(evento.FechaDispositivo).UtcDateTime,
                        evento.Observaciones), cancellationToken);
                }

                var anteriorV1 = envio.Estado;
                envio.CambiarEstado(evento.Estado, evento.NombreReceptor, evento.Observaciones);
                var eventIdV1 = await _outbox.EjecutarAsync(
                    operadorId,
                    envio.Id,
                    anteriorV1,
                    envio.Estado,
                    cancellationToken,
                    motivo: $"Sync repartidor: {evento.Observaciones}",
                    responsable: usuario);
                if (eventIdV1 is Guid idV1)
                    eventIdsOutbox.Add(idV1);

                // Transición 11: al agotar intentos tras NoEntregado, paso automático a EnDevolucion.
                // Convive con "Pedir otra visita" (10): esa sigue siendo Reprogramado explícito, solo con intentos.
                // Con la política "Esperar decisión", queda No entregado y el operador decide en Monitoreo.
                if (envio.Estado == EstadoEnvio.NoEntregado
                    && envio.IntentosRealizados >= operador.MaximoIntentos
                    && (reglaSync?.DevuelveAlAgotarIntentos ?? true)
                    && TransicionesEnvio.EsValida(envio.Estado, EstadoEnvio.EnDevolucion))
                {
                    var antesDev = envio.Estado;
                    envio.CambiarEstado(EstadoEnvio.EnDevolucion, usuario, "Máximo de intentos: paso automático a devolución");
                    var eventIdDev = await _outbox.EjecutarAsync(
                        operadorId, envio.Id, antesDev, envio.Estado, cancellationToken,
                        motivo: "Transición 11 automática: máximo de intentos",
                        responsable: usuario);
                    if (eventIdDev is Guid idDev)
                        eventIdsOutbox.Add(idDev);
                }
            }
            else
            {
                var anteriorV2 = envio.Estado;
                var errorBultos = await AplicarBultosAsync(
                    operadorId, operador, envio, evento, usuario, eventIdsOutbox, cancellationToken);
                if (errorBultos is not null)
                {
                    rechazados.Add(errorBultos);
                    continue;
                }

                var eventIdV2 = await _outbox.EjecutarAsync(
                    operadorId,
                    envio.Id,
                    anteriorV2,
                    envio.Estado,
                    cancellationToken,
                    motivo: $"Sync repartidor (bultos): {evento.Observaciones}",
                    responsable: usuario);
                if (eventIdV2 is Guid idV2)
                    eventIdsOutbox.Add(idV2);
            }

            // RF 34: al quedar parcial/reprogramado se libera RutaId; quitar la parada de la hoja vieja.
            if (rutaAntes is not null && envio.RutaId is null)
            {
                var parada = await _rutas.ObtenerParadaPorEnvioAsync(operadorId, envio.Id, cancellationToken);
                if (parada is not null)
                    await _rutas.QuitarParadaAsync(parada, cancellationToken);
            }

            var bultosEntregados = evento.Bultos?.Where(bulto => bulto.Estado == EstadoBulto.Entregado).ToArray() ?? [];
            if (bultosEntregados.Length > 0)
            {
                foreach (var bulto in bultosEntregados)
                {
                    await _pruebas.AgregarAsync(
                        PruebaDeEntrega.DesdeSincronizacion(
                            Guid.NewGuid(), operadorId, envio.Id, evento.NombreReceptor, evento.Observaciones,
                            usuario, RelojOperacion.AUtc(evento.FechaDispositivo),
                            bulto.Foto ?? evento.Foto, bulto.Firma ?? evento.Firma,
                            evento.Latitud, evento.Longitud, evento.CodigoEscaneado, bulto.BultoId, evento.DocumentoReceptor),
                        cancellationToken);
                }
            }
            else
            {
                await _pruebas.AgregarAsync(
                    PruebaDeEntrega.DesdeSincronizacion(
                        Guid.NewGuid(), operadorId, envio.Id, evento.NombreReceptor, evento.Observaciones,
                        usuario, RelojOperacion.AUtc(evento.FechaDispositivo), evento.Foto, evento.Firma,
                        evento.Latitud, evento.Longitud, evento.CodigoEscaneado, null, evento.DocumentoReceptor),
                    cancellationToken);
            }

            await _eventosProcesados.AgregarAsync(
                EventoSincronizacionProcesado.Registrar(
                    evento.EventoId,
                    operadorId,
                    envio.Id,
                    envio.Estado,
                    envio.IntentosRealizados),
                cancellationToken);

            aplicados.Add(new EventoAplicado(envio.Id, envio.Estado, envio.IntentosRealizados));
            enviosAplicados.Add(envio);
        }

        if (enviosAplicados.Count > 0 || eventIdsOutbox.Count > 0)
        {
            await _envios.GuardarAsync(cancellationToken);
            foreach (var eventId in eventIdsOutbox)
                await _publicar.EjecutarUnoAsync(eventId, cancellationToken);
            await _cache.QuitarAsync(ObtenerRutasDeHoy.Clave(operadorId, repartidorId, fecha), cancellationToken);
            foreach (var envio in enviosAplicados)
                await CacheSeguimiento.PublicarAsync(_cache, envio, cancellationToken);
        }
        else if (aplicados.Count > 0)
        {
            // Solo reintentos idempotentes: no hubo escrituras nuevas.
        }

        return new ResultadoSincronizacion(aplicados, rechazados);
    }

    private async Task<EventoRechazado?> AplicarBultosAsync(
        Guid operadorId,
        Operador operador,
        Envio envio,
        ComprobanteEntrega evento,
        string usuario,
        List<Guid> eventIdsOutbox,
        CancellationToken cancellationToken)
    {
        var resultados = evento.Bultos!;
        if (resultados.Select(b => b.BultoId).Distinct().Count() != resultados.Count)
            return new EventoRechazado(envio.Id, "Hay bultos duplicados en el mismo evento.", envio.Estado);

        if (resultados.Any(b => b is null || b.BultoId == Guid.Empty))
            return new EventoRechazado(envio.Id, "Hay resultados de bulto incompletos.", envio.Estado);

        var bultos = (await _bultos.ListarPorEnvioAsync(operadorId, envio.Id, cancellationToken)).ToList();
        if (bultos.Count == 0)
            return new EventoRechazado(envio.Id, "El envío no tiene bultos registrados.", envio.Estado);

        var porId = bultos.ToDictionary(b => b.Id);
        foreach (var resultado in resultados)
        {
            if (!porId.ContainsKey(resultado.BultoId))
                return new EventoRechazado(envio.Id, $"El bulto {resultado.BultoId} no pertenece al envío.", envio.Estado);
        }

        var regla = await _reglas.ObtenerVigenteAsync(operadorId, DateTime.UtcNow, cancellationToken);
        var admiteParcial = regla?.AdmiteEntregaParcial ?? false;
        var motivosValidos = regla?.MotivosNoEntrega ?? ReglaOperativa.MotivosNoEntregaPredeterminados;
        if (resultados.Any(resultado =>
                resultado.Estado is EstadoBulto.NoEntregado or EstadoBulto.PendienteNuevaVisita &&
                !motivosValidos.Contains(resultado.MotivoNoEntrega ?? string.Empty, StringComparer.OrdinalIgnoreCase) &&
                !(usuario == CierreRutasSinIntento.UsuarioSistema &&
                    resultado.MotivoNoEntrega == CierreRutasSinIntento.MotivoNoEntregado)))
        {
            return new EventoRechazado(
            envio.Id,
            "El motivo de uno o más bultos no pertenece al catálogo vigente del operador.",
            envio.Estado);
        }

        // Proyectar estados finales del lote (los no mencionados quedan igual).
        var proyectados = bultos.ToDictionary(b => b.Id, b => b.Estado);
        foreach (var resultado in resultados)
            proyectados[resultado.BultoId] = resultado.Estado;

        var entregados = proyectados.Values.Count(e => e == EstadoBulto.Entregado);
        var noEntregados = proyectados.Values.Count(e =>
            e is EstadoBulto.NoEntregado or EstadoBulto.PendienteNuevaVisita);
        if (entregados > 0 && noEntregados > 0 &&
            (!admiteParcial || !await _banderas.EstaActivaAsync(
                operadorId, CatalogoBanderas.EntregaParcial, cancellationToken)))
        {
            return new EventoRechazado(
                envio.Id,
                "La entrega parcial no está habilitada según la bandera o la regla vigente del operador.",
                envio.Estado);
        }

        try
        {
            foreach (var resultado in resultados)
            {
                var bulto = porId[resultado.BultoId];
                if (bulto.Estado is EstadoBulto.Entregado or EstadoBulto.Devuelto or EstadoBulto.Extraviado
                    && bulto.Estado != resultado.Estado)
                {
                    return new EventoRechazado(
                        envio.Id,
                        $"El bulto {bulto.Codigo} ya está en estado terminal {bulto.Estado}.",
                        envio.Estado);
                }

                var anteriorBulto = bulto.Estado;

                // Si todavía está Pendiente y el envío ya salió, lo pasamos a EnRuta antes del resultado.
                if (bulto.Estado is EstadoBulto.Pendiente or EstadoBulto.PendienteNuevaVisita &&
                    resultado.Estado is EstadoBulto.Entregado or EstadoBulto.NoEntregado
                        or EstadoBulto.PendienteNuevaVisita or EstadoBulto.Extraviado)
                {
                    bulto.CambiarEstado(EstadoBulto.EnRuta);
                    await RegistrarOutboxBultoAsync(operadorId, envio.Id, bulto.Id, anteriorBulto, bulto.Estado, eventIdsOutbox, cancellationToken);
                    anteriorBulto = bulto.Estado;
                }

                bulto.CambiarEstado(resultado.Estado, resultado.MotivoNoEntrega ?? evento.Observaciones);
                if (anteriorBulto != bulto.Estado)
                    await RegistrarOutboxBultoAsync(operadorId, envio.Id, bulto.Id, anteriorBulto, bulto.Estado, eventIdsOutbox, cancellationToken);
            }

            // Política post-parcial sobre bultos NoEntregado del lote.
            if (regla is not null && admiteParcial && entregados > 0)
            {
                foreach (var bulto in bultos.Where(b => b.Estado == EstadoBulto.NoEntregado))
                {
                    var anteriorPol = bulto.Estado;
                    if (regla.PoliticaBultosPendientes == PoliticaBultosPendientes.NuevaVisitaSinRecargo
                        || regla.PoliticaBultosPendientes == PoliticaBultosPendientes.NuevaVisitaConRecargo)
                    {
                        if (TransicionesBulto.EsValida(bulto.Estado, EstadoBulto.PendienteNuevaVisita))
                            bulto.CambiarEstado(EstadoBulto.PendienteNuevaVisita, bulto.MotivoNoEntrega);
                    }
                    else if (regla.PoliticaBultosPendientes == PoliticaBultosPendientes.DevolucionAlComercio
                             && TransicionesBulto.EsValida(bulto.Estado, EstadoBulto.Devuelto))
                    {
                        bulto.CambiarEstado(EstadoBulto.Devuelto, bulto.MotivoNoEntrega);
                    }

                    if (anteriorPol != bulto.Estado)
                        await RegistrarOutboxBultoAsync(operadorId, envio.Id, bulto.Id, anteriorPol, bulto.Estado, eventIdsOutbox, cancellationToken);
                }
            }

            var derivado = DerivacionEstadoEnvio.DesdeBultos(bultos.Select(b => b.Estado).ToList());
            if (derivado is EstadoEnvio.NoEntregado or EstadoEnvio.EntregadoParcialmente or EstadoEnvio.Reprogramado)
            {
                // Reprogramado con intentos agotados se rechaza (transición 10); NoEntregado sigue y dispara 11.
                if (envio.IntentosRealizados >= operador.MaximoIntentos && derivado == EstadoEnvio.Reprogramado)
                {
                    return new EventoRechazado(
                        envio.Id,
                        $"Se alcanzó el máximo de {operador.MaximoIntentos} intentos.",
                        envio.Estado);
                }

                if (derivado is EstadoEnvio.NoEntregado or EstadoEnvio.EntregadoParcialmente)
                {
                    envio.RegistrarIntentoFallido(operador.MaximoIntentos);
                    await _intentos.AgregarAsync(IntentoEntrega.Registrar(
                        Guid.NewGuid(), operadorId, envio.Id, envio.IntentosRealizados,
                        RelojOperacion.AUtc(evento.FechaDispositivo).UtcDateTime,
                        evento.Observaciones), cancellationToken);
                }
            }

            envio.AplicarEstadoDerivadoDeBultos(bultos.Select(b => b.Estado).ToList());

            if (envio.Estado == EstadoEnvio.NoEntregado
                && envio.IntentosRealizados >= operador.MaximoIntentos
                && (regla?.DevuelveAlAgotarIntentos ?? true)
                && TransicionesEnvio.EsValida(envio.Estado, EstadoEnvio.EnDevolucion))
            {
                envio.CambiarEstado(EstadoEnvio.EnDevolucion, "sistema", "Máximo de intentos: paso automático a devolución");
            }
        }
        catch (InvalidOperationException ex)
        {
            return new EventoRechazado(envio.Id, ex.Message, envio.Estado);
        }

        return null;
    }

    private async Task RegistrarOutboxBultoAsync(
        Guid operadorId,
        Guid envioId,
        Guid bultoId,
        EstadoBulto anterior,
        EstadoBulto nuevo,
        List<Guid> eventIdsOutbox,
        CancellationToken cancellationToken)
    {
        var evento = BultoEstadoCambiado.Crear(operadorId, envioId, bultoId, anterior, nuevo);
        await _mensajesOutbox.AgregarAsync(
            MensajeOutbox.CrearPendiente(
                evento.EventId,
                operadorId,
                envioId,
                BultoEstadoCambiado.Tipo,
                BultoEstadoCambiado.Version,
                evento.Serializar(),
                evento.Fecha,
                Activity.Current?.Id),
            cancellationToken);
        eventIdsOutbox.Add(evento.EventId);
    }

    private static string? ValidarPodSegunRegla(ComprobanteEntrega evento, ReglaOperativa? regla)
    {
        if (regla is null)
            return null;

        var bultosEntregados = evento.Bultos?.Where(bulto => bulto.Estado == EstadoBulto.Entregado).ToArray() ?? [];
        var esEntrega = evento.Estado == EstadoEnvio.Entregado
            || evento.Estado == EstadoEnvio.EntregadoParcialmente
            || bultosEntregados.Length > 0;
        if (!esEntrega)
            return null;

        if (regla.ExigeFirma && (bultosEntregados.Length > 0
                ? bultosEntregados.Any(bulto => string.IsNullOrWhiteSpace(bulto.Firma))
                : string.IsNullOrWhiteSpace(evento.Firma)))
            return "La regla del operador exige firma en la entrega.";

        if (regla.ExigeFoto && (bultosEntregados.Length > 0
                ? bultosEntregados.Any(bulto => string.IsNullOrWhiteSpace(bulto.Foto))
                : string.IsNullOrWhiteSpace(evento.Foto)))
            return "La regla del operador exige foto en la entrega.";

        if (regla.ExigeDocumentoReceptor && string.IsNullOrWhiteSpace(evento.DocumentoReceptor))
            return "La regla del operador exige el documento del receptor en la entrega.";

        return null;
    }

    private static string? ValidarEvidencia(ComprobanteEntrega evento)
    {
        if (evento.FechaDispositivo == default)
            return "La hora del dispositivo es requerida.";
        if (evento.Latitud.HasValue != evento.Longitud.HasValue ||
            evento.Latitud is < -90 or > 90 || evento.Longitud is < -180 or > 180)
            return "La posición debe incluir latitud y longitud válidas.";
        if (evento.Firma?.Length > 65_536)
            return "La firma supera 64 KiB.";
        if (!string.IsNullOrEmpty(evento.Firma))
        {
            try
            {
                using var documento = System.Text.Json.JsonDocument.Parse(evento.Firma);
                if (documento.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                    return "La firma debe ser un arreglo JSON de trazos.";
            }
            catch (System.Text.Json.JsonException)
            {
                return "La firma no contiene puntos JSON válidos.";
            }
        }
        if (!string.IsNullOrEmpty(evento.Foto))
        {
            if (evento.Foto.Length > 699_052)
                return "La foto supera 512 KiB.";
            try
            {
                var foto = Convert.FromBase64String(evento.Foto);
                if (foto.Length < 4 || foto.Length > 524_288 || foto[0] != 0xff || foto[1] != 0xd8 ||
                    foto[^2] != 0xff || foto[^1] != 0xd9)
                    return "La foto debe ser JPEG de hasta 512 KiB.";
            }
            catch (FormatException)
            {
                return "La foto no es base64 válido.";
            }
        }
        foreach (var bulto in evento.Bultos ?? [])
        {
            var errorBulto = ValidarEvidencia(evento with { Foto = bulto.Foto, Firma = bulto.Firma, Bultos = null });
            if (errorBulto is not null)
                return $"La evidencia del bulto {bulto.BultoId} no es válida: {errorBulto}";
        }
        return null;
    }
}
