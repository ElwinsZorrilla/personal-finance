using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Margen.Ingest;

/// <summary>
/// Deja un correo en texto legible, venga como venga.
/// </summary>
/// <remarks>
/// El Banco Popular manda dos formatos según el aviso: consumo y retiro llegan
/// como texto plano con la tabla aplanada; transferencia y depósito, como HTML
/// entero con hojas de estilo y comentarios condicionales de Outlook.
///
/// Un parser por formato serían dos parsers para el mismo banco, y el día que
/// el banco cambie uno de los dos habría que acordarse de los dos. Aquí se
/// normalizan los dos a lo mismo y el parser mira un solo texto.
/// </remarks>
public static partial class EmailText
{
    /// <summary>
    /// Quita etiquetas, estilos y caracteres invisibles, y colapsa los espacios.
    /// </summary>
    public static string Normalize(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        string text = body
            // Guiones suaves: no se ven y parten una palabra en dos para
            // cualquier expresión regular.
            .Replace("­", string.Empty, StringComparison.Ordinal);

        if (LooksLikeHtml(text))
        {
            // El orden importa: primero lo que lleva texto dentro y no es
            // contenido —estilo, guion, comentarios—, y después las etiquetas.
            // Al revés, el contenido de un `<style>` quedaría suelto en medio
            // del texto y el parser leería nombres de clase como si fueran
            // datos.
            text = StylePattern().Replace(text, " ");
            text = ScriptPattern().Replace(text, " ");
            text = CommentPattern().Replace(text, " ");

            // Los saltos de bloque se convierten en saltos de línea antes de
            // borrar el resto: sin esto, `Monto: RD$ 100</td><td>Fecha: ...`
            // queda pegado y las dos etiquetas caen en la misma línea.
            text = BlockBreakPattern().Replace(text, "\n");
            text = TagPattern().Replace(text, " ");
        }

        text = WebUtility.HtmlDecode(text);

        // El espacio duro se decodifica a U+00A0, que no es espacio para
        // `\s` en modo ordinal ni para `Trim`.
        text = text.Replace(' ', ' ');

        return CollapseSpaces(text);
    }

    /// <summary>
    /// El valor que sigue a una etiqueta, hasta el fin de línea.
    /// </summary>
    /// <remarks>
    /// Devuelve nulo cuando la etiqueta no está, y **nunca una cadena vacía
    /// disfrazada de valor**: quien llama distingue «no venía» de «venía vacío»
    /// y en los dos casos manda el movimiento a revisión en vez de asumir algo.
    /// </remarks>
    public static string? ValueAfter(string text, params string[] labels)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(labels);

        foreach (string label in labels)
        {
            var pattern = new Regex(
                Regex.Escape(label) + @"\s*:?\s*(?<valor>[^\r\n]+)",
                RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(2));

            Match match = pattern.Match(text);
            if (!match.Success) continue;

            string value = match.Groups["valor"].Value.Trim();
            if (value.Length > 0) return value;
        }

        return null;
    }

    private static bool LooksLikeHtml(string text) =>
        text.Contains("<html", StringComparison.OrdinalIgnoreCase)
        || text.Contains("<td", StringComparison.OrdinalIgnoreCase)
        || text.Contains("<table", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Colapsa espacios repetidos sin perder los saltos de línea, que es lo que
    /// separa una etiqueta de la siguiente.
    /// </summary>
    private static string CollapseSpaces(string text)
    {
        var builder = new StringBuilder(text.Length);
        bool lastWasSpace = false;
        bool lastWasNewline = true;

        foreach (char c in text)
        {
            if (c is '\r') continue;

            if (c is '\n')
            {
                if (!lastWasNewline) builder.Append('\n');
                lastWasNewline = true;
                lastWasSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && !lastWasNewline) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            lastWasNewline = false;
            builder.Append(c);
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex(@"<style[^>]*>.*?</style>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex StylePattern();

    [GeneratedRegex(@"<script[^>]*>.*?</script>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex ScriptPattern();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline, 2000)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"</\s*(?:td|tr|th|p|div|br|li|h[1-6])\s*>|<\s*br\s*/?\s*>",
        RegexOptions.IgnoreCase, 2000)]
    private static partial Regex BlockBreakPattern();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, 2000)]
    private static partial Regex TagPattern();
}
