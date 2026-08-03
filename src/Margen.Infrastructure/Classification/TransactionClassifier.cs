using Margen.Classify;
using Margen.Domain;
using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Margen.Infrastructure.Classification;

/// <summary>
/// Lleva los datos de la base al motor de clasificación y guarda lo que decide.
/// </summary>
/// <remarks>
/// El motor no sabe consultar una tabla y esta clase no decide nada: aquí no hay
/// un solo umbral ni una sola comparación de confianza. Es a propósito. Si la
/// regla de «cuándo se aplica sin preguntar» viviera en dos sitios, un día
/// dirían cosas distintas y el que gana sería el que se ejecuta más tarde.
/// </remarks>
public sealed class TransactionClassifier(
    MargenDbContext db,
    TimeProvider clock,
    ICategorySuggester? suggester = null)
{
    /// <summary>
    /// Clasifica un movimiento recién creado y le deja puesto lo que salga.
    /// </summary>
    /// <remarks>
    /// **No guarda.** Quien llama decide cuándo escribir, porque la ingesta
    /// escribe el movimiento y el correo en la misma transacción y partirlo
    /// dejaría un correo marcado como procesado con su movimiento a medias.
    ///
    /// Un movimiento que ya tiene categoría confirmada no se toca: reclasificar
    /// sobre una decisión de una persona es deshacerla.
    /// </remarks>
    public async Task<Outcome<Classify.Classification>> ApplyAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        if (transaction.IsCategoryConfirmed)
        {
            return Outcome.Insufficient<Classify.Classification>(
                "La categoría la confirmó una persona.");
        }

        Outcome<Classify.Classification> result = await ClassifyAsync(
            transaction.MerchantNormalized, cancellationToken).ConfigureAwait(false);

        if (!result.IsComputed)
        {
            // Fallo cerrado: sin respuesta no se inventa una categoría. El
            // movimiento se queda como estaba, en revisión.
            transaction.ConfidenceBasisPoints = 0;
            return result;
        }

        Classify.Classification c = result.Value;

        transaction.CategoryId = c.CategoryId;
        transaction.ConfidenceBasisPoints = c.ConfidenceBasisPoints;
        transaction.ClassificationSource = c.Source.ToString();

        // `Notes` es del usuario y no se toca. Escribir ahí el motivo de la
        // clasificación tenía dos problemas: al reclasificar el mismo
        // movimiento el texto se acumulaba —«se parece a Supermercado · se
        // parece a Supermercado»—, y obligaba a la persona a borrar texto de la
        // máquina para escribir el suyo. El motivo se reconstruye en pantalla
        // con `ClassificationSource` y el nombre de la categoría.

        // Solo lo que se aplica solo sale de revisión. Una sugerencia deja la
        // categoría puesta para que se confirme con un toque, y el estado
        // diciendo que hace falta ese toque.
        if (c.IsAutomatic && transaction.Status == TxStatus.NeedsReview)
        {
            transaction.Status = TxStatus.Posted;
        }

        transaction.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        return result;
    }

    /// <summary>
    /// Qué categoría le toca a un comercio, con todo lo que hay en la base.
    /// </summary>
    public async Task<Outcome<Classify.Classification>> ClassifyAsync(
        string merchantNormalized,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(merchantNormalized);

        ClassificationRequest request = await BuildRequestAsync(
            merchantNormalized, cancellationToken).ConfigureAwait(false);

        return await Cascade
            .ClassifyAsync(request, suggester, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ClassificationRequest> BuildRequestAsync(
        string merchantNormalized,
        CancellationToken cancellationToken)
    {
        List<MerchantRuleSpec> rules = await db.MerchantRules
            .AsNoTracking()
            .Where(r => r.IsActive)
            .Select(r => new MerchantRuleSpec(
                r.CategoryId, r.Pattern, r.MatchKind, r.IsUserDefined, r.Weight))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // **Solo confirmados.** Sin este filtro el automatismo se citaría a sí
        // mismo: ver Transaction.CategoryConfirmedAt.
        List<Guid> confirmed = await db.Transactions
            .AsNoTracking()
            .Where(t => t.MerchantNormalized == merchantNormalized
                && t.CategoryConfirmedAt != null
                && t.CategoryId != null)
            .OrderByDescending(t => t.CategoryConfirmedAt)
            .Take(HistoryWindow)
            .Select(t => t.CategoryId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, Guid> categories = await db.Categories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        return new ClassificationRequest(merchantNormalized, rules, confirmed, categories);
    }

    /// <summary>
    /// Cuántas confirmaciones hacia atrás mira el historial.
    /// </summary>
    /// <remarks>
    /// Diez y no todas: si alguien cambia de opinión sobre un comercio, veinte
    /// confirmaciones viejas tardarían medio año en dejar de mandar sobre las
    /// nuevas.
    /// </remarks>
    private const int HistoryWindow = 10;
}
