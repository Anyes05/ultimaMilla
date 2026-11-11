using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NadiaTarifaPorZonaYBonificaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BonificacionesTarifa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReglaTarifariaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Porcentaje = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: true),
                    MinimoEnviosMes = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BonificacionesTarifa", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecargosZonaTarifa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReglaTarifariaId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Recargo = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecargosZonaTarifa", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BonificacionesTarifa_ReglaTarifariaId",
                table: "BonificacionesTarifa",
                column: "ReglaTarifariaId");

            migrationBuilder.CreateIndex(
                name: "IX_RecargosZonaTarifa_ReglaTarifariaId_ZonaId",
                table: "RecargosZonaTarifa",
                columns: new[] { "ReglaTarifariaId", "ZonaId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BonificacionesTarifa");

            migrationBuilder.DropTable(
                name: "RecargosZonaTarifa");
        }
    }
}
