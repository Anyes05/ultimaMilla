using Plataforma.Mobile.Models;
using Plataforma.Mobile.Data;

namespace Plataforma.Mobile.Services;

// El servidor sigue siendo un doble; la ruta y la cola si se guardan en SQLite.
public sealed class RepartidorDemoService(RepartidorLocalData data) : IRepartidorService
{
    public bool HaySesion => data.Leer().HaySesion;
    public bool ConexionSimulada { get; set; } = true;

    public bool IniciarSesion(string usuario, string clave)
    {
        if (!ConexionSimulada)
            throw new InvalidOperationException("Activa la conexion simulada para iniciar sesion.");

        var respuestaJson = ServidorRepartidorDemo.Login(
            ContratosRepartidor.Escribir(new LoginSolicitud(usuario, clave)));
        if (respuestaJson is null)
            return false;
        var sesion = ContratosRepartidor.Leer<LoginRespuesta>(respuestaJson);
        if (!string.Equals(sesion.Rol, "repartidor", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(sesion.Token)
            || sesion.RepartidorId == Guid.Empty || sesion.OperadorId == Guid.Empty)
            throw new InvalidOperationException("El JSON de login no contiene una sesion demo de repartidor valida.");

        var estado = data.Leer();
        if (estado.Ruta is null)
            estado = CrearRuta();
        data.Guardar(estado with { HaySesion = true });
        return true;
    }

    public Ruta ObtenerRuta()
    {
        return ExigirSesion().Ruta!;
    }

    public void ReintentarRechazados()
    {
        var estado = ExigirSesion();
        data.Guardar(estado with
        {
            Ruta = estado.Ruta! with
            {
                Envios = estado.Ruta.Envios.Select(e => e.Sincronizacion == EstadoSincronizacion.Rechazado
                    ? e with { Sincronizacion = EstadoSincronizacion.Pendiente } : e).ToArray()
            }
        });
    }

    public void RegistrarEvento(EntregaPendiente evento)
    {
        var estado = ExigirSesion();
        var envio = estado.Ruta!.Envios.Single(e => e.Id == evento.EnvioId);
        if (envio.Entrega is not null)
            throw new InvalidOperationException("Sincroniza primero el evento pendiente. No se sobrescribio el comprobante.");
        if (evento.Estado == EstadoEnvio.EnTransito)
        {
            if (!envio.PuedeCargar || evento.CodigoEscaneado != envio.NumeroSeguimiento)
                throw new InvalidOperationException("La carga exige un envio asignado y su codigo exacto.");
        }
        else if (!envio.PuedeEntregar || evento.Estado is not (EstadoEnvio.Entregado or EstadoEnvio.NoEntregado))
            throw new InvalidOperationException("Primero confirma la carga con el servidor antes de registrar la entrega.");
        if (evento.Estado == EstadoEnvio.NoEntregado && string.IsNullOrWhiteSpace(evento.Observaciones))
            throw new InvalidOperationException("Ingresa el motivo del intento fallido.");
        if (evento.Estado == EstadoEnvio.Entregado && string.IsNullOrWhiteSpace(evento.NombreReceptor))
            throw new InvalidOperationException("Ingresa quien recibe.");
        if (evento.NombreReceptor.Length > 100 || evento.Observaciones.Length > 500 ||
            (evento.Latitud is null) != (evento.Longitud is null) ||
            evento.Latitud is < -90 or > 90 || evento.Longitud is < -180 or > 180)
            throw new InvalidOperationException("Datos de comprobante invalidos.");
        try
        {
            if (evento.Foto is not null)
            {
                var foto = Convert.FromBase64String(evento.Foto);
                if (foto.Length > 512 * 1024 || foto.Length < 4 || foto[0] != 255 || foto[1] != 216)
                    throw new InvalidOperationException("Foto JPEG invalida o mayor de 512 KiB.");
            }
            if (evento.Firma is not null)
            {
                if (System.Text.Encoding.UTF8.GetByteCount(evento.Firma) > 64 * 1024)
                    throw new InvalidOperationException("La firma supera 64 KiB.");
                using var firma = System.Text.Json.JsonDocument.Parse(evento.Firma);
                if (firma.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                    throw new InvalidOperationException("La firma debe contener un arreglo de trazos.");
            }
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("La evidencia no tiene un formato valido.", ex);
        }
        data.Guardar(Reemplazar(estado, envio with
        {
            Entrega = evento, Sincronizacion = EstadoSincronizacion.Pendiente, MotivoRechazo = null
        }));
    }

    public void RegistrarEntrega(Guid envioId, string nombreReceptor, string observaciones)
    {
        var estado = ExigirSesion();
        if (string.IsNullOrWhiteSpace(nombreReceptor))
            throw new InvalidOperationException("Ingresa el nombre de quien recibe.");
        if (nombreReceptor.Trim().Length > 100 || observaciones.Length > 500)
            throw new InvalidOperationException("El nombre admite 100 caracteres y las observaciones 500.");

        var envio = estado.Ruta!.Envios.SingleOrDefault(item => item.Id == envioId)
            ?? throw new InvalidOperationException("El envio no pertenece a tu ruta.");
        if (!envio.PuedeEntregar)
            throw new InvalidOperationException("El envio ya tiene una entrega local o no esta en transito.");

        var entrega = new EntregaPendiente(
            Guid.NewGuid(), envioId, nombreReceptor.Trim(), observaciones.Trim(), DateTimeOffset.Now);
        data.Guardar(Reemplazar(estado,
            envio with { Entrega = entrega, Sincronizacion = EstadoSincronizacion.Pendiente }));
    }

    public ResultadoSincronizacion Sincronizar()
    {
        var estado = ExigirSesion();
        if (!ConexionSimulada)
            throw new InvalidOperationException("Sin conexion simulada. Las entregas siguen guardadas en SQLite.");

        var pendientes = estado.Ruta!.Envios.Where(item =>
            item.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado).ToArray();
        var eventos = pendientes.Select(envio =>
        {
            var entrega = envio.Entrega
                ?? throw new InvalidOperationException("La cola contiene un envio sin comprobante local.");
            return new EntregaContrato(entrega.Id, envio.Id, entrega.Estado,
                entrega.NombreReceptor, entrega.Observaciones, entrega.Fecha,
                entrega.Foto, entrega.Firma, entrega.Latitud, entrega.Longitud, entrega.CodigoEscaneado);
        }).ToArray();
        var estadosServidor = estado.EstadosServidor.ToDictionary();
        var solicitud = new SincronizacionSolicitud(estado.Ruta.Fecha, eventos);
        var respuestaJson = ServidorRepartidorDemo.Sincronizar(
            ContratosRepartidor.Escribir(solicitud), estado.Ruta, estadosServidor);
        var respuesta = ContratosRepartidor.Leer<SincronizacionRespuesta>(respuestaJson);
        foreach (var aplicado in respuesta.Aplicados)
        {
            var envio = pendientes.Single(item => item.Id == aplicado.EnvioId);
            estado = Reemplazar(estado, envio with
            {
                Estado = aplicado.Estado,
                Sincronizacion = EstadoSincronizacion.Sincronizado,
                MotivoRechazo = null,
                Historial = (envio.Historial ?? []).Concat(new[] { envio.Entrega! }).ToArray(),
                Entrega = aplicado.Estado == EstadoEnvio.EnTransito ? null : envio.Entrega
            });
        }
        foreach (var rechazado in respuesta.Rechazados)
        {
            var envio = pendientes.Single(item => item.Id == rechazado.EnvioId);
            estado = Reemplazar(estado, envio with
            {
                Estado = rechazado.EstadoActual ?? envio.Estado,
                Sincronizacion = EstadoSincronizacion.Rechazado,
                MotivoRechazo = rechazado.Motivo
            });
        }

        data.Guardar(estado with
        {
            EstadosServidor = estadosServidor,
            ComprobantesConfirmados = (estado.ComprobantesConfirmados ?? [])
                .Concat(pendientes.Where(e => respuesta.Aplicados.Any(a => a.EnvioId == e.Id)).Select(e => e.Entrega!))
                .DistinctBy(e => e.Id).ToArray()
        });
        return new ResultadoSincronizacion(respuesta.Aplicados.Count, respuesta.Rechazados.Count,
            estado.Ruta!.Envios.Count(item => item.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado));
    }

    public void CerrarSesion()
    {
        var estado = ExigirSesion();
        if (estado.Ruta!.Envios.Any(item =>
                item.Sincronizacion is EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado))
            throw new InvalidOperationException("Hay entregas pendientes o rechazadas. Se conserva la sesion para no perderlas.");

        data.Guardar(new EstadoLocalDemo(null, false, new Dictionary<Guid, EstadoEnvio>()));
        ConexionSimulada = true;
    }

    private static EstadoLocalDemo CrearRuta()
    {
        var rutas = ContratosRepartidor.Leer<RutaContrato[]>(ServidorRepartidorDemo.DescargarRutas());
        if (rutas.Length != 1)
            throw new InvalidOperationException("La demo requiere una ruta en el JSON; no se descartaron otras rutas.");
        var contrato = rutas[0];
        if (contrato is null || contrato.Id == Guid.Empty || contrato.Fecha == default
            || contrato.Operador is null || string.IsNullOrWhiteSpace(contrato.Operador.Nombre)
            || contrato.Envios is null || contrato.Envios.Any(envio => envio is null || envio.Id == Guid.Empty)
            || contrato.Envios.Select(envio => envio.Id).Distinct().Count() != contrato.Envios.Count)
            throw new InvalidOperationException("El JSON de ruta contiene datos incompletos o duplicados.");
        var estadosServidor = new Dictionary<Guid, EstadoEnvio>();
        var envios = contrato.Envios.Select(envio =>
        {
            // Mantiene los conflictos de la demo anterior, posteriores a la descarga.
            estadosServidor.Add(envio.Id, envio.NumeroSeguimiento switch
            {
                "DEMO-002" => EstadoEnvio.Entregado,
                "DEMO-003" => EstadoEnvio.Extraviado,
                _ => envio.Estado
            });
            return new Envio(envio.Id, envio.NumeroSeguimiento, envio.Destinatario,
                envio.DireccionEntrega, envio.Estado, null, EstadoSincronizacion.SinCambios, null);
        }).ToArray();
        var ruta = new Ruta(contrato.Id, contrato.Fecha, contrato.Operador.Nombre, envios);
        return new EstadoLocalDemo(ruta, true, estadosServidor);
    }

    private static EstadoLocalDemo Reemplazar(EstadoLocalDemo estado, Envio envio)
    {
        return estado with
        {
            Ruta = estado.Ruta! with
            {
                Envios = estado.Ruta.Envios.Select(item => item.Id == envio.Id ? envio : item).ToArray()
            }
        };
    }

    private EstadoLocalDemo ExigirSesion()
    {
        var estado = data.Leer();
        if (!estado.HaySesion)
            throw new InvalidOperationException("Debes iniciar sesion para acceder a la ruta.");
        return estado;
    }
}
