using System.Globalization;
using System.Text;
using Plataforma.Application;

namespace Plataforma.PortalComercio;

/// <summary>Una fila del archivo lista para importar, o el error que impide importarla.</summary>
public sealed record FilaCsv(int Linea, string Clave, EnvioImportacionItem? Item, string? Error);

/// <summary>
/// Lee la planilla CSV de importación de envíos (RF 7). Acepta punto y coma o coma como separador, comillas,
/// coma decimal con punto y coma, encabezados con o sin tildes, y zona y franja por nombre.
/// </summary>
public static class ImportacionCsv
{
    public const int MaximoFilas = 500;
    public static readonly string[] Obligatorias = ["clave_externa", "destinatario", "direccion", "peso_kg"];

    /// <exception cref="FormatException">Si el archivo no tiene encabezado válido o supera el máximo de filas.</exception>
    public static IReadOnlyList<FilaCsv> Leer(
        string texto,
        IReadOnlyDictionary<string, Guid> zonasPorNombre,
        IReadOnlyDictionary<string, Guid> franjasPorNombre)
    {
        var lineas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Select((contenido, indice) => (Contenido: contenido, Numero: indice + 1))
            .Where(linea => !string.IsNullOrWhiteSpace(linea.Contenido))
            .ToList();
        if (lineas.Count < 2)
            throw new FormatException("El archivo necesita el encabezado y al menos una fila.");
        if (lineas.Count - 1 > MaximoFilas)
            throw new FormatException($"El archivo tiene {lineas.Count - 1} filas; el máximo es {MaximoFilas}.");

        var primera = lineas[0].Contenido;
        var separador = primera.Count(c => c == ';') >= primera.Count(c => c == ',') ? ';' : ',';
        var encabezado = Partir(primera, separador).Select(Normalizar).ToList();
        var faltan = Obligatorias.Where(columna => !encabezado.Contains(columna)).ToList();
        if (faltan.Count > 0)
            throw new FormatException("Faltan columnas obligatorias: " + string.Join(", ", faltan) + ".");

        var zonas = zonasPorNombre.ToDictionary(par => Normalizar(par.Key), par => par.Value);
        var franjas = franjasPorNombre.ToDictionary(par => Normalizar(par.Key), par => par.Value);
        var claves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filas = new List<FilaCsv>();

        foreach (var (contenido, numero) in lineas.Skip(1))
        {
            var valores = Partir(contenido, separador);
            string Valor(string columna)
            {
                var indice = encabezado.IndexOf(columna);
                return indice >= 0 && indice < valores.Count ? valores[indice].Trim() : string.Empty;
            }

            var clave = Valor("clave_externa");
            try
            {
                if (clave.Length == 0)
                    throw new FormatException("Falta la clave externa.");
                if (!claves.Add(clave))
                    throw new FormatException("La clave está repetida en el archivo.");

                Guid? zonaId = null;
                var zona = Valor("zona");
                if (zona.Length > 0)
                    zonaId = zonas.TryGetValue(Normalizar(zona), out var id) ? id : throw new FormatException($"No existe la zona \"{zona}\".");

                Guid? franjaId = null;
                var franja = Valor("franja");
                if (franja.Length > 0)
                    franjaId = franjas.TryGetValue(Normalizar(franja), out var id) ? id : throw new FormatException($"No existe la franja \"{franja}\".");

                var bultos = Numero(Valor("bultos"), "bultos", separador) ?? 1;
                if (bultos != decimal.Truncate(bultos))
                    throw new FormatException("La cantidad de bultos tiene que ser un número entero.");

                filas.Add(new FilaCsv(numero, clave, new EnvioImportacionItem(
                    clave,
                    Valor("destinatario"),
                    Valor("direccion"),
                    Valor("email"),
                    Numero(Valor("peso_kg"), "peso_kg", separador) ?? 0,
                    Numero(Valor("distancia_km"), "distancia_km", separador) ?? 0,
                    SiNo(Valor("fragil")),
                    SiNo(Valor("urgente")),
                    (int)bultos,
                    zonaId,
                    franjaId,
                    Numero(Valor("volumen_cm3"), "volumen_cm3", separador) ?? 0), null));
            }
            catch (FormatException ex)
            {
                filas.Add(new FilaCsv(numero, clave, null, ex.Message));
            }
        }

        return filas;
    }

    internal static List<string> Partir(string linea, char separador)
    {
        const char Comilla = '"';
        var campos = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;
        for (var i = 0; i < linea.Length; i++)
        {
            var c = linea[i];
            if (c == Comilla)
            {
                if (entreComillas && i + 1 < linea.Length && linea[i + 1] == Comilla)
                {
                    actual.Append(Comilla);
                    i++;
                }
                else
                {
                    entreComillas = !entreComillas;
                }
            }
            else if (c == separador && !entreComillas)
            {
                campos.Add(actual.ToString());
                actual.Clear();
            }
            else
            {
                actual.Append(c);
            }
        }

        campos.Add(actual.ToString());
        return campos;
    }

    /// <summary>Minúsculas, sin tildes y con guion bajo en lugar de espacios.</summary>
    internal static string Normalizar(string texto)
    {
        var descompuesto = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(descompuesto.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Replace(' ', '_');
    }

    private static decimal? Numero(string valor, string columna, char separador)
    {
        if (valor.Length == 0)
            return null;
        // Con punto y coma, la coma es decimal (planillas en español).
        var normal = separador == ';' ? valor.Replace(',', '.') : valor;
        return decimal.TryParse(normal, NumberStyles.Number, CultureInfo.InvariantCulture, out var numero)
            ? numero
            : throw new FormatException($"El valor \"{valor}\" de {columna} no es un número.");
    }

    private static bool SiNo(string valor) =>
        Normalizar(valor) is "si" or "s" or "true" or "1" or "x" or "yes";
}
