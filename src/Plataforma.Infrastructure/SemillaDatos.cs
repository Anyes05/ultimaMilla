using Microsoft.EntityFrameworkCore;
using Plataforma.Application;
using Plataforma.Domain;

namespace Plataforma.Infrastructure;

public static class SemillaDatos
{
    public static async Task AplicarAsync(PlataformaDbContext context, CancellationToken cancellationToken = default)
    {
        if (await context.Operadores.AnyAsync(cancellationToken))
            return;

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
}
