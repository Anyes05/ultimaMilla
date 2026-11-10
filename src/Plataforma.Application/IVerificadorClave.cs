namespace Plataforma.Application;

public interface IVerificadorClave
{
    string Generar(string clave);
    bool Coincide(string clave, string hashAlmacenado);
}
