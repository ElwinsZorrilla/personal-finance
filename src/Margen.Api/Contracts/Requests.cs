namespace Margen.Api.Contracts;

/// <summary>
/// Registro de un gasto en efectivo.
/// </summary>
/// <remarks>
/// El monto viaja en **centavos enteros**. No es un decimal ni una cadena a
/// interpretar: el JSON de este API no tiene un solo número con coma en un
/// camino monetario, porque un <c>double</c> de JavaScript no representa 0.10
/// exactamente y el Atajo de iOS serializa desde JavaScript.
///
/// La frase de la Fase 9 entra por **otra ruta**, no por un campo opcional de
/// esta. Un cuerpo con dos formas válidas —o monto y comercio, o frase— obliga
/// a quien lo lee a averiguar cuál de las dos le mandaron, y la primera vez que
/// llegan las dos a la vez hay que elegir una en silencio. Es la misma trampa
/// que llevó el PUT de movimientos a dejar de ser PATCH.
/// </remarks>
public sealed record CreateCashRequest(
    long AmountCents,
    string Merchant,
    Guid? CategoryId,
    DateOnly? OccurredOn,
    string? Notes);

/// <summary>
/// Un gasto de efectivo escrito o dictado: «Gasté 450 pesos en almuerzo».
/// </summary>
/// <remarks>
/// Es lo que manda el Atajo de iOS. El monto sale de la frase con una expresión
/// regular y **nunca con un modelo**: ver `CashPhrase`. Lo que sí puede opinar
/// un modelo es la categoría, a través de la cascada, y allí tampoco decide
/// solo.
/// </remarks>
public sealed record CreateCashPhraseRequest(string Text);

/// <summary>
/// Estado clasificable de un movimiento. Es un reemplazo completo, no un
/// parche.
/// </summary>
/// <remarks>
/// Todos los campos se aplican tal como llegan: un nulo **borra**, no «deja
/// como estaba». Por eso el verbo es PUT y no PATCH.
///
/// La primera versión mezclaba las dos reglas en el mismo cuerpo —la categoría
/// nula borraba y las notas nulas se conservaban— y eso es una trampa: quien
/// llamara para cambiar solo el estado habría descategorizado el movimiento sin
/// pedirlo, y la cifra de esa categoría habría cambiado sin que nadie lo
/// tocara. Una sola regla para todo el cuerpo es predecible; dos, no.
///
/// Lo que este cuerpo **no** lleva es el monto, la moneda ni la fecha. Esas tres
/// las fija el correo del banco y corregirlas a mano rompería la conciliación
/// sin dejar rastro. Lo que el usuario corrige es la clasificación, no el hecho.
/// </remarks>
public sealed record PutTransactionRequest(
    Guid? CategoryId,
    string? Status,
    string? Notes,
    bool CreateRule = false);

public sealed record CreateRuleRequest(
    string Pattern,
    string MatchKind,
    Guid CategoryId,
    int Weight = 100);

public sealed record RedistributeRequest(
    Guid FromCategoryId,
    Guid ToCategoryId,
    long AmountCents);

public sealed record ResolveAlertRequest(string? Note);

/// <summary>
/// Cómo se lee el CSV de un banco.
/// </summary>
/// <remarks>
/// Las columnas van por índice desde cero. Es lo que el humano ve al abrir su
/// archivo, y pedirle el nombre de la cabecera fallaría con los bancos que no
/// la traen o que la escriben distinta cada mes.
/// </remarks>
public sealed record CreateStatementProfileRequest(
    string Name,
    Guid AccountId,
    string? Delimiter,
    int SkipRows,
    int DateColumn,
    string DateFormat,
    int DescriptionColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn,
    string? Decimals,
    bool InvertSign);

/// <summary>Alta de una cuenta o tarjeta.</summary>
/// <remarks>
/// El saldo va en **centavos enteros**, como todo el dinero de este API. Es la
/// única cifra del sistema que no sale de un correo ni de un cálculo: la pone
/// una persona, y entra directa en la fórmula del dinero seguro.
/// </remarks>
public sealed record CreateAccountRequest(
    string Name,

    /// <summary>Los cuatro últimos dígitos: es lo único que trae el correo del banco.</summary>
    string LastFour,

    /// <summary>`Checking`, `Savings`, `Credit` o `Cash`.</summary>
    string Kind,

    long BalanceCents,
    long? CreditLimitCents);

/// <summary>
/// Apertura del período que contiene hoy.
/// </summary>
/// <remarks>
/// No va del 1 al 30: va de un ingreso al siguiente, que es el ciclo real del
/// dinero de una persona asalariada. Por eso se dan los días de cobro y no dos
/// fechas.
/// </remarks>
public sealed record OpenPeriodRequest(
    /// <summary>
    /// Los días del mes en que entra el sueldo. Uno o varios.
    /// </summary>
    /// <remarks>
    /// Era un solo <c>int</c>. Quien cobra quincena y fin de mes tiene **dos
    /// ciclos por mes**, y con un solo día el reparto diario dividía el dinero
    /// de una quincena entre treinta días: la mitad de lo que se puede gastar.
    ///
    /// «Fin de mes» se escribe 31 y el calendario lo corre al último día que
    /// exista en cada mes.
    /// </remarks>
    IReadOnlyList<int> PayDays,

    /// <summary>
    /// Lo que se cobra **en cada uno** de esos días, no el total del mes.
    /// </summary>
    /// <remarks>
    /// Un período es un ciclo, y su ingreso es el de ese ciclo. Poner aquí el
    /// sueldo mensual con dos cobros duplicaría el ingreso esperado de cada
    /// período.
    ///
    /// No entra en la fórmula del dinero seguro —esa parte del saldo real— pero
    /// sí en lo que se enseña y en el cierre de período.
    /// </remarks>
    long ExpectedIncomeCents,

    long? SafetyFundCents,
    long? CommittedSavingsCents);
