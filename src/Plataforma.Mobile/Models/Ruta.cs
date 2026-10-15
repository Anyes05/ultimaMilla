namespace Plataforma.Mobile.Models;

public sealed record Ruta(Guid Id, DateOnly Fecha, string Operador, IReadOnlyList<Envio> Envios);
