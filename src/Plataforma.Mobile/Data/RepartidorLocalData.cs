using System.Text.Json;
using Microsoft.Data.Sqlite;
using Plataforma.Mobile.Models;

namespace Plataforma.Mobile.Data;

public sealed record EstadoLocalDemo(
    Ruta? Ruta,
    bool HaySesion,
    IReadOnlyDictionary<Guid, EstadoEnvio> EstadosServidor,
    Guid? RepartidorId = null,
    Guid? OperadorId = null,
    IReadOnlyList<EntregaPendiente>? ComprobantesConfirmados = null,
    bool RequiereAutenticacion = false,
    IReadOnlyList<Ruta>? RutasPendientes = null,
    IReadOnlyList<RetiroEnRuta>? RetirosConComprobante = null);

public sealed class RepartidorLocalData(string databasePath, ISnapshotCipher? cipher = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { IgnoreReadOnlyProperties = true };

    public static IReadOnlyList<Ruta> RutasConEventosSinResolver(EstadoLocalDemo estado)
    {
        var rutas = (estado.RutasPendientes ?? []).Append(estado.Ruta)
            .Where(ruta => ruta is not null)
            .Select(ruta => ruta!)
            .Select(ruta => ruta with
            {
                Envios = ruta.Envios.Where(envio => envio.Sincronizacion is
                    EstadoSincronizacion.Pendiente or EstadoSincronizacion.Rechazado).ToArray()
            })
            .Where(ruta => ruta.Envios.Count > 0);

        return rutas.GroupBy(ruta => (ruta.Id, ruta.Fecha)).Select(grupo =>
        {
            var ruta = grupo.First();
            return ruta with { Envios = grupo.SelectMany(item => item.Envios).DistinctBy(envio => envio.Id).ToArray() };
        }).ToArray();
    }

    public EstadoLocalDemo Leer()
    {
        try
        {
            using var connection = Abrir();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Datos FROM EstadoDemo WHERE Id = 1";
            var json = command.ExecuteScalar() as string;
            if (json is null)
                return new EstadoLocalDemo(null, false, new Dictionary<Guid, EstadoEnvio>());

            var cifrado = json.StartsWith("aesgcm1:", StringComparison.Ordinal);
            if (cifrado)
                json = (cipher ?? throw new InvalidOperationException("No podemos acceder a tus datos protegidos. No borres la app; solicita ayuda al operador.")).Descifrar(json);
            var estado = JsonSerializer.Deserialize<EstadoLocalDemo>(json, JsonOptions)
                ?? throw new InvalidOperationException("No pudimos recuperar tus datos guardados. No borres la app; solicita ayuda al operador.");
            if (estado.EstadosServidor is null
                || (estado.HaySesion && estado.Ruta is null)
                || (estado.Ruta is not null && (estado.Ruta.Envios is null
                    || estado.Ruta.Envios.Any(envio => !estado.EstadosServidor.ContainsKey(envio.Id)))))
                throw new InvalidOperationException("Tu ruta guardada esta incompleta. No se reemplazo; solicita ayuda al operador.");
            if (!cifrado && cipher is not null)
            {
                Guardar(estado);
                using var limpiar = connection.CreateCommand();
                limpiar.CommandText = "VACUUM";
                limpiar.ExecuteNonQuery();
            }
            return estado;
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException("No pudimos abrir tus datos guardados. No borres la app; solicita ayuda al operador.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("No pudimos recuperar la ruta guardada. No borramos tus datos; solicita ayuda al operador.", ex);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidOperationException("No pudimos desbloquear tus datos protegidos. No reinstales la app ni borres sus datos; solicita ayuda al operador.", ex);
        }
    }

    public void Guardar(EstadoLocalDemo estado)
    {
        try
        {
            using var connection = Abrir();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            // Ruta, comprobantes, cola y estado del doble se confirman juntos.
            command.CommandText = """
                INSERT INTO EstadoDemo (Id, Datos) VALUES (1, $datos)
                ON CONFLICT(Id) DO UPDATE SET Datos = excluded.Datos
                """;
            var json = JsonSerializer.Serialize(estado, JsonOptions);
            var payload = cipher?.Cifrar(json) ?? json;
            if (cipher is not null && cipher.Descifrar(payload) != json)
                throw new InvalidOperationException("No pudimos proteger los nuevos datos. Se conservo el registro anterior; solicita ayuda al operador antes de continuar.");
            command.Parameters.AddWithValue("$datos", payload);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException("No pudimos guardar el registro en el telefono. Comprueba el espacio disponible y vuelve a intentar antes de salir.", ex);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidOperationException("No pudimos proteger ni guardar el registro. No salgas del formulario; solicita ayuda al operador.", ex);
        }
    }

    private SqliteConnection Abrir()
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString());
        try
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA synchronous = FULL;
                PRAGMA secure_delete = ON;
                CREATE TABLE IF NOT EXISTS EstadoDemo (
                    Id INTEGER PRIMARY KEY CHECK (Id = 1),
                    Datos TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
