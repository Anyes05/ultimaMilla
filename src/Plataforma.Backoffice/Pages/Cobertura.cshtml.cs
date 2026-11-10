using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>Zonas, franjas horarias y vehículos del operador (RF 2 y 6).</summary>
[Authorize(Roles = PermisosPerfil.Administracion)]
public class CoberturaModel : PageModel
{
    private readonly GestionCobertura _gestion;
    private readonly IOperadorRepositorio _operadores;

    public CoberturaModel(GestionCobertura gestion, IOperadorRepositorio operadores)
    {
        _gestion = gestion;
        _operadores = operadores;
    }

    public Operador? Operador { get; private set; }
    public IReadOnlyList<ZonaCobertura> Zonas { get; private set; } = [];
    public IReadOnlyList<FranjaHoraria> Franjas { get; private set; } = [];
    public IReadOnlyList<Vehiculo> Vehiculos { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    [BindProperty(SupportsGet = true)]
    public Guid? EditarZona { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? EditarFranja { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? EditarVehiculo { get; set; }

    // Cada formulario llega como parámetro de su handler: así un envío no valida los otros dos.
    public ZonaForm FormZona { get; private set; } = new();
    public FranjaForm FormFranja { get; private set; } = new();
    public VehiculoForm FormVehiculo { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        await CargarAsync();

        if (Zonas.FirstOrDefault(zona => zona.Id == EditarZona) is { } zonaEditada)
            FormZona = new ZonaForm { Nombre = zonaEditada.Nombre, Poligono = zonaEditada.Poligono };

        if (Franjas.FirstOrDefault(franja => franja.Id == EditarFranja) is { } franjaEditada)
        {
            FormFranja = new FranjaForm
            {
                Nombre = franjaEditada.Nombre,
                Desde = franjaEditada.Desde,
                Hasta = franjaEditada.Hasta,
                ZonaId = franjaEditada.ZonaId
            };
        }

        if (Vehiculos.FirstOrDefault(vehiculo => vehiculo.Id == EditarVehiculo) is { } vehiculoEditado)
        {
            FormVehiculo = new VehiculoForm
            {
                Patente = vehiculoEditado.Patente,
                Descripcion = vehiculoEditado.Descripcion,
                CapacidadPesoKg = vehiculoEditado.CapacidadPesoKg,
                CapacidadVolumenCm3 = vehiculoEditado.CapacidadVolumenCm3,
                MaximoParadas = vehiculoEditado.MaximoParadas
            };
        }
    }

    public Task<IActionResult> OnPostGuardarZonaAsync([Bind(Prefix = nameof(FormZona))] ZonaForm formZona)
    {
        FormZona = formZona;
        return GuardarAsync(async (acceso, ct) =>
        {
            if (EditarZona is Guid id)
                await _gestion.ActualizarZonaAsync(acceso.OperadorId, id, formZona.Nombre, formZona.Poligono ?? string.Empty, ct);
            else
                await _gestion.DefinirZonaAsync(acceso.OperadorId, formZona.Nombre, formZona.Poligono ?? string.Empty, ct);
        }, EditarZona is null ? "Zona creada." : "Zona actualizada.");
    }

    public Task<IActionResult> OnPostGuardarFranjaAsync([Bind(Prefix = nameof(FormFranja))] FranjaForm formFranja)
    {
        FormFranja = formFranja;
        return GuardarAsync(async (acceso, ct) =>
        {
            if (EditarFranja is Guid id)
                await _gestion.ActualizarFranjaAsync(
                    acceso.OperadorId, id, formFranja.Nombre, formFranja.Desde!.Value, formFranja.Hasta!.Value, formFranja.ZonaId, null, ct);
            else
                await _gestion.DefinirFranjaAsync(
                    acceso.OperadorId, formFranja.Nombre, formFranja.Desde!.Value, formFranja.Hasta!.Value, formFranja.ZonaId, ct);
        }, EditarFranja is null ? "Franja creada." : "Franja actualizada.");
    }

    public Task<IActionResult> OnPostCambiarFranjaAsync(Guid franjaId, bool activa) =>
        GuardarAsync(async (acceso, ct) =>
        {
            var franja = (await _gestion.ListarTodasLasFranjasAsync(acceso.OperadorId, ct)).FirstOrDefault(item => item.Id == franjaId)
                ?? throw new InvalidOperationException("La franja no existe para ese operador.");
            await _gestion.ActualizarFranjaAsync(acceso.OperadorId, franjaId, franja.Nombre, franja.Desde, franja.Hasta, franja.ZonaId, activa, ct);
        }, activa ? "Franja activada." : "Franja desactivada.");

    public Task<IActionResult> OnPostGuardarVehiculoAsync([Bind(Prefix = nameof(FormVehiculo))] VehiculoForm formVehiculo)
    {
        FormVehiculo = formVehiculo;
        return GuardarAsync(async (acceso, ct) =>
        {
            if (EditarVehiculo is Guid id)
            {
                await _gestion.ActualizarVehiculoAsync(
                    acceso.OperadorId, id, formVehiculo.CapacidadPesoKg, formVehiculo.CapacidadVolumenCm3, formVehiculo.MaximoParadas, null, ct);
                return;
            }

            var patente = formVehiculo.Patente.Trim();
            var existentes = await _gestion.ListarTodosLosVehiculosAsync(acceso.OperadorId, ct);
            if (existentes.Any(vehiculo => string.Equals(vehiculo.Patente, patente, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Ya hay un vehículo con esa patente.");

            await _gestion.RegistrarVehiculoAsync(
                acceso.OperadorId, patente, formVehiculo.Descripcion ?? string.Empty,
                formVehiculo.CapacidadPesoKg, formVehiculo.CapacidadVolumenCm3, formVehiculo.MaximoParadas, ct);
        }, EditarVehiculo is null ? "Vehículo creado." : "Vehículo actualizado.");
    }

    public Task<IActionResult> OnPostCambiarVehiculoAsync(Guid vehiculoId, bool activo) =>
        GuardarAsync(async (acceso, ct) =>
        {
            var vehiculo = (await _gestion.ListarTodosLosVehiculosAsync(acceso.OperadorId, ct)).FirstOrDefault(item => item.Id == vehiculoId)
                ?? throw new InvalidOperationException("El vehículo no existe para ese operador.");
            await _gestion.ActualizarVehiculoAsync(
                acceso.OperadorId, vehiculoId, vehiculo.CapacidadPesoKg, vehiculo.CapacidadVolumenCm3, vehiculo.MaximoParadas, activo, ct);
        }, activo ? "Vehículo activado." : "Vehículo desactivado.");

    public string NombreZona(Guid? zonaId) =>
        Zonas.FirstOrDefault(zona => zona.Id == zonaId)?.Nombre ?? "Todas";

    private async Task<IActionResult> GuardarAsync(Func<AccesoActual, CancellationToken, Task> accion, string mensaje)
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        if (!ModelState.IsValid)
        {
            await CargarAsync();
            return Page();
        }

        try
        {
            await accion(acceso, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = TextosUsuario.Error(ex);
            await CargarAsync();
            return Page();
        }

        TempData["Mensaje"] = mensaje;
        // PRG: recargar no vuelve a guardar.
        return RedirectToPage();
    }

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Zonas = (await _gestion.ListarZonasAsync(acceso.OperadorId, ct)).OrderBy(zona => zona.Nombre).ToList();
        Franjas = (await _gestion.ListarTodasLasFranjasAsync(acceso.OperadorId, ct)).OrderBy(franja => franja.Desde).ToList();
        Vehiculos = (await _gestion.ListarTodosLosVehiculosAsync(acceso.OperadorId, ct)).OrderBy(vehiculo => vehiculo.Patente).ToList();
    }

    public sealed class ZonaForm
    {
        [Required(ErrorMessage = "El nombre de la zona es requerido.")]
        [StringLength(120, ErrorMessage = "El nombre de la zona es demasiado largo.")]
        public string Nombre { get; set; } = string.Empty;

        public string? Poligono { get; set; }
    }

    public sealed class FranjaForm
    {
        [Required(ErrorMessage = "El nombre de la franja es requerido.")]
        [StringLength(120, ErrorMessage = "El nombre de la franja es demasiado largo.")]
        public string Nombre { get; set; } = string.Empty;

        [Required(ErrorMessage = "Indicá la hora de inicio.")]
        public TimeOnly? Desde { get; set; }

        [Required(ErrorMessage = "Indicá la hora de fin.")]
        public TimeOnly? Hasta { get; set; }

        public Guid? ZonaId { get; set; }
    }

    public sealed class VehiculoForm
    {
        [Required(ErrorMessage = "La patente es requerida.")]
        [StringLength(20, ErrorMessage = "La patente es demasiado larga.")]
        public string Patente { get; set; } = string.Empty;

        [StringLength(200, ErrorMessage = "La descripción es demasiado larga.")]
        public string? Descripcion { get; set; }

        [Range(typeof(decimal), "0", "1000000", ErrorMessage = "La capacidad de peso no es válida.")]
        public decimal CapacidadPesoKg { get; set; }

        [Range(typeof(decimal), "0", "1000000000", ErrorMessage = "La capacidad de volumen no es válida.")]
        public decimal CapacidadVolumenCm3 { get; set; }

        [Range(0, 1000, ErrorMessage = "El máximo de paradas no es válido.")]
        public int MaximoParadas { get; set; }
    }
}
