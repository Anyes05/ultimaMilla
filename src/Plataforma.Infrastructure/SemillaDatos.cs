using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public static class SemillaDatos
{
    public static async Task AplicarAsync(PlataformaDbContext context, CancellationToken cancellationToken = default)
    {
        if (!await context.Operadores.AnyAsync(cancellationToken))
        {
            var norte = Operador.Crear(
                DatosSemilla.OperadorNorteId,
                DatosSemilla.NorteNombre,
                DatosSemilla.NorteColor,
                DatosSemilla.NorteMaximoIntentos);

            var sur = Operador.Crear(
                DatosSemilla.OperadorSurId,
                DatosSemilla.SurNombre,
                DatosSemilla.SurColor,
                DatosSemilla.SurMaximoIntentos);

            var comercioNorte = Comercio.Registrar(
                DatosSemilla.ComercioNorteId,
                DatosSemilla.OperadorNorteId,
                "Comercio Norte",
                "Comercio Norte SA",
                "RUT-NORTE-1",
                "Av. Italia 1000");

            var comercioSur = Comercio.Registrar(
                DatosSemilla.ComercioSurId,
                DatosSemilla.OperadorSurId,
                "Comercio Sur",
                "Comercio Sur SA",
                "RUT-SUR-1",
                "Av. Brasil 2000");

            var clave = HashClave.Crear(DatosSemilla.ClaveDesarrollo);

            var despachadorNorte = UsuarioPlataforma.Registrar(
                DatosSemilla.UsuarioDespachadorNorteId,
                DatosSemilla.OperadorNorteId,
                "Despachador Norte",
                "despachador@norte.test",
                DatosSemilla.UsuarioDespachadorNorte,
                clave,
                RolUsuario.Despachador,
                null);

            var comercioUsuarioNorte = UsuarioPlataforma.Registrar(
                DatosSemilla.UsuarioComercioNorteId,
                DatosSemilla.OperadorNorteId,
                "Comercio Norte",
                "comercio@norte.test",
                DatosSemilla.UsuarioComercioNorte,
                clave,
                RolUsuario.Comercio,
                DatosSemilla.ComercioNorteId);

            var despachadorSur = UsuarioPlataforma.Registrar(
                DatosSemilla.UsuarioDespachadorSurId,
                DatosSemilla.OperadorSurId,
                "Despachador Sur",
                "despachador@sur.test",
                DatosSemilla.UsuarioDespachadorSur,
                clave,
                RolUsuario.Despachador,
                null);

            var comercioUsuarioSur = UsuarioPlataforma.Registrar(
                DatosSemilla.UsuarioComercioSurId,
                DatosSemilla.OperadorSurId,
                "Comercio Sur",
                "comercio@sur.test",
                DatosSemilla.UsuarioComercioSur,
                clave,
                RolUsuario.Comercio,
                DatosSemilla.ComercioSurId);

            context.Operadores.AddRange(norte, sur);
            context.Comercios.AddRange(comercioNorte, comercioSur);
            context.Usuarios.AddRange(despachadorNorte, comercioUsuarioNorte, despachadorSur, comercioUsuarioSur);
            await context.SaveChangesAsync(cancellationToken);
        }

        await AsegurarRepartidoresAsync(context, cancellationToken);
    }

    private static async Task AsegurarRepartidoresAsync(PlataformaDbContext context, CancellationToken cancellationToken)
    {
        if (await context.Repartidores.AnyAsync(cancellationToken))
            return;

        var clave = HashClave.Crear(DatosSemilla.ClaveDesarrollo);

        if (!await context.Usuarios.AnyAsync(usuario => usuario.Id == DatosSemilla.UsuarioRepartidorNorteId, cancellationToken))
        {
            context.Usuarios.Add(UsuarioPlataforma.Registrar(
                DatosSemilla.UsuarioRepartidorNorteId,
                DatosSemilla.OperadorNorteId,
                "Repartidor Norte",
                "alvaro@demo.test",
                DatosSemilla.UsuarioRepartidorNorte,
                clave,
                RolUsuario.Repartidor,
                null));
        }

        if (!await context.Usuarios.AnyAsync(usuario => usuario.Id == DatosSemilla.UsuarioRepartidorSurId, cancellationToken))
        {
            context.Usuarios.Add(UsuarioPlataforma.Registrar(
                DatosSemilla.UsuarioRepartidorSurId,
                DatosSemilla.OperadorSurId,
                "Repartidor Sur",
                "repartidor@sur.test",
                DatosSemilla.UsuarioRepartidorSur,
                clave,
                RolUsuario.Repartidor,
                null));
        }

        context.Repartidores.AddRange(
            Repartidor.Registrar(
                DatosSemilla.RepartidorNorteId,
                DatosSemilla.OperadorNorteId,
                "Alvaro - demostracion",
                "DOC-NORTE-R1",
                "099000001",
                DatosSemilla.UsuarioRepartidorNorteId),
            Repartidor.Registrar(
                DatosSemilla.RepartidorSurId,
                DatosSemilla.OperadorSurId,
                "Repartidor Sur",
                "DOC-SUR-R1",
                "099000002",
                DatosSemilla.UsuarioRepartidorSurId));

        await context.SaveChangesAsync(cancellationToken);
    }
}
