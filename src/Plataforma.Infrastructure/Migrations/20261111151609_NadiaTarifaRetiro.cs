using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NadiaTarifaRetiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReglaTarifariaId",
                table: "SolicitudesRetiro",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TarifaCalculada",
                table: "SolicitudesRetiro",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PrecioRetiro",
                table: "ReglasTarifarias",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReglaTarifariaId",
                table: "SolicitudesRetiro");

            migrationBuilder.DropColumn(
                name: "TarifaCalculada",
                table: "SolicitudesRetiro");

            migrationBuilder.DropColumn(
                name: "PrecioRetiro",
                table: "ReglasTarifarias");
        }
    }
}
