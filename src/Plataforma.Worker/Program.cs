using Plataforma.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<OpcionesRabbitMqWorker>(builder.Configuration.GetSection(OpcionesRabbitMqWorker.Seccion));
builder.Services.Configure<OpcionesAvisos>(builder.Configuration.GetSection(OpcionesAvisos.Seccion));
builder.Services.AddHostedService<ConsumidorEnvioEstadoCambiado>();

// Apagado controlado: da tiempo a terminar el mensaje en curso antes de cortar (SIGTERM de Kubernetes).
builder.Services.Configure<HostOptions>(opciones => opciones.ShutdownTimeout = TimeSpan.FromSeconds(20));

builder.Build().Run();
