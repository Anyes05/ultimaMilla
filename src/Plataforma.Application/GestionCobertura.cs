using Plataforma.Domain;

namespace Plataforma.Application;

public interface ICoberturaRepositorio
{
    Task AgregarZonaAsync(ZonaCobertura zona, CancellationToken cancellationToken = default);
    Task AgregarFranjaAsync(FranjaHoraria franja, CancellationToken cancellationToken = default);
    Task AgregarVehiculoAsync(Vehiculo vehiculo, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ZonaCobertura>> ListarZonasAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FranjaHoraria>> ListarFranjasAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Vehiculo>> ListarVehiculosAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task<Vehiculo?> ObtenerVehiculoAsync(Guid operadorId, Guid vehiculoId, CancellationToken cancellationToken = default);
    Task GuardarAsync(CancellationToken cancellationToken = default);
}

public sealed class GestionCobertura
{
    private readonly ICoberturaRepositorio _repo;

    public GestionCobertura(ICoberturaRepositorio repo) => _repo = repo;

    public async Task<ZonaCobertura> DefinirZonaAsync(
        Guid operadorId,
        string nombre,
        string poligono,
        CancellationToken cancellationToken = default)
    {
        var zona = ZonaCobertura.Definir(Guid.NewGuid(), operadorId, nombre, poligono);
        await _repo.AgregarZonaAsync(zona, cancellationToken);
        await _repo.GuardarAsync(cancellationToken);
        return zona;
    }

    public async Task<FranjaHoraria> DefinirFranjaAsync(
        Guid operadorId,
        string nombre,
        TimeOnly desde,
        TimeOnly hasta,
        Guid? zonaId = null,
        CancellationToken cancellationToken = default)
    {
        var franja = FranjaHoraria.Definir(Guid.NewGuid(), operadorId, nombre, desde, hasta, zonaId);
        await _repo.AgregarFranjaAsync(franja, cancellationToken);
        await _repo.GuardarAsync(cancellationToken);
        return franja;
    }

    public async Task<Vehiculo> RegistrarVehiculoAsync(
        Guid operadorId,
        string patente,
        string descripcion,
        decimal capacidadPesoKg,
        decimal capacidadVolumenCm3,
        int maximoParadas,
        CancellationToken cancellationToken = default)
    {
        var vehiculo = Vehiculo.Registrar(
            Guid.NewGuid(), operadorId, patente, descripcion,
            capacidadPesoKg, capacidadVolumenCm3, maximoParadas);
        await _repo.AgregarVehiculoAsync(vehiculo, cancellationToken);
        await _repo.GuardarAsync(cancellationToken);
        return vehiculo;
    }

    public Task<IReadOnlyList<ZonaCobertura>> ListarZonasAsync(Guid operadorId, CancellationToken ct = default) =>
        _repo.ListarZonasAsync(operadorId, ct);

    public Task<IReadOnlyList<FranjaHoraria>> ListarFranjasAsync(Guid operadorId, CancellationToken ct = default) =>
        _repo.ListarFranjasAsync(operadorId, ct);

    public Task<IReadOnlyList<Vehiculo>> ListarVehiculosAsync(Guid operadorId, CancellationToken ct = default) =>
        _repo.ListarVehiculosAsync(operadorId, ct);
}