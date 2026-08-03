using Margen.Budget;
using Margen.Domain;

namespace Margen.Budget.Tests;

public sealed class SpendingTests
{
    private static readonly Guid Comida = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Ropa = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static BudgetCycle Ciclo()
        => BudgetCycle.Between(new DateOnly(2026, 6, 25), new DateOnly(2026, 7, 25)).Value;

    /// <summary>
    /// Atajo para las pruebas del camino feliz. Leer <c>.Value</c> lanza si el
    /// resumen no se pudo calcular, así que una entrada rechazada rompe la
    /// prueba en lugar de colarse como un cero.
    /// </summary>
    private static SpendingSummary Resumir(
        BudgetCycle ciclo,
        IReadOnlyCollection<LedgerEntry> movimientos)
        => SpendingLedger.Summarize(ciclo, movimientos).Value;

    private static LedgerEntry Compra(
        Guid id,
        Guid? categoria,
        long centavos,
        int dia = 1,
        TxStatus estado = TxStatus.Posted)
        => new(
            id,
            categoria,
            new Money(centavos),
            TxKind.Purchase,
            estado,
            new DateOnly(2026, 6, 25).AddDays(dia));

    [Fact]
    public void una_compra_suma_al_gasto_de_su_categoria()
    {
        SpendingSummary resumen = Resumir(
            Ciclo(),
            [Compra(Guid.NewGuid(), Comida, 245_000)]);

        Assert.Equal(new Money(245_000), resumen.Total);
        Assert.Equal(new Money(245_000), resumen.ByCategory[Comida]);
    }

    [Fact]
    public void una_devolucion_resta_de_su_categoria()
    {
        Guid compra = Guid.NewGuid();

        var movimientos = new List<LedgerEntry>
        {
            Compra(compra, Ropa, 500_000),
            new(
                Guid.NewGuid(),
                Ropa,
                new Money(200_000),
                TxKind.Refund,
                TxStatus.Posted,
                new DateOnly(2026, 6, 28),
                RefundsTransactionId: compra),
        };

        SpendingSummary resumen = Resumir(Ciclo(), movimientos);

        Assert.Equal(new Money(300_000), resumen.Total);
        Assert.Equal(new Money(300_000), resumen.ByCategory[Ropa]);
    }

    [Fact]
    public void una_devolucion_resta_de_la_categoria_de_la_compra_original_y_no_de_la_suya()
    {
        // El banco no siempre categoriza la devolución igual que la compra. Si
        // se restara de donde caiga, Ropa quedaría inflada para siempre y
        // Comida en negativo.
        Guid compra = Guid.NewGuid();

        var movimientos = new List<LedgerEntry>
        {
            Compra(compra, Ropa, 500_000),
            new(
                Guid.NewGuid(),
                Comida,
                new Money(200_000),
                TxKind.Refund,
                TxStatus.Posted,
                new DateOnly(2026, 6, 28),
                RefundsTransactionId: compra),
        };

        SpendingSummary resumen = Resumir(Ciclo(), movimientos);

        Assert.Equal(new Money(300_000), resumen.ByCategory[Ropa]);
        Assert.False(resumen.ByCategory.ContainsKey(Comida));
    }

    [Fact]
    public void una_devolucion_sin_compra_original_conocida_usa_su_propia_categoria()
    {
        // La compra es de un ciclo que ya no está cargado. El dinero volvió y
        // el total tiene que reflejarlo aunque no se sepa de dónde: descartar
        // el movimiento sería peor que imputarlo mal.
        var movimientos = new List<LedgerEntry>
        {
            new(
                Guid.NewGuid(),
                Comida,
                new Money(200_000),
                TxKind.Refund,
                TxStatus.Posted,
                new DateOnly(2026, 6, 28),
                RefundsTransactionId: Guid.NewGuid()),
        };

        SpendingSummary resumen = Resumir(Ciclo(), movimientos);

        Assert.Equal(new Money(-200_000), resumen.Total);
        Assert.Equal(new Money(-200_000), resumen.ByCategory[Comida]);
    }

    [Fact]
    public void un_pago_de_tarjeta_no_es_gasto()
    {
        // Mueve saldo entre cuentas propias. Contarlo duplicaría el gasto: una
        // vez al comprar con la tarjeta y otra al pagarla.
        var movimientos = new List<LedgerEntry>
        {
            Compra(Guid.NewGuid(), Comida, 100_000),
            new(
                Guid.NewGuid(),
                null,
                new Money(1_500_000),
                TxKind.Payment,
                TxStatus.Posted,
                new DateOnly(2026, 7, 1)),
        };

        SpendingSummary resumen = Resumir(Ciclo(), movimientos);

        Assert.Equal(new Money(100_000), resumen.Total);
        Assert.Equal(Money.Zero, resumen.Uncategorized);
    }

    [Fact]
    public void una_transferencia_enviada_si_es_gasto()
    {
        // Cambió al leer los correos reales del banco: una transferencia
        // enviada es dinero que se fue a otra persona. El correo dice que
        // salió, no a dónde fue, y darla por traspaso la sacaría del gasto del
        // período.
        var movimientos = new List<LedgerEntry>
        {
            new(
                Guid.NewGuid(),
                null,
                new Money(2_000_000),
                TxKind.Transfer,
                TxStatus.Posted,
                new DateOnly(2026, 7, 1)),
        };

        Assert.Equal(new Money(2_000_000), Resumir(Ciclo(), movimientos).Total);
    }

    [Fact]
    public void una_transferencia_marcada_como_traspaso_no_es_gasto()
    {
        // El caso que el correo no puede resolver: a tu propia cuenta de
        // ahorro. Lo corrige el usuario y la dirección guardada manda.
        var movimientos = new List<LedgerEntry>
        {
            new(
                Guid.NewGuid(),
                null,
                new Money(2_000_000),
                TxKind.Transfer,
                TxStatus.Posted,
                new DateOnly(2026, 7, 1),
                Direction: TxDirection.Internal),
        };

        Assert.Equal(Money.Zero, Resumir(Ciclo(), movimientos).Total);
    }

    [Fact]
    public void un_deposito_no_reduce_ninguna_categoria()
    {
        // Es dinero nuevo, no un gasto negativo. Restarlo de Comida haría que
        // ingresar dinero pareciera haber gastado menos.
        var movimientos = new List<LedgerEntry>
        {
            Compra(Guid.NewGuid(), Comida, 100_000),
            new(
                Guid.NewGuid(),
                Comida,
                new Money(5_000_000),
                TxKind.Deposit,
                TxStatus.Posted,
                new DateOnly(2026, 7, 1)),
        };

        SpendingSummary resumen = Resumir(Ciclo(), movimientos);

        Assert.Equal(new Money(100_000), resumen.Total);
        Assert.Equal(new Money(100_000), resumen.ByCategory[Comida]);
    }

    [Fact]
    public void olvidar_la_direccion_da_el_resultado_correcto()
    {
        // La dirección se deduce del tipo cuando no se da. Con un valor fijo
        // por defecto, quien construyera la entrada sin acordarse convertía un
        // pago de tarjeta en gasto.
        var pago = new LedgerEntry(
            Guid.NewGuid(),
            null,
            new Money(1_500_000),
            TxKind.Payment,
            TxStatus.Posted,
            new DateOnly(2026, 7, 1));

        Assert.Equal(TxDirection.Internal, pago.EffectiveDirection);
        Assert.Equal(Money.Zero, pago.SpendingEffect);
    }

    [Theory]
    [InlineData(TxStatus.Rejected)]
    [InlineData(TxStatus.Duplicate)]
    public void un_movimiento_descartado_no_suma(TxStatus estado)
    {
        var movimientos = new List<LedgerEntry>
        {
            Compra(Guid.NewGuid(), Comida, 300_000, estado: estado),
        };

        Assert.Equal(Money.Zero, Resumir(Ciclo(), movimientos).Total);
    }

    [Fact]
    public void un_movimiento_pendiente_si_suma()
    {
        // Una compra aprobada y sin asentar ya gastó el dinero. Esperar a que
        // el banco la asiente daría dos días de cifra optimista.
        var movimientos = new List<LedgerEntry>
        {
            Compra(Guid.NewGuid(), Comida, 300_000, estado: TxStatus.Pending),
        };

        Assert.Equal(new Money(300_000), Resumir(Ciclo(), movimientos).Total);
    }

    [Fact]
    public void un_movimiento_en_revision_si_suma()
    {
        // Fallo cerrado: mientras no se sepa qué es, se cuenta. Dejarlo fuera
        // haría la cifra mayor de lo que es, que es el error caro.
        var movimientos = new List<LedgerEntry>
        {
            Compra(Guid.NewGuid(), Comida, 300_000, estado: TxStatus.NeedsReview),
        };

        Assert.Equal(new Money(300_000), Resumir(Ciclo(), movimientos).Total);
    }

    [Fact]
    public void un_movimiento_de_otro_ciclo_no_cuenta()
    {
        var movimientos = new List<LedgerEntry>
        {
            new(
                Guid.NewGuid(),
                Comida,
                new Money(999_000),
                TxKind.Purchase,
                TxStatus.Posted,
                new DateOnly(2026, 6, 24)),
            new(
                Guid.NewGuid(),
                Comida,
                new Money(888_000),
                TxKind.Purchase,
                TxStatus.Posted,
                new DateOnly(2026, 7, 25)),
        };

        Assert.Equal(Money.Zero, Resumir(Ciclo(), movimientos).Total);
    }

    [Fact]
    public void una_devolucion_de_este_ciclo_descuenta_de_una_compra_del_anterior()
    {
        // La compra está fuera del ciclo, así que no suma; la devolución está
        // dentro, así que resta, y lo hace en la categoría de aquella compra.
        Guid compra = Guid.NewGuid();

        var movimientos = new List<LedgerEntry>
        {
            new(
                compra,
                Ropa,
                new Money(500_000),
                TxKind.Purchase,
                TxStatus.Posted,
                new DateOnly(2026, 6, 10)),
            new(
                Guid.NewGuid(),
                null,
                new Money(500_000),
                TxKind.Refund,
                TxStatus.Posted,
                new DateOnly(2026, 6, 28),
                RefundsTransactionId: compra),
        };

        SpendingSummary resumen = Resumir(Ciclo(), movimientos);

        Assert.Equal(new Money(-500_000), resumen.Total);
        Assert.Equal(new Money(-500_000), resumen.ByCategory[Ropa]);
    }

    [Fact]
    public void un_gasto_sin_categoria_va_al_apartado_de_sin_clasificar()
    {
        var movimientos = new List<LedgerEntry>
        {
            Compra(Guid.NewGuid(), null, 120_000),
            Compra(Guid.NewGuid(), Comida, 80_000),
        };

        SpendingSummary resumen = Resumir(Ciclo(), movimientos);

        Assert.Equal(new Money(200_000), resumen.Total);
        Assert.Equal(new Money(120_000), resumen.Uncategorized);
        Assert.Equal(new Money(80_000), resumen.ByCategory[Comida]);
    }

    [Fact]
    public void el_total_es_siempre_la_suma_de_las_categorias_mas_lo_sin_clasificar()
    {
        // Es la invariante que impide que un movimiento se cuente en el total y
        // desaparezca del desglose, o al revés.
        var movimientos = new List<LedgerEntry>
        {
            Compra(Guid.NewGuid(), Comida, 245_000),
            Compra(Guid.NewGuid(), Ropa, 130_050),
            Compra(Guid.NewGuid(), null, 45_099),
            new(
                Guid.NewGuid(),
                Ropa,
                new Money(30_000),
                TxKind.Refund,
                TxStatus.Posted,
                new DateOnly(2026, 7, 2)),
        };

        SpendingSummary resumen = Resumir(Ciclo(), movimientos);

        Money suma = Money.Sum(resumen.ByCategory.Values) + resumen.Uncategorized;

        Assert.Equal(resumen.Total, suma);
    }

    [Fact]
    public void un_retiro_de_efectivo_es_gasto()
    {
        var movimientos = new List<LedgerEntry>
        {
            new(
                Guid.NewGuid(),
                Comida,
                new Money(500_000),
                TxKind.Withdrawal,
                TxStatus.Posted,
                new DateOnly(2026, 7, 1)),
        };

        Assert.Equal(new Money(500_000), Resumir(Ciclo(), movimientos).Total);
    }

    [Fact]
    public void un_ciclo_sin_movimientos_da_cero_y_no_falla()
    {
        SpendingSummary resumen = Resumir(Ciclo(), []);

        Assert.Equal(Money.Zero, resumen.Total);
        Assert.Empty(resumen.ByCategory);
    }

    [Fact]
    public void el_mismo_movimiento_repetido_se_rechaza_en_vez_de_contarse_dos_veces()
    {
        // Una consulta con un join que multiplica filas es la forma más común
        // de que llegue el mismo movimiento dos veces. Sumarlo dos veces
        // inflaría el gasto sin que nada avisara; descartarlo en silencio
        // taparía el defecto de quien llama.
        LedgerEntry movimiento = Compra(Guid.NewGuid(), Comida, 245_000);

        Outcome<SpendingSummary> resultado = SpendingLedger.Summarize(
            Ciclo(),
            [movimiento, movimiento]);

        Assert.Equal(OutcomeKind.Invalid, resultado.Kind);
        Assert.NotNull(resultado.Reason);
    }

    [Fact]
    public void dos_movimientos_distintos_con_los_mismos_datos_si_se_admiten()
    {
        // Dos cafés iguales el mismo día son dos gastos, no un duplicado. Lo
        // que se rechaza es el mismo identificador, no la misma cifra.
        SpendingSummary resumen = Resumir(
            Ciclo(),
            [
                Compra(Guid.NewGuid(), Comida, 25_000),
                Compra(Guid.NewGuid(), Comida, 25_000),
            ]);

        Assert.Equal(new Money(50_000), resumen.Total);
    }

    [Fact]
    public void un_movimiento_repetido_fuera_del_ciclo_tambien_se_rechaza()
    {
        // La comprobación es sobre la entrada entera, no solo sobre lo que cae
        // dentro del ciclo: el índice de categorías se construye con todo, así
        // que un duplicado de fuera también lo corrompe.
        LedgerEntry viejo = new(
            Guid.NewGuid(),
            Ropa,
            new Money(500_000),
            TxKind.Purchase,
            TxStatus.Posted,
            new DateOnly(2026, 6, 10));

        Outcome<SpendingSummary> resultado = SpendingLedger.Summarize(
            Ciclo(),
            [viejo, viejo]);

        Assert.Equal(OutcomeKind.Invalid, resultado.Kind);
    }
}
