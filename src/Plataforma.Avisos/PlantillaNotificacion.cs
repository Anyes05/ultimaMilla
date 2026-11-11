using System.Net;

namespace Plataforma.Avisos;

public sealed record DatosNotificacion(
    string Operador,
    string ColorMarca,
    string NumeroSeguimiento,
    string TokenSeguimiento,
    string EstadoNuevo,
    string TelefonoContacto = "",
    string EmailContacto = "");

public sealed record MensajeCorreo(string Asunto, string Texto, string Html);

/// <summary>Correo al destinatario (RF 27), con la marca del operador (RF 5) y el enlace público de seguimiento.</summary>
public static class PlantillaNotificacion
{
    // Estados que le importan a quien espera el paquete. AsignadoARuta es interno.
    private static readonly IReadOnlyDictionary<string, (string Asunto, string Detalle)> Textos =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Admitido"] = ("Registramos tu envío", "Ya podés seguir tu envío con este número o desde el botón de abajo."),
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

        var contacto = string.Join(" · ", new[] { datos.TelefonoContacto, datos.EmailContacto }.Where(dato => !string.IsNullOrWhiteSpace(dato)));
        var texto = $"{datos.Operador}\n\n{detalle}\n\nNúmero de seguimiento: {datos.NumeroSeguimiento}\nSeguí tu envío: {enlace}\n"
                    + (contacto.Length > 0 ? $"\nConsultas: {contacto}\n" : string.Empty);
        var filaContacto = contacto.Length == 0
            ? string.Empty
            : $"""<tr><td style="padding: 0 32px 24px; color: #52606d; font-size: 14px; line-height: 1.5">¿Consultas? {WebUtility.HtmlEncode(contacto)}</td></tr>""";
        var color = Color(datos.ColorMarca);
        // Tablas y estilos en línea: es lo único que respetan todos los clientes de correo.
        var html = $"""
            <div style="margin: 0; padding: 32px 16px; background-color: #f1f3f5; font-family: Arial, Helvetica, sans-serif">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width: 520px; margin: 0 auto; background-color: #ffffff; border-radius: 12px; overflow: hidden; border: 1px solid #e3e6ea">
                <tr>
                  <td style="padding: 20px 32px; background-color: {color}; color: #ffffff; font-size: 16px; font-weight: bold">{operador}</td>
                </tr>
                <tr>
                  <td style="padding: 32px 32px 8px">
                    <h1 style="margin: 0 0 12px; color: #1f2933; font-size: 24px; line-height: 1.3">{WebUtility.HtmlEncode(asunto)}</h1>
                    <p style="margin: 0; color: #52606d; font-size: 16px; line-height: 1.5">{WebUtility.HtmlEncode(detalle)}</p>
                  </td>
                </tr>
                <tr>
                  <td style="padding: 20px 32px 8px">
                    <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color: #f1f3f5; border-radius: 8px">
                      <tr>
                        <td style="padding: 14px 16px">
                          <div style="color: #7b8794; font-size: 13px">Número de seguimiento</div>
                          <div style="margin-top: 4px; color: #1f2933; font-size: 14px; font-weight: bold; word-break: break-all">{numero}</div>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
                <tr>
                  <td style="padding: 20px 32px 32px">
                    <a href="{enlace}" style="display: block; padding: 14px 18px; background-color: {color}; color: #ffffff; font-size: 16px; font-weight: bold; text-align: center; text-decoration: none; border-radius: 8px">Seguir mi envío</a>
                  </td>
                </tr>
                {filaContacto}
                <tr>
                  <td style="padding: 16px 32px; border-top: 1px solid #e3e6ea; color: #7b8794; font-size: 12px; line-height: 1.5">Este es un mensaje automático de {operador}. No hace falta que lo respondas.</td>
                </tr>
              </table>
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
