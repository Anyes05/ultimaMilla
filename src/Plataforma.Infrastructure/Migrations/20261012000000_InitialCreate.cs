using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations;

public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Envios",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                NumeroSeguimiento = table.Column<string>(type: "text", nullable: false),
                TokenSeguimientoPublico = table.Column<string>(type: "text", nullable: false),
                Destinatario = table.Column<string>(type: "text", nullable: false),
                DireccionEntrega = table.Column<string>(type: "text", nullable: false),
                EmailDestinatario = table.Column<string>(type: "text", nullable: false),
                Estado = table.Column<int>(type: "integer", nullable: false),
                TarifaCalculada = table.Column<decimal>(type: "numeric", nullable: false),
                FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Envios", x => x.Id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Envios");
    }
}