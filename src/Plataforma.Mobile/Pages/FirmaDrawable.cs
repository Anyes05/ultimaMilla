using System.Text.Json;

namespace Plataforma.Mobile.Pages;

public sealed class FirmaDrawable : IDrawable
{
    public List<List<PointF>> Trazos { get; } = [];
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.StrokeColor = Colors.Black;
        canvas.StrokeSize = 3;
        foreach (var trazo in Trazos)
            for (var i = 1; i < trazo.Count; i++)
                canvas.DrawLine(trazo[i - 1], trazo[i]);
    }

    public string? Exportar() => Trazos.Any(t => t.Count > 1)
        ? JsonSerializer.Serialize(Trazos.Select(t => t.Select(p => new { x = p.X, y = p.Y })))
        : null;
}
