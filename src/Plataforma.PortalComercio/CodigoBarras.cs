using System.Globalization;
using System.Text;

namespace Plataforma.PortalComercio;

/// <summary>
/// Código de barras Code 128 (juego B) dibujado como SVG, para las etiquetas de bultos. El lector del depósito
/// (recepción y rendición) "teclea" el código; no hace falta ninguna librería ni servicio externo.
/// </summary>
public static class CodigoBarras
{
    // Anchos de barra y espacio de cada símbolo (0 a 105) y del cierre (106), según la norma ISO/IEC 15417.
    private static readonly string[] Patrones =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    ];

    private const int InicioB = 104;
    private const int Cierre = 106;
    private const int ZonaSilencio = 10;

    /// <summary>SVG con el código; acepta caracteres ASCII imprimibles (32 a 126).</summary>
    public static string Svg(string texto, int alto = 60)
    {
        if (string.IsNullOrEmpty(texto) || texto.Any(c => c < 32 || c > 126))
            throw new ArgumentException("El código de barras solo admite caracteres ASCII imprimibles.", nameof(texto));

        var simbolos = new List<int> { InicioB };
        var suma = InicioB;
        for (var i = 0; i < texto.Length; i++)
        {
            var valor = texto[i] - 32;
            simbolos.Add(valor);
            suma += valor * (i + 1);
        }

        simbolos.Add(suma % 103);
        simbolos.Add(Cierre);

        var barras = new StringBuilder();
        var x = ZonaSilencio;
        foreach (var simbolo in simbolos)
        {
            var patron = Patrones[simbolo];
            for (var i = 0; i < patron.Length; i++)
            {
                var ancho = patron[i] - '0';
                if (i % 2 == 0)
                    barras.Append(CultureInfo.InvariantCulture, $"<rect x=\"{x}\" y=\"0\" width=\"{ancho}\" height=\"{alto}\"/>");
                x += ancho;
            }
        }

        var total = x + ZonaSilencio;
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {total} {alto}\" preserveAspectRatio=\"none\" " +
               $"role=\"img\" aria-label=\"Código de barras {System.Net.WebUtility.HtmlEncode(texto)}\" shape-rendering=\"crispEdges\">" +
               $"<rect width=\"{total}\" height=\"{alto}\" fill=\"#fff\"/><g fill=\"#000\">{barras}</g></svg>";
    }
}
