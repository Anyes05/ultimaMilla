namespace Plataforma.Domain;

public enum RolUsuario
{
    Administrador,
    Despachador,
    OperarioDeposito,
    Comercio,
    Repartidor
}

public static class Roles
{
    public const string Administrador = "administrador";
    public const string Despachador = "despachador";
    public const string OperarioDeposito = "operario_deposito";
    public const string Comercio = "comercio";
    public const string Repartidor = "repartidor";

    public static string De(RolUsuario rol) => rol switch
    {
        RolUsuario.Administrador => Administrador,
        RolUsuario.Despachador => Despachador,
        RolUsuario.OperarioDeposito => OperarioDeposito,
        RolUsuario.Comercio => Comercio,
        RolUsuario.Repartidor => Repartidor,
        _ => throw new ArgumentOutOfRangeException(nameof(rol))
    };
}
