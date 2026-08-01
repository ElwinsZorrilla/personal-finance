using Margen.Domain;

namespace Margen.Ingest;

/// <summary>Un correo tal como llegó, sin interpretar.</summary>
public sealed record RawEmail(
    string MessageId,
    string Sender,
    string Subject,
    string Body,
    DateTime ReceivedAtUtc);

/// <summary>
/// Un movimiento extraído de un correo.
/// </summary>
/// <remarks>
/// Tipo cerrado y con <see cref="Money"/>, no un diccionario de textos. Es la
/// barrera del riesgo «el modelo no toca cifras»: no hay ninguna ruta por la que
/// un texto libre llegue a un campo monetario sin pasar por
/// <c>Money.Parse</c>, que exige que se le diga el formato.
///
/// <see cref="OccurredAtUtc"/> ya viene convertido. El parser sabe en qué zona
/// escribe su banco; nadie después lo sabe.
/// </remarks>
public sealed record ParsedTransaction(
    Money Amount,
    string Currency,
    DateTime OccurredAtUtc,
    string MerchantRaw,
    TxKind Kind,
    string AccountLastFour,
    string? Reference = null);

public enum ParseStatus
{
    /// <summary>Se extrajo todo lo necesario.</summary>
    Parsed,

    /// <summary>Este parser no reconoce el correo. Que lo intente otro.</summary>
    NotMine,

    /// <summary>
    /// Lo reconoce pero falta un dato crítico. No se asume ninguno: el
    /// resultado va a revisión humana.
    /// </summary>
    NeedsReview,
}

public sealed record ParseResult(
    ParseStatus Status,
    ParsedTransaction? Transaction,
    string? Reason)
{
    public static ParseResult NotMine() => new(ParseStatus.NotMine, null, null);

    public static ParseResult Parsed(ParsedTransaction transaction) =>
        new(ParseStatus.Parsed, transaction, null);

    /// <summary>
    /// Reconocido pero incompleto. El motivo es lo que se le enseña a la
    /// persona en Revisión, así que se escribe para ella.
    /// </summary>
    public static ParseResult NeedsReview(string reason) =>
        new(ParseStatus.NeedsReview, null, reason);
}

/// <summary>
/// Lo que sabe leer los correos de un banco.
/// </summary>
/// <remarks>
/// <see cref="Version"/> es lo que hace posible el reproceso: cuando un parser
/// mejora, se sube la versión y la herramienta de reproceso busca los correos
/// interpretados con una anterior. Sin ese número no hay forma de saber qué
/// vale la pena volver a mirar.
/// </remarks>
public interface IEmailParser
{
    string Name { get; }

    int Version { get; }

    /// <summary>
    /// Filtro barato antes de intentar interpretar. Existe para no correr diez
    /// expresiones regulares sobre un correo que no es de este banco.
    /// </summary>
    bool CanHandle(RawEmail email);

    ParseResult Parse(RawEmail email);
}

/// <summary>
/// Los parsers disponibles, en orden.
/// </summary>
/// <remarks>
/// El orden es el de registro y es estable: dos parsers que digan que pueden
/// con el mismo correo lo resuelven siempre igual, no según cómo se cargó el
/// ensamblado. Un orden que cambie entre ejecuciones haría que reprocesar el
/// mismo buzón diera resultados distintos.
/// </remarks>
public sealed class ParserRegistry
{
    private readonly List<IEmailParser> _parsers;

    public ParserRegistry(IEnumerable<IEmailParser> parsers)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _parsers = [.. parsers];

        var repeated = _parsers
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (repeated.Count > 0)
        {
            throw new ArgumentException(
                $"Hay más de un parser llamado {string.Join(", ", repeated)}. "
                + "El nombre se guarda en cada correo procesado y tiene que "
                + "identificar a uno solo.",
                nameof(parsers));
        }
    }

    public IReadOnlyList<IEmailParser> All => _parsers;

    /// <summary>
    /// El primero que dice que puede, o nulo. Nulo significa que el correo va a
    /// <c>Unrecognized</c> y no crea nada.
    /// </summary>
    public IEmailParser? Find(RawEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        foreach (IEmailParser parser in _parsers)
        {
            if (parser.CanHandle(email))
            {
                return parser;
            }
        }

        return null;
    }

    public IEmailParser? ByName(string name) =>
        _parsers.Find(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
