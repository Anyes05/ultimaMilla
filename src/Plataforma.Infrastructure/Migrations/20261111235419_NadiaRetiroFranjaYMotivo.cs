using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NadiaRetiroFranjaYMotivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FranjaId",
                table: "SolicitudesRetiro",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Motivo",
                table: "SolicitudesRetiro",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FranjaId",
                table: "SolicitudesRetiro");

            migrationBuilder.DropColumn(
                name: "Motivo",
                table: "SolicitudesRetiro");
        }
    }
}
