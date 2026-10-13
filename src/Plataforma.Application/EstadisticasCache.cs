namespace Plataforma.Application;

public sealed record EstadisticasCache(long Hits, long Misses)
{
    public long Total => Hits + Misses;

    public decimal Ratio => Total == 0 ? 0 : Math.Round((decimal)Hits / Total, 4);
}
