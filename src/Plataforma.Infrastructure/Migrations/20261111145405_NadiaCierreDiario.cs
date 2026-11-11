using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NadiaCierreDiario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CierresDiarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Dia = table.Column<DateOnly>(type: "date", nullable: false),
                    EnviosAdmitidos = table.Column<int>(type: "integer", nullable: false),
                    Entregados = table.Column<int>(type: "integer", nullable: false),
                    EntregadosConPlazo = table.Column<int>(type: "integer", nullable: false),
                    EntregadosATiempo = table.Column<int>(type: "integer", nullable: false),
                    IntentosFallidos = table.Column<int>(type: "integer", nullable: false),
                    Devueltos = table.Column<int>(type: "integer", nullable: false),
                    RetirosCompletados = table.Column<int>(type: "integer", nullable: false),
                    CalculadoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CierresDiarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CierresDiarios_OperadorId_Dia",
                table: "CierresDiarios",
                columns: new[] { "OperadorId", "Dia" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CierresDiarios");
        }
    }
}
