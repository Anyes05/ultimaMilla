namespace Plataforma.Application;

public static class ClavesCache
{
    public static string Operador(Guid operadorId) => $"operador:{operadorId:N}";

    public static string TarifaVigente(Guid operadorId) => $"operador:{operadorId:N}:tarifa:vigente";

    public static string Banderas(Guid operadorId) => $"operador:{operadorId:N}:banderas";

    public static string Seguimiento(string tokenSeguimientoPublico) =>
        $"seguimiento:{tokenSeguimientoPublico}";

    public static string SeguimientoPorNumero(string numeroSeguimiento) =>
        $"seguimiento:num:{numeroSeguimiento}";

    public static string DetalleSeguimiento(string tokenSeguimientoPublico) =>
        $"seguimiento:detalle:{tokenSeguimientoPublico}";

    public static string DetalleSeguimientoPorNumero(string numeroSeguimiento) =>
        $"seguimiento:detalle:num:{numeroSeguimiento}";
}
