using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Plataforma.Application;
using Plataforma.Domain;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Tests;

public class AislamientoOperadorTests
{
    [Fact]
    public async Task Filtro_Norte_NoVeEnviosDeSur()
    {
        var db = NombreDb();
        Guid envioSurId;

        await using (var seed = CrearContexto(db, operadorId: null))
        {
            var norte = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Ana Norte", "Calle Norte 1");
            var sur = Envio.Admitir(DatosSemilla.OperadorSurId, DatosSemilla.ComercioSurId, "Ana Sur", "Calle Sur 1");
            envioSurId = sur.Id;
            seed.Envios.AddRange(norte, sur);
            await seed.SaveChangesAsync();
        }

        await using var contextoNorte = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var visibles = await contextoNorte.Envios.ToListAsync();

        Assert.Single(visibles);
        Assert.Equal(DatosSemilla.OperadorNorteId, visibles[0].OperadorId);
        Assert.Null(await contextoNorte.Envios.FirstOrDefaultAsync(envio => envio.Id == envioSurId));
    }

    [Fact]
    public async Task Interceptor_Norte_NoPuedeEditarEnvioDeSur()
    {
        var db = NombreDb();
        Guid envioSurId;

        await using (var seed = CrearContexto(db, operadorId: null))
        {
            var sur = Envio.Admitir(DatosSemilla.OperadorSurId, DatosSemilla.ComercioSurId, "Ana Sur", "Calle Sur 1");
            envioSurId = sur.Id;
            seed.Envios.Add(sur);
            await seed.SaveChangesAsync();
        }

        await using var contextoNorte = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var envioSur = await contextoNorte.Envios.IgnoreQueryFilters()
            .FirstAsync(envio => envio.Id == envioSurId);
        envioSur.CambiarEstado(EstadoEnvio.EnDeposito);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => contextoNorte.SaveChangesAsync());
        Assert.Contains("otro operador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Interceptor_Norte_NoPuedeCrearEnvioParaSur()
    {
        var db = NombreDb();
        await using var contextoNorte = CrearContexto(db, DatosSemilla.OperadorNorteId);

        contextoNorte.Envios.Add(Envio.Admitir(
            DatosSemilla.OperadorSurId,
            DatosSemilla.ComercioSurId,
            "Intruso",
            "Calle Sur 9"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => contextoNorte.SaveChangesAsync());
        Assert.Contains("otro operador", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CambiarEstadoEnvio_Norte_NoModificaEnvioDeSur()
    {
        var db = NombreDb();
        Guid envioSurId;

        await using (var seed = CrearContexto(db, operadorId: null))
        {
            var sur = Envio.Admitir(DatosSemilla.OperadorSurId, DatosSemilla.ComercioSurId, "Ana Sur", "Calle Sur 1");
            envioSurId = sur.Id;
            seed.Envios.Add(sur);
            await seed.SaveChangesAsync();
        }

        await using var contextoNorte = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var casoUso = new CambiarEstadoEnvio(new EnvioRepositorio(contextoNorte));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            casoUso.EjecutarAsync(envioSurId, DatosSemilla.OperadorNorteId, EstadoEnvio.EnDeposito));

        await using var verificacion = CrearContexto(db, operadorId: null);
        var envioSur = await verificacion.Envios.SingleAsync(envio => envio.Id == envioSurId);
        Assert.Equal(EstadoEnvio.Admitido, envioSur.Estado);
    }

    [Fact]
    public async Task CambiarEstadoEnvio_Norte_SiPuedeCambiarSuEnvio()
    {
        var db = NombreDb();
        Guid envioNorteId;

        await using (var seed = CrearContexto(db, operadorId: null))
        {
            var norte = Envio.Admitir(DatosSemilla.OperadorNorteId, DatosSemilla.ComercioNorteId, "Ana Norte", "Calle Norte 1");
            envioNorteId = norte.Id;
            seed.Envios.Add(norte);
            await seed.SaveChangesAsync();
        }

        await using var contextoNorte = CrearContexto(db, DatosSemilla.OperadorNorteId);
        var casoUso = new CambiarEstadoEnvio(new EnvioRepositorio(contextoNorte));

        await casoUso.EjecutarAsync(envioNorteId, DatosSemilla.OperadorNorteId, EstadoEnvio.EnDeposito);

        Assert.Equal(EstadoEnvio.EnDeposito,
            (await contextoNorte.Envios.SingleAsync(envio => envio.Id == envioNorteId)).Estado);
    }

    [Fact]
    public async Task LoginReal_EmiteClaimsConOperadorIdDeNorte()
    {
        var db = NombreDb();
        await using (var seed = CrearContexto(db, operadorId: null))
        {
            seed.Usuarios.Add(UsuarioPlataforma.Registrar(
                DatosSemilla.UsuarioDespachadorNorteId,
                DatosSemilla.OperadorNorteId,
                "Despachador Norte",
                "norte@demo.test",
                DatosSemilla.UsuarioDespachadorNorte,
                HashClave.Crear(DatosSemilla.ClaveDesarrollo),
                RolUsuario.Despachador,
                comercioId: null));
            await seed.SaveChangesAsync();
        }

        await using var contextoLogin = CrearContexto(db, operadorId: null);
        var iniciarSesion = new IniciarSesion(new UsuarioRepositorio(contextoLogin), new HashClave());
        var resultado = await iniciarSesion.Ejecutar(
            DatosSemilla.UsuarioDespachadorNorte,
            DatosSemilla.ClaveDesarrollo);

        var ok = Assert.IsType<ResultadoInicio.Ok>(resultado);
        Assert.Equal(DatosSemilla.OperadorNorteId, ok.Sesion.OperadorId);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Clave"] = "desarrollo-ultima-milla-clave-firmado-local-32",
                ["Jwt:Emisor"] = "ultima-milla",
                ["Jwt:Audiencia"] = "ultima-milla"
            })
            .Build();

        var token = new EmisorTokenJwt(configuration).Emitir(ok.Sesion);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(DatosSemilla.OperadorNorteId.ToString(), jwt.Claims.First(c => c.Type == ClaimsPlataforma.OperadorId).Value);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "Bearer"))
        };
        var accessor = new HttpContextAccessor { HttpContext = http };
        var operadorActual = new OperadorActualHttp(accessor);

        Assert.Equal(DatosSemilla.OperadorNorteId, operadorActual.OperadorId);
        Assert.True(AccesoActual.Intentar(http.User, out var acceso));
        Assert.Equal(DatosSemilla.OperadorNorteId, acceso.OperadorId);
    }

    private static string NombreDb() => $"aislamiento-{Guid.NewGuid():N}";

    private static PlataformaDbContext CrearContexto(string nombreDb, Guid? operadorId)
    {
        var operador = new OperadorActualFijo(operadorId);
        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(nombreDb)
            .AddInterceptors(new OperadorSaveChangesInterceptor(operador))
            .Options;

        return new PlataformaDbContext(options, operador);
    }
}
