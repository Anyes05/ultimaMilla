using Plataforma.Worker;
using Microsoft.EntityFrameworkCore;
using Plataforma.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<OpcionesRabbitMqWorker>(builder.Configuration.GetSection(OpcionesRabbitMqWorker.Seccion));
builder.Services.Configure<OpcionesAvisos>(builder.Configuration.GetSection(OpcionesAvisos.Seccion));
builder.Services.AddDbContextFactory<PlataformaDbContext>(opciones =>
	opciones.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpClient("receptor-avisos", cliente =>
	cliente.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHostedService<ConsumidorEnvioEstadoCambiado>();

// Apagado controlado: da tiempo a terminar el mensaje en curso antes de cortar (SIGTERM de Kubernetes).
builder.Services.Configure<HostOptions>(opciones => opciones.ShutdownTimeout = TimeSpan.FromSeconds(20));

builder.Build().Run();
