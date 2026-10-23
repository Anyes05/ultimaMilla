using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Application;

namespace Plataforma.Infrastructure;

public sealed class EmisorTokenJwt : IEmisorToken
{
    private readonly IConfiguration _configuration;

    public EmisorTokenJwt(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Emitir(Sesion sesion)
    {
        var clave = AutenticacionJwt.Clave(_configuration);
        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave)),
            SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(ClaimsPlataforma.UsuarioId, sesion.UsuarioId.ToString()),
            new("unique_name", sesion.NombreUsuario),
            new(ClaimsPlataforma.OperadorId, sesion.OperadorId.ToString()),
            new(ClaimsPlataforma.Rol, sesion.RolClaim)
        };

        if (sesion.ComercioId is Guid comercioId)
            claims.Add(new Claim(ClaimsPlataforma.ComercioId, comercioId.ToString()));

        if (sesion.RepartidorId is Guid repartidorId)
            claims.Add(new Claim(ClaimsPlataforma.RepartidorId, repartidorId.ToString()));

        var horas = AutenticacionJwt.HorasVigencia(_configuration);
        var token = new JwtSecurityToken(
            issuer: AutenticacionJwt.Emisor(_configuration),
            audience: AutenticacionJwt.Audiencia(_configuration),
            claims: claims,
            expires: DateTime.UtcNow.AddHours(horas),
            signingCredentials: credenciales);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
