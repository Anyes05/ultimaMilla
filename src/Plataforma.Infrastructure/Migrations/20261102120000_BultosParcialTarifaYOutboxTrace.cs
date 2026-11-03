using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Plataforma.Infrastructure;

#nullable disable

namespace Plataforma.Infrastructure.Migrations;

/// <summary>
/// Gate 04/11: bultos/parcial, snapshot tarifario, concurrencia envío, reglas parciales, traceparent outbox.
/// </summary>
[DbContext(typeof(PlataformaDbContext))]
[Migration("20261102120000_BultosParcialTarifaYOutboxTrace")]
public partial class BultosParcialTarifaYOutboxTrace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ReglaTarifariaId",
            table: "Envios",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "VigenciaTarifaId",
            table: "Envios",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Version",
            table: "Envios",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "TraceParent",
            table: "MensajesOutbox",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "AdmiteEntregaParcial",
            table: "ReglasOperativas",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "PoliticaBultosPendientes",
            table: "ReglasOperativas",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "Bultos",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                Codigo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PesoKg = table.Column<decimal>(type: "numeric", nullable: false),
                AltoCm = table.Column<decimal>(type: "numeric", nullable: false),
                AnchoCm = table.Column<decimal>(type: "numeric", nullable: false),
                LargoCm = table.Column<decimal>(type: "numeric", nullable: false),
                Estado = table.Column<int>(type: "integer", nullable: false),
                MotivoNoEntrega = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Bultos", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Bultos_OperadorId_EnvioId_Codigo",
            table: "Bultos",
            columns: new[] { "OperadorId", "EnvioId", "Codigo" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Bultos_OperadorId_Estado",
            table: "Bultos",
            columns: new[] { "OperadorId", "Estado" });

        migrationBuilder.Sql("""
            INSERT INTO "Bultos" ("Id", "OperadorId", "EnvioId", "Codigo", "PesoKg", "AltoCm", "AnchoCm", "LargoCm", "Estado", "MotivoNoEntrega")
            SELECT gen_random_uuid(), e."OperadorId", e."Id", e."NumeroSeguimiento" || '-B01', 0, 0, 0, 0, 0, NULL
            FROM "Envios" e
            WHERE NOT EXISTS (
                SELECT 1 FROM "Bultos" b WHERE b."EnvioId" = e."Id"
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Bultos");

        migrationBuilder.DropColumn(name: "ReglaTarifariaId", table: "Envios");
        migrationBuilder.DropColumn(name: "VigenciaTarifaId", table: "Envios");
        migrationBuilder.DropColumn(name: "Version", table: "Envios");
        migrationBuilder.DropColumn(name: "TraceParent", table: "MensajesOutbox");
        migrationBuilder.DropColumn(name: "AdmiteEntregaParcial", table: "ReglasOperativas");
        migrationBuilder.DropColumn(name: "PoliticaBultosPendientes", table: "ReglasOperativas");
    }
}
