using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Avisos.Migrations
{
    /// <inheritdoc />
    public partial class InicialAvisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "avisos");

            migrationBuilder.CreateTable(
                name: "Entregas",
                schema: "avisos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SuscripcionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TipoEvento = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Intentos = table.Column<int>(type: "integer", nullable: false),
                    UltimoCodigoHttp = table.Column<int>(type: "integer", nullable: true),
                    UltimoError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EntregadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EnCursoDesde = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Entregas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suscripciones",
                schema: "avisos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Secreto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TiposEvento = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false),
                    CreadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActualizadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suscripciones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Entregas_EventId_SuscripcionId",
                schema: "avisos",
                table: "Entregas",
                columns: new[] { "EventId", "SuscripcionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Entregas_OperadorId_ComercioId_CreadaEn",
                schema: "avisos",
                table: "Entregas",
                columns: new[] { "OperadorId", "ComercioId", "CreadaEn" });

            migrationBuilder.CreateIndex(
                name: "IX_Entregas_OperadorId_Estado",
                schema: "avisos",
                table: "Entregas",
                columns: new[] { "OperadorId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Suscripciones_OperadorId_ComercioId",
                schema: "avisos",
                table: "Suscripciones",
                columns: new[] { "OperadorId", "ComercioId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Entregas",
                schema: "avisos");

            migrationBuilder.DropTable(
                name: "Suscripciones",
                schema: "avisos");
        }
    }
}
