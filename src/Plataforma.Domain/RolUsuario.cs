namespace Plataforma.Domain;

public enum RolUsuario
{
    Administrador,
    Despachador,
    OperarioDeposito,
    Comercio,
    Repartidor,
    AdministradorPlataforma
}

public static class Roles
{
    public const string Administrador = "administrador";
    public const string Despachador = "despachador";
    public const string OperarioDeposito = "operario_deposito";
    public const string Comercio = "comercio";
    public const string Repartidor = "repartidor";
    public const string AdministradorPlataforma = "administrador_plataforma";

    public static string De(RolUsuario rol) => rol switch
    {
        RolUsuario.Administrador => Administrador,
        RolUsuario.Despachador => Despachador,
        RolUsuario.OperarioDeposito => OperarioDeposito,
        RolUsuario.Comercio => Comercio,
        RolUsuario.Repartidor => Repartidor,
        RolUsuario.AdministradorPlataforma => AdministradorPlataforma,
        _ => throw new ArgumentOutOfRangeException(nameof(rol))
    };
}
