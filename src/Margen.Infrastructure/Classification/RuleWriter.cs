using Margen.Classify;
using Margen.Domain;
using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Margen.Infrastructure.Classification;

/// <summary>
/// Convierte la corrección de un usuario en la regla que evita la siguiente.
/// </summary>
public sealed class RuleWriter(MargenDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Aprende de una corrección. Devuelve qué se hizo.
    /// </summary>
    /// <remarks>
    /// **No guarda**, por lo mismo que <see cref="TransactionClassifier"/>: la
    /// corrección del movimiento y la regla que enseña tienen que escribirse en
    /// la misma transacción. Guardar la regla y que falle el movimiento dejaría
    /// una app que aprendió algo que el usuario no llegó a ver.
    /// </remarks>
    public async Task<CorrectionAction> LearnAsync(
        string merchantNormalized,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(merchantNormalized);

        List<MerchantRule> existing = await db.MerchantRules
            .Where(r => r.IsActive)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        CorrectionPlan plan = Corrections.Plan(
            merchantNormalized,
            categoryId,
            [.. existing.Select(r => new MerchantRuleSpec(
                r.CategoryId, r.Pattern, r.MatchKind, r.IsUserDefined, r.Weight))]);

        DateTime now = clock.GetUtcNow().UtcDateTime;

        switch (plan.Action)
        {
            case CorrectionAction.Create:
                db.MerchantRules.Add(new MerchantRule
                {
                    Id = Guid.CreateVersion7(),
                    Pattern = plan.Pattern,
                    MatchKind = MatchKind.Exact,
                    CategoryId = plan.CategoryId,
                    Weight = plan.Weight,
                    IsUserDefined = true,
                    IsActive = true,
                    CreatedAt = now,
                });
                break;

            case CorrectionAction.Update:
                // Se le cambia el destino a la que ya existe. Añadir una segunda
                // dejaría dos reglas del mismo peso apuntando a categorías
                // distintas, y cuál gana dependería del orden de la consulta.
                MerchantRule? target = existing.FirstOrDefault(r =>
                    r.MatchKind == MatchKind.Exact
                    && r.IsUserDefined
                    && string.Equals(
                        r.Pattern, plan.Pattern, StringComparison.OrdinalIgnoreCase));

                if (target is not null)
                {
                    target.CategoryId = plan.CategoryId;
                }
                break;

            case CorrectionAction.None:
            default:
                break;
        }

        return plan.Action;
    }
}
