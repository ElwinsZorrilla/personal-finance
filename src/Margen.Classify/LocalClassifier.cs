using System.Collections.Immutable;

namespace Margen.Classify;

/// <summary>
/// Los nombres de categoría que conoce el clasificador local.
/// </summary>
/// <remarks>
/// Son constantes y no cadenas sueltas porque el llamador tiene que traducirlas
/// a los identificadores de sus categorías, y una errata en una cadena suelta
/// se manifiesta como «el clasificador nunca acierta» en vez de como un error
/// de compilación.
/// </remarks>
public static class CategoryNames
{
    public const string Supermercado = "Supermercado";
    public const string Restaurantes = "Restaurantes";
    public const string Transporte = "Transporte";
    public const string Suscripciones = "Suscripciones";
    public const string Salud = "Salud";
    public const string Servicios = "Servicios";
    public const string Efectivo = "Efectivo";
}

/// <summary>
/// Adivina la categoría por el nombre del comercio, con una tabla de palabras.
/// </summary>
/// <remarks>
/// Sin red, sin coste y determinista: el mismo comercio da la misma respuesta
/// hoy y dentro de un año. Es el último escalón antes de gastar una llamada al
/// modelo, y resuelve la mayoría de los comercios de todos los días.
///
/// **Gana la palabra más larga que case**, no la primera de la lista. Con la
/// primera, colocar «UBER» antes que «UBER EATS» mandaría la cena a transporte,
/// y el orden de una lista de cuarenta entradas es justo la clase de detalle que
/// alguien rompe sin darse cuenta al añadir la cuarenta y uno.
///
/// La tabla está sembrada con los comercios que el Banco Popular escribió de
/// verdad en las muestras de la Fase 7.
/// </remarks>
public static class LocalClassifier
{
    /// <summary>
    /// Confianza de una respuesta del clasificador local: 7000 puntos básicos.
    /// </summary>
    /// <remarks>
    /// **Por debajo del umbral de auto-asignación a propósito.** Acierta con lo
    /// que conoce y no tiene manera de saber cuándo no conoce algo: `SM NACIONAL`
    /// es un supermercado y `SM` a secas puede ser cualquier cosa.
    ///
    /// Con 7000 sugiere y pide confirmación, y la primera confirmación crea la
    /// regla del usuario que lo sube a 10000 para siempre. Es un escalón que se
    /// desgasta con el uso, que es lo que tiene que hacer.
    /// </remarks>
    public const int Confidence = 7_000;

    /// <summary>
    /// Palabra a categoría. Todo en mayúsculas: el comercio llega normalizado.
    /// </summary>
    private static readonly ImmutableArray<KeyValuePair<string, string>> Keywords =
    [
        // Supermercados dominicanos. «SM NACIONAL CHARLES» salió de una muestra.
        new("SM NACIONAL", CategoryNames.Supermercado),
        new("SUPERMERCADO", CategoryNames.Supermercado),
        new("SUPERMERCADOS", CategoryNames.Supermercado),
        new("JUMBO", CategoryNames.Supermercado),
        new("LA SIRENA", CategoryNames.Supermercado),
        new("BRAVO", CategoryNames.Supermercado),
        new("PRICESMART", CategoryNames.Supermercado),
        new("PRICE SMART", CategoryNames.Supermercado),
        new("PLAZA LAMA", CategoryNames.Supermercado),
        new("COLMADO", CategoryNames.Supermercado),

        // Comida. «UBER EATS» antes que «UBER» lo resuelve la longitud, no el
        // orden de estas líneas.
        new("UBER EATS", CategoryNames.Restaurantes),
        new("UBEREATS", CategoryNames.Restaurantes),
        new("PEDIDOSYA", CategoryNames.Restaurantes),
        new("RESTAURANT", CategoryNames.Restaurantes),
        new("PIZZA", CategoryNames.Restaurantes),
        new("BURGER", CategoryNames.Restaurantes),
        new("MCDONALD", CategoryNames.Restaurantes),
        new("WENDY", CategoryNames.Restaurantes),
        new("KFC", CategoryNames.Restaurantes),
        new("ADRIAN TROPICAL", CategoryNames.Restaurantes),
        new("CAFE", CategoryNames.Restaurantes),

        new("UBER", CategoryNames.Transporte),
        new("INDRIVE", CategoryNames.Transporte),
        new("PARQUEO", CategoryNames.Transporte),
        new("ESTACION", CategoryNames.Transporte),
        new("GASOLINA", CategoryNames.Transporte),
        new("SHELL", CategoryNames.Transporte),
        new("TEXACO", CategoryNames.Transporte),
        new("PEAJE", CategoryNames.Transporte),

        // «OPENAI *CHATGPT SUBSCR» salió de una muestra.
        new("OPENAI", CategoryNames.Suscripciones),
        new("CHATGPT", CategoryNames.Suscripciones),
        new("NETFLIX", CategoryNames.Suscripciones),
        new("SPOTIFY", CategoryNames.Suscripciones),
        new("APPLE.COM", CategoryNames.Suscripciones),
        new("GOOGLE", CategoryNames.Suscripciones),
        new("MICROSOFT", CategoryNames.Suscripciones),
        new("ADOBE", CategoryNames.Suscripciones),
        new("ANTHROPIC", CategoryNames.Suscripciones),
        new("CLAUDE.AI", CategoryNames.Suscripciones),

        new("FARMACIA", CategoryNames.Salud),
        new("FARMACIAS", CategoryNames.Salud),
        new("CLINICA", CategoryNames.Salud),
        new("LABORATORIO", CategoryNames.Salud),
        new("HOSPITAL", CategoryNames.Salud),

        new("EDESUR", CategoryNames.Servicios),
        new("EDENORTE", CategoryNames.Servicios),
        new("EDEESTE", CategoryNames.Servicios),
        new("CAASD", CategoryNames.Servicios),
        new("CORAASAN", CategoryNames.Servicios),
        new("CLARO", CategoryNames.Servicios),
        new("ALTICE", CategoryNames.Servicios),
        new("VIVA", CategoryNames.Servicios),

        // «BANCO POPULAR OF.CHAR DE» es como el banco escribe un retiro en
        // sucursal. Un retiro no dice en qué se gastó el dinero; efectivo es
        // exactamente eso y no una mentira cómoda.
        new("BANCO POPULAR", CategoryNames.Efectivo),
        new("CAJERO", CategoryNames.Efectivo),
        new("ATM", CategoryNames.Efectivo),
    ];

    /// <summary>
    /// El nombre de categoría que sugiere la tabla, o nulo si no reconoce nada.
    /// </summary>
    public static string? Suggest(string merchantNormalized)
    {
        ArgumentNullException.ThrowIfNull(merchantNormalized);

        string haystack = $" {Flatten(merchantNormalized)} ";

        string? best = null;
        int bestLength = 0;

        foreach ((string keyword, string category) in Keywords)
        {
            if (keyword.Length <= bestLength) continue;

            if (haystack.Contains($" {Flatten(keyword)} ", StringComparison.OrdinalIgnoreCase))
            {
                best = category;
                bestLength = keyword.Length;
            }
        }

        return best;
    }

    /// <summary>
    /// Deja solo letras y dígitos separados por espacios simples.
    /// </summary>
    /// <remarks>
    /// Los dos motivos salieron de comercios reales:
    ///
    /// 1. **El banco pega las palabras con cualquier símbolo.** `UBER*EATS` no
    ///    contiene «UBER EATS», así que solo casaba «UBER» y la cena se iba a
    ///    transporte. Aplanando, las dos formas se leen igual.
    /// 2. **La comparación es por palabra entera.** Con la palabra suelta,
    ///    «ATM» casaría dentro de «ATMOSFERA» y «SM» dentro de cualquier cosa.
    ///    Rodear de espacios los dos lados hace que solo casen palabras
    ///    completas, sin necesidad de una expresión regular por entrada.
    ///
    /// Se aplana también la palabra de la tabla, porque algunas llevan símbolo
    /// —`APPLE.COM`, `CLAUDE.AI`— y tienen que casar con la misma regla.
    /// </remarks>
    private static string Flatten(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        bool lastWasSpace = true;

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                lastWasSpace = false;
                continue;
            }

            if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }
}
