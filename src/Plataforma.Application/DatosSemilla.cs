namespace Plataforma.Application;

/// <summary>
/// Identificadores y credenciales del seed Norte/Sur (contrato del 12/10).
/// Nadia y Alvaro pueden apoyarse en estos valores fijos.
/// </summary>
public static class DatosSemilla
{
    public static readonly Guid OperadorNorteId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OperadorSurId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static readonly Guid ComercioNorteId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid ComercioSurId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static readonly Guid UsuarioDespachadorNorteId = Guid.Parse("55555555-5555-5555-5555-555555555501");
    public static readonly Guid UsuarioComercioNorteId = Guid.Parse("55555555-5555-5555-5555-555555555502");
    public static readonly Guid UsuarioDespachadorSurId = Guid.Parse("55555555-5555-5555-5555-555555555503");
    public static readonly Guid UsuarioComercioSurId = Guid.Parse("55555555-5555-5555-5555-555555555504");

    public const string NorteNombre = "Operador Norte";
    public const string NorteColor = "azul";
    public const int NorteMaximoIntentos = 3;

    public const string SurNombre = "Operador Sur";
    public const string SurColor = "verde";
    public const int SurMaximoIntentos = 1;

    public const string UsuarioDespachadorNorte = "norte.despachador";
    public const string UsuarioComercioNorte = "norte.comercio";
    public const string UsuarioDespachadorSur = "sur.despachador";
    public const string UsuarioComercioSur = "sur.comercio";

    public const string ClaveDesarrollo = "Demo123!";
}
