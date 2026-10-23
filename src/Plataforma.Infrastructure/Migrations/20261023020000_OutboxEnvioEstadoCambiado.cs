using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations;

/// <inheritdoc />
public partial class OutboxEnvioEstadoCambiado : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MensajesOutbox",
            columns: table => new
            {
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                Tipo = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Version = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Payload = table.Column<string>(type: "text", nullable: false),
                Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PublicadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MensajesOutbox", x => x.EventId);
            });

        migrationBuilder.CreateIndex(
            name: "IX_MensajesOutbox_OperadorId_PublicadoEn",
            table: "MensajesOutbox",
            columns: new[] { "OperadorId", "PublicadoEn" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "MensajesOutbox");
    }
}
