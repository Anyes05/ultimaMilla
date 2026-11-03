using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Plataforma.Avisos;

/// <summary>
/// Persistencia propia del módulo de avisos, en el esquema "avisos" y con su propia tabla de historial
/// de migraciones. No comparte snapshot con PlataformaDbContext.
/// Todas las lecturas filtran explícitamente por OperadorId (y ComercioId en el Portal).
/// </summary>
public sealed class AvisosDbContext : DbContext
{
    public const string Esquema = "avisos";

    public AvisosDbContext(DbContextOptions<AvisosDbContext> options) : base(options)
    {
    }

    public DbSet<SuscripcionAviso> Suscripciones => Set<SuscripcionAviso>();
    public DbSet<EntregaAviso> Entregas => Set<EntregaAviso>();
    public DbSet<NotificacionPush> NotificacionesPush => Set<NotificacionPush>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Esquema);
        var esNpgsql = Database.IsNpgsql();

        modelBuilder.Entity<SuscripcionAviso>(entidad =>
        {
            entidad.ToTable("Suscripciones");
            entidad.HasKey(suscripcion => suscripcion.Id);
            entidad.Property(suscripcion => suscripcion.Url).HasMaxLength(500).IsRequired();
            entidad.Property(suscripcion => suscripcion.Secreto).HasMaxLength(200).IsRequired();
            entidad.Property(suscripcion => suscripcion.TiposEvento).HasMaxLength(300).IsRequired();
            entidad.Ignore(suscripcion => suscripcion.Tipos);
            entidad.HasIndex(suscripcion => new { suscripcion.OperadorId, suscripcion.ComercioId });
            if (esNpgsql)
                entidad.Property(suscripcion => suscripcion.Version).IsRowVersion();
            else
                entidad.Ignore(suscripcion => suscripcion.Version);
        });

        modelBuilder.Entity<EntregaAviso>(entidad =>
        {
            entidad.ToTable("Entregas");
            entidad.HasKey(entrega => entrega.Id);
            entidad.Property(entrega => entrega.TipoEvento).HasMaxLength(50).IsRequired();
            entidad.Property(entrega => entrega.Payload).IsRequired();
            entidad.Property(entrega => entrega.UltimoError).HasMaxLength(500);
            entidad.Property(entrega => entrega.Estado).HasConversion<string>().HasMaxLength(20);

            // Idempotencia: un evento se entrega una sola vez por suscripción.
            entidad.HasIndex(entrega => new { entrega.EventId, entrega.SuscripcionId }).IsUnique();
            entidad.HasIndex(entrega => new { entrega.OperadorId, entrega.Estado });
            entidad.HasIndex(entrega => new { entrega.OperadorId, entrega.ComercioId, entrega.CreadaEn });

            if (esNpgsql)
                entidad.Property(entrega => entrega.Version).IsRowVersion();
            else
                entidad.Ignore(entrega => entrega.Version);
        });

        modelBuilder.Entity<NotificacionPush>(entidad =>
        {
            entidad.ToTable("NotificacionesPush");
            entidad.HasKey(notificacion => notificacion.Id);
            entidad.Property(notificacion => notificacion.Token).HasMaxLength(512).IsRequired();
            entidad.Property(notificacion => notificacion.Plataforma).HasMaxLength(20).IsRequired();
            entidad.Property(notificacion => notificacion.UltimoError).HasMaxLength(500);
            entidad.Property(notificacion => notificacion.Estado).HasConversion<string>().HasMaxLength(20);

            // Idempotencia: un evento llega una sola vez a cada dispositivo.
            entidad.HasIndex(notificacion => new { notificacion.EventId, notificacion.Token }).IsUnique();
            entidad.HasIndex(notificacion => new { notificacion.OperadorId, notificacion.Estado });
        });
    }

    public static void Configurar(DbContextOptionsBuilder opciones, string connectionString) =>
        opciones.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Esquema));
}

/// <summary>Solo para dotnet-ef (generar migraciones); no se conecta a ninguna base.</summary>
public sealed class AvisosDbContextFactory : IDesignTimeDbContextFactory<AvisosDbContext>
{
    public AvisosDbContext CreateDbContext(string[] args)
    {
        var opciones = new DbContextOptionsBuilder<AvisosDbContext>();
        AvisosDbContext.Configurar(
            opciones,
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                ?? "Host=localhost;Database=diseno;Username=diseno;Password=diseno");
        return new AvisosDbContext(opciones.Options);
    }
}
