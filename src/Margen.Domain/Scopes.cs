namespace Margen.Domain;

/// <summary>
/// Alcances de un token de acceso.
/// </summary>
/// <remarks>
/// Un token lleva la lista de lo que puede hacer, no un papel con nombre. La
/// diferencia importa para el Atajo de iOS: ese token vive en un teléfono
/// dentro de una automatización que cualquiera con el teléfono desbloqueado
/// puede leer, y solo tiene que poder decir «gasté 450 pesos en almuerzo».
/// Con un papel llamado «dispositivo» heredaría todo lo que ese papel gane
/// después.
/// </remarks>
public static class Scopes
{
    /// <summary>Lectura y escritura completas. Es lo que recibe la app.</summary>
    public const string Full = "full";

    /// <summary>Registrar un gasto en efectivo y nada más. Es lo que recibe el Atajo.</summary>
    public const string CashCreate = "cash:create";

    public static readonly IReadOnlyList<string> All = [Full, CashCreate];

    public static bool IsKnown(string scope) => All.Contains(scope);
}
