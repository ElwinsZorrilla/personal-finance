using Margen.Domain;
using Margen.Domain.Entities;

namespace Margen.Domain.Tests;

/// <summary>
/// Ingreso, egreso o traspaso, y qué cuenta como gasto.
/// </summary>
/// <remarks>
/// La tabla salió de leer los correos reales del Banco Popular. Dos de los
/// cuatro tipos que llegaron —«Depósito por ATM» y «Pagos al Instante»— no
/// estaban previstos, y uno de ellos es dinero que entra.
/// </remarks>
public sealed class DirectionTests
{
    private static Transaction Tx(TxKind kind, TxStatus status = TxStatus.Posted) => new()
    {
        Id = Guid.NewGuid(),
        MerchantRaw = "X",
        MerchantNormalized = "X",
        Fingerprint = "x",
        Amount = new Money(100_000),
        Kind = kind,
        Status = status,
        Direction = Directions.Of(kind),
    };

    [Theory]
    [InlineData(TxKind.Deposit, TxDirection.Inflow)]
    [InlineData(TxKind.Refund, TxDirection.Inflow)]
    [InlineData(TxKind.Payment, TxDirection.Internal)]
    [InlineData(TxKind.Purchase, TxDirection.Outflow)]
    [InlineData(TxKind.Withdrawal, TxDirection.Outflow)]
    [InlineData(TxKind.Transfer, TxDirection.Outflow)]
    [InlineData(TxKind.Fee, TxDirection.Outflow)]
    [InlineData(TxKind.Cash, TxDirection.Outflow)]
    public void cada_tipo_nace_con_su_direccion(TxKind kind, TxDirection esperada)
    {
        Assert.Equal(esperada, Directions.Of(kind));
    }

    [Fact]
    public void un_deposito_es_ingreso_y_no_es_gasto()
    {
        // Es dinero nuevo, no un gasto negativo. Restarlo de una categoría
        // haría que ingresar dinero pareciera haber gastado menos en comida.
        Transaction deposito = Tx(TxKind.Deposit);

        Assert.True(deposito.IsIncome);
        Assert.False(deposito.AffectsSpending);
        Assert.Equal(Money.Zero, deposito.SpendingEffect);

        // Pero sí sube el saldo.
        Assert.Equal(new Money(100_000), deposito.BalanceEffect);
    }

    [Fact]
    public void una_devolucion_es_ingreso_y_si_reduce_el_gasto()
    {
        // La diferencia con el depósito: una devolución deshace un gasto
        // concreto de una categoría concreta.
        Transaction devolucion = Tx(TxKind.Refund);

        Assert.True(devolucion.IsIncome);
        Assert.True(devolucion.AffectsSpending);
        Assert.Equal(new Money(-100_000), devolucion.SpendingEffect);
    }

    [Fact]
    public void un_retiro_es_egreso_aunque_el_dinero_siga_siendo_tuyo()
    {
        // Está en el bolsillo, no gastado. Pero si no registras en qué lo
        // gastaste, tratarlo como traspaso lo haría desaparecer del gasto del
        // período y la pantalla diría que te queda más de lo que hay. Contar de
        // más es el error barato.
        Transaction retiro = Tx(TxKind.Withdrawal);

        Assert.Equal(TxDirection.Outflow, retiro.Direction);
        Assert.True(retiro.AffectsSpending);
    }

    [Fact]
    public void una_transferencia_enviada_nace_como_egreso()
    {
        // El correo dice que el dinero salió, no a dónde fue.
        Transaction enviada = Tx(TxKind.Transfer);

        Assert.Equal(TxDirection.Outflow, enviada.Direction);
        Assert.True(enviada.AffectsSpending);
        Assert.Equal(new Money(100_000), enviada.SpendingEffect);
    }

    [Fact]
    public void marcar_una_transferencia_como_traspaso_la_saca_del_gasto()
    {
        // Es el caso que el correo no puede resolver: una transferencia a tu
        // propia cuenta de ahorro. Lo corrige el usuario desde Revisión.
        Transaction propia = Tx(TxKind.Transfer);
        propia.Direction = TxDirection.Internal;

        Assert.False(propia.AffectsSpending);
        Assert.Equal(Money.Zero, propia.SpendingEffect);
    }

    [Fact]
    public void un_pago_de_tarjeta_es_traspaso_y_no_cuenta()
    {
        // El único caso que el correo sí resuelve solo: paga una deuda tuya con
        // dinero tuyo. Contarlo duplicaría el gasto, una vez al comprar con la
        // tarjeta y otra al pagarla.
        Transaction pago = Tx(TxKind.Payment);

        Assert.Equal(TxDirection.Internal, pago.Direction);
        Assert.False(pago.AffectsSpending);
        Assert.False(pago.IsIncome);

        // Pero sí baja el saldo de la cuenta desde la que se pagó.
        Assert.Equal(new Money(-100_000), pago.BalanceEffect);
    }

    [Theory]
    [InlineData(TxStatus.Rejected)]
    [InlineData(TxStatus.Duplicate)]
    public void un_movimiento_descartado_no_cuenta_ni_para_gasto_ni_para_saldo(TxStatus status)
    {
        Transaction descartado = Tx(TxKind.Purchase, status);

        Assert.False(descartado.AffectsSpending);
        Assert.Equal(Money.Zero, descartado.SpendingEffect);
        Assert.Equal(Money.Zero, descartado.BalanceEffect);
    }

    [Fact]
    public void un_deposito_descartado_tampoco_sube_el_saldo()
    {
        Transaction descartado = Tx(TxKind.Deposit, TxStatus.Duplicate);

        Assert.Equal(Money.Zero, descartado.BalanceEffect);
    }

    [Theory]
    [InlineData(TxDirection.Inflow, "Ingreso")]
    [InlineData(TxDirection.Outflow, "Egreso")]
    [InlineData(TxDirection.Internal, "Traspaso")]
    public void la_etiqueta_es_una_palabra_sin_jerga(TxDirection direction, string esperada)
    {
        Assert.Equal(esperada, Directions.Label(direction));
    }

    [Fact]
    public void el_gasto_y_el_saldo_son_preguntas_distintas()
    {
        // Un pago de tarjeta baja el saldo sin ser gasto; un depósito lo sube
        // sin reducir ninguna categoría. Confundir las dos preguntas es lo que
        // haría que pagar la tarjeta pareciera un gasto más.
        Assert.Equal(Money.Zero, Tx(TxKind.Payment).SpendingEffect);
        Assert.NotEqual(Money.Zero, Tx(TxKind.Payment).BalanceEffect);

        Assert.Equal(Money.Zero, Tx(TxKind.Deposit).SpendingEffect);
        Assert.NotEqual(Money.Zero, Tx(TxKind.Deposit).BalanceEffect);
    }
}
