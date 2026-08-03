using Margen.Infrastructure;
using Margen.Infrastructure.Classification;
using Margen.Ingest;
using Margen.Worker;
using Margen.Worker.Ingestion;

// Qué se va a hacer, antes de montar nada. Las herramientas de línea de órdenes
// no necesitan lo mismo que el servicio: pedirle una base de datos a la captura
// de muestras —que no escribe una sola fila— la hacía fallar por algo que no
// usa.
bool capturarMuestras = args.Contains("--capturar-muestras", StringComparer.Ordinal);
bool reprocesar = args.Contains("--reprocesar", StringComparer.Ordinal);
bool servicio = !capturarMuestras && !reprocesar;

// Para la ejecución local: `infra/.env` rellena lo que no esté ya en el
// entorno. En el servidor no hace nada, porque el compose ya pasó las
// variables al contenedor.
DotEnv.Load();

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<MailboxOptions>()
    .Bind(builder.Configuration.GetSection(MailboxOptions.SectionName));

// El registro de parsers, en orden. Cada banco elige por remitente, así que
// añadir uno no toca a los que ya funcionan; el sintético se queda al final
// para que la tubería siga teniendo una prueba de punta a punta que no dependa
// de ningún banco real.
builder.Services.AddSingleton(new ParserRegistry([
    new PopularParser(),
    new QikParser(),
    new BanreservasParser(),
    new SampleBankParser(),
]));

if (servicio || reprocesar)
{
    // El worker lee la misma base que el API. No aplica migraciones: eso lo
    // hace el API y solo el API. Dos procesos migrando la misma base a la vez
    // es una carrera que se resuelve con un bloqueo que a veces no llega a
    // tiempo.
    builder.Services.AddMargenDatabase(builder.Configuration);

    // El clasificador y el detector de anomalías. Sin ICategorySuggester
    // registrado, la cascada corre sus cuatro escalones locales y se salta el
    // modelo: no hay proveedor decidido, y enchufar uno a escondidas sería
    // tomar esa decisión sin que nadie la vea.
    builder.Services.AddScoped<TransactionClassifier>();
    builder.Services.AddScoped<AnomalyScanner>();
}

if (servicio)
{
    builder.Services.AddHostedService<MailboxWorker>();
}

IHost host = builder.Build();

// Captura de muestras para escribir el parser del banco:
//   dotnet run --project src/Margen.Worker -- --capturar-muestras
// Abre el buzón en solo lectura, redacta lo personal y escribe en
// docs/muestras/. No toca la base de datos ni el estado del buzón.
if (capturarMuestras)
{
    Environment.ExitCode = await SampleCapture.RunAsync(host.Services, args);
    return;
}

// Reproceso contra una versión nueva del parser:
//   dotnet run --project src/Margen.Worker -- --reprocesar
// No duplica: la huella del movimiento es índice único y la tubería trata su
// rechazo igual que una coincidencia exacta.
if (reprocesar)
{
    await Reprocessor.RunAsync(host.Services, args);
    return;
}

await host.RunAsync();
