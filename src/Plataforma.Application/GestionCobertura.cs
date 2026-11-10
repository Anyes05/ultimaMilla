using Plataforma.Domain;

namespace Plataforma.Application;

public interface ICoberturaRepositorio
{
    Task AgregarZonaAsync(ZonaCobertura zona, CancellationToken cancellationToken = default);
    Task AgregarFranjaAsync(FranjaHoraria franja, CancellationToken cancellationToken = default);
    Task AgregarVehiculoAsync(Vehiculo vehiculo, CancellationToken cancellationToken = default);
    Task<ZonaCobertura?> ObtenerZonaAsync(Guid operadorId, Guid zonaId, CancellationToken cancellationToken = default);
    Task<FranjaHoraria?> ObtenerFranjaAsync(Guid operadorId, Guid franjaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ZonaCobertura>> ListarZonasAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FranjaHoraria>> ListarFranjasAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Vehiculo>> ListarVehiculosAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FranjaHoraria>> ListarTodasLasFranjasAsync(Guid operadorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Vehiculo>> ListarTodosLosVehiculosAsync(Guid operadorId, CancellationToken cancellationToken = default);
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

    public async Task ActualizarZonaAsync(
        Guid operadorId,
        Guid zonaId,
        string nombre,
        string poligono,
        CancellationToken cancellationToken = default)
    {
        var zona = await _repo.ObtenerZonaAsync(operadorId, zonaId, cancellationToken)
            ?? throw new InvalidOperationException("La zona no existe para ese operador.");
        zona.Actualizar(nombre, poligono);
        await _repo.GuardarAsync(cancellationToken);
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

    public async Task ActualizarFranjaAsync(
        Guid operadorId,
        Guid franjaId,
        string nombre,
        TimeOnly desde,
        TimeOnly hasta,
        Guid? zonaId = null,
        bool? activa = null,
        CancellationToken cancellationToken = default)
    {
        var franja = await _repo.ObtenerFranjaAsync(operadorId, franjaId, cancellationToken)
            ?? throw new InvalidOperationException("La franja no existe para ese operador.");
        franja.Actualizar(nombre, desde, hasta, zonaId);
        if (activa == false)
            franja.Desactivar();
        else if (activa == true)
            franja.Activar();
        await _repo.GuardarAsync(cancellationToken);
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

    public async Task ActualizarVehiculoAsync(
        Guid operadorId,
        Guid vehiculoId,
        decimal capacidadPesoKg,
        decimal capacidadVolumenCm3,
        int maximoParadas,
        bool? activo = null,
        CancellationToken cancellationToken = default)
    {
        var vehiculo = await _repo.ObtenerVehiculoAsync(operadorId, vehiculoId, cancellationToken)
            ?? throw new InvalidOperationException("El vehículo no existe para ese operador.");
        vehiculo.ActualizarCapacidad(capacidadPesoKg, capacidadVolumenCm3, maximoParadas);
        if (activo == false)
            vehiculo.Desactivar();
        else if (activo == true)
            vehiculo.Activar();
        await _repo.GuardarAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ZonaCobertura>> ListarZonasAsync(Guid operadorId, CancellationToken ct = default) =>
        _repo.ListarZonasAsync(operadorId, ct);

    public Task<IReadOnlyList<FranjaHoraria>> ListarFranjasAsync(Guid operadorId, CancellationToken ct = default) =>
        _repo.ListarFranjasAsync(operadorId, ct);

    public Task<IReadOnlyList<Vehiculo>> ListarVehiculosAsync(Guid operadorId, CancellationToken ct = default) =>
        _repo.ListarVehiculosAsync(operadorId, ct);

    /// <summary>Incluye las inactivas: la pantalla de administración tiene que poder reactivarlas.</summary>
    public Task<IReadOnlyList<FranjaHoraria>> ListarTodasLasFranjasAsync(Guid operadorId, CancellationToken ct = default) =>
        _repo.ListarTodasLasFranjasAsync(operadorId, ct);

    /// <summary>Incluye los inactivos: la pantalla de administración tiene que poder reactivarlos.</summary>
    public Task<IReadOnlyList<Vehiculo>> ListarTodosLosVehiculosAsync(Guid operadorId, CancellationToken ct = default) =>
        _repo.ListarTodosLosVehiculosAsync(operadorId, ct);
}
