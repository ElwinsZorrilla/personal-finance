using Margen.Classify;
using Margen.Domain;

namespace Margen.Classify.Tests;

public sealed class CascadeTests
{
    private static readonly Guid Supermercado = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Restaurantes = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Transporte = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid Suscripciones = Guid.Parse("00000000-0000-0000-0000-000000000004");

    private static readonly Dictionary<string, Guid> Categorias = new(StringComparer.Ordinal)
    {
        [CategoryNames.Supermercado] = Supermercado,
        [CategoryNames.Restaurantes] = Restaurantes,
        [CategoryNames.Transporte] = Transporte,
        [CategoryNames.Suscripciones] = Suscripciones,
    };

    private static ClassificationRequest Pide(
        string comercio,
        IReadOnlyList<MerchantRuleSpec>? reglas = null,
        IReadOnlyList<Guid>? confirmadas = null,
        IReadOnlyDictionary<string, Guid>? categorias = null) =>
        new(comercio, reglas ?? [], confirmadas ?? [], categorias ?? Categorias);

    private static MerchantRuleSpec Regla(
        string patron, Guid categoria, MatchKind kind = MatchKind.Exact, int peso = 100) =>
        new(categoria, patron, kind, true, peso);

    private static Guid[] Veces(Guid id, int n) => Enumerable.Repeat(id, n).ToArray();

    /// <summary>Un modelo que siempre responde lo mismo.</summary>
    private sealed class ModeloFijo(string? respuesta) : ICategorySuggester
    {
        public int Llamadas { get; private set; }

        public IReadOnlyList<string>? UltimasCategorias { get; private set; }

        public Task<string?> SuggestAsync(
            string merchantNormalized,
            IReadOnlyList<string> categoryNames,
            CancellationToken cancellationToken)
        {
            Llamadas++;
            UltimasCategorias = categoryNames;
            return Task.FromResult(respuesta);
        }
    }

    /// <summary>Un modelo caído.</summary>
    private sealed class ModeloRoto : ICategorySuggester
    {
        public Task<string?> SuggestAsync(
            string merchantNormalized,
            IReadOnlyList<string> categoryNames,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("el proveedor no responde");
    }

    // ---------- El orden de los escalones ----------

    [Fact]
    public void la_regla_exacta_del_usuario_gana_a_todo()
    {
        // Historial en contra, tabla de palabras en contra. Manda la persona.
        Outcome<Classification> r = Cascade.Classify(Pide(
            "SM NACIONAL CHARLES",
            reglas: [Regla("SM NACIONAL CHARLES", Transporte)],
            confirmadas: Veces(Restaurantes, 10)));

        Assert.Equal(Transporte, r.Value.CategoryId);
        Assert.Equal(ClassifiedBy.UserRule, r.Value.Source);
        Assert.Equal(Cascade.UserRuleConfidence, r.Value.ConfidenceBasisPoints);
    }

    [Fact]
    public void la_regla_por_patron_va_despues_de_la_exacta_y_antes_del_historial()
    {
        Outcome<Classification> r = Cascade.Classify(Pide(
            "UBER EATS-WB*UBER EATS-WB",
            reglas: [Regla("UBER EATS", Restaurantes, MatchKind.Contains)],
            confirmadas: Veces(Transporte, 10)));

        Assert.Equal(Restaurantes, r.Value.CategoryId);
        Assert.Equal(ClassifiedBy.PatternRule, r.Value.Source);
        Assert.Equal(Cascade.PatternRuleConfidence, r.Value.ConfidenceBasisPoints);
    }

    [Fact]
    public void el_historial_gana_a_la_tabla_de_palabras()
    {
        // La tabla diría supermercado. El usuario lo ha puesto tres veces en
        // transporte, y sabe algo que la tabla no.
        Outcome<Classification> r = Cascade.Classify(Pide(
            "SM NACIONAL CHARLES",
            confirmadas: Veces(Transporte, 3)));

        Assert.Equal(Transporte, r.Value.CategoryId);
        Assert.Equal(ClassifiedBy.History, r.Value.Source);
    }

    [Fact]
    public void la_tabla_de_palabras_responde_cuando_no_hay_nada_mas()
    {
        Outcome<Classification> r = Cascade.Classify(Pide("SM NACIONAL CHARLES"));

        Assert.Equal(Supermercado, r.Value.CategoryId);
        Assert.Equal(ClassifiedBy.LocalTable, r.Value.Source);
        Assert.Equal(LocalClassifier.Confidence, r.Value.ConfidenceBasisPoints);
    }

    [Fact]
    public void cuando_nadie_responde_queda_para_revision_humana()
    {
        // Un resultado, no un fallo. Y sin categoría inventada.
        Outcome<Classification> r = Cascade.Classify(Pide("CENTRO NEOCATECUMENAL"));

        Assert.Equal(OutcomeKind.Insufficient, r.Kind);
        Assert.Throws<InvalidOperationException>(() => r.Value);
    }

    [Fact]
    public void una_peticion_nula_lanza()
    {
        Assert.Throws<ArgumentNullException>(() => Cascade.Classify(null!));
    }

    // ---------- Qué se aplica solo ----------

    [Fact]
    public void una_regla_del_usuario_se_aplica_sola()
    {
        Classification c = Cascade.Classify(Pide(
            "SM NACIONAL", reglas: [Regla("SM NACIONAL", Supermercado)])).Value;

        Assert.True(c.IsAutomatic);
    }

    [Fact]
    public void un_historial_unanime_se_aplica_solo()
    {
        Classification c = Cascade.Classify(Pide(
            "LO QUE SEA", confirmadas: Veces(Supermercado, 10))).Value;

        Assert.True(c.IsAutomatic);
        Assert.True(c.ConfidenceBasisPoints >= Cascade.AutoAssignThreshold);
    }

    [Fact]
    public void un_historial_apretado_no_se_aplica_solo()
    {
        // Siete de diez pasa el umbral del historial pero no el de aplicar sin
        // preguntar. Sugiere y espera.
        Guid[] historia = [.. Veces(Supermercado, 7), .. Veces(Restaurantes, 3)];

        Classification c = Cascade.Classify(Pide("LO QUE SEA", confirmadas: historia)).Value;

        Assert.False(c.IsAutomatic);
    }

    [Fact]
    public void la_tabla_de_palabras_no_se_aplica_sola()
    {
        // «SM NACIONAL» es un supermercado y «SM» a secas puede ser cualquier
        // cosa; la tabla no distingue. Sugiere y pide confirmación.
        Classification c = Cascade.Classify(Pide("SM NACIONAL CHARLES")).Value;

        Assert.False(c.IsAutomatic);
    }

    // ---------- El modelo ----------

    [Fact]
    public async Task el_modelo_sugiere_cuando_nadie_mas_responde()
    {
        var modelo = new ModeloFijo(CategoryNames.Suscripciones);

        Outcome<Classification> r = await Cascade.ClassifyAsync(
            Pide("CENTRO NEOCATECUMENAL"), modelo, CancellationToken.None);

        Assert.Equal(Suscripciones, r.Value.CategoryId);
        Assert.Equal(ClassifiedBy.Model, r.Value.Source);
        Assert.Equal(1, modelo.Llamadas);
    }

    [Fact]
    public async Task la_sugerencia_del_modelo_nunca_se_aplica_sola()
    {
        // La decisión principal de la fase. Un modelo que se equivoca de
        // categoría mueve dinero de un presupuesto a otro y el «cuánto puedo
        // gastar» sale mal por un motivo que nadie ve.
        var modelo = new ModeloFijo(CategoryNames.Suscripciones);

        Classification c = (await Cascade.ClassifyAsync(
            Pide("CENTRO NEOCATECUMENAL"),
            modelo,
            CancellationToken.None)).Value;

        Assert.False(c.IsAutomatic);
    }

    [Fact]
    public void bajar_el_umbral_no_deja_que_el_modelo_decida()
    {
        // Aunque alguien ponga el umbral por debajo de la confianza del modelo,
        // el origen sigue vetándolo. La defensa no depende de un solo número.
        var delModelo = new Classification(
            Supermercado, 10_000, ClassifiedBy.Model, "sugerencia");

        Assert.False(delModelo.IsAutomatic);
    }

    [Fact]
    public async Task una_regla_del_usuario_evita_la_llamada_al_modelo()
    {
        // «Una corrección del usuario genera una regla y evita la siguiente
        // llamada al modelo», literal del plan.
        var modelo = new ModeloFijo(CategoryNames.Suscripciones);

        await Cascade.ClassifyAsync(
            Pide("CENTRO NEOCATECUMENAL", reglas: [Regla("CENTRO NEOCATECUMENAL", Supermercado)]),
            modelo,
            CancellationToken.None);

        Assert.Equal(0, modelo.Llamadas);
    }

    [Fact]
    public async Task la_tabla_de_palabras_tambien_evita_la_llamada()
    {
        var modelo = new ModeloFijo(CategoryNames.Suscripciones);

        await Cascade.ClassifyAsync(
            Pide("SM NACIONAL CHARLES"), modelo, CancellationToken.None);

        Assert.Equal(0, modelo.Llamadas);
    }

    [Fact]
    public async Task una_categoria_que_el_modelo_se_invento_se_descarta()
    {
        // Un modelo que inventa una categoría inventaría también el presupuesto
        // donde meterla.
        var modelo = new ModeloFijo("Criptomonedas");

        Outcome<Classification> r = await Cascade.ClassifyAsync(
            Pide("CENTRO NEOCATECUMENAL"), modelo, CancellationToken.None);

        Assert.Equal(OutcomeKind.Insufficient, r.Kind);
    }

    [Fact]
    public async Task un_modelo_que_no_sabe_deja_el_movimiento_en_revision()
    {
        Outcome<Classification> r = await Cascade.ClassifyAsync(
            Pide("CENTRO NEOCATECUMENAL"),
            new ModeloFijo(null),
            CancellationToken.None);

        Assert.Equal(OutcomeKind.Insufficient, r.Kind);
    }

    [Fact]
    public async Task un_modelo_caido_no_bloquea_la_ingesta()
    {
        // Un proveedor externo no puede impedir que entre un movimiento.
        Outcome<Classification> r = await Cascade.ClassifyAsync(
            Pide("CENTRO NEOCATECUMENAL"),
            new ModeloRoto(),
            CancellationToken.None);

        Assert.Equal(OutcomeKind.Insufficient, r.Kind);
    }

    [Fact]
    public async Task sin_modelo_configurado_la_cascada_funciona_igual()
    {
        Outcome<Classification> r = await Cascade.ClassifyAsync(
            Pide("SM NACIONAL CHARLES"), null, CancellationToken.None);

        Assert.Equal(Supermercado, r.Value.CategoryId);
    }

    [Fact]
    public async Task sin_categorias_no_se_llama_al_modelo()
    {
        // Preguntarle a un modelo por cuál de cero categorías es una llamada
        // pagada cuya única respuesta posible es un error.
        var modelo = new ModeloFijo(CategoryNames.Supermercado);

        await Cascade.ClassifyAsync(
            Pide("CENTRO NEOCATECUMENAL", categorias: new Dictionary<string, Guid>()),
            modelo,
            CancellationToken.None);

        Assert.Equal(0, modelo.Llamadas);
    }

    [Fact]
    public async Task al_modelo_solo_le_llegan_el_comercio_y_los_nombres()
    {
        // La barrera del riesgo 3. Esta prueba comprueba lo que se puede
        // comprobar en tiempo de ejecución; lo demás lo garantiza la firma, que
        // no admite Money, ni fecha, ni cuenta.
        var modelo = new ModeloFijo(null);

        await Cascade.ClassifyAsync(
            Pide("CENTRO NEOCATECUMENAL"), modelo, CancellationToken.None);

        Assert.NotNull(modelo.UltimasCategorias);
        Assert.Equal(
            [.. Categorias.Keys.Order(StringComparer.Ordinal)],
            [.. modelo.UltimasCategorias.Order(StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task la_cancelacion_del_llamador_se_propaga()
    {
        // Cancelar no es «el modelo falló»: lo pidió quien llama, y tragárselo
        // dejaría un proceso que no se puede parar.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() => Cascade.ClassifyAsync(
            Pide("CENTRO NEOCATECUMENAL"), new ModeloCancelable(), cts.Token));
    }

    private sealed class ModeloCancelable : ICategorySuggester
    {
        public Task<string?> SuggestAsync(
            string merchantNormalized,
            IReadOnlyList<string> categoryNames,
            CancellationToken cancellationToken) =>
            Task.FromCanceled<string?>(cancellationToken);
    }

    [Fact]
    public async Task una_peticion_nula_lanza_tambien_en_la_version_con_modelo()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => Cascade.ClassifyAsync(
            null!, null, CancellationToken.None));
    }
}
