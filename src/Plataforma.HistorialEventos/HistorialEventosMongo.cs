using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Plataforma.HistorialEventos;

public sealed class OpcionesMongo
{
    public const string Seccion = "Mongo";
    public string ConnectionString { get; set; } = string.Empty;
    public string BaseDatos { get; set; } = "tallernet";
    public string Coleccion { get; set; } = "eventos_envio";
    public int RetencionDias { get; set; } = 180;
    public bool Configurado => !string.IsNullOrWhiteSpace(ConnectionString);
}

public sealed class MongoNoConfiguradoException : InvalidOperationException
{
    public MongoNoConfiguradoException() : base("Mongo:ConnectionString no está configurado.") { }
}

public sealed record EventoHistorialMongo(
    [property: BsonId] string Id,
    [property: BsonElement("operadorId")] Guid OperadorId,
    [property: BsonElement("envioId")] Guid EnvioId,
    [property: BsonElement("tipo")] string Tipo,
    [property: BsonElement("version")] int Version,
    [property: BsonElement("estadoAnterior")] string EstadoAnterior,
    [property: BsonElement("estadoNuevo")] string EstadoNuevo,
    [property: BsonElement("fecha")] DateTime Fecha,
    [property: BsonElement("registradoEn")] DateTime RegistradoEn,
    [property: BsonElement("traceId")] string? TraceId);

public sealed record ConsultaHistorialEventos(Guid OperadorId, Guid? EnvioId, string? Estado,
    DateTimeOffset? Desde, DateTimeOffset? Hasta, string? DespuesDe, int Tamano);

public sealed record PaginaHistorialEventos(IReadOnlyList<EventoHistorialMongo> Items, string? Siguiente);

public sealed class RepositorioHistorialMongo
{
    private readonly OpcionesMongo _opciones;
    private readonly IMongoCollection<EventoHistorialMongo>? _eventos;

    public RepositorioHistorialMongo(IOptions<OpcionesMongo> opciones)
    {
        _opciones = opciones.Value;
        if (!_opciones.Configurado) return;
        var cliente = new MongoClient(_opciones.ConnectionString);
        _eventos = cliente.GetDatabase(_opciones.BaseDatos).GetCollection<EventoHistorialMongo>(_opciones.Coleccion);
    }

    public bool Configurado => _eventos is not null;

    public async Task AsegurarIndicesAsync(CancellationToken cancellationToken = default)
    {
        var ttl = TimeSpan.FromDays(Math.Max(1, _opciones.RetencionDias));
        var modelos = new CreateIndexModel<EventoHistorialMongo>[]
        {
            new(Builders<EventoHistorialMongo>.IndexKeys.Ascending(item => item.OperadorId).Ascending(item => item.EnvioId).Ascending(item => item.Fecha)),
            new(Builders<EventoHistorialMongo>.IndexKeys.Ascending(item => item.OperadorId).Descending(item => item.Fecha).Descending(item => item.Id)),
            new(Builders<EventoHistorialMongo>.IndexKeys.Ascending(item => item.OperadorId).Ascending(item => item.EstadoNuevo).Descending(item => item.Fecha)),
            new(Builders<EventoHistorialMongo>.IndexKeys.Ascending(item => item.RegistradoEn), new CreateIndexOptions { ExpireAfter = ttl })
        };
        await ObtenerColeccion().Indexes.CreateManyAsync(modelos, cancellationToken);
    }

    public async Task<bool> InsertarAsync(EventoHistorialMongo evento, CancellationToken cancellationToken = default)
    {
        try
        {
            await ObtenerColeccion().InsertOneAsync(evento, cancellationToken: cancellationToken);
            return true;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<PaginaHistorialEventos> ConsultarAsync(ConsultaHistorialEventos consulta, CancellationToken cancellationToken = default)
    {
        var filtro = Builders<EventoHistorialMongo>.Filter.Eq(item => item.OperadorId, consulta.OperadorId);
        if (consulta.EnvioId is not null) filtro &= Builders<EventoHistorialMongo>.Filter.Eq(item => item.EnvioId, consulta.EnvioId.Value);
        if (!string.IsNullOrWhiteSpace(consulta.Estado)) filtro &= Builders<EventoHistorialMongo>.Filter.Eq(item => item.EstadoNuevo, consulta.Estado);
        if (consulta.Desde is not null) filtro &= Builders<EventoHistorialMongo>.Filter.Gte(item => item.Fecha, consulta.Desde.Value.UtcDateTime);
        if (consulta.Hasta is not null) filtro &= Builders<EventoHistorialMongo>.Filter.Lte(item => item.Fecha, consulta.Hasta.Value.UtcDateTime);
        if (!string.IsNullOrWhiteSpace(consulta.DespuesDe))
        {
            var cursor = CursorHistorial.Decodificar(consulta.DespuesDe);
            filtro &= Builders<EventoHistorialMongo>.Filter.Or(
                Builders<EventoHistorialMongo>.Filter.Lt(item => item.Fecha, cursor.Fecha.UtcDateTime),
                Builders<EventoHistorialMongo>.Filter.And(Builders<EventoHistorialMongo>.Filter.Eq(item => item.Fecha, cursor.Fecha.UtcDateTime),
                    Builders<EventoHistorialMongo>.Filter.Lt(item => item.Id, cursor.Id)));
        }
        var limite = Math.Clamp(consulta.Tamano, 1, 200);
        var items = await ObtenerColeccion().Find(filtro).SortByDescending(item => item.Fecha).ThenByDescending(item => item.Id)
            .Limit(limite + 1).ToListAsync(cancellationToken);
        var haySiguiente = items.Count > limite;
        if (haySiguiente) items.RemoveAt(limite);
        var ultimo = items.LastOrDefault();
        return new PaginaHistorialEventos(items, haySiguiente && ultimo is not null ? CursorHistorial.Codificar(ultimo.Fecha, ultimo.Id) : null);
    }

    private IMongoCollection<EventoHistorialMongo> ObtenerColeccion() => _eventos ?? throw new MongoNoConfiguradoException();
}

public static class RegistroHistorialMongo
{
    public static IServiceCollection AddHistorialMongo(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OpcionesMongo>(configuration.GetSection(OpcionesMongo.Seccion));
        services.AddSingleton<RepositorioHistorialMongo>();
        return services;
    }
}

public static class CursorHistorial
{
    public static string Codificar(DateTime fecha, string id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{fecha:O}|{id}"))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static (DateTimeOffset Fecha, string Id) Decodificar(string cursor)
    {
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
            var partes = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|', 2);
            if (partes.Length != 2 || !DateTimeOffset.TryParse(partes[0], out var fecha) || !Guid.TryParse(partes[1], out _)) throw new FormatException();
            return (fecha, partes[1]);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new ArgumentException("El cursor despuesDe es inválido.", nameof(cursor), exception);
        }
    }
}