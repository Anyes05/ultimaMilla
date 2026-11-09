using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Options;
using Plataforma.Avisos;

namespace Plataforma.Worker.Notificaciones;

/// <summary>Envío de un correo. Es el único punto que conoce el proveedor.</summary>
public interface IEmisorCorreo
{
    Task EnviarAsync(string destinatario, string nombreRemitente, MensajeCorreo mensaje, CancellationToken cancellationToken);
}

public sealed class EmisorCorreoSmtp(IOptions<OpcionesNotificaciones> opciones) : IEmisorCorreo
{
    private readonly OpcionesNotificaciones _opciones = opciones.Value;

    public async Task EnviarAsync(string destinatario, string nombreRemitente, MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        using var correo = new MailMessage(new MailAddress(_opciones.Remitente, nombreRemitente), new MailAddress(destinatario))
        {
            Subject = mensaje.Asunto,
            SubjectEncoding = Encoding.UTF8,
            Body = mensaje.Texto,
            BodyEncoding = Encoding.UTF8
        };
        correo.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(mensaje.Html, Encoding.UTF8, MediaTypeNames.Text.Html));

        // SmtpClient no admite envíos simultáneos: uno por correo.
        using var cliente = new SmtpClient(_opciones.SmtpHost, _opciones.SmtpPuerto) { EnableSsl = _opciones.SmtpSsl };
        if (!string.IsNullOrWhiteSpace(_opciones.SmtpUsuario))
            cliente.Credentials = new NetworkCredential(_opciones.SmtpUsuario, _opciones.SmtpClave);

        await cliente.SendMailAsync(correo, cancellationToken);
    }
}

/// <summary>Sin servidor de correo configurado: deja constancia en el log, sin la dirección del destinatario.</summary>
public sealed class EmisorCorreoRegistro(ILogger<EmisorCorreoRegistro> logger) : IEmisorCorreo
{
    public Task EnviarAsync(string destinatario, string nombreRemitente, MensajeCorreo mensaje, CancellationToken cancellationToken)
    {
        logger.LogInformation("Correo (registro, sin SMTP) de {Remitente}: {Asunto}", nombreRemitente, mensaje.Asunto);
        return Task.CompletedTask;
    }
}
