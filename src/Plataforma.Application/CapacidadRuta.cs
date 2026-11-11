using Plataforma.Domain;

namespace Plataforma.Application;

/// <summary>Una parada del recorrido y la carga que queda en el vehículo después de ella.</summary>
public sealed record ParadaCarga(
    int Numero,
    string Descripcion,
    bool EsRetiro,
    decimal PesoKg,
    decimal VolumenCm3,
    decimal CargaPesoKg,
    decimal CargaVolumenCm3);

/// <summary>
/// Carga del vehículo a lo largo de la ruta mixta (RF 39): sale con todas las entregas, baja en cada entrega
/// y sube en cada retiro desde la parada donde se retira.
/// </summary>
public sealed record PerfilCargaRuta(
    Guid RutaId,
    string? Vehiculo,
    decimal CapacidadPesoKg,
    decimal CapacidadVolumenCm3,
    int MaximoParadas,
    decimal CargaInicialPesoKg,
    decimal CargaInicialVolumenCm3,
    decimal PicoPesoKg,
    decimal PicoVolumenCm3,
    IReadOnlyList<ParadaCarga> Paradas)
{
    /// <summary>Por qué no entra en el vehículo, o null si entra (capacidad 0 = sin límite).</summary>
    public string? Problema =>
        CapacidadPesoKg > 0 && PicoPesoKg > CapacidadPesoKg
            ? $"En el recorrido el vehículo llegaría a {PicoPesoKg:0.##} kg y su capacidad es {CapacidadPesoKg:0.##} kg."
        : CapacidadVolumenCm3 > 0 && PicoVolumenCm3 > CapacidadVolumenCm3
            ? $"En el recorrido el vehículo llegaría a {PicoVolumenCm3:0.##} cm³ y su capacidad es {CapacidadVolumenCm3:0.##} cm³."
        : MaximoParadas > 0 && Paradas.Count > MaximoParadas
            ? $"La ruta tendría {Paradas.Count} paradas y el vehículo admite {MaximoParadas}."
        : null;
}

/// <summary>Una entrega de la ruta, en orden de visita, con lo que todavía lleva a bordo.</summary>
public sealed record EntregaEnRuta(string Descripcion, decimal PesoKg, decimal VolumenCm3);

/// <summary>Un retiro de la ruta: se hace después de <see cref="Posicion"/> entregas (null = al final).</summary>
public sealed record RetiroEnRuta(string Descripcion, int? Posicion, decimal PesoKg, decimal VolumenCm3);

public static class CapacidadRuta
{
    public static PerfilCargaRuta Calcular(
        Guid rutaId,
        Vehiculo? vehiculo,
        IReadOnlyList<EntregaEnRuta> entregas,
        IReadOnlyList<RetiroEnRuta> retiros)
    {
        var peso = entregas.Sum(entrega => entrega.PesoKg);
        var volumen = entregas.Sum(entrega => entrega.VolumenCm3);
        var inicialPeso = peso;
        var inicialVolumen = volumen;
        var picoPeso = peso;
        var picoVolumen = volumen;
        var paradas = new List<ParadaCarga>(entregas.Count + retiros.Count);

        for (var hechas = 0; hechas <= entregas.Count; hechas++)
        {
            // Los retiros "después de N entregas" (o al final, si la posición pasa del total).
            foreach (var retiro in retiros.Where(retiro => Math.Min(retiro.Posicion ?? int.MaxValue, entregas.Count) == hechas))
            {
                peso += retiro.PesoKg;
                volumen += retiro.VolumenCm3;
                picoPeso = Math.Max(picoPeso, peso);
                picoVolumen = Math.Max(picoVolumen, volumen);
                paradas.Add(new ParadaCarga(paradas.Count + 1, retiro.Descripcion, true, retiro.PesoKg, retiro.VolumenCm3, peso, volumen));
            }

            if (hechas == entregas.Count)
                break;

            var entrega = entregas[hechas];
            peso -= entrega.PesoKg;
            volumen -= entrega.VolumenCm3;
            paradas.Add(new ParadaCarga(paradas.Count + 1, entrega.Descripcion, false, entrega.PesoKg, entrega.VolumenCm3, peso, volumen));
        }

        return new PerfilCargaRuta(
            rutaId,
            vehiculo?.Patente,
            vehiculo?.CapacidadPesoKg ?? 0,
            vehiculo?.CapacidadVolumenCm3 ?? 0,
            vehiculo?.MaximoParadas ?? 0,
            inicialPeso,
            inicialVolumen,
            picoPeso,
            picoVolumen,
            paradas);
    }
}
