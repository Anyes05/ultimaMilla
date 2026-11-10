using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Plataforma.Infrastructure;

namespace Plataforma.Infrastructure.Migrations;

[DbContext(typeof(PlataformaDbContext))]
[Migration("20261110161000_PruebaEntregaPorBulto")]
public partial class PruebaEntregaPorBulto : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "BultoId",
            table: "PruebasDeEntrega",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_PruebasDeEntrega_OperadorId_BultoId",
            table: "PruebasDeEntrega",
            columns: new[] { "OperadorId", "BultoId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_PruebasDeEntrega_OperadorId_BultoId", table: "PruebasDeEntrega");
        migrationBuilder.DropColumn(name: "BultoId", table: "PruebasDeEntrega");
    }
}