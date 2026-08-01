using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Margen.Domain;

namespace Margen.Ingest;

/// <summary>
/// Las huellas que hacen que reprocesar no duplique.
/// </summary>
/// <remarks>
/// Tres defensas, cada una para un fallo distinto:
///
/// **El identificador de mensaje** cubre el caso normal: el worker vuelve a
/// mirar el buzón y el correo sigue ahí.
///
/// **El hash del cuerpo** cubre que el servidor de correo reescriba el
/// identificador al reenviar. El cuerpo es el mismo.
///
/// **La huella del movimiento** cubre que el banco mande dos correos distintos
/// —una notificación y su confirmación— por la misma compra.
/// </remarks>
public static class Fingerprints
{
    /// <summary>
    /// Hash del cuerpo, normalizado.
    /// </summary>
    /// <remarks>
    /// Se normalizan los espacios y los saltos de línea antes de calcularlo:
    /// dos servidores de correo entregan el mismo cuerpo con saltos distintos
    /// —CRLF frente a LF, o reflujo a 78 columnas— y sin normalizar serían dos
    /// hashes distintos y el correo se procesaría dos veces.
    /// </remarks>
    public static string OfBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        return Hex(NormalizeWhitespace(body));
    }

    /// <summary>
    /// Huella de un movimiento: cuenta, día local, monto, comercio y —cuando el
    /// banco la da— su referencia.
    /// </summary>
    /// <remarks>
    /// El día es el **local**, no el UTC. Con el día UTC, dos compras idénticas
    /// hechas a las once de la noche de dos días locales distintos caerían en
    /// el mismo día UTC y la segunda se rechazaría siendo real.
    ///
    /// La referencia entra cuando existe, y es lo que distingue dos cargos
    /// legítimos de un duplicado. Sin ella, la huella tiene granularidad de día
    /// y **dos cafés seguidos en el mismo sitio compartirían huella**: el
    /// segundo se rechazaría como duplicado y ese gasto real desaparecería del
    /// período sin dejar rastro.
    ///
    /// Cuando no hay referencia se vuelve a la granularidad de día, que es el
    /// lado conservador: es preferible mandar un cargo real a Revisión que
    /// crear dos por una compra.
    /// </remarks>
    public static string OfTransaction(
        Guid accountId,
        DateOnly localDay,
        Money amount,
        string merchantNormalized,
        string? reference = null)
    {
        ArgumentNullException.ThrowIfNull(merchantNormalized);

        string tail = string.IsNullOrWhiteSpace(reference)
            ? string.Empty
            : $"|{reference.Trim().ToUpperInvariant()}";

        return Hex(string.Create(
            CultureInfo.InvariantCulture,
            $"{accountId:D}|{localDay:yyyy-MM-dd}|{amount.Cents}|{merchantNormalized}{tail}"));
    }

    /// <summary>
    /// Normaliza el comercio para agrupar el historial: mayúsculas, sin
    /// acentos, sin espacios repetidos y sin la cola del terminal.
    /// </summary>
    /// <remarks>
    /// Los bancos añaden sufijos que cambian entre cargos del mismo sitio
    /// —número de terminal, sucursal, ciudad—. Sin quitarlos, cada compra en el
    /// mismo supermercado parecería un comercio nuevo y ninguna regla llegaría
    /// a aplicarse dos veces.
    /// </remarks>
    public static string NormalizeMerchant(string merchant)
    {
        ArgumentNullException.ThrowIfNull(merchant);

        string upper = merchant.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(upper.Length);
        bool lastWasSpace = false;

        foreach (char c in upper)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || c is '-' or '_' or '*' or '#')
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            builder.Append(c);
        }

        return builder.ToString().Trim().Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        bool lastWasSpace = false;

        foreach (char c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            builder.Append(c);
        }

        return builder.ToString().TrimEnd();
    }

    private static string Hex(string material) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
}
