namespace Plataforma.Domain;

public enum RolUsuario
{
    Despachador,
    Comercio,
    Repartidor
}

public static class Roles
{
    public const string Despachador = "despachador";
    public const string Comercio = "comercio";
    public const string Repartidor = "repartidor";

    public static string De(RolUsuario rol) => rol switch
    {
        RolUsuario.Despachador => Despachador,
        RolUsuario.Comercio => Comercio,
        RolUsuario.Repartidor => Repartidor,
        _ => throw new ArgumentOutOfRangeException(nameof(rol))
    };
}
