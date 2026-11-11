using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Plataforma.Mobile.Data;

public sealed record PosicionPendiente(double Latitud, double Longitud,
    DateTimeOffset FechaDispositivo, double? PrecisionMetros);

public sealed class ColaPosiciones(string databasePath, ISnapshotCipher cipher)
{
    private readonly object acceso = new();

    public void Agregar(string cuenta, PosicionPendiente posicion)
    {
        if (!double.IsFinite(posicion.Latitud) || !double.IsFinite(posicion.Longitud) ||
            posicion.Latitud is < -90 or > 90 || posicion.Longitud is < -180 or > 180 ||
            posicion.PrecisionMetros is < 0) return;
        lock (acceso)
        {
            using var conexion = Abrir();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "INSERT INTO PosicionesPendientes (Cuenta, Datos) VALUES ($cuenta, $datos)";
            comando.Parameters.AddWithValue("$cuenta", cuenta);
            comando.Parameters.AddWithValue("$datos", cipher.Cifrar(JsonSerializer.Serialize(posicion)));
            comando.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<(long Id, PosicionPendiente Posicion)> Tomar(string cuenta)
    {
        lock (acceso)
        {
            using var conexion = Abrir();
            using var comando = conexion.CreateCommand();
            comando.CommandText = "SELECT Id, Datos FROM PosicionesPendientes WHERE Cuenta = $cuenta ORDER BY Id LIMIT 500";
            comando.Parameters.AddWithValue("$cuenta", cuenta);
            using var lector = comando.ExecuteReader();
            var resultado = new List<(long, PosicionPendiente)>();
            while (lector.Read())
            {
                var posicion = JsonSerializer.Deserialize<PosicionPendiente>(cipher.Descifrar(lector.GetString(1)))
                    ?? throw new InvalidOperationException("Posicion local invalida.");
                resultado.Add((lector.GetInt64(0), posicion));
            }
            return resultado;
        }
    }

    public void Borrar(string cuenta, IEnumerable<long> ids)
    {
        lock (acceso)
        {
            using var conexion = Abrir();
            using var transaccion = conexion.BeginTransaction();
            foreach (var id in ids)
            {
                using var comando = conexion.CreateCommand();
                comando.Transaction = transaccion;
                comando.CommandText = "DELETE FROM PosicionesPendientes WHERE Cuenta = $cuenta AND Id = $id";
                comando.Parameters.AddWithValue("$cuenta", cuenta);
                comando.Parameters.AddWithValue("$id", id);
                comando.ExecuteNonQuery();
            }
            transaccion.Commit();
        }
    }

    private SqliteConnection Abrir()
    {
        SQLitePCL.Batteries_V2.Init();
        var conexion = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = databasePath, Pooling = false }.ToString());
        try
        {
            conexion.Open();
            using var comando = conexion.CreateCommand();
            comando.CommandText = """
                PRAGMA secure_delete = ON;
                CREATE TABLE IF NOT EXISTS PosicionesPendientes (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Cuenta TEXT NOT NULL,
                    Datos TEXT NOT NULL);
                """;
            comando.ExecuteNonQuery();
            return conexion;
        }
        catch { conexion.Dispose(); throw; }
    }
}