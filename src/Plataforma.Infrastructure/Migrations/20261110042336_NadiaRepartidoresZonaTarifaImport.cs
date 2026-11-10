using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NadiaRepartidoresZonaTarifaImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PrecioPorCm3",
                table: "ReglasTarifarias",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RecargoZona",
                table: "ReglasTarifarias",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "ClaveExterna",
                table: "Envios",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FranjaId",
                table: "Envios",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ZonaId",
                table: "Envios",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Envios_OperadorId_ComercioId_ClaveExterna",
                table: "Envios",
                columns: new[] { "OperadorId", "ComercioId", "ClaveExterna" },
                unique: true,
                filter: "\"ClaveExterna\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Envios_OperadorId_ComercioId_ClaveExterna",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "PrecioPorCm3",
                table: "ReglasTarifarias");

            migrationBuilder.DropColumn(
                name: "RecargoZona",
                table: "ReglasTarifarias");

            migrationBuilder.DropColumn(
                name: "ClaveExterna",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "FranjaId",
                table: "Envios");

            migrationBuilder.DropColumn(
                name: "ZonaId",
                table: "Envios");
        }
    }
}
