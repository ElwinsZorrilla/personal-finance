using Margen.Classify;
using Margen.Domain;

namespace Margen.Classify.Tests;

public sealed class CorrectionsTests
{
    private static readonly Guid Supermercado = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Restaurantes = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static MerchantRuleSpec Regla(
        string patron,
        Guid categoria,
        MatchKind kind = MatchKind.Exact,
        bool user = true,
        int peso = Corrections.UserWeight) =>
        new(categoria, patron, kind, user, peso);

    [Fact]
    public void la_primera_correccion_crea_la_regla()
    {
        CorrectionPlan plan = Corrections.Plan("SM NACIONAL CHARLES", Supermercado, []);

        Assert.Equal(CorrectionAction.Create, plan.Action);
        Assert.Equal("SM NACIONAL CHARLES", plan.Pattern);
        Assert.Equal(Supermercado, plan.CategoryId);
        Assert.Equal(Corrections.UserWeight, plan.Weight);
    }

    [Fact]
    public void la_segunda_correccion_del_mismo_comercio_no_apila_otra_regla()
    {
        // Con dos reglas del mismo peso y categorías distintas, cuál gana
        // depende del orden en que las devuelva la base. Eso es un resultado
        // que cambia solo.
        CorrectionPlan plan = Corrections.Plan(
            "SM NACIONAL CHARLES",
            Restaurantes,
            [Regla("SM NACIONAL CHARLES", Supermercado)]);

        Assert.Equal(CorrectionAction.Update, plan.Action);
        Assert.Equal(Restaurantes, plan.CategoryId);
    }

    [Fact]
    public void corregir_a_lo_que_ya_decia_no_toca_nada()
    {
        // Reescribirla movería su fecha y su contador sin que haya pasado nada.
        CorrectionPlan plan = Corrections.Plan(
            "SM NACIONAL CHARLES",
            Supermercado,
            [Regla("SM NACIONAL CHARLES", Supermercado)]);

        Assert.Equal(CorrectionAction.None, plan.Action);
    }

    [Fact]
    public void una_regla_de_otro_comercio_no_estorba()
    {
        CorrectionPlan plan = Corrections.Plan(
            "SM NACIONAL CHARLES",
            Supermercado,
            [Regla("UBER EATS", Restaurantes)]);

        Assert.Equal(CorrectionAction.Create, plan.Action);
    }

    [Fact]
    public void una_regla_por_patron_no_se_actualiza_al_corregir_un_comercio()
    {
        // «Contiene UBER» cubre una familia de comercios. Cambiarla porque el
        // usuario corrigió uno de ellos reclasificaría todos los demás sin que
        // nadie lo haya pedido: se crea una regla exacta que la gana por peso.
        CorrectionPlan plan = Corrections.Plan(
            "UBER EATS-WB*UBER EATS-WB",
            Supermercado,
            [Regla("UBER", Restaurantes, MatchKind.Contains)]);

        Assert.Equal(CorrectionAction.Create, plan.Action);
    }

    [Fact]
    public void una_regla_deducida_no_se_actualiza_sino_que_se_crea_la_del_usuario()
    {
        // Las deducidas pesan menos por convenio; la nueva del usuario las gana
        // en la cascada y la deducida queda como rastro de lo que se dedujo.
        CorrectionPlan plan = Corrections.Plan(
            "SM NACIONAL CHARLES",
            Supermercado,
            [Regla("SM NACIONAL CHARLES", Restaurantes, user: false, peso: 500)]);

        Assert.Equal(CorrectionAction.Create, plan.Action);
        Assert.Equal(Corrections.UserWeight, plan.Weight);
    }

    [Fact]
    public void la_regla_creada_pesa_mas_que_cualquier_deducida()
    {
        CorrectionPlan plan = Corrections.Plan("X", Supermercado, []);

        Assert.True(plan.Weight >= Corrections.UserWeight);
    }

    [Fact]
    public void la_regla_creada_gana_en_la_cascada()
    {
        // La prueba de que las dos piezas encajan: lo que produce Corrections
        // es exactamente lo que RuleMatcher elige.
        CorrectionPlan plan = Corrections.Plan("SM NACIONAL CHARLES", Restaurantes, []);

        MerchantRuleSpec? mejor = RuleMatcher.BestMatch("SM NACIONAL CHARLES", [
            Regla("SM NACIONAL CHARLES", Supermercado, user: false, peso: 500),
            new MerchantRuleSpec(plan.CategoryId, plan.Pattern, MatchKind.Exact, true, plan.Weight),
        ]);

        Assert.Equal(Restaurantes, mejor!.Value.CategoryId);
    }

    [Fact]
    public void corregir_ignora_mayusculas_al_buscar_la_regla()
    {
        CorrectionPlan plan = Corrections.Plan(
            "SM NACIONAL", Restaurantes, [Regla("sm nacional", Supermercado)]);

        Assert.Equal(CorrectionAction.Update, plan.Action);
    }

    [Fact]
    public void un_comercio_vacio_no_crea_ninguna_regla()
    {
        // Una regla con patrón vacío no casa con nada y quedaría de basura en
        // la tabla para siempre.
        Assert.Equal(
            CorrectionAction.None, Corrections.Plan(string.Empty, Supermercado, []).Action);
        Assert.Equal(
            CorrectionAction.None, Corrections.Plan("   ", Supermercado, []).Action);
    }

    [Fact]
    public void los_argumentos_nulos_lanzan()
    {
        Assert.Throws<ArgumentNullException>(() => Corrections.Plan(null!, Supermercado, []));
        Assert.Throws<ArgumentNullException>(() => Corrections.Plan("X", Supermercado, null!));
    }
}
