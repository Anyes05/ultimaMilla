using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Plataforma.Infrastructure.Migrations;

[DbContext(typeof(PlataformaDbContext))]
[Migration("20261112120000_InicioExplicitoRuta")]
public sealed class InicioExplicitoRuta : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<DateTimeOffset>("IniciadaEn", "Rutas",
            type: "timestamp with time zone", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("IniciadaEn", "Rutas");
}