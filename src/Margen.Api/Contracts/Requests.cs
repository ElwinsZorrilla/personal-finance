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
/// La interpretación de «Gasté 450 pesos en almuerzo» es la Fase 9 y entrará
/// por un campo aparte, no reemplazando a este.
/// </remarks>
public sealed record CreateCashRequest(
    long AmountCents,
    string Merchant,
    Guid? CategoryId,
    DateOnly? OccurredOn,
    string? Notes);

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
