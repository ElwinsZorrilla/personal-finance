using Margen.Domain;

namespace Margen.Ingest.Tests;

/// <summary>
/// El parser de Qik Banco Digital Dominicano contra sus muestras reales.
/// </summary>
/// <remarks>
/// Los cuerpos de abajo son la estructura exacta de los correos que hay en
/// `docs/muestras/qik-*.txt`, con los datos ya cambiados por el redactor. Se
/// copian aquí y no se leen del disco por lo mismo que en el parser del
/// Popular: las muestras llevan datos de una persona real y están en
/// `.gitignore`, y una prueba que no corre en una máquina limpia no es una
/// prueba.
///
/// **La diferencia de este banco con el Popular es que su asunto no dice si la
/// transacción pasó.** Los tres asuntos aparecen en aprobadas y en declinadas.
/// </remarks>
public sealed class QikParserTests
{
    private static readonly QikParser Parser = new();

    private static RawEmail Correo(string asunto, string cuerpo) =>
        new("<x@qik.do>", "notificaciones@qik.do", asunto, cuerpo,
            new DateTime(2026, 4, 14, 0, 32, 0, DateTimeKind.Utc));

    /// <summary>Compra aprobada. La etiqueta del comercio es «Localidad».</summary>
    private const string Aprobada = """
        <html><body>
        <td><strong>¡Hola NOMBRE APELLIDO!</strong></td>
        <td>Tarjeta Débito 49*************1234</td>
        <td>Se hizo una transacción de <b>RD$ 111.11</b> en <strong>PedidosYa*Papa Johns Ca</strong>
        con tu Tarjeta de Débito Qik que termina en <strong>49*************1234</strong></td>
        <table><tbody>
        <tr><td>Localidad</td><td><strong>PedidosYa*Papa Johns Ca</strong></td></tr>
        <tr><td>Fecha y hora</td><td><strong>04-13-2026 08:32 PM (AST)</strong></td></tr>
        <tr><td>Monto</td><td><b>RD$ 111.11</b></td></tr>
        <tr><td>Balance Disponible</td><td><strong>RD$ 11.11</strong></td></tr>
        </tbody></table>
        </body></html>
        """;

    /// <summary>Compra declinada. Trae «Estatus» y la etiqueta pasa a «Lugar».</summary>
    private const string Declinada = """
        <html><body>
        <td>¡Hola, NOMBRE APELLIDO!</td>
        <td>Tarjeta débito 49*************1234</td>
        <td>Se intentó realizar una compra de <strong>$ 1.11</strong> en <strong>APPLE.COM/BILL</strong>
        con tu Tarjeta de Débito Qik Visa que termina en <strong>*1234</strong>.</td>
        <table><tbody>
        <tr><td>Estatus</td><td>Declinado</td></tr>
        <tr><td>Motivo</td><td>Balance Insuficiente</td></tr>
        <tr><td>Fecha y hora</td><td>06-20-2026 11:57 AM (AST)</td></tr>
        <tr><td>Monto</td><td>$ 1.11</td></tr>
        <tr><td>Lugar</td><td>APPLE.COM/BILL</td></tr>
        <tr><td>Balance Disponible</td><td>RD$ 11.11</td></tr>
        </tbody></table>
        </body></html>
        """;

    // ---------- Quién es suyo ----------

    [Fact]
    public void reconoce_sus_correos_por_el_remitente()
    {
        Assert.True(Parser.CanHandle(Correo("Usaste tu tarjeta de débito Qik", Aprobada)));
    }

    [Fact]
    public void no_toca_los_de_otro_banco()
    {
        var ajeno = new RawEmail(
            "<x@popularenlinea.com>", "notificaciones@popularenlinea.com",
            "Notificación de Consumo", "lo que sea", DateTime.UtcNow);

        Assert.False(Parser.CanHandle(ajeno));
    }

    // ---------- La compra aprobada ----------

    [Fact]
    public void interpreta_una_compra()
    {
        ParseResult r = Parser.Parse(Correo("Usaste tu tarjeta de débito Qik", Aprobada));

        Assert.Equal(ParseStatus.Parsed, r.Status);

        ParsedTransaction tx = r.Transaction!;
        Assert.Equal(new Money(11_111), tx.Amount);
        Assert.Equal(TxKind.Purchase, tx.Kind);
        Assert.Equal("1234", tx.AccountLastFour);
        Assert.Equal("DOP", tx.Currency);
        Assert.Equal("PedidosYa*Papa Johns Ca", tx.MerchantRaw);
    }

    [Fact]
    public void el_balance_disponible_no_se_confunde_con_el_monto()
    {
        // Las dos cifras están en el mismo correo y la de abajo es el saldo que
        // queda. Tomar la última, o la primera del texto, guardaría el saldo
        // como si fuera el gasto.
        ParsedTransaction tx = Parser
            .Parse(Correo("Usaste tu tarjeta de débito Qik", Aprobada)).Transaction!;

        Assert.Equal(new Money(11_111), tx.Amount);
        Assert.NotEqual(new Money(1_111), tx.Amount);
    }

    // ---------- El asunto no manda ----------

    [Fact]
    public void una_compra_declinada_no_crea_movimiento()
    {
        // **El criterio de este parser.** Un movimiento por una compra que el
        // banco rechazó infla el gasto del período, y nada avisa.
        ParseResult r = Parser.Parse(
            Correo("Se intentó realizar una compra con tu tarjeta de débito Qik", Declinada));

        Assert.Equal(ParseStatus.NeedsReview, r.Status);
        Assert.Null(r.Transaction);
        Assert.Contains("declinó", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void el_motivo_del_rechazo_llega_a_la_pantalla_de_revision()
    {
        ParseResult r = Parser.Parse(
            Correo("Se hizo una transacción con tu tarjeta de débito Qik", Declinada));

        Assert.Contains("balance insuficiente", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Usaste tu tarjeta de débito Qik")]
    [InlineData("Se hizo una transacción con tu tarjeta de débito Qik")]
    [InlineData("Se intentó realizar una compra con tu tarjeta de débito Qik")]
    public void el_mismo_asunto_puede_ser_aprobada_o_declinada(string asunto)
    {
        // Los tres asuntos aparecen en los dos casos, así que lo que decide es
        // el cuerpo. Un parser que se fiara del asunto —como el del Popular,
        // donde sí decide— crearía movimientos que nunca existieron.
        Assert.Equal(ParseStatus.Parsed, Parser.Parse(Correo(asunto, Aprobada)).Status);
        Assert.Equal(ParseStatus.NeedsReview, Parser.Parse(Correo(asunto, Declinada)).Status);
    }

    // ---------- La fecha ----------

    [Fact]
    public void la_fecha_empieza_por_el_mes()
    {
        // `04-13-2026` es el 13 de abril. Es la cuarta forma de escribir una
        // fecha en este proyecto y la primera que empieza por el mes.
        ParsedTransaction tx = Parser
            .Parse(Correo("Usaste tu tarjeta de débito Qik", Aprobada)).Transaction!;

        DateOnly local = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                tx.OccurredAtUtc,
                TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")));

        Assert.Equal(new DateOnly(2026, 4, 13), local);
    }

    [Fact]
    public void la_hora_del_banco_es_local_y_se_guarda_en_utc()
    {
        // Qik sí manda la hora y dice la zona: `(AST)`. Las 20:32 en Santo
        // Domingo son las 00:32 UTC del día siguiente, y guardar la hora del
        // banco como si fuera UTC movería la compra cuatro horas.
        ParsedTransaction tx = Parser
            .Parse(Correo("Usaste tu tarjeta de débito Qik", Aprobada)).Transaction!;

        Assert.Equal(new DateTime(2026, 4, 14, 0, 32, 0, DateTimeKind.Utc), tx.OccurredAtUtc);
    }

    [Fact]
    public void las_doce_del_mediodia_no_se_confunden_con_medianoche()
    {
        // `11:57 AM` es media mañana. El formato de doce horas es donde esto se
        // rompe: con `HH` en vez de `h`, las 12 PM se leen como las 12 de la
        // noche y el movimiento cambia de día.
        ParsedTransaction? tx = Parser
            .Parse(Correo("Usaste tu tarjeta", Aprobada.Replace(
                "04-13-2026 08:32 PM", "04-13-2026 11:57 AM", StringComparison.Ordinal)))
            .Transaction;

        Assert.Equal(new DateTime(2026, 4, 13, 15, 57, 0, DateTimeKind.Utc), tx!.OccurredAtUtc);
    }

    // ---------- Fallo cerrado ----------

    [Fact]
    public void sin_fecha_no_se_crea_nada()
    {
        string sinFecha = Aprobada.Replace("Fecha y hora", "Otra cosa", StringComparison.Ordinal);

        ParseResult r = Parser.Parse(Correo("Usaste tu tarjeta de débito Qik", sinFecha));

        Assert.Equal(ParseStatus.NeedsReview, r.Status);
        Assert.Contains("fecha", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void sin_los_cuatro_digitos_no_se_crea_nada()
    {
        string sinTarjeta = Aprobada
            .Replace("49*************1234", "la tarjeta", StringComparison.Ordinal)
            .Replace("*1234", "la tarjeta", StringComparison.Ordinal);

        ParseResult r = Parser.Parse(Correo("Usaste tu tarjeta de débito Qik", sinTarjeta));

        Assert.Equal(ParseStatus.NeedsReview, r.Status);
    }

    [Fact]
    public void un_cuerpo_vacio_no_crea_nada()
    {
        ParseResult r = Parser.Parse(Correo("Usaste tu tarjeta de débito Qik", string.Empty));

        Assert.Equal(ParseStatus.NeedsReview, r.Status);
    }

    [Fact]
    public void un_correo_nulo_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => Parser.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => Parser.CanHandle(null!));
    }

    // ---------- El comercio ----------

    [Fact]
    public void la_etiqueta_del_comercio_cambia_segun_el_resultado()
    {
        // `Localidad` cuando pasa y `Lugar` cuando no. Buscar solo una dejaría
        // la mitad de los correos sin comercio.
        ParsedTransaction aprobada = Parser
            .Parse(Correo("Usaste tu tarjeta", Aprobada)).Transaction!;

        Assert.Equal("PedidosYa*Papa Johns Ca", aprobada.MerchantRaw);

        // En la declinada no se crea movimiento, así que se comprueba sobre una
        // copia sin la fila de estatus: la etiqueta sigue siendo «Lugar».
        string sinEstatus = Declinada
            .Replace("<tr><td>Estatus</td><td>Declinado</td></tr>", string.Empty,
                StringComparison.Ordinal);

        ParsedTransaction conLugar = Parser
            .Parse(Correo("Usaste tu tarjeta", sinEstatus)).Transaction!;

        Assert.Equal("APPLE.COM/BILL", conLugar.MerchantRaw);
    }

    [Fact]
    public void el_signo_de_peso_a_secas_tambien_es_peso_dominicano()
    {
        // Qik escribe `$ 1.11` y `RD$ 111.11` en el mismo correo, y las dos son
        // pesos.
        string sinEstatus = Declinada
            .Replace("<tr><td>Estatus</td><td>Declinado</td></tr>", string.Empty,
                StringComparison.Ordinal);

        ParsedTransaction tx = Parser
            .Parse(Correo("Usaste tu tarjeta", sinEstatus)).Transaction!;

        Assert.Equal(new Money(111), tx.Amount);
        Assert.Equal("DOP", tx.Currency);
    }
}
