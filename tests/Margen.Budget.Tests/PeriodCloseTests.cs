using Margen.Budget;
using Margen.Domain;

namespace Margen.Budget.Tests;

/// <summary>
/// El presupuesto recomendado del período que viene.
/// </summary>
/// <remarks>
/// Sale de **lo que se gastó de verdad**, no de lo que se asignó. Un
/// presupuesto que se copia a sí mismo mes tras mes repite el error del primer
/// mes para siempre.
/// </remarks>
public sealed class PeriodCloseTests
{
    private static readonly Guid Comida = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Alquiler = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Ocio = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private static IReadOnlyList<CategoryHistory> Historia(
        Guid id, Priority prioridad, params long[] centavosNuevoPrimero) =>
        [.. centavosNuevoPrimero.Select(c => new CategoryHistory(id, prioridad, new Money(c)))];

    private static Dictionary<Guid, IReadOnlyList<CategoryHistory>> Solo(
        Guid id, Priority prioridad, params long[] centavos) =>
        new() { [id] = Historia(id, prioridad, centavos) };

    // ---------- Sin historia no hay recomendación ----------

    [Fact]
    public void sin_periodos_cerrados_no_hay_recomendacion()
    {
        // Proponer ceros, o el ingreso repartido a partes iguales, sería
        // inventarse un presupuesto y presentarlo como si saliera de algún
        // sitio.
        Outcome<RecommendedBudget> r = PeriodClose.Recommend(
            new Dictionary<Guid, IReadOnlyList<CategoryHistory>>(), Money.FromUnits(50_000));

        Assert.Equal(OutcomeKind.Insufficient, r.Kind);
    }

    [Fact]
    public void con_categorias_pero_sin_gasto_registrado_tampoco()
    {
        var vacio = new Dictionary<Guid, IReadOnlyList<CategoryHistory>>
        {
            [Comida] = [],
        };

        Assert.Equal(OutcomeKind.Insufficient, PeriodClose.Recommend(vacio, Money.FromUnits(50_000)).Kind);
    }

    [Fact]
    public void un_diccionario_nulo_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => PeriodClose.Recommend(null!, Money.Zero));
    }

    // ---------- Sale de lo gastado ----------

    [Fact]
    public void recomienda_lo_que_se_gasto_y_no_lo_que_se_asigno()
    {
        // Tres períodos gastando 14 000 en Comida: la recomendación es 14 000
        // aunque el presupuesto dijera 10 000 cada vez.
        RecommendedBudget b = PeriodClose.Recommend(
            Solo(Comida, Priority.Flexible, 1_400_000, 1_400_000, 1_400_000),
            Money.FromUnits(50_000)).Value;

        Assert.Equal(new Money(1_400_000), b.Categories[0].Recommended);
    }

    [Fact]
    public void el_periodo_mas_reciente_pesa_mas()
    {
        // La media ponderada de la base histórica: 50, 30 y 20. Un mes que
        // subió tiene que notarse, no diluirse.
        RecommendedBudget b = PeriodClose.Recommend(
            Solo(Comida, Priority.Flexible, 2_000_000, 1_000_000, 1_000_000),
            Money.FromUnits(50_000)).Value;

        // 50 % de 20 000 + 30 % de 10 000 + 20 % de 10 000 = 15 000.
        Assert.Equal(new Money(1_500_000), b.Categories[0].Recommended);
    }

    [Fact]
    public void con_un_solo_periodo_cerrado_se_usa_ese()
    {
        RecommendedBudget b = PeriodClose.Recommend(
            Solo(Comida, Priority.Flexible, 1_400_000), Money.FromUnits(50_000)).Value;

        Assert.Equal(new Money(1_400_000), b.Categories[0].Recommended);
        Assert.Contains("1 período", b.Categories[0].Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void lo_que_sobra_del_ingreso_se_dice()
    {
        RecommendedBudget b = PeriodClose.Recommend(
            Solo(Comida, Priority.Flexible, 1_000_000), Money.FromUnits(50_000)).Value;

        Assert.Equal(Money.FromUnits(40_000), b.Unallocated);
    }

    // ---------- Cuando no cabe en el ingreso ----------

    [Fact]
    public void si_lo_recomendado_no_cabe_se_recorta()
    {
        // Presentar un presupuesto que no cabe en el ingreso es proponer que se
        // gaste dinero que no está.
        var historia = new Dictionary<Guid, IReadOnlyList<CategoryHistory>>
        {
            [Alquiler] = Historia(Alquiler, Priority.Essential, 2_500_000),
            [Ocio] = Historia(Ocio, Priority.Optional, 2_000_000),
        };

        RecommendedBudget b = PeriodClose.Recommend(historia, Money.FromUnits(40_000)).Value;

        Assert.Equal(Money.FromUnits(40_000), b.Total);
    }

    [Fact]
    public void lo_esencial_no_se_recorta()
    {
        // El alquiler no es negociable a mitad de mes.
        var historia = new Dictionary<Guid, IReadOnlyList<CategoryHistory>>
        {
            [Alquiler] = Historia(Alquiler, Priority.Essential, 2_500_000),
            [Ocio] = Historia(Ocio, Priority.Optional, 2_000_000),
        };

        RecommendedBudget b = PeriodClose.Recommend(historia, Money.FromUnits(40_000)).Value;

        Recommendation alquiler = b.Categories.Single(c => c.CategoryId == Alquiler);
        Assert.Equal(new Money(2_500_000), alquiler.Recommended);
    }

    [Fact]
    public void el_recorte_se_reparte_sin_perder_un_centavo()
    {
        // Reparto de resto mayor: la suma de los recortes es exactamente el
        // exceso. Con división entera a secas quedan centavos sin asignar, y
        // luego aparecen como una diferencia que nadie sabe de dónde sale.
        var historia = new Dictionary<Guid, IReadOnlyList<CategoryHistory>>
        {
            [Comida] = Historia(Comida, Priority.Flexible, 1_000_001),
            [Ocio] = Historia(Ocio, Priority.Optional, 1_000_000),
        };

        RecommendedBudget b = PeriodClose.Recommend(historia, new Money(1_999_999)).Value;

        Assert.Equal(new Money(1_999_999), b.Total);
        Assert.Equal(Money.Zero, b.Unallocated);
    }

    [Fact]
    public void si_los_compromisos_no_caben_en_el_sueldo_se_dice_en_vez_de_taparlo()
    {
        // Solo hay esencial, y se pasa. Recortarlo escondería el problema:
        // el cálculo no está mal, es que no cabe.
        var historia = new Dictionary<Guid, IReadOnlyList<CategoryHistory>>
        {
            [Alquiler] = Historia(Alquiler, Priority.Essential, 5_000_000),
        };

        RecommendedBudget b = PeriodClose.Recommend(historia, Money.FromUnits(30_000)).Value;

        Assert.Equal(new Money(5_000_000), b.Total);
        Assert.True(b.Unallocated.Cents < 0);
    }

    [Fact]
    public void lo_importante_tampoco_se_recorta()
    {
        var historia = new Dictionary<Guid, IReadOnlyList<CategoryHistory>>
        {
            [Alquiler] = Historia(Alquiler, Priority.Important, 3_000_000),
            [Ocio] = Historia(Ocio, Priority.Flexible, 2_000_000),
        };

        RecommendedBudget b = PeriodClose.Recommend(historia, Money.FromUnits(40_000)).Value;

        Assert.Equal(
            new Money(3_000_000),
            b.Categories.Single(c => c.CategoryId == Alquiler).Recommended);
    }

    [Fact]
    public void el_recorte_se_dice_en_el_motivo()
    {
        var historia = new Dictionary<Guid, IReadOnlyList<CategoryHistory>>
        {
            [Ocio] = Historia(Ocio, Priority.Optional, 2_000_000),
        };

        RecommendedBudget b = PeriodClose.Recommend(historia, Money.FromUnits(10_000)).Value;

        Assert.Contains("recortada", b.Categories[0].Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void el_total_es_siempre_la_suma_de_lo_repartido()
    {
        // Que la cifra de arriba y las de abajo cuadren no es decorativo: es lo
        // que hace que la pantalla no se contradiga a sí misma.
        var historia = new Dictionary<Guid, IReadOnlyList<CategoryHistory>>
        {
            [Comida] = Historia(Comida, Priority.Flexible, 1_400_000, 1_300_000),
            [Alquiler] = Historia(Alquiler, Priority.Essential, 2_500_000),
            [Ocio] = Historia(Ocio, Priority.Optional, 700_000, 900_000),
        };

        RecommendedBudget b = PeriodClose.Recommend(historia, Money.FromUnits(60_000)).Value;

        Assert.Equal(Money.Sum(b.Categories.Select(c => c.Recommended)), b.Total);
    }
}
