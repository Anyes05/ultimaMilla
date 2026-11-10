using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Plataforma.Infrastructure.Migrations;

[DbContext(typeof(PlataformaDbContext))]
[Migration("20261110160000_MotivosNoEntregaRegla")]
public partial class MotivosNoEntregaRegla : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string[]>(
            name: "MotivosNoEntrega",
            table: "ReglasOperativas",
            type: "text[]",
            nullable: false,
            defaultValueSql: "ARRAY['Ausente','Destinatario ausente','Rechazo del destinatario','Domicilio no encontrado','Bulto danado']::text[]");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MotivosNoEntrega", table: "ReglasOperativas");
    }
}