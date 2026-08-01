using Margen.Infrastructure;
using Margen.Ingest;
using Margen.Worker.Ingestion;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// El worker lee la misma base que el API. No aplica migraciones: eso lo hace
// el API y solo el API. Dos procesos migrando la misma base a la vez es una
// carrera que se resuelve con un bloqueo que a veces no llega a tiempo.
builder.Services.AddMargenDatabase(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<MailboxOptions>()
    .Bind(builder.Configuration.GetSection(MailboxOptions.SectionName));

// El registro de parsers. Cuando llegue la muestra real del banco, lo único
// que hará falta es añadir una clase aquí: todo lo que la rodea ya está
// escrito y probado.
builder.Services.AddSingleton(new ParserRegistry([new SampleBankParser()]));

builder.Services.AddHostedService<MailboxWorker>();

IHost host = builder.Build();

// Reproceso contra una versión nueva del parser:
//   dotnet run --project src/Margen.Worker -- --reprocesar
// No duplica: la huella del movimiento es índice único y la tubería trata su
// rechazo igual que una coincidencia exacta.
if (args.Contains("--reprocesar", StringComparer.Ordinal))
{
    await Reprocessor.RunAsync(host.Services, args);
    return;
}

// Captura de muestras para escribir el parser del banco:
//   dotnet run --project src/Margen.Worker -- --capturar-muestras
// Abre el buzón en solo lectura, redacta lo personal y escribe en
// docs/muestras/. No toca la base de datos ni el estado del buzón.
if (args.Contains("--capturar-muestras", StringComparer.Ordinal))
{
    Environment.ExitCode = await SampleCapture.RunAsync(host.Services, args);
    return;
}

await host.RunAsync();
