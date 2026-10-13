using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Plataforma.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AvisosComercio",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mensaje = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Leido = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvisosComercio", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Comercios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreComercial = table.Column<string>(type: "text", nullable: false),
                    RazonSocial = table.Column<string>(type: "text", nullable: false),
                    DocumentoFiscal = table.Column<string>(type: "text", nullable: false),
                    DireccionPrincipal = table.Column<string>(type: "text", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comercios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Destinatarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    NombreCompleto = table.Column<string>(type: "text", nullable: false),
                    Documento = table.Column<string>(type: "text", nullable: false),
                    Telefono = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Destinatarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Direcciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Calle = table.Column<string>(type: "text", nullable: false),
                    Numero = table.Column<string>(type: "text", nullable: false),
                    Apartamento = table.Column<string>(type: "text", nullable: false),
                    Referencia = table.Column<string>(type: "text", nullable: false),
                    Latitud = table.Column<decimal>(type: "numeric", nullable: true),
                    Longitud = table.Column<decimal>(type: "numeric", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Direcciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Envios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    NumeroSeguimiento = table.Column<string>(type: "text", nullable: false),
                    TokenSeguimientoPublico = table.Column<string>(type: "text", nullable: false),
                    Destinatario = table.Column<string>(type: "text", nullable: false),
                    DireccionEntrega = table.Column<string>(type: "text", nullable: false),
                    EmailDestinatario = table.Column<string>(type: "text", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    TarifaCalculada = table.Column<decimal>(type: "numeric", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: true),
                    IntentosRealizados = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Envios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HistorialEstados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Estado = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Motivo = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistorialEstados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Incidencias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Resuelta = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidencias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntentosEntrega",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Numero = table.Column<int>(type: "integer", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Resultado = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntentosEntrega", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Liquidaciones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodoDesde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodoHasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Monto = table.Column<decimal>(type: "numeric", nullable: false),
                    Cerrada = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Liquidaciones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Novedades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Novedades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Operadores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    ColorMarca = table.Column<string>(type: "text", nullable: false),
                    MaximoIntentos = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operadores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Paradas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RutaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Orden = table.Column<int>(type: "integer", nullable: false),
                    CompletadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paradas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PruebasDeEntrega",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Foto = table.Column<string>(type: "text", nullable: false),
                    Firma = table.Column<string>(type: "text", nullable: false),
                    Latitud = table.Column<decimal>(type: "numeric", nullable: true),
                    Longitud = table.Column<decimal>(type: "numeric", nullable: true),
                    Fecha = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PruebasDeEntrega", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReglasOperativas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaximoIntentos = table.Column<int>(type: "integer", nullable: false),
                    PlazoEntregaHoras = table.Column<int>(type: "integer", nullable: false),
                    ExigeFirma = table.Column<bool>(type: "boolean", nullable: false),
                    ExigeFoto = table.Column<bool>(type: "boolean", nullable: false),
                    ExigeGeocerca = table.Column<bool>(type: "boolean", nullable: false),
                    PlazoReprogramacionHoras = table.Column<int>(type: "integer", nullable: false),
                    VigenciaDesde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VigenciaHasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglasOperativas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReglasTarifarias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrecioBase = table.Column<decimal>(type: "numeric", nullable: false),
                    PrecioPorKg = table.Column<decimal>(type: "numeric", nullable: false),
                    PrecioPorKm = table.Column<decimal>(type: "numeric", nullable: false),
                    RecargoFragil = table.Column<decimal>(type: "numeric", nullable: false),
                    RecargoUrgente = table.Column<decimal>(type: "numeric", nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReglasTarifarias", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Repartidores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Documento = table.Column<string>(type: "text", nullable: false),
                    Telefono = table.Column<string>(type: "text", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repartidores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rutas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RepartidorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Fecha = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rutas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SuscripcionesWebhook",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Activa = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SuscripcionesWebhook", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComercioId = table.Column<Guid>(type: "uuid", nullable: true),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    NombreUsuario = table.Column<string>(type: "text", nullable: false),
                    HashClave = table.Column<string>(type: "text", nullable: false),
                    Rol = table.Column<int>(type: "integer", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Usuarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vehiculos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Patente = table.Column<string>(type: "text", nullable: false),
                    Descripcion = table.Column<string>(type: "text", nullable: false),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehiculos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VigenciasTarifa",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReglaTarifariaId = table.Column<Guid>(type: "uuid", nullable: false),
                    VigenciaDesde = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VigenciaHasta = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VigenciasTarifa", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ZonasCobertura",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperadorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nombre = table.Column<string>(type: "text", nullable: false),
                    Poligono = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZonasCobertura", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvisosComercio");

            migrationBuilder.DropTable(
                name: "Comercios");

            migrationBuilder.DropTable(
                name: "Destinatarios");

            migrationBuilder.DropTable(
                name: "Direcciones");

            migrationBuilder.DropTable(
                name: "Envios");

            migrationBuilder.DropTable(
                name: "HistorialEstados");

            migrationBuilder.DropTable(
                name: "Incidencias");

            migrationBuilder.DropTable(
                name: "IntentosEntrega");

            migrationBuilder.DropTable(
                name: "Liquidaciones");

            migrationBuilder.DropTable(
                name: "Novedades");

            migrationBuilder.DropTable(
                name: "Operadores");

            migrationBuilder.DropTable(
                name: "Paradas");

            migrationBuilder.DropTable(
                name: "PruebasDeEntrega");

            migrationBuilder.DropTable(
                name: "ReglasOperativas");

            migrationBuilder.DropTable(
                name: "ReglasTarifarias");

            migrationBuilder.DropTable(
                name: "Repartidores");

            migrationBuilder.DropTable(
                name: "Rutas");

            migrationBuilder.DropTable(
                name: "SuscripcionesWebhook");

            migrationBuilder.DropTable(
                name: "Usuarios");

            migrationBuilder.DropTable(
                name: "Vehiculos");

            migrationBuilder.DropTable(
                name: "VigenciasTarifa");

            migrationBuilder.DropTable(
                name: "ZonasCobertura");
        }
    }
}
