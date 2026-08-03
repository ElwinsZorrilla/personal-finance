using System.Text;
using System.Text.RegularExpressions;

namespace Margen.Ingest;

/// <summary>
/// Quita los datos personales de un correo dejando el formato intacto.
/// </summary>
/// <remarks>
/// El parser se escribe **contra el formato**, no contra el contenido: las
/// etiquetas de los campos, el orden de las líneas, los espacios y la forma de
/// los números. Por eso cada sustitución conserva la longitud y la forma de lo
/// que reemplaza. Un `2,450.00` se convierte en otro número con la misma coma
/// de miles y el mismo punto decimal en el mismo sitio; si se convirtiera en
/// `XXX`, la muestra dejaría de servir para escribir el parser.
///
/// Es lógica pura y está probada. Lo que sale de aquí es lo que se versiona en
/// `docs/muestras/`, así que la prueba de que no queda nada personal tiene que
/// poder correrse sin conectarse a ningún buzón.
/// </remarks>
public sealed partial class Redactor(RedactionSettings settings)
{
    private readonly RedactionSettings _settings = settings;

    public string Redact(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        // Los correos del banco vienen sembrados de guiones suaves (U+00AD),
        // que no se ven pero parten una palabra por la mitad para cualquier
        // expresión regular. Se quitan antes de mirar nada: sin esto, una
        // dirección escrita `voz­delcliente­@bpd­.com` no la reconoce el patrón
        // de correo y sobrevive a la redacción.
        string result = body.Replace("­", string.Empty, StringComparison.Ordinal);

        // El nombre que sigue a una etiqueta de persona, sea de quien sea.
        //
        // Va primero y es la regla que más cubre. Los términos que da el
        // usuario solo conocen su propio nombre, y una notificación de
        // transferencia lleva el del beneficiario: **el dato personal de un
        // tercero, que nadie puede enumerar de antemano.** Lo que sí se sabe es
        // dónde lo pone el banco.
        result = NameAfterLabelPattern().Replace(
            result,
            m => m.Groups["etiqueta"].Value + "NOMBRE APELLIDO");

        // Los términos que da el usuario, palabra por palabra.
        //
        // La primera versión buscaba la frase entera y falló con las muestras
        // reales por dos caminos: el banco escribe «SR ELWIN ZORRILLA ESPINAL»
        // —con un apellido de más— y también «ZORRILLA ESPINAL E», en otro
        // orden. Ninguna de las dos contiene la frase que se le dio, así que
        // ninguna se sustituyó.
        foreach (string word in _settings.Words)
        {
            result = Regex.Replace(
                result,
                @"\b" + Regex.Escape(word) + @"\w*",
                "NOMBRE",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(2));
        }

        result = EmailPattern().Replace(result, "finanzas@ejemplo.do");
        result = ReferencePattern().Replace(result, ReplaceReference);
        result = MaskedCardPattern().Replace(result, m => m.Groups["mask"].Value + "1234");
        result = SpelledCardPattern().Replace(result, m => m.Groups["etiqueta"].Value + "1234");
        result = AmountPattern().Replace(result, ReplaceAmount);

        // La última red, después de todo lo demás: cualquier cadena de ocho
        // dígitos seguidos que ninguna etiqueta reconoció. Va al final porque
        // ocho dígitos seguidos ya no pueden ser una fecha —tienen cuatro como
        // mucho— ni un monto con separadores.
        result = LongDigitsPattern().Replace(result, m => new string('0', m.Length));

        return result;
    }

    /// <summary>
    /// Cambia las cifras conservando los separadores en su sitio.
    /// </summary>
    /// <remarks>
    /// `RD$ 2,450.00` se convierte en `RD$ 1,111.11`, no en `RD$ 1111.11` ni en
    /// `RD$ X`. La coma de miles y el punto decimal son justo lo que el parser
    /// tiene que aprender a leer, y son lo que ya se leyó mal tres veces en
    /// este proyecto.
    /// </remarks>
    private static string ReplaceAmount(Match match)
    {
        string number = match.Groups["num"].Value;
        var builder = new StringBuilder(number.Length);

        foreach (char c in number)
        {
            builder.Append(char.IsAsciiDigit(c) ? '1' : c);
        }

        return match.Groups["pre"].Value + builder.ToString();
    }

    /// <summary>
    /// Cambia la referencia conservando su longitud y sus clases de carácter:
    /// las letras siguen siendo letras y los dígitos, dígitos.
    /// </summary>
    private static string ReplaceReference(Match match)
    {
        string prefix = match.Groups["etiqueta"].Value;
        string value = match.Groups["valor"].Value;

        var builder = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            builder.Append(char.IsAsciiDigit(c) ? '9' : char.IsAsciiLetter(c) ? 'X' : c);
        }

        return prefix + builder.ToString();
    }

    [GeneratedRegex(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}",
        RegexOptions.None,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex EmailPattern();

    /// <summary>Los últimos cuatro dígitos con máscara: `****1234`.</summary>
    [GeneratedRegex(
        @"(?<mask>[*x•]{2,}\s?)\d{4}",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex MaskedCardPattern();

    /// <summary>
    /// Los últimos cuatro dígitos **escritos con palabras**: «terminada en
    /// 2074».
    /// </summary>
    /// <remarks>
    /// Esta forma se descubrió leyendo las primeras muestras reales, y la
    /// primera versión del redactor no la reconocía: los cuarenta archivos
    /// salieron con los cuatro dígitos verdaderos de la tarjeta.
    ///
    /// La lección no es que faltara un patrón. Es que **un redactor solo cubre
    /// los formatos que ha visto**, y por eso las muestras se leen antes de
    /// versionarlas en vez de confiar en que la herramienta las dejó limpias.
    /// </remarks>
    [GeneratedRegex(
        @"(?<etiqueta>(?:terminad[ao]s?\s+en|finaliza(?:da|do)?\s+en|final(?:izada)?\s*:?\s*|n[uú]mero\s+)\s*)\d{4}\b",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex SpelledCardPattern();

    /// <summary>
    /// Una cifra **anclada a una marca de moneda o a la etiqueta del monto**.
    /// </summary>
    /// <remarks>
    /// El ancla no es cosmética. La primera versión buscaba cualquier número
    /// con separadores y se comía las fechas —`15/03/2026` quedaba como
    /// `11/11/1111`—, los cuatro dígitos ya sustituidos de la tarjeta y las
    /// referencias. Las tres cosas son formato que el parser tiene que
    /// aprender, no datos personales, y una muestra con la fecha destrozada no
    /// sirve para escribir nada.
    ///
    /// Redactar de más parece la opción segura y no lo es: deja una muestra que
    /// miente sobre el formato, y el parser que se escriba contra ella fallará
    /// con el correo real.
    /// </remarks>
    [GeneratedRegex(
        @"(?<pre>(?:RD\$|US\$|\$|(?:Monto|Valor|Importe|Total)\s*:?)\s*)(?<num>\d[\d.,]*)",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex AmountPattern();

    [GeneratedRegex(
        @"(?<etiqueta>(?:Referencia|Autorizaci[oó]n|Auth|No\.?\s*Transacci[oó]n|Confirmaci[oó]n)\s*:?\s*)(?<valor>[A-Za-z0-9\-]{4,})",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex ReferencePattern();

    /// <summary>
    /// Cualquier cadena de ocho dígitos o más que haya sobrevivido: un número
    /// de cuenta, un documento de identidad, un teléfono.
    /// </summary>
    [GeneratedRegex(@"\d{8,}", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
    private static partial Regex LongDigitsPattern();

    /// <summary>
    /// El nombre de una persona, reconocido por la etiqueta que lo precede.
    /// </summary>
    /// <remarks>
    /// Es la única regla que puede quitar el nombre de **otra persona**. Una
    /// notificación de transferencia lleva el del beneficiario, y no hay lista
    /// de términos que lo prevea: quien ejecuta la captura conoce su propio
    /// nombre, no el de a quién le transfirió dinero el año pasado.
    ///
    /// Se apoya en que estos correos escriben los nombres en mayúsculas y en
    /// que el nombre termina donde empieza una etiqueta HTML, una entidad o el
    /// fin de línea. El tope de sesenta caracteres es para que un correo mal
    /// formado no se coma el documento entero.
    /// </remarks>
    [GeneratedRegex(
        @"(?<etiqueta>(?:Estimad[oa]\s*\(a\)|Estimad[oa]|Beneficiari[oa]|Titular|Destinatari[oa]|Ordenante|Remitente\s+de\s+fondos|A\s+nombre\s+de|Cliente)\s*:?\s*(?:&nbsp;|\s)*)(?<nombre>\p{Lu}[\p{Lu}\p{M}.\s]{2,60}?)(?=\s*(?:<|&nbsp;|\r|\n|,|$))",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 2000)]
    private static partial Regex NameAfterLabelPattern();
}

/// <summary>
/// Qué considerar personal. Los términos los da quien ejecuta la captura: el
/// programa no puede adivinar cómo se llama alguien.
/// </summary>
public sealed record RedactionSettings(IReadOnlyList<string> PersonalTerms)
{
    /// <summary>
    /// Cada palabra de cada término, por separado y sin repetir.
    /// </summary>
    /// <remarks>
    /// Buscar la frase entera no sirve: el banco escribe el nombre con un
    /// apellido de más, en otro orden o abreviado, y ninguna de esas formas
    /// contiene la frase que se le dio. Palabra por palabra sí las cubre las
    /// tres.
    ///
    /// El mínimo de cuatro letras evita destrozar el texto con partículas
    /// —«de», «la», «del»— que aparecen en cualquier frase y no identifican a
    /// nadie.
    /// </remarks>
    public IReadOnlyList<string> Words { get; } =
    [
        .. PersonalTerms
            .SelectMany(t => t.Split(
                [' ', '\t', '.'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(w => w.Length >= 4)
            .Distinct(StringComparer.OrdinalIgnoreCase),
    ];

    public static RedactionSettings Of(string? commaSeparated) =>
        new([.. (commaSeparated ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);
}
