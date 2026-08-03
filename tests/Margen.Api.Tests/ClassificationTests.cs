using Margen.Api.Tests.Infra;
using Margen.Classify;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Margen.Infrastructure.Classification;
using Margen.Ingest;
using Margen.Worker.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Tests;

/// <summary>
/// La cascada de clasificación con la base de datos de verdad detrás.
/// </summary>
/// <remarks>
/// El motor está probado a fondo sin base en `Margen.Classify.Tests`. Aquí se
/// comprueba lo que solo se ve con tablas: qué se lee, qué se escribe, y que el
/// historial no se lea a sí mismo.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ClassificationTests(PostgresFixture postgres)
{
    private static readonly ParserRegistry Registry = new([new SampleBankParser()]);

    private MargenDbContext Db() => new(
        new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private static TransactionClassifier Classifier(MargenDbContext db) =>
        new(db, TimeProvider.System);

    private static EmailIngestor Ingestor(MargenDbContext db) =>
        new(db, Registry, TimeProvider.System, detector: null, classifier: Classifier(db));

    private static RawEmail Correo(
        string messageId,
        string comercio = "SM NACIONAL CHARLES",
        string monto = "2,450.00") => new(
        messageId,
        "alertas@banco-de-muestra.do",
        "Compra aprobada",
        $"Monto: RD$ {monto}\nTarjeta: ****1234\nComercio: {comercio}\n"
        + $"Fecha: 15/03/2026 14:30\nReferencia: {messageId}",
        new DateTime(2026, 3, 15, 18, 35, 0, DateTimeKind.Utc));

    // ---------- La ingesta clasifica ----------

    [Fact]
    public async Task un_comercio_que_la_tabla_reconoce_entra_con_categoria_pero_sigue_en_revision()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        IngestReport report = await Ingestor(db).IngestAsync(Correo("<a@x.do>"), default);

        Transaction tx = await db.Transactions
            .AsNoTracking().SingleAsync(t => t.Id == report.TransactionId);

        Assert.Equal(seed.MarketId, tx.CategoryId);
        Assert.Equal(LocalClassifier.Confidence, tx.ConfidenceBasisPoints);
        Assert.Equal(nameof(ClassifiedBy.LocalTable), tx.ClassificationSource);

        // Sugerida, no decidida: la revisión llega con la respuesta escrita y
        // se confirma con un toque.
        Assert.Equal(TxStatus.NeedsReview, tx.Status);
        Assert.Null(tx.CategoryConfirmedAt);
    }

    [Fact]
    public async Task un_comercio_desconocido_entra_sin_categoria()
    {
        await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        IngestReport report = await Ingestor(db)
            .IngestAsync(Correo("<b@x.do>", comercio: "CENTRO NEOCATECUMENAL"), default);

        Transaction tx = await db.Transactions
            .AsNoTracking().SingleAsync(t => t.Id == report.TransactionId);

        // Fallo cerrado: sin respuesta no se inventa una categoría.
        Assert.Null(tx.CategoryId);
        Assert.Equal(0, tx.ConfidenceBasisPoints);
        Assert.Equal(TxStatus.NeedsReview, tx.Status);
    }

    [Fact]
    public async Task una_regla_del_usuario_clasifica_sin_preguntar()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        db.MerchantRules.Add(new MerchantRule
        {
            Id = Guid.CreateVersion7(),
            Pattern = "SM NACIONAL CHARLES",
            MatchKind = MatchKind.Exact,
            CategoryId = seed.FunId,
            Weight = Corrections.UserWeight,
            IsUserDefined = true,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        IngestReport report = await Ingestor(db).IngestAsync(Correo("<c@x.do>"), default);

        Transaction tx = await db.Transactions
            .AsNoTracking().SingleAsync(t => t.Id == report.TransactionId);

        // La regla gana a la tabla de palabras, y sale de revisión sola.
        Assert.Equal(seed.FunId, tx.CategoryId);
        Assert.Equal(TxStatus.Posted, tx.Status);
        Assert.Equal(Cascade.UserRuleConfidence, tx.ConfidenceBasisPoints);
    }

    // ---------- El historial no es un eco ----------

    [Fact]
    public async Task diez_clasificaciones_automaticas_no_cuentan_como_historial()
    {
        // **El defecto que esta columna existe para impedir.** Sin filtrar por
        // confirmadas, la tabla de palabras clasifica diez movimientos, el
        // historial los lee como confirmación y el error queda fijado con
        // confianza alta sin que nadie pueda ver de dónde salió.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        for (int i = 0; i < 10; i++)
        {
            await Ingestor(db).IngestAsync(Correo($"<eco{i}@x.do>", monto: $"{i + 1},000.00"), default);
        }

        Outcome<Classification> resultado = await Classifier(db)
            .ClassifyAsync("SM NACIONAL CHARLES", default);

        // Sigue respondiendo la tabla, no el historial, y sigue sin aplicarse
        // sola. Diez repeticiones de una suposición no la convierten en un dato.
        Assert.Equal(ClassifiedBy.LocalTable, resultado.Value.Source);
        Assert.False(resultado.Value.IsAutomatic);
    }

    [Fact]
    public async Task tres_confirmaciones_de_una_persona_si_cuentan_como_historial()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        for (int i = 0; i < 3; i++)
        {
            IngestReport r = await Ingestor(db)
                .IngestAsync(Correo($"<ok{i}@x.do>", monto: $"{i + 1},000.00"), default);

            Transaction tx = await db.Transactions.SingleAsync(t => t.Id == r.TransactionId);
            tx.CategoryId = seed.FunId;
            tx.CategoryConfirmedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        Outcome<Classification> resultado = await Classifier(db)
            .ClassifyAsync("SM NACIONAL CHARLES", default);

        Assert.Equal(ClassifiedBy.History, resultado.Value.Source);
        Assert.Equal(seed.FunId, resultado.Value.CategoryId);
    }

    // ---------- La corrección enseña ----------

    [Fact]
    public async Task una_correccion_crea_la_regla()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        var writer = new RuleWriter(db, TimeProvider.System);
        CorrectionAction accion = await writer
            .LearnAsync("SM NACIONAL CHARLES", seed.FunId, default);
        await db.SaveChangesAsync();

        Assert.Equal(CorrectionAction.Create, accion);

        MerchantRule regla = await db.MerchantRules
            .AsNoTracking().SingleAsync(r => r.Pattern == "SM NACIONAL CHARLES");

        Assert.Equal(seed.FunId, regla.CategoryId);
        Assert.True(regla.IsUserDefined);
        Assert.Equal(MatchKind.Exact, regla.MatchKind);
    }

    [Fact]
    public async Task corregir_dos_veces_el_mismo_comercio_deja_una_sola_regla()
    {
        // Con dos reglas del mismo peso y categorías distintas, cuál gana
        // depende del orden en que las devuelva la base: un resultado que
        // cambia solo.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        var writer = new RuleWriter(db, TimeProvider.System);

        await writer.LearnAsync("SM NACIONAL CHARLES", seed.FoodId, default);
        await db.SaveChangesAsync();

        CorrectionAction segunda = await writer
            .LearnAsync("SM NACIONAL CHARLES", seed.FunId, default);
        await db.SaveChangesAsync();

        Assert.Equal(CorrectionAction.Update, segunda);

        List<MerchantRule> reglas = await db.MerchantRules
            .AsNoTracking().Where(r => r.Pattern == "SM NACIONAL CHARLES").ToListAsync();

        Assert.Single(reglas);
        Assert.Equal(seed.FunId, reglas[0].CategoryId);
    }

    [Fact]
    public async Task corregir_a_lo_que_ya_decia_no_escribe_nada()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        var writer = new RuleWriter(db, TimeProvider.System);
        await writer.LearnAsync("SM NACIONAL CHARLES", seed.FunId, default);
        await db.SaveChangesAsync();

        CorrectionAction repetida = await writer
            .LearnAsync("SM NACIONAL CHARLES", seed.FunId, default);

        Assert.Equal(CorrectionAction.None, repetida);
    }

    [Fact]
    public async Task la_regla_aprendida_clasifica_el_siguiente_movimiento()
    {
        // El criterio de la fase de punta a punta: se corrige una vez y la
        // siguiente compra en el mismo sitio ya no pregunta.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        await new RuleWriter(db, TimeProvider.System)
            .LearnAsync("SM NACIONAL CHARLES", seed.FunId, default);
        await db.SaveChangesAsync();

        IngestReport report = await Ingestor(db).IngestAsync(Correo("<d@x.do>"), default);

        Transaction tx = await db.Transactions
            .AsNoTracking().SingleAsync(t => t.Id == report.TransactionId);

        Assert.Equal(seed.FunId, tx.CategoryId);
        Assert.Equal(TxStatus.Posted, tx.Status);
    }

    [Fact]
    public async Task un_movimiento_ya_confirmado_no_se_reclasifica()
    {
        // Reclasificar sobre la decisión de una persona es deshacerla.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        var tx = new Transaction
        {
            Id = Guid.CreateVersion7(),
            AccountId = seed.CheckingId,
            MerchantRaw = "SM NACIONAL CHARLES",
            MerchantNormalized = "SM NACIONAL CHARLES",
            Amount = Money.FromUnits(500),
            OccurredAt = DateTime.UtcNow,
            Kind = TxKind.Purchase,
            Status = TxStatus.Posted,
            Source = TxSource.Manual,
            CategoryId = seed.FunId,
            CategoryConfirmedAt = DateTime.UtcNow,
            Fingerprint = $"confirmado-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        Outcome<Classification> resultado = await Classifier(db).ApplyAsync(tx, default);

        Assert.Equal(OutcomeKind.Insufficient, resultado.Kind);
        Assert.Equal(seed.FunId, tx.CategoryId);
    }

    // ---------- Anomalías ----------

    [Fact]
    public async Task un_cargo_muy_por_encima_de_lo_normal_genera_una_alerta()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        DateTime ahora = DateTime.UtcNow;

        // Cinco cargos normales y uno desmedido, todos dentro de la ventana.
        for (int i = 0; i < 5; i++)
        {
            db.Transactions.Add(Cargo(seed, Money.FromUnits(300), ahora.AddDays(-3).AddMinutes(i)));
        }

        db.Transactions.Add(Cargo(seed, Money.FromUnits(40_000), ahora.AddHours(-1)));
        await db.SaveChangesAsync();

        ScanReport report = await new AnomalyScanner(db, TimeProvider.System).ScanAsync(default);

        Assert.Equal(1, report.Created);

        Alert alerta = await db.Alerts
            .AsNoTracking().SingleAsync(a => a.Kind == AlertKind.UnusualAmount);

        Assert.StartsWith("unusual:", alerta.DedupeKey, StringComparison.Ordinal);
        Assert.NotNull(alerta.TransactionId);
    }

    [Fact]
    public async Task mirar_dos_veces_no_duplica_la_alerta()
    {
        // El worker corre cada pocos minutos. Sin esto, la pantalla de revisión
        // se llena de la misma alerta y se deja de leer.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        DateTime ahora = DateTime.UtcNow;
        for (int i = 0; i < 5; i++)
        {
            db.Transactions.Add(Cargo(seed, Money.FromUnits(300), ahora.AddDays(-3).AddMinutes(i)));
        }

        db.Transactions.Add(Cargo(seed, Money.FromUnits(40_000), ahora.AddHours(-1)));
        await db.SaveChangesAsync();

        var scanner = new AnomalyScanner(db, TimeProvider.System);

        ScanReport primera = await scanner.ScanAsync(default);
        ScanReport segunda = await scanner.ScanAsync(default);

        Assert.Equal(1, primera.Created);
        Assert.Equal(0, segunda.Created);
        Assert.Equal(1, await db.Alerts.CountAsync(a => a.Kind == AlertKind.UnusualAmount));
    }

    [Fact]
    public async Task sin_historial_suficiente_no_hay_alerta_de_gasto_inusual()
    {
        // El camino de los primeros meses: silencioso.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        db.Transactions.Add(Cargo(seed, Money.FromUnits(300), DateTime.UtcNow.AddDays(-2)));
        db.Transactions.Add(Cargo(seed, Money.FromUnits(40_000), DateTime.UtcNow.AddHours(-1)));
        await db.SaveChangesAsync();

        ScanReport report = await new AnomalyScanner(db, TimeProvider.System).ScanAsync(default);

        Assert.Equal(0, report.Created);
    }

    [Fact]
    public async Task un_cargo_viejo_no_dispara_alertas_al_importar_historia()
    {
        // Al importar un año de movimientos, cada cargo antiguo dispararía su
        // alerta y la revisión nacería con trescientas. Nadie lee trescientas:
        // se borran de golpe y con ellas la que importaba.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        DateTime viejo = DateTime.UtcNow.AddDays(-AnomalyScanner.RecentDays - 30);

        for (int i = 0; i < 5; i++)
        {
            db.Transactions.Add(Cargo(seed, Money.FromUnits(300), viejo.AddMinutes(i)));
        }

        db.Transactions.Add(Cargo(seed, Money.FromUnits(40_000), viejo.AddDays(1)));
        await db.SaveChangesAsync();

        ScanReport report = await new AnomalyScanner(db, TimeProvider.System).ScanAsync(default);

        Assert.Equal(0, report.Created);
    }

    [Fact]
    public async Task un_recurrente_vencido_y_sin_cargo_genera_alerta()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        RecurringPayment alquiler = await db.RecurringPayments.FirstAsync();
        alquiler.NextDueDate = DateOnly.FromDateTime(DateTime.UtcNow)
            .AddDays(-(Anomalies.GraceDays + 5));
        alquiler.LastPaidDate = null;
        await db.SaveChangesAsync();

        ScanReport report = await new AnomalyScanner(db, TimeProvider.System).ScanAsync(default);

        Assert.Equal(1, report.Created);

        Alert alerta = await db.Alerts
            .AsNoTracking().SingleAsync(a => a.Kind == AlertKind.MissingRecurring);

        Assert.True(alerta.IsUrgent);
        Assert.Equal(alquiler.Id, alerta.RecurringPaymentId);
    }

    [Fact]
    public async Task un_recurrente_recien_vencido_no_avisa_todavia()
    {
        // El banco no notifica el mismo día.
        await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        RecurringPayment alquiler = await db.RecurringPayments.FirstAsync();
        alquiler.NextDueDate = DateOnly.FromDateTime(DateTime.UtcNow);
        alquiler.LastPaidDate = null;
        await db.SaveChangesAsync();

        ScanReport report = await new AnomalyScanner(db, TimeProvider.System).ScanAsync(default);

        Assert.Equal(0, report.Created);
    }

    [Fact]
    public async Task una_suscripcion_que_cobra_de_mas_genera_alerta()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using MargenDbContext db = Db();

        DateOnly vencimiento = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        RecurringPayment alquiler = await db.RecurringPayments.FirstAsync();
        alquiler.NextDueDate = vencimiento;
        alquiler.LastPaidDate = vencimiento;

        // Se esperaba 25 000 y se cobró 30 000: un 20 %.
        db.Transactions.Add(new Transaction
        {
            Id = Guid.CreateVersion7(),
            AccountId = seed.CheckingId,
            CategoryId = seed.RentId,
            MerchantRaw = "INMOBILIARIA",
            MerchantNormalized = "INMOBILIARIA",
            Amount = Money.FromUnits(30_000),
            OccurredAt = DateTime.UtcNow.AddHours(-2),
            Kind = TxKind.Purchase,
            Status = TxStatus.Posted,
            Source = TxSource.Email,
            Direction = TxDirection.Outflow,
            Fingerprint = $"alquiler-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        await new AnomalyScanner(db, TimeProvider.System).ScanAsync(default);

        Alert alerta = await db.Alerts
            .AsNoTracking().SingleAsync(a => a.Kind == AlertKind.SubscriptionChange);

        Assert.True(alerta.IsUrgent);
        Assert.Contains("+20 %", alerta.Detail, StringComparison.Ordinal);
    }

    private static Transaction Cargo(Seed seed, Money amount, DateTime when) => new()
    {
        Id = Guid.CreateVersion7(),
        AccountId = seed.CheckingId,
        MerchantRaw = "SM NACIONAL CHARLES",
        MerchantNormalized = "SM NACIONAL CHARLES",
        Amount = amount,
        OccurredAt = when,
        Kind = TxKind.Purchase,
        Status = TxStatus.Posted,
        Source = TxSource.Email,
        Direction = TxDirection.Outflow,
        Fingerprint = $"cargo-{Guid.NewGuid():N}",
        CreatedAt = when,
        UpdatedAt = when,
    };
}
