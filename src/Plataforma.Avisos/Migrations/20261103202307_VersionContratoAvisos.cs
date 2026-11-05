using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Avisos.Migrations
{
    /// <inheritdoc />
    public partial class VersionContratoAvisos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VersionContrato",
                schema: "avisos",
                table: "Suscripciones",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "VersionContrato",
                schema: "avisos",
                table: "Entregas",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VersionContrato",
                schema: "avisos",
                table: "Suscripciones");

            migrationBuilder.DropColumn(
                name: "VersionContrato",
                schema: "avisos",
                table: "Entregas");
        }
    }
}
