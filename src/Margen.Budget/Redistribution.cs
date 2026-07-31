using Margen.Domain;

namespace Margen.Budget;

/// <summary>Una categoría con su asignación y lo que lleva gastado.</summary>
public sealed record CategoryAllocation(
    Guid CategoryId,
    Priority Priority,
    Money Allocated,
    Money Spent)
{
    public Money Available => Allocated - Spent;

    /// <summary>
    /// Prioridad 1 y 2 no se recortan. El alquiler no se negocia a mitad de mes
    /// porque un algoritmo lo vea conveniente.
    /// </summary>
    /// <remarks>
    /// Lista blanca y no umbral. Con <c>(int)Priority >= 3</c>, un valor fuera
    /// del enum —el entero 99, que en C# es un <c>Priority</c> perfectamente
    /// construible— pasaría el umbral y quedaría recortable. Enumerando las dos
    /// prioridades que sí se pueden tocar, cualquier valor que no reconozcamos
    /// cae del lado protegido. Es la misma regla de fallo cerrado que el resto
    /// del sistema: ante la duda, no se toca el dinero.
    /// </remarks>
    public bool CanBeTrimmed => Priority is Priority.Flexible or Priority.Optional;
}

public enum RedistributionRefusal
{
    /// <summary>Prioridad 1 o 2. Intocable.</summary>
    ProtectedPriority,

    /// <summary>No hay tanto disponible sin pasarse de lo ya gastado.</summary>
    NotEnoughAvailable,

    /// <summary>Recortar cero o menos no es recortar.</summary>
    NonPositiveAmount,

    /// <summary>Origen y destino son la misma categoría.</summary>
    SameCategory,
}

public sealed record RedistributionResult(
    bool Succeeded,
    RedistributionRefusal? Refusal,
    CategoryAllocation? Source,
    CategoryAllocation? Target)
{
    public static RedistributionResult Refused(RedistributionRefusal reason) =>
        new(false, reason, null, null);

    public static RedistributionResult Ok(
        CategoryAllocation source,
        CategoryAllocation? target = null) =>
        new(true, null, source, target);
}

public static class Redistribution
{
    /// <summary>
    /// Recorta una categoría.
    /// </summary>
    /// <remarks>
    /// Devuelve un rechazo con motivo, no una advertencia que se pueda ignorar
    /// ni una excepción que alguien atrape y siga. La prioridad protegida es la
    /// primera comprobación y ninguna cantidad la sortea.
    ///
    /// El límite es lo **disponible**, no lo asignado: recortar por debajo de
    /// lo ya gastado dejaría la categoría en negativo el día del recorte, y ese
    /// gasto ya ocurrió y no se puede deshacer.
    /// </remarks>
    public static RedistributionResult Trim(CategoryAllocation source, Money amount)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.CanBeTrimmed)
        {
            return RedistributionResult.Refused(RedistributionRefusal.ProtectedPriority);
        }

        if (amount.Cents <= 0)
        {
            return RedistributionResult.Refused(RedistributionRefusal.NonPositiveAmount);
        }

        if (amount > source.Available)
        {
            return RedistributionResult.Refused(RedistributionRefusal.NotEnoughAvailable);
        }

        return RedistributionResult.Ok(
            source with { Allocated = source.Allocated - amount });
    }

    /// <summary>
    /// Mueve una cantidad de una categoría a otra.
    /// </summary>
    /// <remarks>
    /// El recorte se valida entero antes de tocar el destino. Si el origen no
    /// se puede recortar, el destino no se modifica: no hay estado intermedio
    /// en el que el dinero exista dos veces ni en el que haya desaparecido.
    ///
    /// El destino no tiene restricción de prioridad. Que el alquiler no se
    /// pueda recortar no significa que no se le pueda dar más.
    /// </remarks>
    public static RedistributionResult Move(
        CategoryAllocation source,
        CategoryAllocation target,
        Money amount)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (source.CategoryId == target.CategoryId)
        {
            return RedistributionResult.Refused(RedistributionRefusal.SameCategory);
        }

        RedistributionResult trimmed = Trim(source, amount);

        if (!trimmed.Succeeded)
        {
            return trimmed;
        }

        return RedistributionResult.Ok(
            trimmed.Source!,
            target with { Allocated = target.Allocated + amount });
    }

    /// <summary>
    /// Cuánto se puede sacar en total sin tocar lo intocable.
    /// </summary>
    /// <remarks>
    /// Es lo que la pantalla necesita para decir «puedes reasignar hasta X»
    /// antes de que el usuario elija de dónde. Las categorías protegidas no
    /// suman, ni siquiera la parte de ellas que está sin gastar.
    /// </remarks>
    public static Money TrimmableTotal(IEnumerable<CategoryAllocation> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        return Money.Sum(categories
            .Where(c => c.CanBeTrimmed && c.Available.Cents > 0)
            .Select(c => c.Available));
    }
}
