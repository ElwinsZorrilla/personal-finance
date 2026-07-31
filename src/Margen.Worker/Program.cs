using Margen.Infrastructure;
using Margen.Worker;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// El worker lee la misma base que el API. No aplica migraciones: eso lo hace
// el API y solo el API. Dos procesos migrando la misma base a la vez es una
// carrera que se resuelve con un bloqueo que a veces no llega a tiempo.
builder.Services.AddMargenDatabase(builder.Configuration);

builder.Services.AddHostedService<MailboxWorker>();

IHost host = builder.Build();
await host.RunAsync();
