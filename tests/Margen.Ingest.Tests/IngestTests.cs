using Margen.Domain;

namespace Margen.Ingest.Tests;

/// <summary>
/// Muestras sintéticas del banco que no existe. El formato es inventado y está
/// documentado en <see cref="SampleBankParser"/>: sirve para ejercer el camino
/// entero antes de que llegue un correo real.
/// </summary>
internal static class Muestras
{
    public static RawEmail Compra(
        string monto = "2,450.00",
        string comercio = "SUPERMERCADO NACIONAL SUC 12",
        string fecha = "15/03/2026 23:30",
        string tarjeta = "4582",
        string messageId = "<uno@banco-de-muestra.do>",
        string asunto = "Compra aprobada") => new(
        messageId,
        "alertas@banco-de-muestra.do",
        asunto,
        $"""
        Estimado cliente,

        Monto: RD$ {monto}
        Tarjeta: ****{tarjeta}
        Comercio: {comercio}
        Fecha: {fecha}
        Referencia: ABC-123456

        Gracias por preferirnos.
        """,
        new DateTime(2026, 3, 16, 3, 35, 0, DateTimeKind.Utc));

    public static RawEmail SinMonto() => new(
        "<dos@banco-de-muestra.do>",
        "alertas@banco-de-muestra.do",
        "Compra aprobada",
        """
        Tarjeta: ****4582
        Comercio: SUPERMERCADO NACIONAL
        Fecha: 15/03/2026 23:30
        """,
        DateTime.UtcNow);

    public static RawEmail DeOtroRemitente() => new(
        "<tres@otro-banco.do>",
        "notificaciones@otro-banco.do",
        "Compra aprobada",
        "Monto: RD$ 1,000.00",
        DateTime.UtcNow);
}

public sealed class ParserTests
{
    private static readonly SampleBankParser Parser = new();

    [Fact]
    public void interpreta_una_compra_completa()
    {
        ParseResult result = Parser.Parse(Muestras.Compra());

        Assert.Equal(ParseStatus.Parsed, result.Status);

        ParsedTransaction tx = result.Transaction!;
        Assert.Equal(new Money(245_000), tx.Amount);
        Assert.Equal("DOP", tx.Currency);
        Assert.Equal(TxKind.Purchase, tx.Kind);
        Assert.Equal("4582", tx.AccountLastFour);
        Assert.Equal("SUPERMERCADO NACIONAL SUC 12", tx.MerchantRaw);
        Assert.Equal("ABC-123456", tx.Reference);
    }

    [Fact]
    public void una_compra_de_las_23_30_se_convierte_a_utc_una_sola_vez()
    {
        // El correo dice 15/03/2026 23:30 hora de Santo Domingo. En UTC son las
        // 03:30 del 16. El parser es lo único que sabe en qué reloj escribe su
        // banco; nadie después lo sabe.
        ParsedTransaction tx = Parser.Parse(Muestras.Compra()).Transaction!;

        Assert.Equal(
            new DateTime(2026, 3, 16, 3, 30, 0, DateTimeKind.Utc),
            tx.OccurredAtUtc);
        Assert.Equal(DateTimeKind.Utc, tx.OccurredAtUtc.Kind);
    }

    [Fact]
    public void el_monto_se_lee_con_coma_de_miles_y_punto_decimal()
    {
        Assert.Equal(
            new Money(123_456_789),
            Parser.Parse(Muestras.Compra(monto: "1,234,567.89")).Transaction!.Amount);

        Assert.Equal(
            new Money(50),
            Parser.Parse(Muestras.Compra(monto: "0.50")).Transaction!.Amount);

        Assert.Equal(
            new Money(100_000),
            Parser.Parse(Muestras.Compra(monto: "1,000")).Transaction!.Amount);
    }

    [Theory]
    [InlineData("Compra aprobada", TxKind.Purchase)]
    [InlineData("Retiro en cajero", TxKind.Withdrawal)]
    [InlineData("Devolución procesada", TxKind.Refund)]
    [InlineData("Pago de tarjeta recibido", TxKind.Payment)]
    [InlineData("Transferencia enviada", TxKind.Transfer)]
    public void el_asunto_decide_el_tipo(string asunto, TxKind esperado)
    {
        Assert.Equal(
            esperado,
            Parser.Parse(Muestras.Compra(asunto: asunto)).Transaction!.Kind);
    }

    [Fact]
    public void un_asunto_desconocido_va_a_revision_y_no_inventa_un_tipo()
    {
        ParseResult result = Parser.Parse(Muestras.Compra(asunto: "Su estado de cuenta"));

        Assert.Equal(ParseStatus.NeedsReview, result.Status);
        Assert.Null(result.Transaction);
        Assert.NotNull(result.Reason);
    }

    [Fact]
    public void un_correo_sin_monto_va_a_revision_y_no_asume_cero()
    {
        // Un cero por defecto es una mentira con formato de número.
        ParseResult result = Parser.Parse(Muestras.SinMonto());

        Assert.Equal(ParseStatus.NeedsReview, result.Status);
        Assert.Null(result.Transaction);
    }

    [Fact]
    public void un_correo_sin_fecha_va_a_revision_y_no_asume_hoy()
    {
        // Un movimiento con fecha inventada cae en el período equivocado y
        // descuadra los dos.
        var sinFecha = new RawEmail(
            "<x@banco-de-muestra.do>",
            "alertas@banco-de-muestra.do",
            "Compra aprobada",
            "Monto: RD$ 100.00\nTarjeta: ****4582\nComercio: X",
            DateTime.UtcNow);

        Assert.Equal(ParseStatus.NeedsReview, Parser.Parse(sinFecha).Status);
    }

    [Fact]
    public void un_monto_ilegible_va_a_revision()
    {
        ParseResult result = Parser.Parse(Muestras.Compra(monto: "1.234.567"));

        Assert.Equal(ParseStatus.NeedsReview, result.Status);
    }

    [Fact]
    public void un_correo_de_otro_remitente_no_es_suyo()
    {
        Assert.False(Parser.CanHandle(Muestras.DeOtroRemitente()));
        Assert.Equal(ParseStatus.NotMine, Parser.Parse(Muestras.DeOtroRemitente()).Status);
    }
}

public sealed class RegistryTests
{
    private sealed class Falso(string name, bool handles) : IEmailParser
    {
        public string Name => name;

        public int Version => 1;

        public bool CanHandle(RawEmail email) => handles;

        public ParseResult Parse(RawEmail email) => ParseResult.NotMine();
    }

    [Fact]
    public void encuentra_el_parser_que_reconoce_el_correo()
    {
        var registry = new ParserRegistry([new SampleBankParser()]);

        Assert.NotNull(registry.Find(Muestras.Compra()));
        Assert.Null(registry.Find(Muestras.DeOtroRemitente()));
    }

    [Fact]
    public void el_orden_es_el_de_registro_y_es_estable()
    {
        // Dos parsers que digan que pueden con el mismo correo lo resuelven
        // siempre igual. Un orden que cambie entre ejecuciones haría que
        // reprocesar el mismo buzón diera resultados distintos.
        var registry = new ParserRegistry([
            new Falso("primero", handles: true),
            new Falso("segundo", handles: true),
        ]);

        for (int i = 0; i < 10; i++)
        {
            Assert.Equal("primero", registry.Find(Muestras.Compra())!.Name);
        }
    }

    [Fact]
    public void dos_parsers_con_el_mismo_nombre_se_rechazan()
    {
        // El nombre se guarda en cada correo procesado y tiene que identificar
        // a uno solo; si no, el reproceso no sabe cuál volver a aplicar.
        Assert.Throws<ArgumentException>(() => new ParserRegistry([
            new Falso("repetido", handles: true),
            new Falso("REPETIDO", handles: false),
        ]));
    }

    [Fact]
    public void se_puede_buscar_por_nombre_para_reprocesar()
    {
        var registry = new ParserRegistry([new SampleBankParser()]);

        Assert.NotNull(registry.ByName("muestra-sintetica"));
        Assert.Null(registry.ByName("el-que-no-existe"));
    }
}

public sealed class FingerprintTests
{
    [Fact]
    public void el_mismo_cuerpo_con_saltos_distintos_da_la_misma_huella()
    {
        // Dos servidores de correo entregan el mismo cuerpo con saltos
        // distintos. Sin normalizar, serían dos hashes y el correo se
        // procesaría dos veces.
        Assert.Equal(
            Fingerprints.OfBody("Monto: RD$ 100.00\r\nComercio: X\r\n"),
            Fingerprints.OfBody("Monto: RD$ 100.00\nComercio: X\n"));

        Assert.Equal(
            Fingerprints.OfBody("Monto:  RD$ 100.00"),
            Fingerprints.OfBody("Monto: RD$ 100.00"));
    }

    [Fact]
    public void dos_cuerpos_distintos_dan_huellas_distintas()
    {
        Assert.NotEqual(
            Fingerprints.OfBody("Monto: RD$ 100.00"),
            Fingerprints.OfBody("Monto: RD$ 100.01"));
    }

    [Fact]
    public void la_huella_del_movimiento_usa_el_dia_local()
    {
        // Con el día UTC, dos compras idénticas de las once de la noche de dos
        // días locales distintos caerían en el mismo día UTC y la segunda se
        // rechazaría siendo real.
        Guid cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

        string dia15 = Fingerprints.OfTransaction(
            cuenta, new DateOnly(2026, 3, 15), new Money(245_000), "SUPERMERCADO");
        string dia16 = Fingerprints.OfTransaction(
            cuenta, new DateOnly(2026, 3, 16), new Money(245_000), "SUPERMERCADO");

        Assert.NotEqual(dia15, dia16);
    }

    [Fact]
    public void la_huella_es_estable_para_los_mismos_datos()
    {
        Guid cuenta = Guid.NewGuid();

        Assert.Equal(
            Fingerprints.OfTransaction(cuenta, new DateOnly(2026, 3, 15), new Money(1), "X"),
            Fingerprints.OfTransaction(cuenta, new DateOnly(2026, 3, 15), new Money(1), "X"));
    }

    [Fact]
    public void el_comercio_normalizado_agrupa_las_variantes_del_mismo_sitio()
    {
        // Los bancos añaden sufijos que cambian entre cargos. Sin quitarlos,
        // cada compra parecería un comercio nuevo y ninguna regla llegaría a
        // aplicarse dos veces.
        Assert.Equal(
            "SUPERMERCADO NACIONAL",
            Fingerprints.NormalizeMerchant("  supermercado   nacional  "));

        Assert.Equal(
            "SUPERMERCADO NACIONAL 12",
            Fingerprints.NormalizeMerchant("Supermercado-Nacional*12"));

        Assert.Equal(
            "ESTACION SUNIX",
            Fingerprints.NormalizeMerchant("Estación Sunix"));
    }
}

public sealed class DuplicateTests
{
    private static readonly Guid Cuenta = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ParsedTransaction Entrante(
        long centavos = 245_000,
        string comercio = "SUPERMERCADO NACIONAL",
        int minutos = 0) => new(
        new Money(centavos),
        "DOP",
        new DateTime(2026, 3, 16, 3, 30, 0, DateTimeKind.Utc).AddMinutes(minutos),
        comercio,
        TxKind.Purchase,
        "4582");

    private static ExistingTransaction Existente(
        long centavos = 245_000,
        string comercio = "SUPERMERCADO NACIONAL",
        int minutos = 0,
        string huella = "huella-distinta") => new(
        Guid.NewGuid(),
        Cuenta,
        new Money(centavos),
        new DateTime(2026, 3, 16, 3, 30, 0, DateTimeKind.Utc).AddMinutes(minutos),
        comercio,
        huella);

    [Fact]
    public void sin_nada_parecido_el_movimiento_es_nuevo()
    {
        DuplicateCheck check = new DuplicateDetector()
            .Check(Entrante(), Cuenta, "mi-huella", []);

        Assert.Equal(DuplicateVerdict.New, check.Verdict);
    }

    [Fact]
    public void la_misma_huella_es_el_mismo_movimiento()
    {
        DuplicateCheck check = new DuplicateDetector().Check(
            Entrante(),
            Cuenta,
            "mi-huella",
            [Existente(huella: "mi-huella")]);

        Assert.Equal(DuplicateVerdict.Exact, check.Verdict);
        Assert.NotNull(check.MatchId);
    }

    [Fact]
    public void mismo_importe_y_comercio_en_minutos_es_probable_y_no_seguro()
    {
        // Descartarlo perdería un gasto real y el saldo saldría mayor; crearlo
        // sin más duplicaría el gasto. Se crea marcado y decide una persona.
        DuplicateCheck check = new DuplicateDetector().Check(
            Entrante(),
            Cuenta,
            "mi-huella",
            [Existente(minutos: 3)]);

        Assert.Equal(DuplicateVerdict.Probable, check.Verdict);
        Assert.NotNull(check.Reason);
    }

    [Fact]
    public void fuera_de_la_ventana_son_dos_compras_distintas()
    {
        // Dos cafés seguidos en el mismo sitio también existen.
        DuplicateCheck check = new DuplicateDetector().Check(
            Entrante(),
            Cuenta,
            "mi-huella",
            [Existente(minutos: 40)]);

        Assert.Equal(DuplicateVerdict.New, check.Verdict);
    }

    [Fact]
    public void un_importe_distinto_no_es_duplicado()
    {
        DuplicateCheck check = new DuplicateDetector().Check(
            Entrante(),
            Cuenta,
            "mi-huella",
            [Existente(centavos: 245_001, minutos: 1)]);

        Assert.Equal(DuplicateVerdict.New, check.Verdict);
    }

    [Fact]
    public void otro_comercio_no_es_duplicado()
    {
        DuplicateCheck check = new DuplicateDetector().Check(
            Entrante(),
            Cuenta,
            "mi-huella",
            [Existente(comercio: "PANADERIA", minutos: 1)]);

        Assert.Equal(DuplicateVerdict.New, check.Verdict);
    }

    [Fact]
    public void otra_cuenta_no_es_duplicado()
    {
        // El mismo importe en el mismo comercio con dos tarjetas distintas son
        // dos cargos reales.
        var otra = new ExistingTransaction(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new Money(245_000),
            new DateTime(2026, 3, 16, 3, 31, 0, DateTimeKind.Utc),
            "SUPERMERCADO NACIONAL",
            "otra-huella");

        DuplicateCheck check = new DuplicateDetector()
            .Check(Entrante(), Cuenta, "mi-huella", [otra]);

        Assert.Equal(DuplicateVerdict.New, check.Verdict);
    }

    [Fact]
    public void la_huella_exacta_gana_sobre_la_aproximada()
    {
        // Con las dos coincidencias presentes, la respuesta tiene que ser
        // «exacto»: no se crea nada y nadie tiene que mirarlo.
        DuplicateCheck check = new DuplicateDetector().Check(
            Entrante(),
            Cuenta,
            "mi-huella",
            [Existente(minutos: 2), Existente(huella: "mi-huella", minutos: 30)]);

        Assert.Equal(DuplicateVerdict.Exact, check.Verdict);
    }

    [Fact]
    public void el_comercio_se_compara_normalizado()
    {
        // El entrante trae el sufijo de sucursal y el guardado no.
        DuplicateCheck check = new DuplicateDetector().Check(
            Entrante(comercio: "Supermercado-Nacional"),
            Cuenta,
            "mi-huella",
            [Existente(minutos: 1)]);

        Assert.Equal(DuplicateVerdict.Probable, check.Verdict);
    }

    [Fact]
    public void la_ventana_se_puede_ajustar()
    {
        DuplicateCheck check = new DuplicateDetector(TimeSpan.FromHours(1))
            .Check(Entrante(), Cuenta, "mi-huella", [Existente(minutos: 40)]);

        Assert.Equal(DuplicateVerdict.Probable, check.Verdict);
    }
}
