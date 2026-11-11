using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NadiaRetirosEstadosYCapacidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CantidadBultos",
                table: "SolicitudesRetiro",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "MotivoNoRetiro",
                table: "SolicitudesRetiro",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "PesoKg",
                table: "SolicitudesRetiro",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PosicionEnRuta",
                table: "SolicitudesRetiro",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "VolumenCm3",
                table: "SolicitudesRetiro",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "VehiculoId",
                table: "Rutas",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CantidadBultos",
                table: "SolicitudesRetiro");

            migrationBuilder.DropColumn(
                name: "MotivoNoRetiro",
                table: "SolicitudesRetiro");

            migrationBuilder.DropColumn(
                name: "PesoKg",
                table: "SolicitudesRetiro");

            migrationBuilder.DropColumn(
                name: "PosicionEnRuta",
                table: "SolicitudesRetiro");

            migrationBuilder.DropColumn(
                name: "VolumenCm3",
                table: "SolicitudesRetiro");

            migrationBuilder.DropColumn(
                name: "VehiculoId",
                table: "Rutas");
        }
    }
}
