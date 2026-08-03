using Margen.Domain;

namespace Margen.Ingest.Tests;

/// <summary>
/// El parser del Banco de Reservas contra sus muestras reales.
/// </summary>
/// <remarks>
/// **El asunto de este banco no dice nada**: los seis correos de muestra llevan
/// el mismo, «Notificaciones Banreservas». Ni el tipo, ni el resultado, ni el
/// importe. Todo sale del cuerpo, y por eso ninguna de estas pruebas mira el
/// asunto.
/// </remarks>
public sealed class BanreservasParserTests
{
    private static readonly BanreservasParser Parser = new();

    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    private static RawEmail Correo(string cuerpo) =>
        new("<x@banreservas.com>", "notificaciones@banreservas.com",
            "Notificaciones Banreservas", cuerpo,
            new DateTime(2026, 7, 8, 13, 40, 50, DateTimeKind.Utc));

    private static string Cuerpo(
        string frase = "presenta un retiro de cajero automatico",
        string monto = "DOP 11,111.11",
        string estado = "APROBADO",
        string comercio = "BANCO RESERVAS R.D 010REP STDOM DR DO",
        string fecha = "08/07/2026 09:40 AM") => $"""
        <html><body>
        <p>Notificación de Consumo - Banreservas</p>
        <p>Su tarjeta ESTANDAR ••1234 {frase}.</p>
        <table><tbody>
        <tr><td>Monto:</td><td>{monto}</td></tr>
        <tr><td>Estado:</td><td>{estado}</td></tr>
        <tr><td>Comercio:</td><td>{comercio}</td></tr>
        <tr><td>Fecha de transacción:</td><td>{fecha}</td></tr>
        <tr><td>Número de aprobación:</td><td>999999</td></tr>
        </tbody></table>
        </body></html>
        """;

    // ---------- Quién es suyo ----------

    [Fact]
    public void reconoce_sus_correos_por_el_remitente()
    {
        Assert.True(Parser.CanHandle(Correo(Cuerpo())));
    }

    [Fact]
    public void no_toca_los_de_otro_banco()
    {
        var ajeno = new RawEmail(
            "<x@qik.do>", "notificaciones@qik.do", "Usaste tu tarjeta", "algo", DateTime.UtcNow);

        Assert.False(Parser.CanHandle(ajeno));
    }

    // ---------- Lo que lee ----------

    [Fact]
    public void interpreta_un_retiro()
    {
        ParseResult r = Parser.Parse(Correo(Cuerpo()));

        Assert.Equal(ParseStatus.Parsed, r.Status);

        ParsedTransaction tx = r.Transaction!;
        Assert.Equal(new Money(1_111_111), tx.Amount);
        Assert.Equal(TxKind.Withdrawal, tx.Kind);
        Assert.Equal("1234", tx.AccountLastFour);
        Assert.Equal("DOP", tx.Currency);
    }

    [Fact]
    public void la_frase_distingue_un_consumo_de_un_retiro()
    {
        // Es lo único que los distingue: el asunto es el mismo y la tabla
        // también.
        ParsedTransaction consumo = Parser
            .Parse(Correo(Cuerpo(frase: "presenta un consumo"))).Transaction!;

        Assert.Equal(TxKind.Purchase, consumo.Kind);
    }

    [Fact]
    public void una_frase_desconocida_se_queda_como_compra()
    {
        // El caso conservador: una compra cuenta como gasto, y equivocarse por
        // ahí no hace aparecer dinero que no está.
        ParsedTransaction tx = Parser
            .Parse(Correo(Cuerpo(frase: "presenta algo que nadie ha visto"))).Transaction!;

        Assert.Equal(TxKind.Purchase, tx.Kind);
    }

    [Fact]
    public void el_monto_lleva_codigo_de_moneda_y_no_simbolo()
    {
        // `DOP 9,876.54`, no `RD$ 9,876.54`. Es la forma que se le escapó al
        // redactor y dejó un importe real escrito en el disco.
        ParsedTransaction tx = Parser
            .Parse(Correo(Cuerpo(monto: "DOP 9,876.54"))).Transaction!;

        Assert.Equal(new Money(987_654), tx.Amount);
    }

    [Fact]
    public void la_mascara_de_vinetas_tambien_da_los_cuatro_digitos()
    {
        // Cada banco elige su carácter: asteriscos el Popular, viñetas este.
        Assert.Equal("1234", Parser.Parse(Correo(Cuerpo())).Transaction!.AccountLastFour);
    }

    [Fact]
    public void el_numero_de_aprobacion_viaja_como_referencia()
    {
        // Es lo que distingue dos cargos idénticos del mismo día, que fue el
        // Blocker de la Fase 6.
        Assert.Equal("999999", Parser.Parse(Correo(Cuerpo())).Transaction!.Reference);
    }

    // ---------- El reloj de veinticuatro horas ----------

    [Fact]
    public void el_marcador_de_meridiano_es_decorativo()
    {
        // Las muestras traen `19:43 PM`, `18:40 PM` y `21:30 PM`: ninguna es
        // una hora válida de doce. El marcador se deduce de la hora en vez de
        // definirla, y hacerle caso sumaría doce a todas las tardes.
        ParsedTransaction tx = Parser
            .Parse(Correo(Cuerpo(fecha: "10/06/2026 19:43 PM"))).Transaction!;

        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(tx.OccurredAtUtc, Zone);

        Assert.Equal(new DateTime(2026, 6, 10, 19, 43, 0, DateTimeKind.Unspecified), local);
    }

    [Fact]
    public void la_manana_tambien_se_lee_bien()
    {
        ParsedTransaction tx = Parser
            .Parse(Correo(Cuerpo(fecha: "08/07/2026 09:40 AM"))).Transaction!;

        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(tx.OccurredAtUtc, Zone);

        Assert.Equal(new DateTime(2026, 7, 8, 9, 40, 0, DateTimeKind.Unspecified), local);
    }

    [Fact]
    public void el_dia_va_delante_del_mes()
    {
        // `08/07/2026` es el 8 de julio. Qik escribe `MM-dd`, este banco
        // `dd/MM`: **los dos no se ponen de acuerdo**, que es justo por lo que
        // ningún parser adivina el formato.
        ParsedTransaction tx = Parser.Parse(Correo(Cuerpo())).Transaction!;

        DateOnly local = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(tx.OccurredAtUtc, Zone));

        Assert.Equal(new DateOnly(2026, 7, 8), local);
    }

    [Fact]
    public void una_compra_de_noche_cae_en_el_dia_que_ve_el_usuario()
    {
        // Sale de una muestra real: transacción del 19 a las 21:30 locales, y
        // el correo llegó el 20 a las 01:30 UTC. Guardar la hora del banco como
        // si fuera UTC movería la compra al día siguiente.
        ParsedTransaction tx = Parser
            .Parse(Correo(Cuerpo(fecha: "19/05/2026 21:30 PM"))).Transaction!;

        Assert.Equal(new DateTime(2026, 5, 20, 1, 30, 0, DateTimeKind.Utc), tx.OccurredAtUtc);

        DateOnly local = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(tx.OccurredAtUtc, Zone));

        Assert.Equal(new DateOnly(2026, 5, 19), local);
    }

    // ---------- Fallo cerrado ----------

    [Fact]
    public void se_exige_que_el_estado_diga_aprobado()
    {
        // **No hay ninguna muestra de una transacción declinada de este banco**,
        // así que no se sabe qué palabra usa. Aceptar todo lo que no diga
        // «declinado» sería apostar a que su palabra está en una lista que nadie
        // ha visto, y perder esa apuesta es registrar un gasto que no ocurrió.
        ParseResult r = Parser.Parse(Correo(Cuerpo(estado: "EN PROCESO")));

        Assert.Equal(ParseStatus.NeedsReview, r.Status);
        Assert.Contains("EN PROCESO", r.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void sin_estado_no_se_crea_nada()
    {
        string sinEstado = Cuerpo().Replace(
            "<tr><td>Estado:</td><td>APROBADO</td></tr>", string.Empty, StringComparison.Ordinal);

        Assert.Equal(ParseStatus.NeedsReview, Parser.Parse(Correo(sinEstado)).Status);
    }

    [Fact]
    public void sin_fecha_no_se_crea_nada()
    {
        string sinFecha = Cuerpo().Replace(
            "Fecha de transacción:", "Otra cosa:", StringComparison.Ordinal);

        ParseResult r = Parser.Parse(Correo(sinFecha));

        Assert.Equal(ParseStatus.NeedsReview, r.Status);
        Assert.Contains("fecha", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void sin_comercio_no_se_crea_nada()
    {
        string sinComercio = Cuerpo().Replace(
            "Comercio:", "Otra cosa:", StringComparison.Ordinal);

        Assert.Equal(ParseStatus.NeedsReview, Parser.Parse(Correo(sinComercio)).Status);
    }

    [Fact]
    public void sin_monto_no_se_crea_nada()
    {
        string sinMonto = Cuerpo().Replace("Monto:", "Otra cosa:", StringComparison.Ordinal);

        ParseResult r = Parser.Parse(Correo(sinMonto));

        Assert.Equal(ParseStatus.NeedsReview, r.Status);
        Assert.Contains("monto", r.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void un_cuerpo_vacio_no_crea_nada()
    {
        Assert.Equal(ParseStatus.NeedsReview, Parser.Parse(Correo(string.Empty)).Status);
    }

    [Fact]
    public void un_correo_nulo_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => Parser.Parse(null!));
        Assert.Throws<ArgumentNullException>(() => Parser.CanHandle(null!));
    }
}
