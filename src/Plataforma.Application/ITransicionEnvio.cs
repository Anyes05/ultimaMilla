using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>
/// Caso de uso para transicionar un envío dentro del operador autenticado.
/// Nadia lo consume desde el backoffice; no elige OperadorId en el formulario.
/// </summary>
public interface ITransicionEnvio
{
    Task<ResultadoTransicionEnvio> EjecutarAsync(
        Guid envioId,
        Guid operadorId,
        EstadoEnvio nuevoEstado,
        string? usuario = null,
        string? motivo = null,
        CancellationToken cancellationToken = default);

    IReadOnlyList<EstadoEnvio> DestinosDesde(EstadoEnvio estadoActual);
}

public sealed record ResultadoTransicionEnvio(
    Guid EnvioId,
    EstadoEnvio EstadoAnterior,
    EstadoEnvio EstadoNuevo,
    Guid? EventId);
