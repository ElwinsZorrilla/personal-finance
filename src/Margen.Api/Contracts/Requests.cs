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
