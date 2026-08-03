using Margen.Classify;

namespace Margen.Classify.Tests;

public sealed class LocalClassifierTests
{
    // ---------- Los comercios reales de la Fase 7 ----------

    [Theory]
    [InlineData("SM NACIONAL CHARLES", CategoryNames.Supermercado)]
    [InlineData("UBER EATS-WB*UBER EATS-WB", CategoryNames.Restaurantes)]
    [InlineData("UBER*EATS", CategoryNames.Restaurantes)]
    [InlineData("OPENAI *CHATGPT SUBSCR", CategoryNames.Suscripciones)]
    [InlineData("BANCO POPULAR DO", CategoryNames.Efectivo)]
    [InlineData("BANCO POPULAR OF.CHAR DE", CategoryNames.Efectivo)]
    public void los_comercios_que_escribio_el_banco_se_reconocen(string comercio, string esperada)
    {
        // Estos seis salieron de las dieciséis muestras reales, tal cual los
        // escribe el Popular. Si la tabla no acierta con estos, no acierta con
        // nada de lo que este usuario gasta.
        Assert.Equal(esperada, LocalClassifier.Suggest(comercio));
    }

    // ---------- La palabra más larga ----------

    [Fact]
    public void uber_eats_es_comida_y_no_transporte()
    {
        // Las dos palabras casan. Gana la más larga, no la primera de la tabla.
        Assert.Equal(CategoryNames.Restaurantes, LocalClassifier.Suggest("UBER EATS"));
    }

    [Fact]
    public void uber_a_secas_sigue_siendo_transporte()
    {
        Assert.Equal(CategoryNames.Transporte, LocalClassifier.Suggest("UBER TRIP HELP.UBER.COM"));
    }

    [Fact]
    public void el_asterisco_con_que_el_banco_pega_las_palabras_no_estorba()
    {
        // `UBER*EATS` es literal de una muestra. Sin aplanar, no contiene
        // «UBER EATS» y la cena se iba a transporte.
        Assert.Equal(LocalClassifier.Suggest("UBER EATS"), LocalClassifier.Suggest("UBER*EATS"));
    }

    [Fact]
    public void solo_casan_palabras_enteras()
    {
        // «ATM» dentro de «ATMOSFERA» no es un cajero.
        Assert.Null(LocalClassifier.Suggest("ATMOSFERA LOUNGE"));
    }

    [Fact]
    public void una_palabra_de_la_tabla_con_simbolo_tambien_casa()
    {
        // `APPLE.COM` lleva punto, y el comercio real trae más cosas detrás.
        Assert.Equal(
            CategoryNames.Suscripciones, LocalClassifier.Suggest("APPLE.COM/BILL ITUNES"));
    }

    [Fact]
    public void el_criterio_de_la_palabra_mas_larga_no_depende_del_orden_de_la_tabla()
    {
        // La prueba de arriba pasaría igual con una tabla ordenada a mano. Esta
        // dice por qué no hace falta ordenarla: la longitud es el criterio.
        Assert.Equal(
            LocalClassifier.Suggest("UBER EATS"),
            LocalClassifier.Suggest("PAGO EN UBER EATS SANTO DOMINGO"));
    }

    // ---------- Cuando no sabe ----------

    [Fact]
    public void un_comercio_desconocido_no_recibe_categoria()
    {
        // Nulo y no una categoría cualquiera. Una tabla de palabras que
        // responde siempre es una tabla que no sirve para nada.
        Assert.Null(LocalClassifier.Suggest("CENTRO NEOCATECUMENAL"));
    }

    [Fact]
    public void un_comercio_vacio_no_recibe_categoria()
    {
        Assert.Null(LocalClassifier.Suggest(string.Empty));
    }

    [Fact]
    public void un_comercio_nulo_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => LocalClassifier.Suggest(null!));
    }

    [Fact]
    public void reconoce_sin_importar_mayusculas()
    {
        Assert.Equal(CategoryNames.Supermercado, LocalClassifier.Suggest("sm nacional charles"));
    }

    // ---------- La confianza ----------

    [Fact]
    public void la_confianza_del_clasificador_local_esta_por_debajo_del_umbral()
    {
        // Es lo que hace que sugiera en vez de decidir. Si alguien la sube por
        // encima del umbral, el clasificador empieza a clasificar solo comercios
        // que reconoce a medias, y esta prueba se pone roja.
        Assert.True(LocalClassifier.Confidence < Cascade.AutoAssignThreshold);
    }
}
