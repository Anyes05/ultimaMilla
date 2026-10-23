using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Vista pública mínima: sin nombre, dirección ni token.</summary>
public sealed record SeguimientoVisible(
    string NumeroSeguimiento,
    EstadoEnvio Estado,
    int IntentosRealizados)
{
    public static SeguimientoVisible Desde(Envio envio) =>
        new(envio.NumeroSeguimiento, envio.Estado, envio.IntentosRealizados);
}
