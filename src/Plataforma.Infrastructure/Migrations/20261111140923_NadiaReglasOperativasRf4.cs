using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NadiaReglasOperativasRf4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExigeDocumentoReceptor",
                table: "ReglasOperativas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PlazoEntreIntentosHoras",
                table: "ReglasOperativas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PlazoEntregaUrgenteHoras",
                table: "ReglasOperativas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PoliticaDevolucion",
                table: "ReglasOperativas",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DocumentoReceptor",
                table: "PruebasDeEntrega",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "Urgente",
                table: "Envios",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExigeDocumentoReceptor",
                table: "ReglasOperativas");

            migrationBuilder.DropColumn(
                name: "PlazoEntreIntentosHoras",
                table: "ReglasOperativas");

            migrationBuilder.DropColumn(
                name: "PlazoEntregaUrgenteHoras",
                table: "ReglasOperativas");

            migrationBuilder.DropColumn(
                name: "PoliticaDevolucion",
                table: "ReglasOperativas");

            migrationBuilder.DropColumn(
                name: "DocumentoReceptor",
                table: "PruebasDeEntrega");

            migrationBuilder.DropColumn(
                name: "Urgente",
                table: "Envios");
        }
    }
}
