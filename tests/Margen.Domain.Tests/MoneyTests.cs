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
    public void el_promedio_ponderado_redondea_una_sola_vez()
    {
        // Escalar cada término por su peso y sumar redondearía tres veces y
        // sumaría los tres errores. Con tres valores de 5 centavos, el
        // promedio ponderado tiene que ser 5, no 7.
        Money promedio = Money.WeightedAverage(
            [new Money(5), new Money(5), new Money(5)],
            [50, 30, 20]);

        Assert.Equal(5, promedio.Cents);
    }

    [Fact]
    public void el_promedio_ponderado_usa_solo_los_pesos_que_hacen_falta()
    {
        // Con dos valores y tres pesos, el denominador es 80 y no 100. Es lo
        // que hace que un usuario con dos meses de historia no vea una base
        // un 20 % menor de lo que gasta.
        Money promedio = Money.WeightedAverage(
            [new Money(800_000), Money.Zero],
            [50, 30, 20]);

        Assert.Equal(500_000, promedio.Cents);
    }

    [Fact]
    public void el_promedio_ponderado_no_desborda_con_cifras_grandes()
    {
        // Diez millones de pesos por un peso de mil millones desborda un long
        // si el numerador no se acumula en Int128.
        Money promedio = Money.WeightedAverage(
            [new Money(1_000_000_000), new Money(1_000_000_000)],
            [1_000_000_000, 1_000_000_000]);

        Assert.Equal(1_000_000_000, promedio.Cents);
    }

    [Fact]
    public void el_promedio_ponderado_redondea_alejandose_del_cero()
    {
        // (1 + 2) / 2 = 1.5 → 2. Y con signo negativo, −2.
        Assert.Equal(2, Money.WeightedAverage([new Money(1), new Money(2)], [1, 1]).Cents);
        Assert.Equal(-2, Money.WeightedAverage([new Money(-1), new Money(-2)], [1, 1]).Cents);
    }

    [Fact]
    public void el_promedio_ponderado_rechaza_una_lista_vacia()
    {
        Assert.Throws<ArgumentException>(() => Money.WeightedAverage([], [50]));
    }

    [Fact]
    public void el_promedio_ponderado_rechaza_pesos_insuficientes()
    {
        Assert.Throws<ArgumentException>(
            () => Money.WeightedAverage([new Money(1), new Money(2)], [50]));
    }

    [Fact]
    public void el_promedio_ponderado_rechaza_pesos_que_suman_cero()
    {
        Assert.Throws<ArgumentException>(
            () => Money.WeightedAverage([new Money(1), new Money(2)], [0, 0]));
    }

    [Fact]
    public void el_promedio_ponderado_rechaza_un_peso_negativo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Money.WeightedAverage([new Money(1), new Money(2)], [3, -1]));
    }

    [Fact]
    public void sum_suma_una_secuencia_sin_perder_centavos()
    {
        Assert.Equal(1000, Money.Sum(Enumerable.Repeat(new Money(10), 100)).Cents);
        Assert.Equal(0, Money.Sum([]).Cents);
        Assert.Equal(-500, Money.Sum([new Money(500), new Money(-1000)]).Cents);
    }

    [Fact]
    public void las_comparaciones_ordenan_por_centavos()
    {
        Assert.True(new Money(200) > new Money(100));
        Assert.True(new Money(-200) < new Money(-100));
        Assert.True(new Money(100) >= new Money(100));
        Assert.True(new Money(100) <= new Money(100));
    }

    [Fact]
    public void compare_to_ordena_una_lista()
    {
        // Es lo que usa cualquier ordenación del servidor. Sin él, ordenar
        // montos caería en la comparación por referencia.
        List<Money> montos = [new Money(300), new Money(-100), new Money(0)];
        montos.Sort();

        Assert.Equal([-100, 0, 300], montos.Select(m => m.Cents));
    }

    [Fact]
    public void los_predicados_de_signo_dicen_lo_que_prometen()
    {
        Assert.True(new Money(-1).IsNegative);
        Assert.False(Money.Zero.IsNegative);
        Assert.False(new Money(1).IsNegative);

        Assert.True(Money.Zero.IsZero);
        Assert.False(new Money(1).IsZero);
        Assert.False(new Money(-1).IsZero);
    }

    [Fact]
    public void el_valor_absoluto_quita_el_signo_sin_tocar_la_cifra()
    {
        Assert.Equal(new Money(1205), new Money(-1205).Abs);
        Assert.Equal(new Money(1205), new Money(1205).Abs);
        Assert.Equal(Money.Zero, Money.Zero.Abs);
    }

    [Fact]
    public void el_menos_unario_invierte_el_signo()
    {
        Assert.Equal(new Money(-500), -new Money(500));
        Assert.Equal(new Money(500), -new Money(-500));
        Assert.Equal(Money.Zero, -Money.Zero);
    }

    [Fact]
    public void prorate_rechaza_una_lista_de_pesos_vacia()
    {
        Assert.Throws<ArgumentException>(() => new Money(100).Prorate([]));
    }

    [Fact]
    public void la_suma_desbordada_lanza_en_vez_de_dar_la_vuelta()
    {
        // Sin `checked`, sumar al máximo daría un número negativo enorme y el
        // dinero se convertiría en deuda sin que nada avisara.
        Assert.Throws<OverflowException>(() => new Money(long.MaxValue) + new Money(1));
        Assert.Throws<OverflowException>(() => new Money(long.MinValue) - new Money(1));
        Assert.Throws<OverflowException>(() => Money.FromUnits(long.MaxValue));
    }
}
