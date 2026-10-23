using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(PlataformaDbContext))]
[Migration("20261023010000_SeguimientoUnicoComprobanteYZona")]
public partial class SeguimientoUnicoComprobanteYZona : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ZonaHoraria",
            table: "Operadores",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "America/Montevideo");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "FechaDispositivo",
            table: "PruebasDeEntrega",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), TimeSpan.Zero));

        migrationBuilder.AddColumn<string>(
            name: "NombreReceptor",
            table: "PruebasDeEntrega",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Observaciones",
            table: "PruebasDeEntrega",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Usuario",
            table: "PruebasDeEntrega",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_Envios_NumeroSeguimiento",
            table: "Envios",
            column: "NumeroSeguimiento",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Envios_TokenSeguimientoPublico",
            table: "Envios",
            column: "TokenSeguimientoPublico",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Envios_NumeroSeguimiento",
            table: "Envios");

        migrationBuilder.DropIndex(
            name: "IX_Envios_TokenSeguimientoPublico",
            table: "Envios");

        migrationBuilder.DropColumn(
            name: "ZonaHoraria",
            table: "Operadores");

        migrationBuilder.DropColumn(
            name: "FechaDispositivo",
            table: "PruebasDeEntrega");

        migrationBuilder.DropColumn(
            name: "NombreReceptor",
            table: "PruebasDeEntrega");

        migrationBuilder.DropColumn(
            name: "Observaciones",
            table: "PruebasDeEntrega");

        migrationBuilder.DropColumn(
            name: "Usuario",
            table: "PruebasDeEntrega");
    }
}
