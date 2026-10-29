using System.Diagnostics.Metrics;
using Plataforma.Avisos;

namespace Plataforma.Avisos.Tests;

/// <summary>Las métricas del tablero técnico salen con el nombre y las etiquetas que espera Grafana.</summary>
public class MetricasAvisosTests
{
    [Fact]
    public void Intento_SeInformaConSuResultado()
    {
        var medidos = new List<(string Instrumento, long Valor, string? Resultado)>();
        using var listener = Escuchar((instrumento, valor, etiquetas) =>
            medidos.Add((instrumento, valor, Etiqueta(etiquetas, "resultado"))));

        MetricasAvisos.RegistrarIntento("fallido");

        Assert.Contains(("plataforma.avisos.intentos", 1L, "fallido"), medidos);
    }

    [Fact]
    public void ProfundidadYEstados_SeInformanEnCadaLectura()
    {
        var medidos = new List<(string Instrumento, long Valor, string? Cola, string? Estado)>();
        using var listener = Escuchar((instrumento, valor, etiquetas) =>
            medidos.Add((instrumento, valor, Etiqueta(etiquetas, "cola"), Etiqueta(etiquetas, "estado"))));

        MetricasAvisos.ActualizarCola(TopologiaAvisos.ColaFallidos, 3);
        MetricasAvisos.ActualizarEntregas(new Dictionary<EstadoEntrega, long> { [EstadoEntrega.Fallido] = 2 });
        listener.RecordObservableInstruments();

        Assert.Contains(("plataforma.cola.mensajes", 3L, TopologiaAvisos.ColaFallidos, null), medidos);
        Assert.Contains(("plataforma.avisos.entregas", 2L, null, "Fallido"), medidos);
        // Los estados sin entregas se informan en cero, para que el tablero no quede vacío.
        Assert.Contains(("plataforma.avisos.entregas", 0L, null, "Entregado"), medidos);
    }

    private static MeterListener Escuchar(Action<string, long, ReadOnlySpan<KeyValuePair<string, object?>>> alMedir)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrumento, l) =>
            {
                if (instrumento.Meter.Name == MetricasAvisos.NombreMedidor)
                    l.EnableMeasurementEvents(instrumento);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrumento, valor, etiquetas, _) =>
            alMedir(instrumento.Name, valor, etiquetas));
        listener.Start();
        return listener;
    }

    private static string? Etiqueta(ReadOnlySpan<KeyValuePair<string, object?>> etiquetas, string clave)
    {
        foreach (var etiqueta in etiquetas)
            if (etiqueta.Key == clave)
                return etiqueta.Value?.ToString();
        return null;
    }
}
