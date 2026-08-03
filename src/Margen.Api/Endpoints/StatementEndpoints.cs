using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Margen.Infrastructure.Statements;
using Margen.Ingest.Statements;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Endpoints;

/// <summary>
/// Importar el estado de cuenta del banco y cuadrarlo.
/// </summary>
/// <remarks>
/// Un perfil por banco dice qué columna es cuál. Se escribe una vez, se ve una
/// vista previa, y a partir de ahí importar el estado de cuenta del mes es
/// subir un archivo.
///
/// **La vista previa no es un adorno.** Un mapeo con la columna de fecha
/// equivocada se nota ahí, no después de meter trescientos movimientos con el
/// día mal; y un formato de fecha ambiguo —`01/02/2026`— solo se puede resolver
/// enseñándole a una persona qué se entendió.
/// </remarks>
public static class StatementEndpoints
{
    /// <summary>
    /// Tope del archivo: 4 MB.
    /// </summary>
    /// <remarks>
    /// Un estado de cuenta de un año son unos cientos de kilobytes. El tope
    /// está para que el cuerpo de una petición no pueda llenar la memoria del
    /// proceso, no porque haga falta.
    /// </remarks>
    private const int MaxBytes = 4 * 1024 * 1024;

    public static IEndpointRouteBuilder MapStatementEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/statements").WithTags("Conciliación");

        group.MapGet("/profiles", ListProfilesAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ListStatementProfiles")
            .Produces<Page<StatementProfileView>>();

        group.MapPost("/profiles", CreateProfileAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("CreateStatementProfile")
            .Produces<StatementProfileView>(StatusCodes.Status201Created);

        group.MapDelete("/profiles/{id:guid}", DeleteProfileAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("DeleteStatementProfile");

        group.MapPost("/{profileId:guid}/preview", PreviewAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("PreviewStatement")
            .Produces<StatementPreviewView>();

        group.MapPost("/{profileId:guid}/import", ImportAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ImportStatement")
            .Produces<ImportReportView>();

        return app;
    }

    private static async Task<IResult> ListProfilesAsync(
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken)
    {
        List<StatementProfileView> rows = await db.StatementProfiles
            .AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new StatementProfileView(
                p.Id, p.Name, p.AccountId, p.Delimiter, p.SkipRows,
                p.DateColumn, p.DateFormat, p.DescriptionColumn,
                p.AmountColumn, p.DebitColumn, p.CreditColumn, p.Decimals, p.InvertSign))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new Page<StatementProfileView>(rows, null, rows.Count));
    }

    private static async Task<IResult> CreateProfileAsync(
        [FromBody] CreateStatementProfileRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiResults.BadRequest("El perfil necesita un nombre.");
        }

        if (request.AmountColumn is null
            && request.DebitColumn is null
            && request.CreditColumn is null)
        {
            // Un perfil sin manera de leer el monto no puede importar nada, y
            // guardarlo deja una opción en la lista que falla al usarla.
            return ApiResults.BadRequest(
                "Falta decir de qué columna sale el monto: una con signo, o cargo y abono.");
        }

        if (string.IsNullOrWhiteSpace(request.DateFormat))
        {
            return ApiResults.BadRequest(
                "Falta el formato de la fecha. Es explícito a propósito: «01/02/2026» "
                + "es el 1 de febrero o el 2 de enero según el banco.");
        }

        bool accountExists = await db.Accounts
            .AnyAsync(a => a.Id == request.AccountId, cancellationToken)
            .ConfigureAwait(false);

        if (!accountExists) return ApiResults.BadRequest("Esa cuenta no existe.");

        DateTime now = clock.GetUtcNow().UtcDateTime;

        var profile = new StatementProfile
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            AccountId = request.AccountId,
            Delimiter = string.IsNullOrEmpty(request.Delimiter) ? null : request.Delimiter,
            SkipRows = Math.Max(0, request.SkipRows),
            DateColumn = request.DateColumn,
            DateFormat = request.DateFormat.Trim(),
            DescriptionColumn = request.DescriptionColumn,
            AmountColumn = request.AmountColumn,
            DebitColumn = request.DebitColumn,
            CreditColumn = request.CreditColumn,
            Decimals = request.Decimals ?? "Point",
            InvertSign = request.InvertSign,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.StatementProfiles.Add(profile);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // El nombre es único: dos perfiles llamados igual hacen imposible
            // saber cuál se está usando al elegirlo en una lista.
            return ApiResults.Conflict("Ya hay un perfil con ese nombre.");
        }

        return Results.Created($"/statements/profiles/{profile.Id}", ToView(profile));
    }

    private static async Task<IResult> DeleteProfileAsync(
        Guid id,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        StatementProfile? profile = await db.StatementProfiles
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null) return ApiResults.NotFound("No existe ese perfil.");

        // Se desactiva, no se borra: los movimientos que entraron por él siguen
        // ahí y saber de dónde salieron es lo que permite deshacer una
        // importación equivocada.
        profile.IsActive = false;
        profile.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.NoContent();
    }

    private static Task<IResult> PreviewAsync(
        Guid profileId,
        [FromServices] MargenDbContext db,
        [FromServices] StatementService statements,
        HttpRequest http,
        CancellationToken cancellationToken) =>
        RunAsync(profileId, db, http, async (profile, content) =>
        {
            Outcome<StatementPreview> preview = await statements
                .PreviewAsync(profile, content, cancellationToken).ConfigureAwait(false);

            if (!preview.IsComputed) return ApiResults.BadRequest(preview.Reason!);

            StatementPreview p = preview.Value;

            return Results.Ok(new StatementPreviewView(
                p.Delimiter.ToString(),
                [.. p.Report.Verdicts.Select(v => new StatementLineView(
                    v.Line.LineNumber,
                    v.Line.Date,
                    v.Line.Description,
                    v.Line.Amount.Cents,
                    v.Line.Direction.ToString(),
                    v.State.ToString(),
                    v.TransactionId,
                    v.Difference.Cents))],
                [.. p.Rejected.Select(r => new RejectedLineView(r.LineNumber, r.Raw, r.Reason))],
                p.Report.PendingTransactionIds.Count));
        }, cancellationToken);

    private static Task<IResult> ImportAsync(
        Guid profileId,
        [FromServices] MargenDbContext db,
        [FromServices] StatementService statements,
        HttpRequest http,
        CancellationToken cancellationToken) =>
        RunAsync(profileId, db, http, async (profile, content) =>
        {
            Outcome<ImportReport> report = await statements
                .ImportAsync(profile, content, cancellationToken).ConfigureAwait(false);

            if (!report.IsComputed) return ApiResults.BadRequest(report.Reason!);

            ImportReport r = report.Value;

            return Results.Ok(new ImportReportView(
                r.Reconciled, r.Created, r.Discrepant, r.Duplicate, r.Pending, r.Rejected));
        }, cancellationToken);

    /// <summary>
    /// Lo común de las dos rutas: buscar el perfil y leer el archivo.
    /// </summary>
    private static async Task<IResult> RunAsync(
        Guid profileId,
        MargenDbContext db,
        HttpRequest http,
        Func<StatementProfile, string, Task<IResult>> run,
        CancellationToken cancellationToken)
    {
        StatementProfile? profile = await db.StatementProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == profileId && p.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null) return ApiResults.NotFound("No existe ese perfil.");

        if (http.ContentLength > MaxBytes)
        {
            return ApiResults.BadRequest($"El archivo no puede pasar de {MaxBytes / 1024 / 1024} MB.");
        }

        using var buffer = new MemoryStream();
        await http.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        if (buffer.Length == 0) return ApiResults.BadRequest("El archivo llegó vacío.");

        if (buffer.Length > MaxBytes)
        {
            // La cabecera puede mentir o faltar; se comprueba también lo leído.
            return ApiResults.BadRequest($"El archivo no puede pasar de {MaxBytes / 1024 / 1024} MB.");
        }

        return await run(profile, Csv.Decode(buffer.ToArray())).ConfigureAwait(false);
    }

    private static StatementProfileView ToView(StatementProfile p) => new(
        p.Id, p.Name, p.AccountId, p.Delimiter, p.SkipRows,
        p.DateColumn, p.DateFormat, p.DescriptionColumn,
        p.AmountColumn, p.DebitColumn, p.CreditColumn, p.Decimals, p.InvertSign);
}
