using System.Text.RegularExpressions;
using Margen.Api.Auth;
using Margen.Api.Contracts;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Endpoints;

public static class RuleEndpoints
{
    public static IEndpointRouteBuilder MapRuleEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/rules").WithTags("Reglas");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ListRules");

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("CreateRule");

        group.MapDelete("/{id:guid}", DeleteAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("DeleteRule");

        return app;
    }

    private static async Task<IResult> ListAsync(
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken)
    {
        List<RuleView> rules = await db.MerchantRules
            .AsNoTracking()
            .Include(r => r.Category)
            .OrderByDescending(r => r.Weight)
            .ThenBy(r => r.Pattern)
            .Select(r => new RuleView(
                r.Id,
                r.Pattern,
                r.MatchKind.ToString(),
                r.CategoryId,
                r.Category!.Name,
                r.Weight,
                r.IsUserDefined,
                r.IsActive,
                r.TimesApplied,
                r.LastAppliedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(new Page<RuleView>(rules, null, rules.Count));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateRuleRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Pattern))
        {
            return ApiResults.BadRequest("Falta el patrón.");
        }

        if (!Enum.TryParse(request.MatchKind, ignoreCase: true, out MatchKind matchKind))
        {
            return ApiResults.BadRequest($"Forma de comparar desconocida: {request.MatchKind}.");
        }

        // Un patrón de expresión regular que no compila se guardaría sin
        // protestar y reventaría más tarde, dentro del worker, sobre un correo
        // real y lejos de quien lo escribió.
        if (matchKind == MatchKind.Regex && !IsValidRegex(request.Pattern))
        {
            return ApiResults.BadRequest("Esa expresión regular no compila.");
        }

        bool categoryExists = await db.Categories
            .AnyAsync(c => c.Id == request.CategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (!categoryExists)
        {
            return ApiResults.BadRequest("Esa categoría no existe.");
        }

        string pattern = CashEndpoints.Normalize(request.Pattern);

        MerchantRule? existing = await db.MerchantRules
            .FirstOrDefaultAsync(
                r => r.Pattern == pattern && r.MatchKind == matchKind,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            // El índice único de la Fase 2 lo impediría con una excepción y un
            // 500. Aquí se resuelve antes: crear la misma regla dos veces
            // actualiza la que hay.
            existing.CategoryId = request.CategoryId;
            existing.Weight = request.Weight;
            existing.IsUserDefined = true;
            existing.IsActive = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            return Results.Ok(existing.Id);
        }

        var rule = new MerchantRule
        {
            Id = Guid.CreateVersion7(),
            Pattern = pattern,
            MatchKind = matchKind,
            CategoryId = request.CategoryId,
            Weight = request.Weight,
            IsUserDefined = true,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        db.MerchantRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Created($"/rules/{rule.Id}", rule.Id);
    }

    /// <summary>
    /// Desactiva una regla. No la borra.
    /// </summary>
    /// <remarks>
    /// Borrarla perdería <c>TimesApplied</c> y <c>LastAppliedAt</c>, que es lo
    /// único que permite entender después por qué un montón de movimientos
    /// quedaron en una categoría.
    /// </remarks>
    private static async Task<IResult> DeleteAsync(
        Guid id,
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken)
    {
        int affected = await db.MerchantRules
            .Where(r => r.Id == id && r.IsActive)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.IsActive, false),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            bool exists = await db.MerchantRules
                .AnyAsync(r => r.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (!exists)
            {
                return ApiResults.NotFound("No existe esa regla.");
            }
        }

        return Results.NoContent();
    }

    private static bool IsValidRegex(string pattern)
    {
        try
        {
            _ = Regex.Match(
                string.Empty,
                pattern,
                RegexOptions.None,
                TimeSpan.FromMilliseconds(100));

            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            // Una expresión que agota 100 ms sobre la cadena vacía es una que
            // va a colgar el worker sobre un correo real.
            return false;
        }
    }
}
