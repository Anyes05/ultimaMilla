using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(PlataformaDbContext))]
[Migration("20261023040000_EventosSincronizacionProcesados")]
public partial class EventosSincronizacionProcesados : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "EventosSincronizacionProcesados",
            columns: table => new
            {
                EventoId = table.Column<Guid>(type: "uuid", nullable: false),
                OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                Estado = table.Column<int>(type: "integer", nullable: false),
                IntentosRealizados = table.Column<int>(type: "integer", nullable: false),
                ProcesadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EventosSincronizacionProcesados", x => x.EventoId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_EventosSincronizacionProcesados_OperadorId_EnvioId",
            table: "EventosSincronizacionProcesados",
            columns: new[] { "OperadorId", "EnvioId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "EventosSincronizacionProcesados");
    }
}
