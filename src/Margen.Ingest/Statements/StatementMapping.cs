using System.Globalization;
using Margen.Domain;

namespace Margen.Ingest.Statements;

/// <summary>Cómo escribe los decimales el banco.</summary>
public enum DecimalStyle
{
    /// <summary>`1,234.56`. Millares con coma, decimales con punto.</summary>
    Point,

    /// <summary>`1.234,56`. Al revés. Lo usan los bancos con la configuración europea.</summary>
    Comma,
}

/// <summary>
/// Qué columna es cuál en el CSV de un banco.
/// </summary>
/// <param name="Delimiter">El separador, o nulo para detectarlo.</param>
/// <param name="SkipRows">Filas de cabecera o de membrete que se saltan.</param>
/// <param name="DateColumn">Índice de la columna de la fecha, desde cero.</param>
/// <param name="DateFormat">Su formato exacto, por ejemplo <c>dd/MM/yyyy</c>.</param>
/// <param name="DescriptionColumn">Índice de la descripción.</param>
/// <param name="AmountColumn">Índice del monto con signo, si el banco usa una sola columna.</param>
/// <param name="DebitColumn">Índice de los cargos, si usa dos columnas.</param>
/// <param name="CreditColumn">Índice de los abonos, si usa dos columnas.</param>
/// <param name="Decimals">Cómo escribe los decimales.</param>
/// <param name="InvertSign">Si en la columna única los cargos vienen positivos.</param>
/// <remarks>
/// **Esto es lo que hace que no haga falta conocer el formato de antemano.** El
/// humano mira su CSV, dice qué columna es cuál, ve una vista previa de lo que
/// se entendió y solo entonces importa. Es lo contrario de adivinar, y con
/// varios bancos es la única forma que escala: un perfil por banco, escrito una
/// vez.
///
/// El formato de fecha es explícito y no una lista de candidatos: `01/02/2026`
/// es el 1 de febrero o el 2 de enero según el banco, y las dos lecturas son
/// plausibles. Adivinar aquí mete movimientos en el período equivocado.
/// </remarks>
public sealed record StatementMapping(
    char? Delimiter,
    int SkipRows,
    int DateColumn,
    string DateFormat,
    int DescriptionColumn,
    int? AmountColumn,
    int? DebitColumn,
    int? CreditColumn,
    DecimalStyle Decimals,
    bool InvertSign)
{
    /// <summary>Si el mapeo describe algo que se puede leer.</summary>
    public bool IsUsable =>
        DateColumn >= 0
        && DescriptionColumn >= 0
        && !string.IsNullOrWhiteSpace(DateFormat)
        && (AmountColumn is >= 0 || DebitColumn is >= 0 || CreditColumn is >= 0);
}

/// <summary>Una línea del estado de cuenta, ya interpretada.</summary>
/// <param name="LineNumber">Qué fila del archivo era. Para poder decir cuál falló.</param>
/// <param name="Date">El día, tal como lo escribió el banco.</param>
/// <param name="Description">La descripción, sin tocar.</param>
/// <param name="Amount">El monto, siempre positivo.</param>
/// <param name="Direction">Si entró o salió dinero.</param>
public readonly record struct StatementLine(
    int LineNumber,
    DateOnly Date,
    string Description,
    Money Amount,
    TxDirection Direction);

/// <summary>Lo que salió de leer un archivo entero.</summary>
/// <param name="Lines">Las filas que se entendieron.</param>
/// <param name="Rejected">Las que no, con el motivo. **No se tiran en silencio.**</param>
public sealed record StatementParse(
    IReadOnlyList<StatementLine> Lines,
    IReadOnlyList<RejectedLine> Rejected);

/// <summary>Una fila que no se pudo leer, y por qué.</summary>
public readonly record struct RejectedLine(int LineNumber, string Raw, string Reason);

/// <summary>
/// Aplica un mapeo a un CSV y devuelve las líneas del estado de cuenta.
/// </summary>
public static class StatementReader
{
    /// <summary>
    /// Lee el archivo con el mapeo dado.
    /// </summary>
    /// <remarks>
    /// Las filas que no se entienden **se devuelven con su número y su motivo**,
    /// no se descartan. Un importador que se come en silencio las filas raras
    /// deja un estado de cuenta que parece cuadrado y no lo está, y eso es peor
    /// que no importar nada.
    /// </remarks>
    public static StatementParse Read(string content, StatementMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(mapping);

        char delimiter = mapping.Delimiter ?? Csv.DetectDelimiter(content);
        List<string[]> rows = Csv.Parse(content, delimiter);

        var lines = new List<StatementLine>();
        var rejected = new List<RejectedLine>();

        for (int i = mapping.SkipRows; i < rows.Count; i++)
        {
            string[] row = rows[i];
            int lineNumber = i + 1;

            string? problem = TryRead(row, mapping, lineNumber, out StatementLine line);

            if (problem is null) lines.Add(line);
            else rejected.Add(new RejectedLine(lineNumber, string.Join(delimiter, row), problem));
        }

        return new StatementParse(lines, rejected);
    }

    private static string? TryRead(
        string[] row,
        StatementMapping mapping,
        int lineNumber,
        out StatementLine line)
    {
        line = default;

        string? rawDate = At(row, mapping.DateColumn);
        if (rawDate is null) return $"No hay columna {mapping.DateColumn} para la fecha.";

        if (!DateOnly.TryParseExact(
                rawDate, mapping.DateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out DateOnly date))
        {
            return $"«{rawDate}» no es una fecha con formato {mapping.DateFormat}.";
        }

        string description = At(row, mapping.DescriptionColumn) ?? string.Empty;
        if (description.Length == 0) return "La descripción está vacía.";

        (Money amount, TxDirection direction, string? problem) = ReadAmount(row, mapping);
        if (problem is not null) return problem;

        line = new StatementLine(lineNumber, date, description, amount, direction);

        return null;
    }

    private static (Money Amount, TxDirection Direction, string? Problem) ReadAmount(
        string[] row,
        StatementMapping mapping)
    {
        // Dos columnas: cargos en una, abonos en otra. Es lo más frecuente en
        // los estados de cuenta dominicanos y no tiene ninguna ambigüedad de
        // signo, así que se prueba primero.
        if (mapping.DebitColumn is int debitAt || mapping.CreditColumn is int)
        {
            Money debit = ReadCents(At(row, mapping.DebitColumn ?? -1), mapping.Decimals);
            Money credit = ReadCents(At(row, mapping.CreditColumn ?? -1), mapping.Decimals);

            if (debit.Cents != 0 && credit.Cents != 0)
            {
                return (Money.Zero, TxDirection.Outflow,
                    "La fila trae cargo y abono a la vez.");
            }

            if (debit.Cents != 0) return (debit.Abs, TxDirection.Outflow, null);
            if (credit.Cents != 0) return (credit.Abs, TxDirection.Inflow, null);

            return (Money.Zero, TxDirection.Outflow, "La fila no trae ni cargo ni abono.");
        }

        string? raw = At(row, mapping.AmountColumn ?? -1);
        if (string.IsNullOrWhiteSpace(raw)) return (Money.Zero, TxDirection.Outflow, "Sin monto.");

        Money signed = ReadCents(raw, mapping.Decimals);
        if (signed.Cents == 0) return (Money.Zero, TxDirection.Outflow, $"«{raw}» no es un monto.");

        // Con una sola columna, el signo dice la dirección. `InvertSign` existe
        // porque hay bancos que escriben los cargos en positivo y los abonos en
        // negativo, que es exactamente al revés de lo habitual: sin la opción,
        // un estado de cuenta entero entraría con todos los gastos como
        // ingresos y el saldo saldría al doble.
        bool outflow = mapping.InvertSign ? signed.Cents > 0 : signed.Cents < 0;

        return (signed.Abs, outflow ? TxDirection.Outflow : TxDirection.Inflow, null);
    }

    /// <summary>
    /// Convierte el texto de un importe en centavos, sin coma flotante.
    /// </summary>
    /// <remarks>
    /// Se quita la moneda, los espacios y el separador de millares —cuál es
    /// depende del estilo—, se parte por el separador decimal y se construye
    /// entero: unidades por cien más centavos.
    ///
    /// Los paréntesis significan negativo en contabilidad: `(1,234.56)` es un
    /// cargo. Aparece en exportaciones hechas desde una hoja de cálculo.
    /// </remarks>
    internal static Money ReadCents(string? raw, DecimalStyle style)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Money.Zero;

        string text = raw.Trim();
        bool negative = text.StartsWith('-') || (text.StartsWith('(') && text.EndsWith(')'));

        var digits = new System.Text.StringBuilder(text.Length);
        char decimalMark = style == DecimalStyle.Point ? '.' : ',';
        char groupMark = style == DecimalStyle.Point ? ',' : '.';

        foreach (char c in text)
        {
            if (char.IsAsciiDigit(c)) digits.Append(c);
            else if (c == decimalMark) digits.Append('.');
            else if (c == groupMark) continue;
        }

        string clean = digits.ToString();
        if (clean.Length == 0) return Money.Zero;

        string[] parts = clean.Split('.');
        if (parts.Length > 2) return Money.Zero;

        if (!long.TryParse(
                parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long units))
        {
            return Money.Zero;
        }

        long cents = 0;
        if (parts.Length == 2 && parts[1].Length > 0)
        {
            string padded = parts[1].Length == 1 ? parts[1] + "0" : parts[1][..2];
            if (!long.TryParse(
                    padded, NumberStyles.None, CultureInfo.InvariantCulture, out cents))
            {
                return Money.Zero;
            }
        }

        try
        {
            long total = checked((units * 100) + cents);
            return new Money(negative ? -total : total);
        }
        catch (OverflowException)
        {
            return Money.Zero;
        }
    }

    private static string? At(string[] row, int index) =>
        index >= 0 && index < row.Length ? row[index] : null;
}
