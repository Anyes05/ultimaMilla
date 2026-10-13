using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace Plataforma.Infrastructure.Migrations;

[DbContext(typeof(PlataformaDbContext))]
public partial class PlataformaDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasAnnotation("ProductVersion", "10.0.12")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        modelBuilder.Entity("Plataforma.Domain.Envio", entity =>
        {
            entity.Property<Guid>("Id")
                .ValueGeneratedOnAdd()
                .HasColumnType("uuid");

            entity.Property<Guid>("ComercioId").HasColumnType("uuid");
            entity.Property<string>("Destinatario").IsRequired().HasColumnType("text");
            entity.Property<string>("DireccionEntrega").IsRequired().HasColumnType("text");
            entity.Property<string>("EmailDestinatario").IsRequired().HasColumnType("text");
            entity.Property<int>("Estado").HasColumnType("integer");
            entity.Property<DateTime>("FechaCreacion").HasColumnType("timestamp with time zone");
            entity.Property<string>("NumeroSeguimiento").IsRequired().HasColumnType("text");
            entity.Property<Guid>("OperadorId").HasColumnType("uuid");
            entity.Property<decimal>("TarifaCalculada").HasColumnType("numeric");
            entity.Property<string>("TokenSeguimientoPublico").IsRequired().HasColumnType("text");

            entity.HasKey("Id");
            entity.ToTable("Envios");
        });
    }
}