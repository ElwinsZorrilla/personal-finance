using Margen.Classify;
using Margen.Domain;

namespace Margen.Classify.Tests;

public sealed class StatisticsTests
{
    private static Money[] Cents(params long[] values) =>
        values.Select(v => new Money(v)).ToArray();

    // ---------- Mediana ----------

    [Fact]
    public void la_mediana_de_una_lista_impar_es_el_del_medio()
    {
        Assert.Equal(new Money(300), Statistics.Median(Cents(100, 300, 900)));
    }

    [Fact]
    public void la_mediana_de_una_lista_par_es_el_punto_medio_de_los_dos_centrales()
    {
        Assert.Equal(new Money(250), Statistics.Median(Cents(100, 200, 300, 900)));
    }

    [Fact]
    public void la_mediana_no_depende_del_orden_de_entrada()
    {
        Assert.Equal(
            Statistics.Median(Cents(900, 100, 300)),
            Statistics.Median(Cents(100, 300, 900)));
    }

    [Fact]
    public void un_valor_extremo_no_mueve_la_mediana()
    {
        // El motivo entero de usar mediana. La media de estos seis es 6 900 y no
        // se parece a ninguna de las compras; la mediana sí.
        Money[] compras = Cents(30_000, 30_000, 30_000, 30_000, 30_000, 4_000_000);

        Assert.Equal(new Money(30_000), Statistics.Median(compras));
    }

    [Fact]
    public void la_mediana_no_desborda_con_importes_enormes()
    {
        // Sumar los dos centrales desbordaría long; restarlos no.
        long grande = long.MaxValue - 10;
        Assert.Equal(new Money(grande - 3), Statistics.Median(Cents(grande - 4, grande - 2)));
    }

    [Fact]
    public void la_mediana_de_una_lista_vacia_lanza()
    {
        // No devuelve cero: cero es un importe y aquí no hay ninguno.
        Assert.Throws<ArgumentException>(() => Statistics.Median(Cents()));
    }

    [Fact]
    public void la_mediana_de_una_lista_nula_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => Statistics.Median(null!));
    }

    // ---------- Desviación absoluta mediana ----------

    [Fact]
    public void la_desviacion_de_valores_identicos_es_cero()
    {
        Assert.Equal(Money.Zero, Statistics.MedianAbsoluteDeviation(Cents(500, 500, 500)));
    }

    [Fact]
    public void la_desviacion_mide_la_distancia_tipica_a_la_mediana()
    {
        // Mediana 300; distancias 200, 100, 0, 100, 200; su mediana es 100.
        Assert.Equal(
            new Money(100),
            Statistics.MedianAbsoluteDeviation(Cents(100, 200, 300, 400, 500)));
    }

    [Fact]
    public void un_valor_extremo_tampoco_infla_la_desviacion()
    {
        // Si el cargo raro definiera lo normal, dejaría de ser raro.
        Money[] compras = Cents(30_000, 30_000, 30_000, 30_000, 30_000, 4_000_000);

        Assert.Equal(Money.Zero, Statistics.MedianAbsoluteDeviation(compras));
    }

    [Fact]
    public void la_desviacion_de_una_lista_vacia_lanza()
    {
        Assert.Throws<ArgumentException>(() => Statistics.MedianAbsoluteDeviation(Cents()));
    }

    [Fact]
    public void la_desviacion_de_una_lista_nula_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => Statistics.MedianAbsoluteDeviation(null!));
    }

    // ---------- Variación en puntos básicos ----------

    [Fact]
    public void el_doble_son_diez_mil_puntos_basicos()
    {
        Outcome<int> cambio = Statistics.ChangeInBasisPoints(new Money(1_000), new Money(2_000));

        Assert.Equal(10_000, cambio.Value);
    }

    [Fact]
    public void una_bajada_da_puntos_basicos_negativos()
    {
        Outcome<int> cambio = Statistics.ChangeInBasisPoints(new Money(2_000), new Money(1_000));

        Assert.Equal(-5_000, cambio.Value);
    }

    [Fact]
    public void una_subida_del_diez_por_ciento_son_mil_puntos_basicos()
    {
        // El caso real: una suscripción de 1 200 pesos que pasa a 1 320.
        Outcome<int> cambio = Statistics.ChangeInBasisPoints(
            new Money(120_000), new Money(132_000));

        Assert.Equal(1_000, cambio.Value);
    }

    [Fact]
    public void sin_cambio_no_hay_puntos_basicos()
    {
        Outcome<int> cambio = Statistics.ChangeInBasisPoints(new Money(500), new Money(500));

        Assert.Equal(0, cambio.Value);
    }

    [Fact]
    public void no_hay_variacion_respecto_de_cero()
    {
        // Dividir entre cero, o peor: inventar un «infinito por ciento» que
        // luego alguien compara contra un umbral.
        Outcome<int> cambio = Statistics.ChangeInBasisPoints(Money.Zero, new Money(500));

        Assert.Equal(OutcomeKind.Invalid, cambio.Kind);
        Assert.Throws<InvalidOperationException>(() => cambio.Value);
    }

    [Fact]
    public void la_variacion_no_desborda_con_importes_grandes()
    {
        // 10 000 × un importe grande desborda long a partir de unos 920
        // millones de pesos. El producto se hace en Int128.
        Outcome<int> cambio = Statistics.ChangeInBasisPoints(
            new Money(1_000_000_000_000), new Money(2_000_000_000_000));

        Assert.Equal(10_000, cambio.Value);
    }

    [Fact]
    public void una_variacion_que_no_cabe_en_un_entero_se_rechaza()
    {
        // De un centavo a un billón: 10^17 puntos básicos, que no caben en int.
        // Devolver el truncado sería devolver un número inventado.
        Outcome<int> cambio = Statistics.ChangeInBasisPoints(
            new Money(1), new Money(1_000_000_000_000_000));

        Assert.Equal(OutcomeKind.Invalid, cambio.Kind);
    }
}
