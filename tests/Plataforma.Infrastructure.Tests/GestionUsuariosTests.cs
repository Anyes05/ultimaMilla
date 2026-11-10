using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class GestionUsuariosTests
{
    [Fact]
    public async Task RegistrarYDesactivar_PersistePerfil()
    {
        await using var contexto = CrearContexto();
        var identity = new IdentityPrueba();
        var gestion = new GestionUsuarios(new UsuarioRepositorio(contexto), identity, new HashClave());

        var usuario = await gestion.RegistrarAsync(
            DatosSemilla.OperadorNorteId,
            "Nadia Test",
            "nadia@test.local",
            "nadia.admin",
            "secreto1",
            RolUsuario.Administrador);

        Assert.Equal(1, identity.Creados);
        Assert.True(usuario.Activo);
        Assert.Equal(1, await contexto.Usuarios.CountAsync());

        await gestion.CambiarActivoAsync(DatosSemilla.OperadorNorteId, usuario.Id, activo: false);
        Assert.False((await contexto.Usuarios.SingleAsync()).Activo);
    }

    [Fact]
    public async Task Registrar_NombreDuplicado_Rechaza()
    {
        await using var contexto = CrearContexto();
        var gestion = new GestionUsuarios(new UsuarioRepositorio(contexto), new IdentityPrueba(), new HashClave());
        await gestion.RegistrarAsync(
            DatosSemilla.OperadorNorteId, "A", "a@t.local", "mismo", "secreto1", RolUsuario.Despachador);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gestion.RegistrarAsync(
                DatosSemilla.OperadorNorteId, "B", "b@t.local", "mismo", "secreto1", RolUsuario.Despachador));
    }

    private static PlataformaDbContext CrearContexto()
    {
        var operador = new OperadorActualFijo(DatosSemilla.OperadorNorteId);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase($"usuarios-{Guid.NewGuid():N}")
            .AddInterceptors(new OperadorSaveChangesInterceptor(operador))
            .Options;
        return new PlataformaDbContext(options, operador);
    }

    private sealed class IdentityPrueba : IAutenticacionIdentity
    {
        public int Creados { get; private set; }

        public Task<bool> VerificarAsync(
            Guid usuarioId, string nombreUsuario, string email, string clave, string hashLegado,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task CrearAsync(
            Guid usuarioId, string nombreUsuario, string email, string clave,
            CancellationToken cancellationToken = default)
        {
            Creados++;
            return Task.CompletedTask;
        }
    }
}
