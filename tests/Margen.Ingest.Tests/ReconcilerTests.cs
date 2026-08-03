using Margen.Domain;
using Margen.Ingest.Statements;

namespace Margen.Ingest.Tests;

/// <summary>
/// Cuadrar el estado de cuenta contra lo registrado.
/// </summary>
/// <remarks>
/// Es la última red del sistema: la ingesta de correo puede perderse un
/// movimiento —un correo que no llegó, una plantilla nueva, un banco sin parser
/// todavía— y el efectivo depende de que alguien se acuerde. Lo que salga
/// «ausente» es exactamente lo que al sistema le faltaba.
/// </remarks>
public sealed class ReconcilerTests
{
    private static readonly DateOnly Dia = new(2026, 7, 26);

    private static StatementLine Linea(
        long centavos,
        string descripcion = "SM NACIONAL CHARLES",
        int dias = 0,
        TxDirection direccion = TxDirection.Outflow,
        int numero = 1) =>
        new(numero, Dia.AddDays(dias), descripcion, new Money(centavos), direccion);

    private static BookedTransaction Movimiento(
        long centavos,
        string comercio = "SM NACIONAL CHARLES",
        int dias = 0,
        TxDirection direccion = TxDirection.Outflow,
        Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), Dia.AddDays(dias), comercio, new Money(centavos), direccion);

    // ---------- Conciliado ----------

    [Fact]
    public void mismo_monto_mismo_dia_es_conciliado()
    {
        BookedTransaction m = Movimiento(123_456);

        ReconcileReport r = Reconciler.Reconcile([Linea(123_456)], [m]);

        Assert.Equal(ReconcileState.Matched, r.Verdicts[0].State);
        Assert.Equal(m.Id, r.Verdicts[0].TransactionId);
        Assert.Empty(r.PendingTransactionIds);
    }

    [Fact]
    public void el_banco_asienta_dos_dias_despues_y_sigue_siendo_el_mismo_cargo()
    {
        // Sin holgura, cada compra del fin de semana saldría por duplicado:
        // ausente en el estado de cuenta y pendiente en la aplicación.
        ReconcileReport r = Reconciler.Reconcile(
            [Linea(123_456, dias: 2)], [Movimiento(123_456)]);

        Assert.Equal(ReconcileState.Matched, r.Verdicts[0].State);
    }

    [Fact]
    public void mas_alla_de_la_holgura_ya_no_cuadra()
    {
        ReconcileReport r = Reconciler.Reconcile(
            [Linea(123_456, dias: Reconciler.DayTolerance + 1)], [Movimiento(123_456)]);

        Assert.Equal(ReconcileState.Missing, r.Verdicts[0].State);
    }

    [Fact]
    public void el_estado_de_cuenta_y_el_correo_escriben_el_comercio_distinto()
    {
        // `SM NACIONAL` y `SM NACIONAL CHARLES` son el mismo sitio.
        ReconcileReport r = Reconciler.Reconcile(
            [Linea(123_456, "SM NACIONAL")], [Movimiento(123_456, "SM NACIONAL CHARLES")]);

        Assert.Equal(ReconcileState.Matched, r.Verdicts[0].State);
    }

    [Fact]
    public void un_ingreso_no_cuadra_con_un_egreso_del_mismo_monto()
    {
        // Un depósito de 1 234,56 y una compra de 1 234,56 el mismo día no son
        // lo mismo, y cuadrarlos escondería los dos.
        ReconcileReport r = Reconciler.Reconcile(
            [Linea(123_456, direccion: TxDirection.Inflow)],
            [Movimiento(123_456, direccion: TxDirection.Outflow)]);

        Assert.Equal(ReconcileState.Missing, r.Verdicts[0].State);
    }

    // ---------- Ausente ----------

    [Fact]
    public void lo_que_esta_en_el_banco_y_no_en_la_aplicacion_es_ausente()
    {
        // Es el motivo entero de la conciliación: el efectivo y los correos
        // perdidos aparecen aquí.
        ReconcileReport r = Reconciler.Reconcile([Linea(123_456)], []);

        Assert.Equal(ReconcileState.Missing, r.Verdicts[0].State);
        Assert.Null(r.Verdicts[0].TransactionId);
    }

    // ---------- Pendiente ----------

    [Fact]
    public void lo_que_esta_en_la_aplicacion_y_no_en_el_banco_es_pendiente()
    {
        BookedTransaction m = Movimiento(123_456);

        ReconcileReport r = Reconciler.Reconcile([], [m]);

        Assert.Equal([m.Id], r.PendingTransactionIds);
    }

    // ---------- Discrepante ----------

    [Fact]
    public void mismo_comercio_y_otro_monto_es_discrepante_y_no_ausente()
    {
        // Una propina añadida después, un ajuste de divisa. Es el mismo
        // movimiento con otra cifra, y decirlo así permite corregirlo en vez de
        // duplicarlo.
        BookedTransaction m = Movimiento(100_000);

        ReconcileReport r = Reconciler.Reconcile([Linea(120_000)], [m]);

        Assert.Equal(ReconcileState.Discrepant, r.Verdicts[0].State);
        Assert.Equal(m.Id, r.Verdicts[0].TransactionId);
        Assert.Equal(new Money(20_000), r.Verdicts[0].Difference);
    }

    [Fact]
    public void la_diferencia_lleva_signo()
    {
        // Negativa si el banco cobró menos de lo registrado.
        ReconcileReport r = Reconciler.Reconcile([Linea(80_000)], [Movimiento(100_000)]);

        Assert.Equal(new Money(-20_000), r.Verdicts[0].Difference);
    }

    [Fact]
    public void un_comercio_distinto_con_otro_monto_es_ausente_y_no_discrepante()
    {
        ReconcileReport r = Reconciler.Reconcile(
            [Linea(120_000, "UBER EATS")], [Movimiento(100_000, "SM NACIONAL")]);

        Assert.Equal(ReconcileState.Missing, r.Verdicts[0].State);
    }

    // ---------- Duplicado ----------

    [Fact]
    public void dos_candidatos_iguales_no_se_eligen_al_azar()
    {
        // Dos cargos idénticos del mismo comercio el mismo día. Elegir uno
        // sería inventarse un criterio.
        ReconcileReport r = Reconciler.Reconcile(
            [Linea(123_456)],
            [Movimiento(123_456), Movimiento(123_456)]);

        Assert.Equal(ReconcileState.Duplicate, r.Verdicts[0].State);
    }

    [Fact]
    public void un_movimiento_solo_cuadra_con_una_linea()
    {
        // Dos líneas iguales y un solo movimiento: la primera cuadra y la
        // segunda sale ausente, que es la que de verdad falta registrar.
        ReconcileReport r = Reconciler.Reconcile(
            [Linea(123_456, numero: 1), Linea(123_456, numero: 2)],
            [Movimiento(123_456)]);

        Assert.Equal(ReconcileState.Matched, r.Verdicts[0].State);
        Assert.Equal(ReconcileState.Missing, r.Verdicts[1].State);
    }

    // ---------- El orden no decide ----------

    [Fact]
    public void una_linea_aproximada_no_se_queda_con_el_movimiento_de_una_exacta()
    {
        // Resolviendo línea por línea en orden, la primera —que solo cuadra
        // aproximada— se llevaría el movimiento que la segunda cuadraba exacto,
        // y la segunda saldría ausente por culpa de la primera.
        BookedTransaction exacto = Movimiento(100_000);

        ReconcileReport r = Reconciler.Reconcile(
            [Linea(120_000, numero: 1), Linea(100_000, numero: 2)],
            [exacto]);

        Assert.Equal(ReconcileState.Matched, r.Verdicts[1].State);
        Assert.Equal(exacto.Id, r.Verdicts[1].TransactionId);
        Assert.Equal(ReconcileState.Missing, r.Verdicts[0].State);
    }

    // ---------- El recuento ----------

    [Fact]
    public void el_informe_cuenta_por_estado()
    {
        ReconcileReport r = Reconciler.Reconcile(
            [Linea(100_000, numero: 1), Linea(999_999, "OTRO SITIO", numero: 2)],
            [Movimiento(100_000)]);

        Assert.Equal(1, r.CountOf(ReconcileState.Matched));
        Assert.Equal(1, r.CountOf(ReconcileState.Missing));
    }

    [Fact]
    public void los_argumentos_nulos_lanzan()
    {
        Assert.Throws<ArgumentNullException>(() => Reconciler.Reconcile(null!, []));
        Assert.Throws<ArgumentNullException>(() => Reconciler.Reconcile([], null!));
    }

    [Fact]
    public void un_comercio_corto_no_se_parece_a_todo()
    {
        // `POS` contenido en cualquier cosa emparejaría medio estado de cuenta.
        Assert.False(Reconciler.Resembles("POS", "POSADA LA VEGA"));
        Assert.True(Reconciler.Resembles("UBER", "UBER EATS"));
    }
}
