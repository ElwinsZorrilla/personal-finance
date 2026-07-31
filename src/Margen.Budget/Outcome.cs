namespace Margen.Budget;

public enum OutcomeKind
{
    /// <summary>Hay cifra y es de fiar.</summary>
    Computed,

    /// <summary>Faltan datos para calcularla. No es lo mismo que valer cero.</summary>
    Insufficient,

    /// <summary>Los datos que hay se contradicen.</summary>
    Invalid,
}

/// <summary>
/// Resultado de un cálculo que puede no tener respuesta.
/// </summary>
/// <remarks>
/// Existe para no devolver <c>Money.Zero</c> cuando no se puede calcular. En
/// esta pantalla, cero pesos significa «no gastes nada», que es una respuesta
/// concreta a una pregunta que no se pudo responder. Un cero por defecto es una
/// mentira con formato de número.
///
/// Leer <see cref="Value"/> sin haber mirado <see cref="Kind"/> lanza. Es
/// deliberado: un valor por defecto silencioso es justo lo que este tipo
/// existe para impedir.
/// </remarks>
public sealed class Outcome<T>
{
    private readonly T? _value;

    private Outcome(OutcomeKind kind, T? value, string? reason)
    {
        Kind = kind;
        _value = value;
        Reason = reason;
    }

    public OutcomeKind Kind { get; }

    /// <summary>Por qué no hay cifra. Nulo cuando sí la hay.</summary>
    public string? Reason { get; }

    public bool IsComputed => Kind == OutcomeKind.Computed;

    public T Value => IsComputed
        ? _value!
        : throw new InvalidOperationException(
            $"No hay cifra que leer: {Kind}. {Reason}");

    /// <summary>
    /// Devuelve la cifra o la alternativa. El nombre es largo a propósito:
    /// quien lo escribe está eligiendo un valor por defecto y tiene que verlo
    /// al escribirlo.
    /// </summary>
    public T ValueOrFallback(T fallback) => IsComputed ? _value! : fallback;

    internal static Outcome<T> Create(OutcomeKind kind, T? value, string? reason) =>
        new(kind, value, reason);
}

/// <summary>
/// Fábricas de <see cref="Outcome{T}"/>.
/// </summary>
/// <remarks>
/// Van en una clase no genérica y no como estáticos de <c>Outcome&lt;T&gt;</c>
/// porque un miembro estático en un tipo genérico obliga a escribir el
/// parámetro de tipo en cada llamada aunque se pueda deducir, y produce un
/// miembro distinto por cada instanciación.
/// </remarks>
public static class Outcome
{
    public static Outcome<T> Computed<T>(T value) =>
        Outcome<T>.Create(OutcomeKind.Computed, value, null);

    public static Outcome<T> Insufficient<T>(string reason) =>
        Outcome<T>.Create(OutcomeKind.Insufficient, default, reason);

    public static Outcome<T> Invalid<T>(string reason) =>
        Outcome<T>.Create(OutcomeKind.Invalid, default, reason);
}
