using System.Net;

namespace Plataforma.Avisos;

public sealed record DatosNotificacion(
    string Operador,
    string ColorMarca,
    string NumeroSeguimiento,
    string TokenSeguimiento,
    string EstadoNuevo);

public sealed record MensajeCorreo(string Asunto, string Texto, string Html);

/// <summary>Correo al destinatario (RF 27), con la marca del operador (RF 5) y el enlace público de seguimiento.</summary>
public static class PlantillaNotificacion
{
    // Estados que le importan a quien espera el paquete. AsignadoARuta es interno y Admitido no es un cambio de estado.
    private static readonly IReadOnlyDictionary<string, (string Asunto, string Detalle)> Textos =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["EnDeposito"] = ("Recibimos tu envío", "Tu envío está en nuestro depósito, preparándose para salir."),
            ["EnTransito"] = ("Tu envío está en camino", "Tu envío salió a reparto."),
            ["Entregado"] = ("Tu envío fue entregado", "Tu envío fue entregado."),
            ["EntregadoParcialmente"] = ("Entregamos parte de tu envío", "Entregamos parte de tu envío. El resto se entrega en otra visita o vuelve al comercio."),
            ["NoEntregado"] = ("No pudimos entregar tu envío", "Intentamos entregarlo y no pudimos. Desde el enlace podés ver cómo sigue o pedir otra visita."),
            ["Reprogramado"] = ("Tu entrega se reprogramó", "La entrega se reprogramó para otra visita."),
            ["EnDevolucion"] = ("Tu envío vuelve al comercio", "No fue posible entregarlo y el envío vuelve al comercio."),
            ["Devuelto"] = ("Tu envío fue devuelto", "El envío fue devuelto al comercio."),
            ["Extraviado"] = ("Hay un problema con tu envío", "Hay un problema con tu envío. Comunicate con el comercio.")
        };

    public static bool EsRelevante(string estado) => Textos.ContainsKey(estado);

    public static MensajeCorreo Armar(DatosNotificacion datos, string urlSeguimiento)
    {
        var (asunto, detalle) = Textos[datos.EstadoNuevo];
        var enlace = $"{urlSeguimiento.TrimEnd('/')}/seguimiento/{Uri.EscapeDataString(datos.TokenSeguimiento)}";
        var operador = WebUtility.HtmlEncode(datos.Operador);
        var numero = WebUtility.HtmlEncode(datos.NumeroSeguimiento);

        var texto = $"{datos.Operador}\n\n{detalle}\n\nNúmero de seguimiento: {datos.NumeroSeguimiento}\nSeguí tu envío: {enlace}\n";
        var html = $"""
            <div style="font-family: Arial, sans-serif; max-width: 520px; margin: 0 auto; color: #212529">
              <p style="font-weight: bold; color: {Color(datos.ColorMarca)}">{operador}</p>
              <h1 style="font-size: 20px">{WebUtility.HtmlEncode(asunto)}</h1>
              <p>{WebUtility.HtmlEncode(detalle)}</p>
              <p>Número de seguimiento: <strong>{numero}</strong></p>
              <p><a href="{enlace}" style="display: inline-block; padding: 10px 18px; background: {Color(datos.ColorMarca)}; color: #ffffff; text-decoration: none; border-radius: 6px">Seguir mi envío</a></p>
            </div>
            """;

        return new MensajeCorreo($"{datos.Operador}: {asunto}", texto, html);
    }

    private static string Color(string colorMarca) => colorMarca switch
    {
        "azul" => "#0d6efd",
        "verde" => "#198754",
        _ => "#4f46e5"
    };
}
