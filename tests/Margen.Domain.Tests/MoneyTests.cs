using Margen.Domain;

namespace Margen.Domain.Tests;

public sealed class MoneyTests
{
    [Fact]
    public void sumar_cien_veces_diez_centavos_da_exactamente_diez_pesos()
    {
        Money total = Money.Zero;
        for (int i = 0; i < 100; i++)
        {
            total += new Money(10);
        }

        // Con double esto da 9.999999999999998.
        Assert.Equal(1000, total.Cents);
    }

    [Fact]
    public void split_reparte_sin_perder_ni_inventar_centavos()
    {
        Money[] partes = new Money(1000).Split(3);

        Assert.Equal(1000, partes.Sum(p => p.Cents));
        Assert.Equal([334, 333, 333], partes.Select(p => p.Cents));
    }

    [Fact]
    public void split_conserva_el_signo_en_montos_negativos()
    {
        Money[] partes = new Money(-1000).Split(3);

        Assert.Equal(-1000, partes.Sum(p => p.Cents));
        Assert.Equal([-334, -333, -333], partes.Select(p => p.Cents));
    }

    [Fact]
    public void split_rechaza_cero_partes_en_lugar_de_dividir_por_cero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(100).Split(0));
    }

    [Fact]
    public void prorate_reparte_la_base_historica_sin_perder_un_centavo()
    {
        // 50 % del último período, 30 % del anterior, 20 % del tercero. El
        // total tiene que salir exacto: si sobra o falta un centavo, la suma de
        // los presupuestos por categoría no cuadra con el presupuesto.
        Money[] partes = new Money(100_001).Prorate([50, 30, 20]);

        Assert.Equal(100_001, partes.Sum(p => p.Cents));
        Assert.Equal([50_001, 30_000, 20_000], partes.Select(p => p.Cents));
    }

    [Fact]
    public void prorate_reparte_los_sobrantes_por_resto_mayor()
    {
        // 10 centavos entre tres partes iguales: 3.33 cada una y un centavo que
        // sobra. Va a la primera, y el reparto es el mismo cada vez que se
        // ejecuta.
        Money[] partes = new Money(10).Prorate([1, 1, 1]);

        Assert.Equal(10, partes.Sum(p => p.Cents));
        Assert.Equal([4, 3, 3], partes.Select(p => p.Cents));
    }

    [Fact]
    public void prorate_conserva_el_signo()
    {
        Money[] partes = new Money(-10).Prorate([1, 1, 1]);

        Assert.Equal(-10, partes.Sum(p => p.Cents));
    }

    [Fact]
    public void prorate_rechaza_pesos_que_suman_cero()
    {
        Assert.Throws<ArgumentException>(() => new Money(100).Prorate([0, 0]));
    }

    [Fact]
    public void prorate_rechaza_un_peso_negativo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(100).Prorate([3, -1]));
    }

    [Fact]
    public void scale_redondea_a_la_mitad_alejandose_del_cero()
    {
        // 5 centavos por 1/2 son 2.5. Redondear hacia abajo perdería medio
        // centavo en cada operación; redondear a la baja siempre sesga las
        // cifras del período hacia el optimismo.
        Assert.Equal(3, new Money(5).Scale(1, 2).Cents);
        Assert.Equal(-3, new Money(-5).Scale(1, 2).Cents);
    }

    [Fact]
    public void scale_no_desborda_con_cifras_normales()
    {
        // Diez millones de pesos por un numerador de mil millones desborda un
        // long si el producto no se calcula en Int128.
        var grande = new Money(1_000_000_000);
        Money resultado = grande.Scale(1_000_000_000, 1_000_000_000);

        Assert.Equal(1_000_000_000, resultado.Cents);
    }

    [Fact]
    public void scale_rechaza_denominador_cero()
    {
        Assert.Throws<DivideByZeroException>(() => new Money(100).Scale(1, 0));
    }

    [Fact]
    public void scale_con_denominador_negativo_invierte_el_signo_una_sola_vez()
    {
        Assert.Equal(-50, new Money(100).Scale(1, -2).Cents);
        Assert.Equal(50, new Money(-100).Scale(1, -2).Cents);
    }

    [Fact]
    public void from_units_multiplica_por_cien()
    {
        Assert.Equal(245_000, Money.FromUnits(2450).Cents);
    }

    [Fact]
    public void la_igualdad_es_por_valor()
    {
        Assert.Equal(new Money(1234), new Money(1234));
        Assert.NotEqual(new Money(1234), new Money(1235));
    }

    [Fact]
    public void el_texto_conserva_el_signo_en_montos_menores_a_un_peso()
    {
        // -50 centavos dividido entre 100 trunca a cero y el menos desaparece
        // si el signo lo pone la división.
        Assert.Equal("-0.50", new Money(-50).ToString());
        Assert.Equal("0.50", new Money(50).ToString());
        Assert.Equal("-12.05", new Money(-1205).ToString());
    }

    [Fact]
    public void restar_da_negativo_sin_saturar()
    {
        Assert.Equal(new Money(-500), new Money(1000) - new Money(1500));
    }

    [Fact]
    public void las_comparaciones_ordenan_por_centavos()
    {
        Assert.True(new Money(200) > new Money(100));
        Assert.True(new Money(-200) < new Money(-100));
        Assert.True(new Money(100) >= new Money(100));
        Assert.True(new Money(100) <= new Money(100));
    }
}
