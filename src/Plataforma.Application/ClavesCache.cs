namespace Plataforma.Application;

public static class ClavesCache
{
    public static string Operador(Guid operadorId) => $"operador:{operadorId:N}";

    public static string Seguimiento(string tokenSeguimientoPublico) =>
        $"seguimiento:{tokenSeguimientoPublico}";

    public static string SeguimientoPorNumero(string numeroSeguimiento) =>
        $"seguimiento:num:{numeroSeguimiento}";
}
