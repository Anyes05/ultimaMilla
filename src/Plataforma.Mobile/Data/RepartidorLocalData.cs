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
    IReadOnlyList<EntregaPendiente>? ComprobantesConfirmados = null);

public sealed class RepartidorLocalData(string databasePath, ISnapshotCipher? cipher = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { IgnoreReadOnlyProperties = true };

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
                json = (cipher ?? throw new InvalidOperationException("Falta la clave segura; no se modifico la base.")).Descifrar(json);
            var estado = JsonSerializer.Deserialize<EstadoLocalDemo>(json, JsonOptions)
                ?? throw new InvalidOperationException("La base local contiene datos vacios.");
            if (estado.EstadosServidor is null
                || (estado.HaySesion && estado.Ruta is null)
                || (estado.Ruta is not null && (estado.Ruta.Envios is null
                    || estado.Ruta.Envios.Any(envio => !estado.EstadosServidor.ContainsKey(envio.Id)))))
                throw new InvalidOperationException("La ruta local esta incompleta. No se reemplazaron los datos.");
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
            throw new InvalidOperationException("No se pudo leer SQLite. Los datos locales no se reiniciaron.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("La ruta guardada no tiene un formato valido. No se descartaron los datos.", ex);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidOperationException("No se pudo descifrar la base. No se reiniciaron ni borraron los datos.", ex);
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
                throw new InvalidOperationException("No se valido el cifrado; snapshot anterior conservado.");
            command.Parameters.AddWithValue("$datos", payload);
            command.ExecuteNonQuery();
            transaction.Commit();
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException("No se pudo guardar en SQLite. La operacion no se confirmo.", ex);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidOperationException("No se pudo cifrar el snapshot. La operacion no se confirmo.", ex);
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
