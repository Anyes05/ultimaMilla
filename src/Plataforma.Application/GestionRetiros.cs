using System.Diagnostics;
using Plataforma.Application.Mensajes;
using Plataforma.Domain;

namespace Plataforma.Application;

public interface IRetiroRepositorio
{
    Task AgregarAsync(SolicitudRetiro retiro, CancellationToken cancellationToken = default);
    Task<SolicitudRetiro?> ObtenerAsync(Guid operadorId, Guid retiroId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SolicitudRetiro>> ListarAsync(
        Guid operadorId,
        Guid? comercioId,
        CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}

public sealed class GestionRetiros
{
    private readonly IRetiroRepositorio _retiros;
    private readonly IBanderasOperador _banderas;
    private readonly IOutboxRepositorio _outbox;
    private readonly PublicarOutboxPendientes _publicar;
    private readonly IComercioRepositorio _comercios;
    private readonly CalcularTarifa? _tarifa;
    private readonly IRutaRepositorio? _rutas;
    private readonly IEnvioRepositorio? _envios;
    private readonly IBultoRepositorio? _bultos;
    private readonly ICoberturaRepositorio? _cobertura;

    public GestionRetiros(
        IRetiroRepositorio retiros,
        IBanderasOperador banderas,
        IOutboxRepositorio outbox,
        PublicarOutboxPendientes publicar,
        IComercioRepositorio comercios,
        CalcularTarifa? tarifa = null,
        IRutaRepositorio? rutas = null,
        IEnvioRepositorio? envios = null,
        IBultoRepositorio? bultos = null,
        ICoberturaRepositorio? cobertura = null)
    {
        _tarifa = tarifa;
        _rutas = rutas;
        _envios = envios;
        _bultos = bultos;
        _cobertura = cobertura;
        _comercios = comercios;
        _retiros = retiros;
        _banderas = banderas;
        _outbox = outbox;
        _publicar = publicar;
    }

    public async Task<SolicitudRetiro> SolicitarAsync(
        Guid operadorId,
        Guid comercioId,
        string direccion,
        string contacto,
        DateOnly fecha,
        string observaciones = "",
        CancellationToken cancellationToken = default,
        int cantidadBultos = 1,
        decimal pesoKg = 0,
        decimal volumenCm3 = 0,
        Guid? franjaId = null,
        string motivo = "")
    {
        if (!await _banderas.EstaActivaAsync(operadorId, CatalogoBanderas.RetiroDomicilio, cancellationToken))
            throw new InvalidOperationException("El retiro en domicilio no está habilitado para este operador.");

        if (!await _comercios.EstaActivoAsync(comercioId, cancellationToken))
            throw new InvalidOperationException("El comercio está dado de baja y no puede pedir retiros.");

        // RF 37: la franja tiene que ser del operador, estar activa y no haber terminado si el retiro es para hoy.
        if (franjaId is Guid franjaPedida && franjaPedida != Guid.Empty && _cobertura is not null)
        {
            var franja = await _cobertura.ObtenerFranjaAsync(operadorId, franjaPedida, cancellationToken);
            if (franja is null || !franja.Activa)
                throw new InvalidOperationException("La franja horaria no existe o no está activa.");
            var ahora = DateTimeOffset.UtcNow.ToOffset(RelojOperacion.Offset);
            if (fecha == DateOnly.FromDateTime(ahora.DateTime) && TimeOnly.FromDateTime(ahora.DateTime) >= franja.Hasta)
                throw new InvalidOperationException($"La franja {franja.Nombre} ya terminó hoy: elegí otra franja u otro día.");
        }

        // RF 41: el retiro tiene tarifa propia; se fija al pedirlo, como la del envío al darlo de alta.
        var tarifa = _tarifa is null ? null : await _tarifa.CalcularRetiroAsync(operadorId, comercioId, cancellationToken);
        var retiro = SolicitudRetiro.Solicitar(
            Guid.NewGuid(), operadorId, comercioId, direccion, contacto, fecha, observaciones,
            tarifa?.Importe ?? 0, tarifa?.ReglaTarifariaId, cantidadBultos, pesoKg, volumenCm3, franjaId, motivo);
        await _retiros.AgregarAsync(retiro, cancellationToken);

        var evento = RetiroSolicitado.Crear(operadorId, retiro.Id, comercioId, retiro.Direccion);
        await _outbox.AgregarAsync(
            MensajeOutbox.CrearPendiente(
                evento.EventId,
                operadorId,
                retiro.Id,
                RetiroSolicitado.Tipo,
                RetiroSolicitado.Version,
                evento.Serializar(),
                evento.Fecha,
                Activity.Current?.Id),
            cancellationToken);

        await _retiros.GuardarAsync(cancellationToken);
        await _publicar.EjecutarUnoAsync(evento.EventId, cancellationToken);
        return retiro;
    }

    /// <summary>Lo que costaría un retiro pedido ahora (para mostrarlo antes de pedirlo); null si no hay tarifa vigente.</summary>
    public async Task<ResultadoTarifa?> CotizarAsync(Guid operadorId, Guid comercioId, CancellationToken cancellationToken = default)
    {
        if (_tarifa is null)
            return null;
        try
        {
            return await _tarifa.CalcularRetiroAsync(operadorId, comercioId, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public Task<IReadOnlyList<SolicitudRetiro>> ListarAsync(
        Guid operadorId,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default) =>
        _retiros.ListarAsync(operadorId, comercioId, cancellationToken);

    /// <summary>
    /// Asigna el retiro a una ruta mixta, después de <paramref name="posicion"/> entregas (null = al final).
    /// RF 39: se valida la capacidad del vehículo a lo largo del recorrido, con lo retirado ocupando lugar desde su parada.
    /// </summary>
    public async Task ProgramarAsync(
        Guid operadorId,
        Guid retiroId,
        Guid rutaId,
        CancellationToken cancellationToken = default,
        int? posicion = null)
    {
        var retiro = await _retiros.ObtenerAsync(operadorId, retiroId, cancellationToken)
            ?? throw new InvalidOperationException("El retiro no existe.");

        Ruta? ruta = null;
        if (_rutas is not null)
            ruta = await _rutas.ObtenerAsync(operadorId, rutaId, cancellationToken)
                ?? throw new InvalidOperationException("La ruta no existe para ese operador.");

        retiro.ProgramarEnRuta(rutaId, posicion, ruta?.Fecha);

        // RF 14 en rutas mixtas: una ruta lleva una sola franja; el retiro con franja tiene que coincidir.
        if (ruta is not null && retiro.FranjaId is Guid franjaRetiro && _envios is not null)
        {
            var franjasRuta = (await _envios.ObtenerPorRutaAsync(operadorId, ruta.Id, cancellationToken))
                .Where(envio => envio.FranjaId is not null)
                .Select(envio => envio.FranjaId!.Value)
                .Distinct()
                .ToList();
            if (franjasRuta.Count > 0 && !franjasRuta.Contains(franjaRetiro))
                throw new InvalidOperationException("El retiro es de otra franja horaria que la ruta: elegí una ruta de su franja.");
        }

        if (ruta is not null && await PerfilCargaAsync(operadorId, ruta, cancellationToken, retiro) is { Problema: { } problema })
            throw new InvalidOperationException($"El retiro no entra en el vehículo de esa ruta. {problema} Elegí otra parada u otra ruta.");

        await _retiros.GuardarAsync(cancellationToken);
    }

    /// <summary>Carga del vehículo parada por parada (entregas en orden de visita y retiros en su parada).</summary>
    public async Task<PerfilCargaRuta?> PerfilCargaAsync(Guid operadorId, Guid rutaId, CancellationToken cancellationToken = default)
    {
        if (_rutas is null)
            return null;
        var ruta = await _rutas.ObtenerAsync(operadorId, rutaId, cancellationToken);
        return ruta is null ? null : await PerfilCargaAsync(operadorId, ruta, cancellationToken);
    }

    /// <param name="programando">El retiro que se está asignando (todavía sin guardar): se cuenta con sus datos nuevos.</param>
    private async Task<PerfilCargaRuta?> PerfilCargaAsync(
        Guid operadorId,
        Ruta ruta,
        CancellationToken cancellationToken,
        SolicitudRetiro? programando = null)
    {
        if (_envios is null || _bultos is null)
            return null;

        var vehiculo = ruta.VehiculoId is Guid vehiculoId && _cobertura is not null
            ? await _cobertura.ObtenerVehiculoAsync(operadorId, vehiculoId, cancellationToken)
            : null;

        var entregas = new List<EntregaEnRuta>();
        var enviosDeRuta = await _envios.ObtenerPorRutaAsync(operadorId, ruta.Id, cancellationToken);
        foreach (var grupo in enviosDeRuta.GroupBy(envio => DespacharRuta.DireccionNormalizada(envio.DireccionEntrega)))
        {
            decimal pesoParada = 0;
            decimal volumenParada = 0;
            foreach (var envio in grupo)
            {
            // A bordo: lo que todavía no se entregó ni se devolvió.
            var aBordo = envio.Estado is EstadoEnvio.AsignadoARuta or EstadoEnvio.EnTransito
                ? (await _bultos.ListarPorEnvioAsync(operadorId, envio.Id, cancellationToken))
                    .Where(bulto => bulto.Estado is EstadoBulto.Pendiente or EstadoBulto.EnRuta or EstadoBulto.PendienteNuevaVisita)
                    .ToList()
                : [];
                pesoParada += aBordo.Sum(bulto => bulto.PesoKg);
                volumenParada += aBordo.Sum(bulto => bulto.AltoCm * bulto.AnchoCm * bulto.LargoCm);
            }
            entregas.Add(new EntregaEnRuta(
                $"Entrega {grupo.First().DireccionEntrega}", pesoParada, volumenParada));
        }

        var retiros = (await _retiros.ListarAsync(operadorId, null, cancellationToken))
            .Where(retiro => retiro.Id != programando?.Id)
            .Append(programando)
            .OfType<SolicitudRetiro>()
            .Where(retiro => retiro.RutaId == ruta.Id
                             && retiro.Estado is EstadoRetiro.Programado or EstadoRetiro.EnCurso or EstadoRetiro.Completado)
            .OrderBy(retiro => retiro.PosicionEnRuta ?? int.MaxValue)
            .ThenBy(retiro => retiro.CreadoEn)
            .Select(retiro => new RetiroEnRuta($"Retiro {retiro.Direccion}", retiro.PosicionEnRuta, retiro.PesoKg, retiro.VolumenCm3))
            .ToList();

        return CapacidadRuta.Calcular(ruta.Id, vehiculo, entregas, retiros);
    }

    /// <summary>Salió la ruta: sus retiros programados pasan a "en curso".</summary>
    public async Task<int> IniciarRutaAsync(Guid operadorId, Guid rutaId, CancellationToken cancellationToken = default)
    {
        var programados = (await _retiros.ListarAsync(operadorId, null, cancellationToken))
            .Where(retiro => retiro.RutaId == rutaId && retiro.Estado == EstadoRetiro.Programado)
            .ToList();
        if (programados.Count == 0)
            return 0;

        foreach (var listado in programados)
            (await _retiros.ObtenerAsync(operadorId, listado.Id, cancellationToken))?.MarcarEnCurso();
        await _retiros.GuardarAsync(cancellationToken);
        return programados.Count;
    }

    /// <summary>El repartidor no pudo retirar (RF 38): queda para una nueva visita.</summary>
    public async Task NoRetirarAsync(Guid operadorId, Guid retiroId, string motivo, CancellationToken cancellationToken = default)
    {
        var retiro = await _retiros.ObtenerAsync(operadorId, retiroId, cancellationToken)
            ?? throw new InvalidOperationException("El retiro no existe.");
        retiro.NoRetirar(motivo);
        await _retiros.GuardarAsync(cancellationToken);
    }

    /// <summary>Lo retirado llegó al depósito (RF 38).</summary>
    public async Task RecibirEnDepositoAsync(Guid operadorId, Guid retiroId, CancellationToken cancellationToken = default)
    {
        var retiro = await _retiros.ObtenerAsync(operadorId, retiroId, cancellationToken)
            ?? throw new InvalidOperationException("El retiro no existe.");
        retiro.RecibirEnDeposito();
        await _retiros.GuardarAsync(cancellationToken);
    }

    public async Task CompletarAsync(
        Guid operadorId,
        Guid retiroId,
        Guid? envioAsociadoId = null,
        CancellationToken cancellationToken = default)
    {
        var retiro = await _retiros.ObtenerAsync(operadorId, retiroId, cancellationToken)
            ?? throw new InvalidOperationException("El retiro no existe.");
        retiro.Completar(envioAsociadoId);

        var evento = RetiroCompletado.Crear(operadorId, retiro.Id, retiro.ComercioId, envioAsociadoId);
        await _outbox.AgregarAsync(
            MensajeOutbox.CrearPendiente(
                evento.EventId,
                operadorId,
                retiro.Id,
                RetiroCompletado.Tipo,
                RetiroCompletado.Version,
                evento.Serializar(),
                evento.Fecha,
                Activity.Current?.Id),
            cancellationToken);

        await _retiros.GuardarAsync(cancellationToken);
        await _publicar.EjecutarUnoAsync(evento.EventId, cancellationToken);
    }

    public async Task CancelarAsync(
        Guid operadorId,
        Guid retiroId,
        string motivo = "",
        CancellationToken cancellationToken = default)
    {
        var retiro = await _retiros.ObtenerAsync(operadorId, retiroId, cancellationToken)
            ?? throw new InvalidOperationException("El retiro no existe.");
        retiro.Cancelar(motivo);
        await _retiros.GuardarAsync(cancellationToken);
    }
}
