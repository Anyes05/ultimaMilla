using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NucleoAnaBanderasRetirosCobertura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CapacidadPesoKg",
                table: "Vehiculos",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CapacidadVolumenCm3",
                table: "Vehiculos",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "MaximoParadas",
                table: "Vehiculos",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Motivo",
                table: "HistorialEstados",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "Responsable",
                table: "HistorialEstados",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BanderasOperador",
                columns: table => new
                {
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Clave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BanderasOperador", x => new { x.OperadorId, x.Clave });
                });

            migrationBuilder.CreateTable(
                name: "CambiosBandera",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Clave = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false),
                    Usuario = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CambiosBandera", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FranjasHorarias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZonaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Desde = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Hasta = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FranjasHorarias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecepcionesEnvio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    BultosEsperados = table.Column<int>(type: "integer", nullable: false),
                    BultosRecibidos = table.Column<int>(type: "integer", nullable: false),
                    HayDiscrepancia = table.Column<bool>(type: "boolean", nullable: false),
                    Observaciones = table.Column<string>(type: "text", nullable: false),
                    Usuario = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecepcionesEnvio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SolicitudesRetiro",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direccion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Contacto = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Observaciones = table.Column<string>(type: "text", nullable: false),
                    FechaSolicitada = table.Column<DateOnly>(type: "date", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: true),
                    EnvioAsociadoId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreadoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SolicitudesRetiro", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CambiosBandera_OperadorId_Fecha",
                table: "CambiosBandera",
                columns: new[] { "OperadorId", "Fecha" });

            migrationBuilder.CreateIndex(
                name: "IX_RecepcionesEnvio_OperadorId_EnvioId",
                table: "RecepcionesEnvio",
                columns: new[] { "OperadorId", "EnvioId" });

            migrationBuilder.CreateIndex(
                name: "IX_SolicitudesRetiro_OperadorId_Estado",
                table: "SolicitudesRetiro",
                columns: new[] { "OperadorId", "Estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BanderasOperador");

            migrationBuilder.DropTable(
                name: "CambiosBandera");

            migrationBuilder.DropTable(
                name: "FranjasHorarias");

            migrationBuilder.DropTable(
                name: "RecepcionesEnvio");

            migrationBuilder.DropTable(
                name: "SolicitudesRetiro");

            migrationBuilder.DropColumn(
                name: "CapacidadPesoKg",
                table: "Vehiculos");

            migrationBuilder.DropColumn(
                name: "CapacidadVolumenCm3",
                table: "Vehiculos");

            migrationBuilder.DropColumn(
                name: "MaximoParadas",
                table: "Vehiculos");

            migrationBuilder.DropColumn(
                name: "Responsable",
                table: "HistorialEstados");

            migrationBuilder.AlterColumn<string>(
                name: "Motivo",
                table: "HistorialEstados",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);
        }
    }
}
