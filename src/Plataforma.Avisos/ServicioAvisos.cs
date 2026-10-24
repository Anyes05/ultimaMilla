using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Plataforma.Avisos;

public sealed record EntregaVista(
    Guid Id,
    Guid EventId,
    Guid EnvioId,
    Guid ComercioId,
    string TipoEvento,
    EstadoEntrega Estado,
    int Intentos,
    int? UltimoCodigoHttp,
    string? UltimoError,
    DateTime CreadaEn,
    DateTime ActualizadaEn,
    string Url);

public sealed record ResumenAvisos(int Pendientes, int Entregados, int Fallidos);

/// <summary>
/// Casos de uso de avisos para las pantallas (Backoffice y Portal). Cada consulta recibe el operador
/// (y el comercio en el Portal) desde el usuario autenticado y filtra por ellos: nunca desde el formulario.
/// </summary>
public sealed class ServicioAvisos
{
    private readonly AvisosDbContext _db;
    private readonly IPublicadorAvisos _publicador;
    private readonly OpcionesAvisos _opciones;

    public ServicioAvisos(AvisosDbContext db, IPublicadorAvisos publicador, IOptions<OpcionesAvisos> opciones)
    {
        _db = db;
        _publicador = publicador;
        _opciones = opciones.Value;
    }

    public Task<SuscripcionAviso?> ObtenerSuscripcionAsync(
        Guid operadorId,
        Guid comercioId,
        CancellationToken cancellationToken = default) =>
        _db.Suscripciones.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OperadorId == operadorId && s.ComercioId == comercioId, cancellationToken);

    /// <summary>Crea o actualiza la suscripción del comercio. Secreto vacío al editar = se conserva el actual.</summary>
    public async Task<SuscripcionAviso> GuardarSuscripcionAsync(
        Guid operadorId,
        Guid comercioId,
        string url,
        string? secreto,
        IEnumerable<string> tiposEvento,
        bool activa,
        CancellationToken cancellationToken = default)
    {
        if (!_opciones.UrlPermitida(url?.Trim() ?? string.Empty))
            throw new ArgumentException("La URL debe ser https (http solo para el receptor de prueba configurado).", nameof(url));

        var ahora = DateTime.UtcNow;
        var suscripcion = await _db.Suscripciones
            .FirstOrDefaultAsync(s => s.OperadorId == operadorId && s.ComercioId == comercioId, cancellationToken);

        if (suscripcion is null)
        {
            suscripcion = SuscripcionAviso.Crear(operadorId, comercioId, url!, secreto ?? string.Empty, tiposEvento, activa, ahora);
            _db.Suscripciones.Add(suscripcion);
        }
        else
        {
            var secretoFinal = string.IsNullOrWhiteSpace(secreto) ? suscripcion.Secreto : secreto;
            suscripcion.Actualizar(url!, secretoFinal, tiposEvento, activa, ahora);
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("La suscripción fue modificada por otro usuario. Recargá la página y volvé a intentar.");
        }

        return suscripcion;
    }

    /// <summary>Historial de entregas del comercio (panel del Portal, letra 6.9).</summary>
    public Task<List<EntregaVista>> ListarEntregasComercioAsync(
        Guid operadorId,
        Guid comercioId,
        int maximo = 50,
        CancellationToken cancellationToken = default) =>
        Proyectar(_db.Entregas.Where(e => e.OperadorId == operadorId && e.ComercioId == comercioId), maximo, cancellationToken);

    /// <summary>Entregas del operador, opcionalmente por estado (pantalla de fallidos del Backoffice, letra 6.8).</summary>
    public Task<List<EntregaVista>> ListarEntregasOperadorAsync(
        Guid operadorId,
        EstadoEntrega? estado = null,
        int maximo = 100,
        CancellationToken cancellationToken = default)
    {
        var consulta = _db.Entregas.Where(e => e.OperadorId == operadorId);
        if (estado is not null)
            consulta = consulta.Where(e => e.Estado == estado);
        return Proyectar(consulta, maximo, cancellationToken);
    }

    public async Task<ResumenAvisos> ResumenOperadorAsync(Guid operadorId, CancellationToken cancellationToken = default)
    {
        var conteos = await _db.Entregas.AsNoTracking()
            .Where(e => e.OperadorId == operadorId)
            .GroupBy(e => e.Estado)
            .Select(g => new { Estado = g.Key, Cantidad = g.Count() })
            .ToListAsync(cancellationToken);

        int Cantidad(EstadoEntrega estado) => conteos.FirstOrDefault(c => c.Estado == estado)?.Cantidad ?? 0;
        return new ResumenAvisos(Cantidad(EstadoEntrega.Pendiente), Cantidad(EstadoEntrega.Entregado), Cantidad(EstadoEntrega.Fallido));
    }

    /// <summary>
    /// Reintento manual de un aviso fallido. Reutiliza el mismo EventId (la entrega no cambia):
    /// si el comercio ya lo había recibido, puede descartarlo como duplicado.
    /// comercioId != null restringe además al comercio (Portal).
    /// </summary>
    public async Task ReintentarAsync(
        Guid entregaId,
        Guid operadorId,
        Guid? comercioId = null,
        CancellationToken cancellationToken = default)
    {
        var entrega = await _db.Entregas.FirstOrDefaultAsync(
            e => e.Id == entregaId && e.OperadorId == operadorId && (comercioId == null || e.ComercioId == comercioId),
            cancellationToken)
            ?? throw new InvalidOperationException("El aviso no existe.");

        entrega.ReiniciarParaReintento(DateTime.UtcNow);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("El aviso cambió mientras lo reintentabas. Recargá la página.");
        }

        // Si la publicación falla, la entrega queda Pendiente y el barrido del worker la vuelve a encolar.
        await _publicador.PublicarEntregaAsync(entrega.Id, cancellationToken);
    }

    private Task<List<EntregaVista>> Proyectar(
        IQueryable<EntregaAviso> consulta,
        int maximo,
        CancellationToken cancellationToken) =>
        consulta.AsNoTracking()
            .OrderByDescending(e => e.ActualizadaEn)
            .Take(Math.Clamp(maximo, 1, 500))
            .Join(
                _db.Suscripciones.AsNoTracking(),
                entrega => entrega.SuscripcionId,
                suscripcion => suscripcion.Id,
                (entrega, suscripcion) => new EntregaVista(
                    entrega.Id,
                    entrega.EventId,
                    entrega.EnvioId,
                    entrega.ComercioId,
                    entrega.TipoEvento,
                    entrega.Estado,
                    entrega.Intentos,
                    entrega.UltimoCodigoHttp,
                    entrega.UltimoError,
                    entrega.CreadaEn,
                    entrega.ActualizadaEn,
                    suscripcion.Url))
            .ToListAsync(cancellationToken);
}
