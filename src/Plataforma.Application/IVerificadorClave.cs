namespace Plataforma.Application;

public interface IVerificadorClave
{
    bool Coincide(string clave, string hashAlmacenado);
}
