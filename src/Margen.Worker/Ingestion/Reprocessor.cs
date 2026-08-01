using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Margen.Ingest;
using Microsoft.EntityFrameworkCore;

namespace Margen.Worker.Ingestion;

/// <summary>
/// Vuelve a interpretar correos ya guardados contra la versión actual de los
/// parsers.
/// </summary>
/// <remarks>
/// Es lo que hace útil guardar el cuerpo original. Cuando un parser mejora
/// —aprende un formato que antes no reconocía, corrige un monto mal leído—, los
/// correos que quedaron en `Unrecognized` o `Failed` se pueden volver a mirar
/// sin pedirle nada al banco.
///
/// **No duplica.** La tubería es la misma que la de la primera vez, así que la
/// huella del movimiento sigue siendo índice único y una reinterpretación que
/// llegue al mismo movimiento se resuelve como coincidencia exacta.
/// </remarks>
public static class Reprocessor
{
    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(args);

        using IServiceScope scope = services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<MargenDbContext>();
        var parsers = scope.ServiceProvider.GetRequiredService<ParserRegistry>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        bool todos = args.Contains("--todos", StringComparer.Ordinal);

        Dictionary<string, int> versions = parsers.All
            .ToDictionary(p => p.Name, p => p.Version, StringComparer.OrdinalIgnoreCase);

        List<IncomingEmail> pendientes = await db.IncomingEmails
            .Where(e => todos
                || e.Status == EmailStatus.Unrecognized
                || e.Status == EmailStatus.Failed
                || e.ParserVersion == null)
            .OrderBy(e => e.ReceivedAt)
            .ToListAsync()
            .ConfigureAwait(false);

        // Los que ya se interpretaron con la versión actual no se vuelven a
        // mirar salvo que se pida `--todos`: reprocesar lo que ya está al día
        // es trabajo sin resultado posible.
        if (!todos)
        {
            pendientes = [.. pendientes.Where(e =>
                e.ParserName is null
                || !versions.TryGetValue(e.ParserName, out int actual)
                || e.ParserVersion is null
                || e.ParserVersion < actual)];
        }

        var ingestor = new EmailIngestor(db, parsers, clock);
        var resumen = new Dictionary<IngestOutcome, int>();

        foreach (IncomingEmail email in pendientes)
        {
            IngestReport report = await ingestor
                .ProcessAsync(email, CancellationToken.None)
                .ConfigureAwait(false);

            resumen[report.Outcome] = resumen.GetValueOrDefault(report.Outcome) + 1;
        }

        Console.WriteLine($"Reprocesados: {pendientes.Count}");
        foreach ((IngestOutcome outcome, int count) in resumen.OrderBy(p => p.Key.ToString(), StringComparer.Ordinal))
        {
            Console.WriteLine($"  {outcome}: {count}");
        }
    }
}
