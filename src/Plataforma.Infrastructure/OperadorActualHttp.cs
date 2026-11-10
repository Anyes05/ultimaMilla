using Microsoft.AspNetCore.Http;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

public sealed class OperadorActualHttp : IOperadorActual
{
    private readonly IHttpContextAccessor _http;

    public OperadorActualHttp(IHttpContextAccessor http)
    {
        _http = http;
    }

    public Guid? OperadorId
    {
        get
        {
            var principal = _http.HttpContext?.User;
            if (principal is null || !AccesoActual.Intentar(principal, out var acceso))
                return null;

            // El administrador de plataforma no pertenece a un operador: sin filtro, no filtrar por Guid vacío.
            return acceso.EsAdministradorPlataforma ? null : acceso.OperadorId;
        }
    }
}

public sealed class OperadorActualFijo : IOperadorActual
{
    public OperadorActualFijo(Guid? operadorId)
    {
        OperadorId = operadorId;
    }

    public Guid? OperadorId { get; }
}
