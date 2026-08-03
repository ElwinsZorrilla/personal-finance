using System.Text;

namespace Margen.Ingest.Statements;

/// <summary>
/// Lee un CSV de estado de cuenta, venga como venga.
/// </summary>
/// <remarks>
/// No se usa una biblioteca porque lo que hay que resolver no es leer CSV
/// —eso son cuarenta líneas— sino **no saber de antemano cómo lo escribe cada
/// banco**: separador, codificación y filas de cabecera cambian entre uno y
/// otro, y con varios bancos eso deja de ser un caso raro.
/// </remarks>
public static class Csv
{
    /// <summary>Separadores que se prueban, en orden de probabilidad.</summary>
    public static readonly char[] Candidates = [',', ';', '\t', '|'];

    /// <summary>
    /// Cuál es el separador de este archivo.
    /// </summary>
    /// <remarks>
    /// Gana el que produce **el mismo número de columnas en más filas**, no el
    /// que más veces aparece. Una descripción con comas dentro —«SUPERMERCADO
    /// NACIONAL, SANTIAGO»— hace que la coma gane por frecuencia en un archivo
    /// separado por punto y coma, y a partir de ahí todo se lee corrido.
    /// </remarks>
    public static char DetectDelimiter(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        char best = ',';
        int bestScore = -1;

        foreach (char candidate in Candidates)
        {
            List<string[]> rows = Parse(content, candidate, 10);
            if (rows.Count == 0) continue;

            int columns = rows[0].Length;
            if (columns < 2) continue;

            int consistent = rows.Count(r => r.Length == columns);

            // Se puntúa por filas consistentes y, a igualdad, por más columnas:
            // un separador equivocado suele dar una sola columna en todas las
            // filas, que también es «consistente».
            int score = (consistent * 100) + columns;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Parte el contenido en filas y celdas, según RFC 4180.
    /// </summary>
    /// <param name="content">El archivo entero.</param>
    /// <param name="delimiter">El separador.</param>
    /// <param name="maxRows">Tope de filas, o cero para todas.</param>
    /// <remarks>
    /// Las comillas dobles protegen separadores y saltos de línea dentro de una
    /// celda, y dos comillas seguidas dentro de una celda entrecomillada son una
    /// comilla literal. Sin eso, una descripción con una coma parte la fila en
    /// dos y el monto se lee de la columna equivocada.
    /// </remarks>
    public static List<string[]> Parse(string content, char delimiter, int maxRows = 0)
    {
        ArgumentNullException.ThrowIfNull(content);

        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (quoted)
            {
                if (c != '"')
                {
                    cell.Append(c);
                    continue;
                }

                if (i + 1 < content.Length && content[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                    continue;
                }

                quoted = false;
                continue;
            }

            if (c == '"' && cell.Length == 0)
            {
                quoted = true;
                continue;
            }

            if (c == delimiter)
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();
                continue;
            }

            if (c is '\r') continue;

            if (c is '\n')
            {
                row.Add(cell.ToString().Trim());
                cell.Clear();

                if (row.Count > 1 || row[0].Length > 0) rows.Add([.. row]);
                row.Clear();

                if (maxRows > 0 && rows.Count >= maxRows) return rows;
                continue;
            }

            cell.Append(c);
        }

        row.Add(cell.ToString().Trim());
        if (row.Count > 1 || row[0].Length > 0) rows.Add([.. row]);

        return rows;
    }

    /// <summary>
    /// Decodifica los bytes del archivo a texto.
    /// </summary>
    /// <remarks>
    /// UTF-8 si lo es —con o sin marca de orden— y **Latin-1 si no**. Los
    /// bancos dominicanos exportan con frecuencia en la página de códigos de
    /// Windows, y leer esos bytes como UTF-8 convierte «SANTIAGO RODRÍGUEZ» en
    /// basura o lanza. Para las letras acentuadas del castellano, Latin-1 y
    /// Windows-1252 coinciden byte a byte.
    ///
    /// Se prueba UTF-8 primero **con excepción activada**: sin eso, el
    /// decodificador sustituye lo que no entiende por un carácter de reemplazo
    /// y no hay manera de saber que se equivocó.
    /// </remarks>
    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
