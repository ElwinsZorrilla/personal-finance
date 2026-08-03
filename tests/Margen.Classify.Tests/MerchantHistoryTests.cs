using Margen.Classify;
using Margen.Domain;

namespace Margen.Classify.Tests;

public sealed class MerchantHistoryTests
{
    private static readonly Guid Supermercado = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Restaurantes = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Transporte = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static Guid[] Veces(Guid id, int n) => Enumerable.Repeat(id, n).ToArray();

    [Fact]
    public void con_menos_de_tres_confirmados_no_opina()
    {
        // Con dos, la primera corrección equivocada se vuelve «lo de siempre».
        Outcome<MerchantVerdict> v = MerchantHistory.Dominant(Veces(Supermercado, 2));

        Assert.Equal(OutcomeKind.Insufficient, v.Kind);
    }

    [Fact]
    public void sin_historial_no_opina()
    {
        Assert.Equal(OutcomeKind.Insufficient, MerchantHistory.Dominant([]).Kind);
    }

    [Fact]
    public void una_lista_nula_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => MerchantHistory.Dominant(null!));
    }

    [Fact]
    public void tres_veces_lo_mismo_es_dominante()
    {
        MerchantVerdict v = MerchantHistory.Dominant(Veces(Supermercado, 3)).Value;

        Assert.Equal(Supermercado, v.CategoryId);
        Assert.Equal(3, v.SampleCount);
    }

    [Fact]
    public void la_unanimidad_no_llega_a_diez_mil()
    {
        // Diez de diez no es certeza: es que las diez veces anteriores fueron
        // iguales. Queda por debajo de una regla que escribió una persona.
        MerchantVerdict v = MerchantHistory.Dominant(Veces(Supermercado, 10)).Value;

        Assert.Equal(MerchantHistory.MaxConfidence, v.DominanceBasisPoints);
        Assert.True(v.DominanceBasisPoints < 10_000);
    }

    [Fact]
    public void la_dominancia_se_expresa_en_puntos_basicos()
    {
        // Siete de diez.
        Guid[] historia = [.. Veces(Supermercado, 7), .. Veces(Restaurantes, 3)];

        Assert.Equal(7_000, MerchantHistory.Dominant(historia).Value.DominanceBasisPoints);
    }

    [Fact]
    public void por_debajo_del_sesenta_por_ciento_no_hay_categoria_habitual()
    {
        // Cinco a cuatro no es «este comercio es comida»: es un comercio donde
        // se compran dos cosas distintas.
        Guid[] historia = [.. Veces(Supermercado, 5), .. Veces(Restaurantes, 4)];

        Outcome<MerchantVerdict> v = MerchantHistory.Dominant(historia);

        Assert.Equal(OutcomeKind.Insufficient, v.Kind);
    }

    [Fact]
    public void justo_en_el_sesenta_por_ciento_si_opina()
    {
        // Seis de diez. El umbral es inclusivo y hay una prueba que lo fija:
        // moverlo un punto deja de ser una decisión y pasa a ser un accidente.
        Guid[] historia = [.. Veces(Supermercado, 6), .. Veces(Restaurantes, 4)];

        Assert.Equal(Supermercado, MerchantHistory.Dominant(historia).Value.CategoryId);
    }

    [Fact]
    public void un_empate_no_es_una_respuesta()
    {
        // Elegir uno por el orden del diccionario daría respuestas distintas
        // con exactamente los mismos datos.
        Guid[] historia = [.. Veces(Supermercado, 3), .. Veces(Restaurantes, 3)];

        Assert.Equal(OutcomeKind.Insufficient, MerchantHistory.Dominant(historia).Kind);
    }

    [Fact]
    public void el_orden_de_la_historia_no_cambia_el_resultado()
    {
        Guid[] a = [Supermercado, Restaurantes, Supermercado, Supermercado];
        Guid[] b = [Supermercado, Supermercado, Supermercado, Restaurantes];

        Assert.Equal(
            MerchantHistory.Dominant(a).Value,
            MerchantHistory.Dominant(b).Value);
    }

    [Fact]
    public void con_tres_categorias_gana_la_que_pasa_del_umbral()
    {
        Guid[] historia =
        [
            .. Veces(Supermercado, 8),
            .. Veces(Restaurantes, 1),
            .. Veces(Transporte, 1),
        ];

        Assert.Equal(Supermercado, MerchantHistory.Dominant(historia).Value.CategoryId);
    }

    [Fact]
    public void tres_categorias_repartidas_no_dan_respuesta()
    {
        Guid[] historia =
        [
            .. Veces(Supermercado, 4),
            .. Veces(Restaurantes, 3),
            .. Veces(Transporte, 3),
        ];

        Assert.Equal(OutcomeKind.Insufficient, MerchantHistory.Dominant(historia).Kind);
    }
}
