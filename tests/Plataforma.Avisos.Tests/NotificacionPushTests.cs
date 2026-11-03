using Microsoft.EntityFrameworkCore;
using Plataforma.Avisos;

namespace Plataforma.Avisos.Tests;

/// <summary>Push de reasignación: un evento llega una sola vez a cada dispositivo y los reintentos terminan.</summary>
public class NotificacionPushTests
{
    private static readonly DateTime Ahora = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static NotificacionPush Nueva(Guid? eventId = null) =>
        NotificacionPush.Crear(eventId ?? Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "token-del-telefono", "android", Ahora);

    [Fact]
    public void Enviada_NoSeVuelveAEnviar()
    {
        var push = Nueva();
        Assert.True(push.PorEnviar);

        push.MarcarEnviada(Ahora);

        Assert.False(push.PorEnviar);
        Assert.Equal(EstadoNotificacionPush.Enviada, push.Estado);
    }

    [Fact]
    public void TokenInvalido_NoSeReintenta()
    {
        var push = Nueva();

        push.MarcarTokenInvalido("UNREGISTERED", Ahora);

        Assert.False(push.PorEnviar);
        Assert.Equal(EstadoNotificacionPush.TokenInvalido, push.Estado);
    }

    [Fact]
    public void ErrorTransitorio_ReintentaHastaElMaximoYQuedaFallida()
    {
        var push = Nueva();

        Assert.True(push.RegistrarFallo("503", maximoReintentos: 2, Ahora));
        Assert.True(push.RegistrarFallo("503", maximoReintentos: 2, Ahora));
        Assert.False(push.RegistrarFallo("503", maximoReintentos: 2, Ahora));

        Assert.Equal(EstadoNotificacionPush.Fallida, push.Estado);
        Assert.Throws<InvalidOperationException>(() => push.RegistrarFallo("503", 2, Ahora));
    }

    [Fact]
    public void MismoEventoYToken_SoloUnRegistro()
    {
        // El índice único (EventId, Token) es lo que evita el push duplicado si el mensaje llega dos veces.
        using var db = new AvisosDbContext(new DbContextOptionsBuilder<AvisosDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var indice = db.Model.FindEntityType(typeof(NotificacionPush))!.GetIndexes()
            .Single(i => i.IsUnique);

        Assert.Equal(["EventId", "Token"], indice.Properties.Select(p => p.Name));
    }
}
