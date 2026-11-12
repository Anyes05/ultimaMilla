using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NadiaRetirosEnLaApp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComprobantesRetiro",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RetiroId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                    Resultado = table.Column<int>(type: "integer", nullable: false),
                    CodigosBultos = table.Column<string>(type: "character varying(10200)", maxLength: 10200, nullable: false),
                    Foto = table.Column<string>(type: "text", nullable: false),
                    Firma = table.Column<string>(type: "text", nullable: false),
                    NombreEntrega = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Motivo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    FechaDispositivo = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Latitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitud = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: true),
                    Usuario = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RegistradoUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprobantesRetiro", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesRetiro_OperadorId_EventoId",
                table: "ComprobantesRetiro",
                columns: new[] { "OperadorId", "EventoId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComprobantesRetiro_RetiroId",
                table: "ComprobantesRetiro",
                column: "RetiroId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComprobantesRetiro");
        }
    }
}
