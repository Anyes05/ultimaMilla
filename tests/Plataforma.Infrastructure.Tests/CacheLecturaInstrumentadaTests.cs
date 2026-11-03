using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Plataforma.Application;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

/// <summary>Métrica por caso de uso y funcionamiento sin caché (letra 6.7 y aclaración 3.1).</summary>
public class CacheLecturaInstrumentadaTests
{
    [Theory]
    [InlineData("seguimiento:abc123", "seguimiento")]
    [InlineData("seguimiento:num:ENV-1", "seguimiento")]
    [InlineData("operador:0a1b", "configuracion-operador")]
    [InlineData("operador:0a1b:seguimiento:abc", "seguimiento")]
    [InlineData("operador:0a1b:tarifa:vigente", "tarifas")]
    [InlineData("otra-cosa", "otro")]
    public void CasoDeUso_SaleDeLaClave(string clave, string esperado) =>
        Assert.Equal(esperado, CacheLecturaInstrumentada.CasoDeUso(clave));

    [Fact]
    public async Task AciertosYFallos_SeInformanPorCasoDeUso()
    {
        var interno = new CacheLecturaMemoria();
        var cache = Crear(interno, new RelojFijo());
        var medidos = Escuchar();

        await cache.GuardarAsync("seguimiento:t1", "{}", TimeSpan.FromMinutes(1));
        await cache.LeerAsync("seguimiento:t1");
        await cache.LeerAsync("operador:x");

        Assert.Contains(("seguimiento", "acierto"), medidos);
        Assert.Contains(("configuracion-operador", "fallo"), medidos);
        Assert.Equal(new EstadisticasCache(1, 1), cache.Estadisticas());
    }

    [Fact]
    public async Task CacheCaido_DevuelveNullSinLanzar_YQuedaEnPausa()
    {
        var interno = new CacheQueFalla();
        var reloj = new RelojFijo();
        var cache = Crear(interno, reloj);
        var medidos = Escuchar();

        // El caso de uso recibe null y va a la base; guardar e invalidar tampoco lanzan.
        Assert.Null(await cache.LeerAsync("seguimiento:t1"));
        await cache.GuardarAsync("seguimiento:t1", "{}", TimeSpan.FromMinutes(1));
        await cache.QuitarAsync("seguimiento:t1");

        // En pausa ni se consulta el caché: no se suma su timeout a cada request.
        var llamadasAntes = interno.Llamadas;
        Assert.Null(await cache.LeerAsync("seguimiento:t1"));
        Assert.Equal(llamadasAntes, interno.Llamadas);
        Assert.Equal(2, medidos.Count(m => m == ("seguimiento", "error")));
    }

    [Fact]
    public async Task PasadaLaPausa_VuelveAUsarElCache()
    {
        var interno = new CacheQueFalla();
        var reloj = new RelojFijo();
        var cache = Crear(interno, reloj);

        await cache.LeerAsync("seguimiento:t1");
        interno.Falla = false;
        reloj.Avanzar(CacheLecturaInstrumentada.Pausa + TimeSpan.FromSeconds(1));

        await cache.LeerAsync("seguimiento:t1");
        Assert.Equal(2, interno.Llamadas);
    }

    private static CacheLecturaInstrumentada Crear(ICacheLectura interno, TimeProvider reloj) =>
        new(interno, NullLogger<CacheLecturaInstrumentada>.Instance, reloj);

    private static List<(string CasoUso, string Resultado)> Escuchar()
    {
        var medidos = new List<(string, string)>();
        var listener = new MeterListener
        {
            InstrumentPublished = (instrumento, l) =>
            {
                if (instrumento.Meter.Name == CacheLecturaInstrumentada.NombreMedidor)
                    l.EnableMeasurementEvents(instrumento);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, etiquetas, _) =>
        {
            string? casoUso = null, resultado = null;
            foreach (var etiqueta in etiquetas)
            {
                if (etiqueta.Key == "caso_uso") casoUso = etiqueta.Value?.ToString();
                if (etiqueta.Key == "resultado") resultado = etiqueta.Value?.ToString();
            }
            lock (medidos) medidos.Add((casoUso!, resultado!));
        });
        listener.Start();
        return medidos;
    }

    private sealed class RelojFijo : TimeProvider
    {
        private DateTimeOffset _ahora = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _ahora;
        public void Avanzar(TimeSpan tiempo) => _ahora += tiempo;
    }

    private sealed class CacheQueFalla : ICacheLectura
    {
        public bool Falla { get; set; } = true;
        public int Llamadas { get; private set; }

        public Task<string?> LeerAsync(string clave, CancellationToken cancellationToken = default)
        {
            Llamadas++;
            return Falla
                ? throw new InvalidOperationException("Redis no responde")
                : Task.FromResult<string?>(null);
        }

        public Task GuardarAsync(string clave, string valor, TimeSpan vigencia, CancellationToken cancellationToken = default) =>
            Falla ? throw new InvalidOperationException("Redis no responde") : Task.CompletedTask;

        public Task QuitarAsync(string clave, CancellationToken cancellationToken = default) =>
            Falla ? throw new InvalidOperationException("Redis no responde") : Task.CompletedTask;

        public EstadisticasCache Estadisticas() => new(0, 0);
    }
}
