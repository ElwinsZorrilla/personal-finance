using Margen.Domain;
using Margen.Ingest;

namespace Margen.Ingest.Tests;

/// <summary>
/// La frase del Atajo de iOS.
/// </summary>
/// <remarks>
/// Las frases de abajo son las formas en que una persona escribe o dicta un
/// gasto de efectivo en la República Dominicana. Cada una que falle es un gasto
/// que no se registra, y un gasto que no se registra infla el «cuánto puedo
/// gastar» de la pantalla.
/// </remarks>
public sealed class CashPhraseTests
{
    private static CashEntry Leer(string frase)
    {
        PhraseResult r = CashPhrase.Parse(frase);
        Assert.True(r.Ok, $"no se entendió «{frase}»: {r.Message}");
        return r.Entry!.Value;
    }

    // ---------- Las formas reales ----------

    [Theory]
    [InlineData("Gasté 450 pesos en almuerzo", 45_000, "almuerzo")]
    [InlineData("gaste 450 pesos en almuerzo", 45_000, "almuerzo")]
    [InlineData("450 en almuerzo", 45_000, "almuerzo")]
    [InlineData("Gasté RD$450 en el almuerzo", 45_000, "almuerzo")]
    [InlineData("Gasté RD$ 450 en almuerzo", 45_000, "almuerzo")]
    [InlineData("pagué 450 de almuerzo", 45_000, "almuerzo")]
    [InlineData("compré 450 pesos de pan", 45_000, "pan")]
    [InlineData("almuerzo 450", 45_000, "almuerzo")]
    [InlineData("Gasté 1,200 pesos en gasolina", 120_000, "gasolina")]
    [InlineData("Gasté 1,200.50 en gasolina", 120_050, "gasolina")]
    [InlineData("$350 en pasaje", 35_000, "pasaje")]
    [InlineData("Gasté 75 pesos en el colmado de la esquina", 7_500, "colmado esquina")]
    public void las_frases_de_todos_los_dias_se_entienden(
        string frase, long centavos, string descripcion)
    {
        CashEntry entrada = Leer(frase);

        Assert.Equal(new Money(centavos), entrada.Amount);
        Assert.Equal(descripcion, entrada.Description, ignoreCase: true);
    }

    // ---------- El monto, sin coma flotante ----------

    [Fact]
    public void un_decimal_de_un_digito_son_decimas()
    {
        // «450.5» son 450 pesos con 50 centavos, no con 5.
        Assert.Equal(new Money(45_050), Leer("450.5 en almuerzo").Amount);
    }

    [Fact]
    public void dos_decimales_se_leen_enteros()
    {
        Assert.Equal(new Money(45_007), Leer("450.07 en almuerzo").Amount);
    }

    [Fact]
    public void el_separador_de_millares_no_se_confunde_con_el_decimal()
    {
        // Aquí la coma separa millares y el punto los decimales. Al revés,
        // «1,200» serían 1,20 pesos.
        Assert.Equal(new Money(120_000), Leer("1,200 en gasolina").Amount);
    }

    [Fact]
    public void varios_millares_seguidos_tambien()
    {
        Assert.Equal(new Money(1_234_567_00), Leer("1,234,567 en carro").Amount);
    }

    [Fact]
    public void el_monto_se_construye_entero_y_no_pierde_centavos()
    {
        // Leer «1234.56» con double y multiplicar por cien da con frecuencia
        // 123455,999... y el truncado se come un centavo. Aquí no hay double.
        Assert.Equal(new Money(123_456), Leer("1,234.56 en algo").Amount);
    }

    [Fact]
    public void un_monto_desmedido_no_se_envuelve()
    {
        // Sin `checked`, un número enorme por cien da un negativo silencioso.
        PhraseResult r = CashPhrase.Parse("999999999999999999999 en almuerzo");

        Assert.False(r.Ok);
    }

    // ---------- Cuál de los números es el monto ----------

    [Fact]
    public void gana_el_numero_que_lleva_la_moneda_pegada()
    {
        // Quedarse siempre con el primero habría registrado **2 pesos** por una
        // frase perfectamente normal. Y es el peor fallo posible de esta fase:
        // el gasto existe, la descripción es correcta, la categoría es correcta
        // y la cifra está mal, así que no aparece hasta que alguien cuadra a
        // mano semanas después.
        Assert.Equal(new Money(2_500), Leer("compré 2 panes de 25 pesos").Amount);
    }

    [Fact]
    public void la_moneda_delante_del_numero_tambien_lo_elige()
    {
        Assert.Equal(new Money(2_500), Leer("compré 2 panes de RD$25").Amount);
    }

    [Fact]
    public void sin_ninguna_marca_de_moneda_gana_el_primero()
    {
        // Es lo que dicen las dos formas más comunes: «450 en almuerzo» y
        // «almuerzo 450». La regla no adivina en ningún caso.
        Assert.Equal(new Money(45_000), Leer("450 en almuerzo para 2").Amount);
    }

    [Fact]
    public void con_la_moneda_en_el_primero_sigue_ganando_el_primero()
    {
        Assert.Equal(new Money(45_000), Leer("gasté 450 pesos en 2 cervezas").Amount);
    }

    [Fact]
    public void con_dos_numeros_marcados_gana_el_primero_marcado()
    {
        Assert.Equal(new Money(45_000), Leer("450 pesos de almuerzo y 50 pesos de pasaje").Amount);
    }

    // ---------- El día ----------

    [Fact]
    public void hoy_es_el_dia_de_hoy()
    {
        Assert.Equal(0, Leer("Gasté 450 en almuerzo").DayOffset);
        Assert.Equal(0, Leer("Gasté 450 hoy en almuerzo").DayOffset);
    }

    [Fact]
    public void ayer_resta_un_dia()
    {
        Assert.Equal(-1, Leer("Gasté 450 ayer en almuerzo").DayOffset);
    }

    [Fact]
    public void ayer_no_se_queda_en_la_descripcion()
    {
        Assert.Equal("almuerzo", Leer("Gasté 450 ayer en almuerzo").Description, ignoreCase: true);
    }

    // ---------- Fallo cerrado ----------

    [Fact]
    public void sin_monto_no_se_crea_nada_y_se_dice_que_falta()
    {
        PhraseResult r = CashPhrase.Parse("gasté en almuerzo");

        Assert.False(r.Ok);
        Assert.Equal(PhraseProblem.NoAmount, r.Problem);
        Assert.Contains("monto", r.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void un_numero_en_palabras_se_rechaza_en_vez_de_adivinarse()
    {
        // El dictado de iOS convierte «mil» en «1000», así que el caso real
        // llega en dígitos. Media implementación —«mil» sí y «mil doscientos»
        // no— enseñaría que funciona para fallar un día cualquiera.
        PhraseResult r = CashPhrase.Parse("Gasté mil pesos en almuerzo");

        Assert.False(r.Ok);
        Assert.Equal(PhraseProblem.NoAmount, r.Problem);
    }

    [Fact]
    public void sin_descripcion_no_se_crea_nada()
    {
        PhraseResult r = CashPhrase.Parse("gasté 450 pesos");

        Assert.False(r.Ok);
        Assert.Equal(PhraseProblem.NoDescription, r.Problem);
    }

    [Fact]
    public void una_frase_vacia_se_rechaza()
    {
        Assert.Equal(PhraseProblem.Empty, CashPhrase.Parse("").Problem);
        Assert.Equal(PhraseProblem.Empty, CashPhrase.Parse("   ").Problem);
        Assert.Equal(PhraseProblem.Empty, CashPhrase.Parse(null).Problem);
    }

    [Fact]
    public void un_monto_de_cero_se_rechaza()
    {
        PhraseResult r = CashPhrase.Parse("gasté 0 pesos en almuerzo");

        Assert.False(r.Ok);
        Assert.Equal(PhraseProblem.AmountNotPositive, r.Problem);
    }

    [Fact]
    public void una_frase_desmedida_se_rechaza()
    {
        PhraseResult r = CashPhrase.Parse("450 en " + new string('a', CashPhrase.MaxLength));

        Assert.False(r.Ok);
        Assert.Equal(PhraseProblem.TooLong, r.Problem);
    }

    // ---------- Moneda ----------

    [Theory]
    [InlineData("Gasté 20 dólares en el aeropuerto")]
    [InlineData("Gasté 20 dolares en el aeropuerto")]
    [InlineData("Gasté US$20 en el aeropuerto")]
    [InlineData("Gasté 20 USD en el aeropuerto")]
    [InlineData("Gasté 20 euros en el aeropuerto")]
    public void la_moneda_extranjera_se_rechaza_en_vez_de_guardarse_como_pesos(string frase)
    {
        // Veinte dólares no son veinte pesos. No hay tasa de cambio en el
        // sistema y elegir una sería inventarse una cifra que después se resta
        // del dinero disponible.
        PhraseResult r = CashPhrase.Parse(frase);

        Assert.False(r.Ok);
        Assert.Equal(PhraseProblem.ForeignCurrency, r.Problem);
    }

    [Fact]
    public void el_signo_de_peso_suelto_es_peso_dominicano()
    {
        // Aquí un precio con `$` está en pesos. Rechazarlo dejaría fuera la
        // forma más corta de escribir un gasto.
        Assert.Equal(new Money(35_000), Leer("$350 en pasaje").Amount);
    }

    // ---------- La descripción ----------

    [Fact]
    public void la_descripcion_conserva_las_palabras_de_la_persona()
    {
        // No se normaliza aquí: eso lo hace la ingesta. Lo que escribió la
        // persona es lo que ve luego en la lista.
        Assert.Equal("almuerzo con Ana", Leer("Gasté 450 en almuerzo con Ana").Description);
    }

    [Fact]
    public void la_descripcion_puede_ir_delante_del_monto()
    {
        Assert.Equal("pasaje", Leer("pasaje 50").Description, ignoreCase: true);
    }

    [Fact]
    public void cuando_hay_texto_a_los_dos_lados_gana_el_de_la_derecha()
    {
        // Es la forma que sale del dictado: verbo, monto y en qué.
        Assert.Equal("almuerzo", Leer("Gasté 450 en almuerzo").Description, ignoreCase: true);
    }
}
