using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Backoffice.Pages;

/// <summary>
/// Cuadro tarifario del operador: vigente, simulador, publicar versión e historial.
/// </summary>
[Authorize(Roles = PermisosPerfil.Administracion)]
public class TarifasModel : PageModel
{
    private readonly ITarifaRepositorio _tarifas;
    private readonly CalcularTarifa _calcular;
    private readonly GestionTarifas _gestion;
    private readonly IOperadorRepositorio _operadores;
    private readonly GestionCobertura _cobertura;
    private readonly GestionComercios _comercios;

    public const int FilasBonificacion = 5;

    public TarifasModel(
        GestionCobertura cobertura,
        GestionComercios comercios,
        ITarifaRepositorio tarifas,
        CalcularTarifa calcular,
        GestionTarifas gestion,
        IOperadorRepositorio operadores)
    {
        _tarifas = tarifas;
        _calcular = calcular;
        _gestion = gestion;
        _operadores = operadores;
        _cobertura = cobertura;
        _comercios = comercios;
    }

    public Operador? Operador { get; private set; }
    public ReglaTarifaria? Vigente { get; private set; }
    public IReadOnlyList<ZonaCobertura> Zonas { get; private set; } = [];
    public IReadOnlyList<Comercio> Comercios { get; private set; } = [];
    public TarifaVersionVista? VersionVigente => Versiones.FirstOrDefault(version => version.Vigente);
    public ResultadoTarifa? Detalle { get; private set; }
    public IReadOnlyList<TarifaVersionVista> Versiones { get; private set; } = [];
    public decimal? ImporteSimulado { get; private set; }
    public string? Error { get; private set; }
    public string? Mensaje { get; private set; }

    public bool VersionadoDisponible => true;

    public string ColorMarcaCss => Operador?.ColorMarca switch
    {
        "verde" => "#198754",
        "azul" => "#0d6efd",
        _ => "#6f42c1"
    };

    [BindProperty]
    public SimulacionForm Simulacion { get; set; } = new();

    [BindProperty]
    public NuevaVersionForm NuevaVersion { get; set; } = new();

    public async Task OnGetAsync()
    {
        Mensaje = TempData["Mensaje"] as string;
        Error = TempData["Error"] as string;
        await CargarAsync();
    }

    public async Task<IActionResult> OnPostSimularAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return Challenge();

        await CargarAsync();
        if (!ModelState.IsValid)
            return Page();

        try
        {
            Detalle = await _calcular.EjecutarConReferenciaAsync(
                acceso.OperadorId,
                Simulacion.PesoKg,
                Simulacion.DistanciaKm,
                Simulacion.Fragil,
                Simulacion.Urgente,
                HttpContext.RequestAborted,
                Simulacion.VolumenCm3,
                Simulacion.ConZona,
                Simulacion.ZonaId,
                Simulacion.ComercioId);
            ImporteSimulado = Detalle.Importe;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Error = TextosUsuario.Error(ex);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostPublicarAsync()
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
            // La fecha del formulario está en la hora del operador (no la del servidor, que en el contenedor es UTC).
            Operador = await _operadores.ObtenerAsync(acceso.OperadorId, HttpContext.RequestAborted);
            var desde = NuevaVersion.VigenteDesdeLocal == default
                ? DateTime.UtcNow
                : TimeZoneInfo.ConvertTimeToUtc(
                    DateTime.SpecifyKind(NuevaVersion.VigenteDesdeLocal, DateTimeKind.Unspecified), ZonaOperador);

            await _gestion.PublicarVersionAsync(
                acceso.OperadorId,
                new NuevaVersionTarifa(
                    NuevaVersion.PrecioBase,
                    NuevaVersion.PrecioPorKg,
                    NuevaVersion.PrecioPorKm,
                    NuevaVersion.RecargoFragil,
                    NuevaVersion.RecargoUrgente,
                    NuevaVersion.PrecioPorCm3,
                    NuevaVersion.RecargoZona,
                    NuevaVersion.Zonas
                        .Where(zona => zona.Recargo is not null)
                        .Select(zona => new RecargoZonaDato(zona.ZonaId, zona.Recargo!.Value))
                        .ToList(),
                    NuevaVersion.Bonificaciones
                        .Where(bonificacion => !string.IsNullOrWhiteSpace(bonificacion.Nombre) || bonificacion.Porcentaje is not null)
                        .Select(bonificacion => new BonificacionDato(
                            bonificacion.Nombre ?? string.Empty,
                            bonificacion.Porcentaje ?? 0,
                            bonificacion.ComercioId,
                            bonificacion.MinimoEnviosMes))
                        .ToList(),
                    NuevaVersion.PrecioRetiro),
                desde,
                User.Identity?.Name ?? acceso.UsuarioId.ToString(),
                HttpContext.RequestAborted);

            TempData["Mensaje"] = "Nueva versión de tarifa publicada.";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            TempData["Error"] = TextosUsuario.Error(ex);
        }

        return RedirectToPage();
    }

    public static string Importe(decimal valor) =>
        valor.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("es-UY"));

    public string NombreZona(Guid zonaId) => Zonas.FirstOrDefault(zona => zona.Id == zonaId)?.Nombre ?? "Zona eliminada";

    public string NombreComercio(Guid? comercioId) =>
        comercioId is null ? "Todos los comercios" : Comercios.FirstOrDefault(comercio => comercio.Id == comercioId)?.NombreComercial ?? "Comercio";

    public string DescribirZonas(NuevaVersionTarifa valores) =>
        valores.RecargosZona is { Count: > 0 } recargos
            ? string.Join(", ", recargos.Select(recargo => $"{NombreZona(recargo.ZonaId)} +{Importe(recargo.Recargo)}"))
            : "—";

    public string DescribirBonificaciones(NuevaVersionTarifa valores) =>
        valores.Bonificaciones is { Count: > 0 } bonificaciones
            ? string.Join("; ", bonificaciones.Select(DescribirBonificacion))
            : "—";

    public string DescribirBonificacion(BonificacionDato bonificacion) =>
        $"{bonificacion.Nombre}: {bonificacion.Porcentaje:0.##} % ({NombreComercio(bonificacion.ComercioId)}" +
        (bonificacion.MinimoEnviosMes is int minimo ? $", desde el envío {minimo} del mes)" : ")");

    /// <summary>Zona del operador (Operador.ZonaHoraria); si no se encuentra en el sistema, UTC-3 de Uruguay.</summary>
    public TimeZoneInfo ZonaOperador
    {
        get
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(Operador?.ZonaHoraria ?? Operador.ZonaHorariaUruguay);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.CreateCustomTimeZone("UY", TimeSpan.FromHours(-3), "Uruguay", "Uruguay");
            }
        }
    }

    public string HoraOperador(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZonaOperador)
            .ToString("dd/MM/yyyy HH:mm");

    private async Task CargarAsync()
    {
        if (!AccesoActual.Intentar(User, out var acceso))
            return;

        var ct = HttpContext.RequestAborted;
        Operador = await _operadores.ObtenerAsync(acceso.OperadorId, ct);
        Vigente = await _tarifas.ObtenerActivaAsync(acceso.OperadorId, DateTime.UtcNow, ct);
        Versiones = await _gestion.ListarVersionesAsync(acceso.OperadorId, ct);
        Zonas = (await _cobertura.ListarZonasAsync(acceso.OperadorId, ct)).OrderBy(zona => zona.Nombre).ToList();
        Comercios = (await _comercios.ListarAsync(acceso.OperadorId, ct)).OrderBy(comercio => comercio.NombreComercial).ToList();
        var condicionesVigentes = VersionVigente?.Valores;
        var formularioNuevo = NuevaVersion.PrecioBase == 0 && Vigente is not null;

        // Una fila por zona: con un error de validación se conserva lo escrito; si no, arranca con los recargos vigentes.
        var escritos = NuevaVersion.Zonas.ToDictionary(zona => zona.ZonaId, zona => zona.Recargo);
        NuevaVersion.Zonas = Zonas.Select(zona => new ZonaRecargoForm
        {
            ZonaId = zona.Id,
            Recargo = escritos.TryGetValue(zona.Id, out var escrito) && !formularioNuevo
                ? escrito
                : condicionesVigentes?.RecargosZona?.FirstOrDefault(recargo => recargo.ZonaId == zona.Id)?.Recargo
        }).ToList();

        if (formularioNuevo && condicionesVigentes?.Bonificaciones is { Count: > 0 } vigentes)
            NuevaVersion.Bonificaciones = vigentes.Select(b => new BonificacionForm
            {
                Nombre = b.Nombre,
                Porcentaje = b.Porcentaje,
                ComercioId = b.ComercioId,
                MinimoEnviosMes = b.MinimoEnviosMes
            }).ToList();
        while (NuevaVersion.Bonificaciones.Count < FilasBonificacion)
            NuevaVersion.Bonificaciones.Add(new BonificacionForm());

        if (NuevaVersion.VigenteDesdeLocal == default)
            NuevaVersion.VigenteDesdeLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ZonaOperador);

        if (NuevaVersion.PrecioBase == 0 && Vigente is not null)
        {
            NuevaVersion.PrecioBase = Vigente.PrecioBase;
            NuevaVersion.PrecioPorKg = Vigente.PrecioPorKg;
            NuevaVersion.PrecioPorKm = Vigente.PrecioPorKm;
            NuevaVersion.RecargoFragil = Vigente.RecargoFragil;
            NuevaVersion.RecargoUrgente = Vigente.RecargoUrgente;
            NuevaVersion.PrecioPorCm3 = Vigente.PrecioPorCm3;
            NuevaVersion.RecargoZona = Vigente.RecargoZona;
            NuevaVersion.PrecioRetiro = Vigente.PrecioRetiro;
        }
    }

    public sealed class SimulacionForm
    {
        [Range(typeof(decimal), "0", "1000", ErrorMessage = "El peso debe estar entre 0 y 1000 kg.")]
        public decimal PesoKg { get; set; } = 1;

        [Range(typeof(decimal), "0", "500", ErrorMessage = "La distancia debe estar entre 0 y 500 km.")]
        public decimal DistanciaKm { get; set; } = 5;

        [Range(typeof(decimal), "0", "10000000", ErrorMessage = "El volumen debe estar entre 0 y 10.000.000 cm³.")]
        public decimal VolumenCm3 { get; set; }

        public bool ConZona { get; set; }

        public Guid? ZonaId { get; set; }

        public Guid? ComercioId { get; set; }

        public bool Urgente { get; set; }

        public bool Fragil { get; set; }
    }

    public sealed class NuevaVersionForm
    {
        [Range(typeof(decimal), "0", "1000000")]
        public decimal PrecioBase { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal PrecioPorKg { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal PrecioPorKm { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal RecargoFragil { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal RecargoUrgente { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal PrecioPorCm3 { get; set; }

        [Range(typeof(decimal), "0", "1000000")]
        public decimal RecargoZona { get; set; }

        /// <summary>Precio de cada retiro en domicilio (RF 41).</summary>
        [Range(typeof(decimal), "0", "1000000")]
        public decimal PrecioRetiro { get; set; }

        /// <summary>Hora del operador; vacía = ahora. Se completa al cargar la página.</summary>
        public DateTime VigenteDesdeLocal { get; set; }

        /// <summary>Recargo propio por zona; vacío = usa el recargo general de zona.</summary>
        public List<ZonaRecargoForm> Zonas { get; set; } = [];

        public List<BonificacionForm> Bonificaciones { get; set; } = [];
    }

    public sealed class ZonaRecargoForm
    {
        public Guid ZonaId { get; set; }

        [Range(typeof(decimal), "0", "1000000", ErrorMessage = "El recargo de una zona debe estar entre 0 y 1.000.000.")]
        public decimal? Recargo { get; set; }
    }

    public sealed class BonificacionForm
    {
        [StringLength(80, ErrorMessage = "El nombre de la bonificación es demasiado largo.")]
        public string? Nombre { get; set; }

        [Range(typeof(decimal), "0.01", "100", ErrorMessage = "El porcentaje debe ser mayor que 0 y de hasta 100.")]
        public decimal? Porcentaje { get; set; }

        public Guid? ComercioId { get; set; }

        [Range(1, 100000, ErrorMessage = "El mínimo de envíos en el mes debe ser al menos 1.")]
        public int? MinimoEnviosMes { get; set; }
    }
}
