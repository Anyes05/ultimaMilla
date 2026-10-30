using Plataforma.Domain;

namespace Plataforma.Backoffice;

/// <summary>
/// Autorización por perfil del Backoffice (letra 3.3 y 6.3).
/// Administrador: configura la operación (tarifas, avisos, técnico) y puede corregir cualquier estado.
/// Despachador: planifica y despacha rutas, monitorea la jornada, resuelve incidencias.
/// Operario de depósito: recibe, clasifica y rinde bultos.
/// Las transiciones por perfil salen de la tabla 4.2 del documento de diseño; se validan en el servidor,
/// no solo se esconden los botones.
/// </summary>
public static class PermisosPerfil
{
    /// <summary>Monitoreo de envíos: los tres perfiles del operador.</summary>
    public const string Operacion = Roles.Administrador + "," + Roles.Despachador + "," + Roles.OperarioDeposito;

    /// <summary>Despacho de rutas y tablero en vivo: quien planifica y monitorea la jornada.</summary>
    public const string Planificacion = Roles.Administrador + "," + Roles.Despachador;

    /// <summary>Trabajo en depósito (rendición de devoluciones; luego recepción por escaneo): quien recibe y rinde bultos.</summary>
    public const string Deposito = Roles.Administrador + "," + Roles.OperarioDeposito;

    /// <summary>Configuración y operación técnica: tarifas, avisos fallidos, caché.</summary>
    public const string Administracion = Roles.Administrador;

    private static readonly HashSet<(EstadoEnvio Desde, EstadoEnvio Hacia)> OperarioDeposito =
    [
        (EstadoEnvio.Admitido, EstadoEnvio.EnDeposito),     // 1 recepción en depósito
        (EstadoEnvio.Admitido, EstadoEnvio.Extraviado),     // 2 extravío
        (EstadoEnvio.EnDeposito, EstadoEnvio.Extraviado),   // 4 extravío en depósito
        (EstadoEnvio.EnDevolucion, EstadoEnvio.Devuelto)    // 13 rendición en depósito
    ];

    private static readonly HashSet<(EstadoEnvio Desde, EstadoEnvio Hacia)> Despachador =
    [
        (EstadoEnvio.Admitido, EstadoEnvio.Extraviado),       // 2 extravío
        (EstadoEnvio.AsignadoARuta, EstadoEnvio.EnDeposito),  // 6 ruta cancelada antes de salir
        (EstadoEnvio.EnTransito, EstadoEnvio.Extraviado)      // 9 extravío en ruta
        // 3 y 12 (asignar a ruta) se hacen desde Despacho, que arma la ruta; 5, 7 y 8 son del repartidor (app).
    ];

    /// <summary>Destinos que el perfil puede disparar desde el Backoffice, dentro de los válidos de la máquina de estados.</summary>
    public static IReadOnlyList<EstadoEnvio> DestinosPermitidos(string rol, EstadoEnvio desde, IEnumerable<EstadoEnvio> validos) =>
        validos.Where(hacia => PuedeCambiar(rol, desde, hacia)).ToList();

    public static bool PuedeCambiar(string rol, EstadoEnvio desde, EstadoEnvio hacia) => rol switch
    {
        // El administrador corrige cualquier transición válida (incluidas las automáticas 10 y 11 y las del
        // repartidor, para cuando la app no puede registrarlas). La validez la sigue controlando TransicionesEnvio.
        Roles.Administrador => true,
        Roles.Despachador => Despachador.Contains((desde, hacia)),
        Roles.OperarioDeposito => OperarioDeposito.Contains((desde, hacia)),
        _ => false
    };

    /// <summary>Perfiles que pueden entrar al Backoffice. El comercio usa el Portal y el repartidor la app.</summary>
    public static bool PuedeEntrarAlBackoffice(RolUsuario rol) =>
        rol is RolUsuario.Administrador or RolUsuario.Despachador or RolUsuario.OperarioDeposito;
}
