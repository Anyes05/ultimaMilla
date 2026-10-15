using Plataforma.Domain;

namespace Plataforma.Application;

public sealed record SeguimientoVisible(
    string NumeroSeguimiento,
    string TokenSeguimientoPublico,
    string Destinatario,
    string DireccionEntrega,
    EstadoEnvio Estado,
    int IntentosRealizados)
{
    public static SeguimientoVisible Desde(Envio envio) =>
        new(
            envio.NumeroSeguimiento,
            envio.TokenSeguimientoPublico,
            envio.Destinatario,
            envio.DireccionEntrega,
            envio.Estado,
            envio.IntentosRealizados);
}
