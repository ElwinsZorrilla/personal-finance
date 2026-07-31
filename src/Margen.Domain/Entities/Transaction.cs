namespace Margen.Domain.Entities;

/// <summary>Un movimiento de dinero. Es la tabla que sostiene todo lo demás.</summary>
public class Transaction
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Account? Account { get; set; }

    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>Texto del comercio tal como lo trajo el origen, sin tocar.</summary>
    public required string MerchantRaw { get; set; }

    /// <summary>
    /// Nombre normalizado del comercio: mayúsculas, sin acentos, sin sufijos de
    /// terminal. Es la clave por la que se agrupa el historial y por la que
    /// buscan las reglas exactas.
    /// </summary>
    public required string MerchantNormalized { get; set; }

    /// <summary>
    /// Monto en centavos, siempre positivo. La dirección la da
    /// <see cref="Kind"/>: guardar el signo en el monto obliga a recordar el
    /// convenio en cada consulta y a alguien se le olvida.
    /// </summary>
    public Money Amount { get; set; }

    public string Currency { get; set; } = "DOP";

    /// <summary>Instante en que ocurrió, en UTC. La columna es timestamptz.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>Instante en que el banco lo asentó. Nulo mientras esté pendiente.</summary>
    public DateTime? PostedAt { get; set; }

    public TxKind Kind { get; set; }

    public TxStatus Status { get; set; } = TxStatus.NeedsReview;

    public TxSource Source { get; set; }

    /// <summary>
    /// Confianza de la clasificación automática, en centésimas de 0 a 100.
    /// Es entero y no <c>double</c> por la misma razón que el dinero: se
    /// compara contra un umbral y un umbral con coma flotante da resultados
    /// distintos según por dónde entró el número.
    /// </summary>
    public int ConfidenceBasisPoints { get; set; } = 10000;

    /// <summary>
    /// Movimiento del que este es devolución. Permite que una devolución
    /// reduzca el gasto de su categoría original en lugar de aparecer como
    /// ingreso suelto.
    /// </summary>
    public Guid? RefundsTransactionId { get; set; }

    public Transaction? RefundsTransaction { get; set; }

    /// <summary>Movimiento del que este es duplicado, si se determinó que lo es.</summary>
    public Guid? DuplicateOfTransactionId { get; set; }

    public Transaction? DuplicateOfTransaction { get; set; }

    /// <summary>Correo que lo originó. Nulo en efectivo y en importaciones.</summary>
    public Guid? IncomingEmailId { get; set; }

    public IncomingEmail? IncomingEmail { get; set; }

    /// <summary>
    /// Huella del movimiento: cuenta, monto, día y comercio normalizado. Con
    /// índice único. Es la segunda línea de defensa contra el duplicado,
    /// detrás del identificador de mensaje del correo; cubre el caso del banco
    /// que manda dos correos distintos por la misma compra.
    /// </summary>
    public required string Fingerprint { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Un movimiento entra en el gasto del período salvo que solo mueva saldo
    /// entre cuentas propias o esté descartado. Réplica de
    /// <c>affectsSpending</c> en el cliente; la que manda es esta, porque el
    /// cálculo vive en el servidor.
    /// </summary>
    /// <remarks>
    /// Una devolución sí entra: entra restando. Excluirla la dejaría fuera del
    /// gasto de su categoría, que es justo lo contrario de lo que hace una
    /// devolución.
    /// </remarks>
    public bool AffectsSpending =>
        Status is not (TxStatus.Rejected or TxStatus.Duplicate)
        && Kind is not (TxKind.Transfer or TxKind.Payment);

    /// <summary>Devuelve dinero en lugar de gastarlo.</summary>
    public bool IsCredit => Kind == TxKind.Refund;

    /// <summary>
    /// Aporte con signo al gasto del período. Es lo que suma el motor: el monto
    /// se guarda siempre positivo y aquí es donde se decide hacia dónde va.
    /// </summary>
    public Money SpendingEffect => !AffectsSpending
        ? Money.Zero
        : IsCredit ? -Amount : Amount;
}
