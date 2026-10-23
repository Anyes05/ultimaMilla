using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(PlataformaDbContext))]
[Migration("20261023030000_DispositivosRepartidor")]
public partial class DispositivosRepartidor : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DispositivosRepartidor",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                RepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                Token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Plataforma = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                RegistradoEn = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DispositivosRepartidor", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DispositivosRepartidor_OperadorId_Token",
            table: "DispositivosRepartidor",
            columns: new[] { "OperadorId", "Token" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DispositivosRepartidor");
    }
}
