using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Plataforma.Infrastructure.Migrations;

[DbContext(typeof(PlataformaDbContext))]
[Migration("20261029210000_EvidenciaCargaMovil")]
public sealed class EvidenciaCargaMovil : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CodigoEscaneado",
            table: "PruebasDeEntrega",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CodigoEscaneado", table: "PruebasDeEntrega");
    }
}
