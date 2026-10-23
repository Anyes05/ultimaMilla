using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class WorkerAvisosYFallidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvisosFallidos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActualizadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResueltoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvisosFallidos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventosAvisoProcesados",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProcesadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventosAvisoProcesados", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "ReceptoresPruebaConfiguracion",
                columns: table => new
                {
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForzarFallo = table.Column<bool>(type: "boolean", nullable: false),
                    ActualizadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceptoresPruebaConfiguracion", x => x.OperadorId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvisosFallidos_EventId",
                table: "AvisosFallidos",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AvisosFallidos_OperadorId_ResueltoEn",
                table: "AvisosFallidos",
                columns: new[] { "OperadorId", "ResueltoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_EventosAvisoProcesados_OperadorId_EnvioId",
                table: "EventosAvisoProcesados",
                columns: new[] { "OperadorId", "EnvioId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvisosFallidos");

            migrationBuilder.DropTable(
                name: "EventosAvisoProcesados");

            migrationBuilder.DropTable(
                name: "ReceptoresPruebaConfiguracion");
        }
    }
}
