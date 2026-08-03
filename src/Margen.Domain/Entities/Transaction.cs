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
    /// Ingreso, egreso o traspaso. Nace deducida de <see cref="Kind"/> y el
    /// usuario la puede corregir.
    /// </summary>
    /// <remarks>
    /// Se guarda en lugar de derivarse siempre porque hay un caso que el correo
    /// del banco no resuelve: una transferencia enviada a tu propia cuenta de
    /// ahorro no es un gasto y una enviada a otra persona sí, y el correo dice
    /// lo mismo en los dos casos.
    /// </remarks>
    public TxDirection Direction { get; set; } = TxDirection.Outflow;

    /// <summary>
    /// Confianza de la clasificación automática, en centésimas de 0 a 100.
    /// Es entero y no <c>double</c> por la misma razón que el dinero: se
    /// compara contra un umbral y un umbral con coma flotante da resultados
    /// distintos según por dónde entró el número.
    /// </summary>
    public int ConfidenceBasisPoints { get; set; } = 10000;

    /// <summary>
    /// Cuándo una persona confirmó esta categoría. Nulo mientras la haya puesto
    /// solo el automatismo.
    /// </summary>
    /// <remarks>
    /// Es lo que impide que la clasificación se muerda la cola. El escalón del
    /// historial mira «qué categoría le puso el usuario a este comercio», y sin
    /// esta columna no habría manera de distinguir eso de «qué categoría le puso
    /// el automatismo»: la tabla de palabras clasificaría diez movimientos mal,
    /// el historial los leería como confirmación, y el error quedaría fijado con
    /// confianza alta sin que nadie pueda ver de dónde salió.
    ///
    /// Un automatismo que se cita a sí mismo como fuente no es historial, es un
    /// eco.
    /// </remarks>
    public DateTime? CategoryConfirmedAt { get; set; }

    /// <summary>
    /// Qué escalón de la cascada puso esta categoría, en texto.
    /// </summary>
    /// <remarks>
    /// Se guarda para que la pantalla de revisión pueda decir **por qué** algo
    /// está donde está. «Regla del usuario» y «se parece a Supermercado» piden
    /// atención distinta, y sin esto las dos se ven igual.
    /// </remarks>
    public string? ClassificationSource { get; set; }

    /// <summary>La categoría la puso una persona, no un automatismo.</summary>
    public bool IsCategoryConfirmed => CategoryConfirmedAt is not null;

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
    ///
    /// Un **depósito** no entra: es dinero nuevo, no un gasto negativo. Restarlo
    /// de una categoría haría que ingresar dinero pareciera haber gastado
    /// menos en comida.
    ///
    /// Lo que decide es <see cref="Direction"/> y no <see cref="Kind"/>, porque
    /// una transferencia puede ser gasto o traspaso y solo el usuario lo sabe.
    /// </remarks>
    public bool AffectsSpending =>
        Status is not (TxStatus.Rejected or TxStatus.Duplicate)
        && Direction != TxDirection.Internal
        && Kind != TxKind.Deposit;

    /// <summary>Devuelve dinero en lugar de gastarlo.</summary>
    public bool IsCredit => Kind == TxKind.Refund;

    /// <summary>Entra dinero, sea devolución o depósito.</summary>
    public bool IsIncome => Direction == TxDirection.Inflow;

    /// <summary>
    /// Aporte con signo al gasto del período. Es lo que suma el motor: el monto
    /// se guarda siempre positivo y aquí es donde se decide hacia dónde va.
    /// </summary>
    public Money SpendingEffect => !AffectsSpending
        ? Money.Zero
        : IsCredit ? -Amount : Amount;

    /// <summary>
    /// Aporte con signo al saldo de la cuenta. Es otra pregunta distinta del
    /// gasto: un pago de tarjeta baja el saldo sin ser gasto, y un depósito lo
    /// sube sin reducir ninguna categoría.
    /// </summary>
    public Money BalanceEffect => Status is TxStatus.Rejected or TxStatus.Duplicate
        ? Money.Zero
        : Direction == TxDirection.Inflow ? Amount : -Amount;
}
