using Margen.Domain;

namespace Margen.Ingest.Tests;

/// <summary>
/// El parser del Banco Popular contra las muestras reales anonimizadas.
/// </summary>
/// <remarks>
/// Los cuerpos de abajo son la estructura exacta de los correos que hay en
/// `docs/muestras/`, con los datos ya cambiados por el redactor. Están copiados
/// aquí y no se leen del disco a propósito: las muestras están en `.gitignore`
/// —llevan datos de una persona real— y una prueba que no corre en una máquina
/// limpia no es una prueba, es una comprobación manual con otro nombre.
///
/// Cuando el banco cambie una plantilla, la prueba que falle dirá qué cambió.
/// </remarks>
public sealed class PopularParserTests
{
    private static readonly PopularParser Parser = new();

    private static RawEmail Correo(string asunto, string cuerpo, DateTime? recibido = null) =>
        new(
            "<x@popularenlinea.com>",
            "notificaciones@popularenlinea.com",
            asunto,
            cuerpo,
            recibido ?? new DateTime(2026, 7, 26, 21, 1, 9, DateTimeKind.Utc));

    /// <summary>Consumo: texto plano con la tabla aplanada y el comercio partido.</summary>
    private const string Consumo = """
        Estimado (a) NOMBRE APELLIDO

        Gracias por utilizar su VISA ISI, terminada en 1234.

        A continuación le informamos el detalle de su transacción:


        Monto 	Moneda 	Fecha 	Comercio	Estatus
        RD$1,111.11 	Peso dominicano 	26/07/2026 	SM NACIONAL
        CHARLES 	Aprobada

        En caso de requerir mayor información, puede comunicarse con nosotros
        llamando al 809-544-5555.
        """;

    private const string Retiro = """
        Estimado (a)

        Gracias por utilizar su Tarjeta Visa Débito Clásica, terminada en 1234.

        A continuación detalle de la transacción:


        Monto 	Moneda 	Fecha 	Cajero Automatico	 Estatus
        RD$11,111.11 	Peso dominicano 	29/07/2026 	BANCO POPULAR
        DO 	Aprobada
        """;

    /// <summary>Transferencia: HTML entero, con etiqueta y valor por línea.</summary>
    private const string Transferencia = """
        <html><head><style>.fix-link a{color:inherit!important}</style></head>
        <body><table><tr><td>Estimado (a) NOMBRE APELLIDO</td></tr>
        <tr><td>Le informamos que su transacción por <span>pagos al instante</span> fue enviada satisfactoriamente.</td></tr>
        <tr><td>Beneficiario: NOMBRE APELLIDO</td></tr>
        <tr><td>Cuenta o Producto:******_1234</td></tr>
        <tr><td>Monto: RD$ 11,111.11</td></tr>
        <tr><td>Fecha: 12/6/2026</td></tr>
        </table></body></html>
        """;

    private const string Deposito = """
        <html><body><table>
        <tr><td>Estimado (a) NOMBRE APELLIDO</td></tr>
        <tr><td>A continuación, le informamos el detalle del depósito:</td></tr>
        <tr><th>Monto </th><th>Fecha </th><th>Canal </th></tr>
        <tr><th>RD&nbsp;1,111.11&nbsp;</th><td>18/6/2026</td><td>ATM</td></tr>
        <tr><td>Cuenta terminada en 1234</td></tr>
        </table></body></html>
        """;

    // ---------- Consumo ----------

    [Fact]
    public void interpreta_una_compra()
    {
        ParseResult result = Parser.Parse(Correo("Notificación de Consumo", Consumo));

        Assert.Equal(ParseStatus.Parsed, result.Status);

        ParsedTransaction tx = result.Transaction!;
        Assert.Equal(new Money(111_111), tx.Amount);
        Assert.Equal(TxKind.Purchase, tx.Kind);
        Assert.Equal("1234", tx.AccountLastFour);
        Assert.Equal("DOP", tx.Currency);
    }

    [Fact]
    public void el_comercio_partido_en_dos_lineas_se_recompone()
    {
        // La tabla llega aplanada y «SM NACIONAL CHARLES» viene en dos líneas.
        // Quedarse con la primera agruparía este comercio aparte de sí mismo.
        ParsedTransaction tx = Parser.Parse(Correo("Notificación de Consumo", Consumo)).Transaction!;

        Assert.Contains("SM NACIONAL", tx.MerchantRaw, StringComparison.Ordinal);
        Assert.Contains("CHARLES", tx.MerchantRaw, StringComparison.Ordinal);
    }

    [Fact]
    public void el_comercio_no_es_la_fila_de_encabezados()
    {
        // «Comercio» aparece primero en la línea de encabezados, así que buscar
        // por etiqueta devolvía «Estatus».
        ParsedTransaction tx = Parser.Parse(Correo("Notificación de Consumo", Consumo)).Transaction!;

        Assert.DoesNotContain("Estatus", tx.MerchantRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Moneda", tx.MerchantRaw, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Retiro ----------

    [Fact]
    public void interpreta_un_retiro()
    {
        ParseResult result = Parser.Parse(Correo("Notificación de Retiro", Retiro));

        Assert.Equal(ParseStatus.Parsed, result.Status);
        Assert.Equal(TxKind.Withdrawal, result.Transaction!.Kind);
        Assert.Equal(new Money(1_111_111), result.Transaction.Amount);
    }

    [Fact]
    public void un_retiro_es_egreso()
    {
        // El dinero sigue siendo tuyo, en el bolsillo. Pero si no registras en
        // qué lo gastaste, tratarlo como traspaso lo haría desaparecer del
        // gasto del período.
        ParsedTransaction tx = Parser.Parse(Correo("Notificación de Retiro", Retiro)).Transaction!;

        Assert.Equal(TxDirection.Outflow, Directions.Of(tx.Kind));
    }

    // ---------- Transferencia ----------

    [Fact]
    public void interpreta_una_transferencia_en_html()
    {
        ParseResult result = Parser.Parse(
            Correo("Notificaciones Pagos al Instante transferencia enviada", Transferencia));

        Assert.Equal(ParseStatus.Parsed, result.Status);

        ParsedTransaction tx = result.Transaction!;
        Assert.Equal(TxKind.Transfer, tx.Kind);
        Assert.Equal(new Money(1_111_111), tx.Amount);
        Assert.Equal("1234", tx.AccountLastFour);
    }

    [Fact]
    public void pagos_al_instante_no_se_confunde_con_pago_de_tarjeta()
    {
        // El asunto lleva la palabra «pago» y caía en pago de tarjeta, que es
        // un traspaso y no contaría como gasto.
        ParsedTransaction tx = Parser.Parse(
            Correo("Notificaciones Pagos al Instante transferencia enviada", Transferencia))
            .Transaction!;

        Assert.NotEqual(TxKind.Payment, tx.Kind);
        Assert.Equal(TxKind.Transfer, tx.Kind);
    }

    [Fact]
    public void la_fecha_sin_cero_delante_tambien_se_lee()
    {
        // El HTML escribe `12/6/2026` y el texto plano `26/07/2026`.
        ParsedTransaction tx = Parser.Parse(
            Correo("Notificaciones Pagos al Instante transferencia enviada", Transferencia))
            .Transaction!;

        DateOnly local = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                tx.OccurredAtUtc,
                TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo")));

        Assert.Equal(new DateOnly(2026, 6, 12), local);
    }

    [Fact]
    public void el_estilo_del_html_no_se_lee_como_datos()
    {
        // Sin quitar el bloque `<style>` primero, sus nombres de clase quedan
        // sueltos en el texto y el parser lee cualquier cosa como comercio.
        ParsedTransaction tx = Parser.Parse(
            Correo("Notificaciones Pagos al Instante transferencia enviada", Transferencia))
            .Transaction!;

        Assert.DoesNotContain("color", tx.MerchantRaw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fix-link", tx.MerchantRaw, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- Depósito ----------

    [Fact]
    public void interpreta_un_deposito()
    {
        ParseResult result = Parser.Parse(Correo("Depósito por ATM", Deposito));

        Assert.Equal(ParseStatus.Parsed, result.Status);
        Assert.Equal(TxKind.Deposit, result.Transaction!.Kind);
    }

    [Fact]
    public void un_deposito_es_ingreso()
    {
        ParsedTransaction tx = Parser.Parse(Correo("Depósito por ATM", Deposito)).Transaction!;

        Assert.Equal(TxDirection.Inflow, Directions.Of(tx.Kind));
    }

    [Fact]
    public void el_monto_con_espacio_duro_y_sin_signo_se_lee()
    {
        // El depósito escribe `RD&nbsp;1,111.11`, sin el signo de peso y con
        // espacio duro. Las dos cosas rompían la lectura.
        ParsedTransaction tx = Parser.Parse(Correo("Depósito por ATM", Deposito)).Transaction!;

        Assert.Equal(new Money(111_111), tx.Amount);
    }

    // ---------- La hora ----------

    [Fact]
    public void una_compra_de_las_23_30_conserva_su_hora()
    {
        // El cuerpo del Popular no trae hora. Si el día del cuerpo coincide con
        // el del correo, la hora del correo es la del movimiento con margen de
        // segundos, y eso es lo que decide si una compra cae en este período o
        // en el siguiente.
        var recibido = new DateTime(2026, 7, 27, 3, 32, 0, DateTimeKind.Utc);

        DateTime instante = PopularParser.ResolveInstant(new DateOnly(2026, 7, 26), recibido);

        // 23:32 hora de Santo Domingo del 26 son las 03:32 UTC del 27.
        Assert.Equal(new DateTime(2026, 7, 27, 3, 32, 0, DateTimeKind.Utc), instante);
    }

    [Fact]
    public void un_correo_que_llega_al_dia_siguiente_usa_el_mediodia()
    {
        // El mediodía y no la medianoche: medianoche está a cuatro horas de la
        // frontera del día en UTC y cualquier desajuste la empuja al día
        // equivocado. El mediodía está a doce de las dos fronteras.
        var recibidoTarde = new DateTime(2026, 7, 28, 14, 0, 0, DateTimeKind.Utc);

        DateTime instante = PopularParser.ResolveInstant(new DateOnly(2026, 7, 26), recibidoTarde);

        Assert.Equal(new DateTime(2026, 7, 26, 16, 0, 0, DateTimeKind.Utc), instante);
    }

    // ---------- Fallo cerrado ----------

    [Fact]
    public void un_asunto_desconocido_no_se_convierte_en_compra()
    {
        ParseResult result = Parser.Parse(Correo("Su estado de cuenta", Consumo));

        Assert.Equal(ParseStatus.NeedsReview, result.Status);
        Assert.Null(result.Transaction);
        Assert.Contains("Su estado de cuenta", result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void una_transaccion_rechazada_no_crea_un_movimiento()
    {
        // No hubo movimiento de dinero. Crearlo inflaría el gasto del período.
        ParseResult result = Parser.Parse(Correo("Consumo rechazado", Consumo));

        Assert.Equal(ParseStatus.NeedsReview, result.Status);
        Assert.Null(result.Transaction);
    }

    [Fact]
    public void un_estatus_declinado_en_el_cuerpo_tampoco()
    {
        string declinado = Consumo.Replace("Aprobada", "Declinada", StringComparison.Ordinal);

        ParseResult result = Parser.Parse(Correo("Notificación de Consumo", declinado));

        Assert.Equal(ParseStatus.NeedsReview, result.Status);
    }

    [Fact]
    public void sin_monto_va_a_revision_y_no_asume_cero()
    {
        string sinMonto = Consumo.Replace("RD$1,111.11", "", StringComparison.Ordinal);

        ParseResult result = Parser.Parse(Correo("Notificación de Consumo", sinMonto));

        Assert.Equal(ParseStatus.NeedsReview, result.Status);
        Assert.Null(result.Transaction);
    }

    [Fact]
    public void sin_fecha_va_a_revision_y_no_asume_la_del_correo()
    {
        // Un aviso que llega con retraso caería en el período equivocado y
        // descuadraría los dos.
        string sinFecha = Consumo.Replace("26/07/2026", "", StringComparison.Ordinal);

        ParseResult result = Parser.Parse(Correo("Notificación de Consumo", sinFecha));

        Assert.Equal(ParseStatus.NeedsReview, result.Status);
    }

    [Fact]
    public void sin_los_cuatro_digitos_va_a_revision()
    {
        string sinCuenta = Consumo.Replace("terminada en 1234", "", StringComparison.Ordinal);

        ParseResult result = Parser.Parse(Correo("Notificación de Consumo", sinCuenta));

        Assert.Equal(ParseStatus.NeedsReview, result.Status);
    }

    [Fact]
    public void un_correo_de_otro_banco_no_es_suyo()
    {
        var ajeno = new RawEmail(
            "<x@otro.do>",
            "alertas@otrobanco.do",
            "Notificación de Consumo",
            Consumo,
            DateTime.UtcNow);

        Assert.False(Parser.CanHandle(ajeno));
        Assert.Equal(ParseStatus.NotMine, Parser.Parse(ajeno).Status);
    }

    // ---------- El monto ----------

    [Theory]
    [InlineData("1,111.11", 111_111)]
    [InlineData("11,111.11", 1_111_111)]
    [InlineData("11.11", 1_111)]
    [InlineData("1,000", 100_000)]
    [InlineData("0.50", 50)]
    public void el_monto_se_lee_con_coma_de_miles_y_punto_decimal(string raw, long centavos)
    {
        Assert.Equal(new Money(centavos), PopularParser.ParseMoney(raw));
    }

    [Theory]
    [InlineData("1.234.567")]
    [InlineData("1.2345")]
    [InlineData("abc")]
    [InlineData(".50")]
    public void un_monto_que_no_encaja_devuelve_nulo_en_vez_de_adivinar(string raw)
    {
        // Interpretar `1.234.567` como `1.23` es el defecto que ya se coló una
        // vez en este proyecto.
        Assert.Null(PopularParser.ParseMoney(raw));
    }
}
