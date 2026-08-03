using Margen.Classify;
using Margen.Domain;

namespace Margen.Classify.Tests;

public sealed class NormalRangeTests
{
    private static Money[] Cents(params long[] values) =>
        values.Select(v => new Money(v)).ToArray();

    [Fact]
    public void con_menos_de_cinco_cargos_no_hay_rango()
    {
        Outcome<NormalRange> rango = NormalRanges.Of(Cents(100, 100, 100, 100));

        Assert.Equal(OutcomeKind.Insufficient, rango.Kind);
        Assert.Contains("5", rango.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void sin_ningun_cargo_tampoco_hay_rango()
    {
        // Y no un rango que lo acepte todo, que sería lo mismo que no mirar pero
        // pareciendo que se mira.
        Assert.Equal(OutcomeKind.Insufficient, NormalRanges.Of(Cents()).Kind);
    }

    [Fact]
    public void una_lista_nula_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => NormalRanges.Of(null!));
    }

    [Fact]
    public void con_cinco_cargos_ya_hay_rango()
    {
        Outcome<NormalRange> rango = NormalRanges.Of(Cents(100, 200, 300, 400, 500));

        Assert.True(rango.IsComputed);
        Assert.Equal(5, rango.Value.SampleCount);
        Assert.Equal(new Money(300), rango.Value.Center);
    }

    [Fact]
    public void el_rango_se_centra_en_la_mediana_y_abarca_tres_desviaciones()
    {
        // Mediana 300, desviación 100, margen 300. El suelo -30- no manda.
        NormalRange rango = NormalRanges.Of(Cents(100, 200, 300, 400, 500)).Value;

        Assert.Equal(Money.Zero, rango.Low);
        Assert.Equal(new Money(600), rango.High);
    }

    [Fact]
    public void el_extremo_bajo_nunca_es_negativo()
    {
        // Un gasto negativo no existe, y un rango que empieza por debajo de cero
        // dice que sí existe.
        NormalRange rango = NormalRanges.Of(Cents(10, 50, 100, 150, 190)).Value;

        Assert.True(rango.Low.Cents >= 0);
    }

    [Fact]
    public void una_suscripcion_identica_recibe_la_holgura_minima()
    {
        // El caso más común: cinco cobros exactamente iguales. La desviación es
        // cero y el rango sería un punto, así que el sexto cobro con un centavo
        // de diferencia saldría anómalo.
        NormalRange rango = NormalRanges.Of(
            Cents(120_000, 120_000, 120_000, 120_000, 120_000)).Value;

        Assert.Equal(new Money(108_000), rango.Low);
        Assert.Equal(new Money(132_000), rango.High);
        Assert.True(rango.Contains(new Money(120_001)));
    }

    [Fact]
    public void la_holgura_minima_no_encoge_un_rango_ya_ancho()
    {
        // El suelo es un mínimo, no un máximo: donde la dispersión real es mayor
        // que la décima parte de la mediana, manda la dispersión real.
        NormalRange rango = NormalRanges.Of(
            Cents(10_000, 50_000, 100_000, 150_000, 190_000)).Value;

        Assert.True(rango.High > new Money(110_000));
    }

    [Fact]
    public void un_cargo_muy_por_encima_queda_fuera()
    {
        NormalRange rango = NormalRanges.Of(
            Cents(30_000, 30_000, 30_000, 30_000, 30_000)).Value;

        Assert.False(rango.Contains(new Money(4_000_000)));
    }

    [Fact]
    public void el_cargo_justo_en_el_borde_esta_dentro()
    {
        // El borde pertenece a lo normal. Un rango medio abierto haría que dos
        // importes idénticos cayeran distinto según de qué lado se mire.
        NormalRange rango = NormalRanges.Of(
            Cents(120_000, 120_000, 120_000, 120_000, 120_000)).Value;

        Assert.True(rango.Contains(rango.Low));
        Assert.True(rango.Contains(rango.High));
    }

    [Fact]
    public void el_valor_extremo_de_la_historia_no_ensancha_el_rango_hasta_taparse()
    {
        // Seis cargos: cinco de 300 pesos y uno de 40 000. El de 40 000 no debe
        // conseguir que un séptimo de 40 000 parezca normal.
        NormalRange rango = NormalRanges.Of(
            Cents(30_000, 30_000, 30_000, 30_000, 30_000, 4_000_000)).Value;

        Assert.False(rango.Contains(new Money(4_000_000)));
    }
}
