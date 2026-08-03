using System.Diagnostics;
using Margen.Classify;
using Margen.Domain;

namespace Margen.Classify.Tests;

public sealed class RuleMatcherTests
{
    private static readonly Guid Supermercado = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Restaurantes = Guid.Parse("00000000-0000-0000-0000-000000000002");

    private static MerchantRuleSpec Rule(
        string pattern,
        MatchKind kind = MatchKind.Exact,
        bool user = true,
        int weight = 100,
        Guid? category = null) =>
        new(category ?? Supermercado, pattern, kind, user, weight);

    // ---------- Los tres modos ----------

    [Fact]
    public void la_regla_exacta_casa_con_el_nombre_entero()
    {
        Assert.True(RuleMatcher.Matches("SM NACIONAL CHARLES", Rule("SM NACIONAL CHARLES")));
    }

    [Fact]
    public void la_regla_exacta_no_casa_con_un_trozo()
    {
        Assert.False(RuleMatcher.Matches("SM NACIONAL CHARLES", Rule("SM NACIONAL")));
    }

    [Fact]
    public void la_regla_exacta_ignora_mayusculas()
    {
        // El comercio llega normalizado a mayúsculas, pero la regla la puede
        // haber escrito una persona en la app.
        Assert.True(RuleMatcher.Matches("SM NACIONAL", Rule("sm nacional")));
    }

    [Fact]
    public void contiene_casa_con_un_trozo()
    {
        Assert.True(RuleMatcher.Matches(
            "UBER EATS-WB*UBER EATS-WB", Rule("UBER EATS", MatchKind.Contains)));
    }

    [Fact]
    public void contiene_no_casa_cuando_no_esta()
    {
        Assert.False(RuleMatcher.Matches(
            "OPENAI *CHATGPT SUBSCR", Rule("UBER", MatchKind.Contains)));
    }

    [Fact]
    public void la_expresion_regular_casa()
    {
        Assert.True(RuleMatcher.Matches(
            "UBER*EATS", Rule(@"^UBER\*?\s?EATS", MatchKind.Regex)));
    }

    [Fact]
    public void un_valor_de_enum_fuera_de_la_lista_no_casa_con_nada()
    {
        // Sin rama por defecto que diga que sí. Un enum inventado no clasifica.
        Assert.False(RuleMatcher.Matches("LO QUE SEA", Rule("LO QUE SEA", (MatchKind)99)));
    }

    // ---------- Fallo cerrado ----------

    [Fact]
    public void un_patron_vacio_no_casa_con_nada()
    {
        // Casaría con todo y mandaría el buzón entero a una categoría.
        Assert.False(RuleMatcher.Matches("SM NACIONAL", Rule(string.Empty, MatchKind.Contains)));
        Assert.False(RuleMatcher.Matches("SM NACIONAL", Rule("   ", MatchKind.Contains)));
    }

    [Fact]
    public void un_patron_desmedido_no_casa()
    {
        string largo = new('A', RuleMatcher.MaxPatternLength + 1);

        Assert.False(RuleMatcher.Matches(largo, Rule(largo)));
    }

    [Fact]
    public void una_expresion_regular_mal_escrita_no_casa_y_no_lanza()
    {
        // Que una regla rota tumbe la ingesta del buzón entero sería peor que
        // ignorarla.
        Assert.False(RuleMatcher.Matches("SM NACIONAL", Rule("[sin cerrar", MatchKind.Regex)));
    }

    [Fact]
    public void una_expresion_regular_con_retroceso_catastrofico_no_cuelga()
    {
        // El patrón clásico: anidar cuantificadores sobre una entrada que casi
        // casa. Sin tiempo límite, esto no termina en la vida de nadie.
        var regla = Rule(@"^(A+)+B$", MatchKind.Regex);
        string entrada = new('A', 40);

        var reloj = Stopwatch.StartNew();
        bool casa = RuleMatcher.Matches(entrada, regla);
        reloj.Stop();

        Assert.False(casa);
        Assert.True(
            reloj.Elapsed < TimeSpan.FromSeconds(5),
            $"tardó {reloj.Elapsed}, que es colgarse con otro nombre");
    }

    // ---------- Cuál gana ----------

    [Fact]
    public void sin_reglas_no_hay_mejor()
    {
        Assert.Null(RuleMatcher.BestMatch("SM NACIONAL", []));
    }

    [Fact]
    public void cuando_ninguna_casa_no_hay_mejor()
    {
        Assert.Null(RuleMatcher.BestMatch("SM NACIONAL", [Rule("UBER EATS")]));
    }

    [Fact]
    public void gana_la_de_mas_peso()
    {
        MerchantRuleSpec? mejor = RuleMatcher.BestMatch("SM NACIONAL", [
            Rule("SM NACIONAL", weight: 10, category: Restaurantes),
            Rule("SM NACIONAL", weight: 90, category: Supermercado),
        ]);

        Assert.Equal(Supermercado, mejor!.Value.CategoryId);
    }

    [Fact]
    public void a_igual_peso_gana_la_del_usuario()
    {
        // La corrección explícita de una persona pesa más que una estadística.
        MerchantRuleSpec? mejor = RuleMatcher.BestMatch("SM NACIONAL", [
            Rule("SM NACIONAL", user: false, weight: 50, category: Restaurantes),
            Rule("SM NACIONAL", user: true, weight: 50, category: Supermercado),
        ]);

        Assert.Equal(Supermercado, mejor!.Value.CategoryId);
    }

    [Fact]
    public void el_orden_de_la_lista_no_cambia_quien_gana()
    {
        MerchantRuleSpec pesada = Rule("SM NACIONAL", weight: 90, category: Supermercado);
        MerchantRuleSpec ligera = Rule("SM NACIONAL", weight: 10, category: Restaurantes);

        Assert.Equal(
            RuleMatcher.BestMatch("SM NACIONAL", [pesada, ligera])!.Value.CategoryId,
            RuleMatcher.BestMatch("SM NACIONAL", [ligera, pesada])!.Value.CategoryId);
    }

    [Fact]
    public void una_regla_por_patron_puede_ganarle_a_una_exacta_si_pesa_mas()
    {
        // El peso es el criterio, no el modo. Es lo que permite que una regla
        // recién corregida por el usuario mande sobre las deducidas.
        MerchantRuleSpec? mejor = RuleMatcher.BestMatch("UBER EATS-WB*UBER EATS-WB", [
            Rule("UBER EATS-WB*UBER EATS-WB", weight: 10, category: Supermercado),
            Rule("UBER EATS", MatchKind.Contains, weight: 90, category: Restaurantes),
        ]);

        Assert.Equal(Restaurantes, mejor!.Value.CategoryId);
    }

    [Fact]
    public void un_comercio_nulo_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => RuleMatcher.Matches(null!, Rule("X")));
        Assert.Throws<ArgumentNullException>(() => RuleMatcher.BestMatch(null!, []));
        Assert.Throws<ArgumentNullException>(() => RuleMatcher.BestMatch("X", null!));
    }
}
