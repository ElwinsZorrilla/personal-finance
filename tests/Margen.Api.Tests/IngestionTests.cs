using Margen.Api.Tests.Infra;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Margen.Ingest;
using Margen.Worker.Ingestion;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Tests;

/// <summary>
/// La tubería de ingesta contra PostgreSQL real.
/// </summary>
/// <remarks>
/// Los índices únicos son la última palabra sobre si algo está duplicado, y un
/// proveedor en memoria no los tiene. Estas pruebas no dirían nada sin base de
/// verdad.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class IngestionTests(PostgresFixture postgres)
{
    private static readonly ParserRegistry Registry = new([new SampleBankParser()]);

    private MargenDbContext Db() => new(
        new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private static EmailIngestor Ingestor(MargenDbContext db) =>
        new(db, Registry, TimeProvider.System);

    /// <summary>Correo sintético del banco que no existe. Ver `SampleBankParser`.</summary>
    private static RawEmail Correo(
        string messageId = "<uno@banco-de-muestra.do>",
        string monto = "2,450.00",
        string comercio = "SUPERMERCADO NACIONAL",
        string fecha = "15/03/2026 23:30",
        string tarjeta = "1234",
        string asunto = "Compra aprobada") => new(
        messageId,
        "alertas@banco-de-muestra.do",
        asunto,
        $"Monto: RD$ {monto}\nTarjeta: ****{tarjeta}\nComercio: {comercio}\n"
        + $"Fecha: {fecha}\nReferencia: ABC-1",
        new DateTime(2026, 3, 16, 3, 35, 0, DateTimeKind.Utc));

    private async Task<Seed> ArrangeAsync() =>
        await Seed.PlantAsync(postgres.ConnectionString);

    [Fact]
    public async Task un_correo_del_banco_crea_un_movimiento()
    {
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        IngestReport report = await Ingestor(db).IngestAsync(Correo(), default);

        Assert.Equal(IngestOutcome.Created, report.Outcome);
        Assert.NotNull(report.TransactionId);

        Transaction tx = await db.Transactions
            .AsNoTracking()
            .SingleAsync(t => t.Id == report.TransactionId);

        Assert.Equal(new Money(245_000), tx.Amount);
        Assert.Equal(TxKind.Purchase, tx.Kind);
        Assert.Equal(TxSource.Email, tx.Source);

        // Nace sin clasificar y en revisión: la categoría es la Fase 8.
        Assert.Equal(TxStatus.NeedsReview, tx.Status);
        Assert.Null(tx.CategoryId);
        Assert.Equal(0, tx.ConfidenceBasisPoints);

        // Las 23:30 hora local del 15 son las 03:30 UTC del 16.
        Assert.Equal(new DateTime(2026, 3, 16, 3, 30, 0, DateTimeKind.Utc), tx.OccurredAt);
    }

    [Fact]
    public async Task reprocesar_el_mismo_correo_dos_veces_no_crea_dos_movimientos()
    {
        // El criterio de la fase. El worker corre cada pocos minutos y el buzón
        // devuelve lo mismo.
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        IngestReport primera = await Ingestor(db).IngestAsync(Correo(), default);
        IngestReport segunda = await Ingestor(db).IngestAsync(Correo(), default);

        Assert.Equal(IngestOutcome.Created, primera.Outcome);
        Assert.Equal(IngestOutcome.AlreadySeen, segunda.Outcome);

        Assert.Equal(1, await db.Transactions.CountAsync());
        Assert.Equal(1, await db.IncomingEmails.CountAsync());
    }

    [Fact]
    public async Task el_mismo_cuerpo_con_otro_identificador_tampoco_duplica()
    {
        // El servidor de correo reescribe el Message-ID al reenviar. El cuerpo
        // es el mismo, y el hash del cuerpo lo detecta.
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        await Ingestor(db).IngestAsync(Correo(messageId: "<a@x>"), default);
        IngestReport segunda = await Ingestor(db).IngestAsync(Correo(messageId: "<b@x>"), default);

        Assert.Equal(IngestOutcome.AlreadySeen, segunda.Outcome);
        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task dos_correos_distintos_de_la_misma_compra_dan_un_solo_movimiento()
    {
        // La notificación y su confirmación: identificadores y cuerpos
        // distintos, misma compra. Lo detecta la huella del movimiento.
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        await Ingestor(db).IngestAsync(Correo(messageId: "<a@x>"), default);

        var confirmacion = new RawEmail(
            "<b@x>",
            "alertas@banco-de-muestra.do",
            "Compra aprobada",
            "Monto: RD$ 2,450.00\nTarjeta: ****1234\nComercio: SUPERMERCADO NACIONAL\n"
            // La misma referencia: el banco repite la suya cuando confirma la
            // misma compra. Es lo que la distingue de un cargo nuevo.
            + "Fecha: 15/03/2026 23:30\nReferencia: ABC-1\nConfirmacion definitiva",
            new DateTime(2026, 3, 16, 3, 40, 0, DateTimeKind.Utc));

        IngestReport segunda = await Ingestor(db).IngestAsync(confirmacion, default);

        Assert.Equal(IngestOutcome.DuplicateExact, segunda.Outcome);
        Assert.Equal(1, await db.Transactions.CountAsync());

        // Los dos correos sí quedan guardados: son dos correos reales.
        Assert.Equal(2, await db.IncomingEmails.CountAsync());
    }

    [Fact]
    public async Task dos_cargos_iguales_con_minutos_de_diferencia_van_a_revision()
    {
        // Podrían ser un duplicado del banco o dos cafés seguidos. Se crea
        // marcado y decide una persona: descartarlo perdería un gasto real.
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        await Ingestor(db).IngestAsync(Correo(messageId: "<a@x>"), default);

        // Dos cargos reales llevan referencias distintas: es lo que los
        // distingue de un duplicado del banco, que repite la suya.
        var segundoCafe = new RawEmail(
            "<b@x>",
            "alertas@banco-de-muestra.do",
            "Compra aprobada",
            """
            Monto: RD$ 2,450.00
            Tarjeta: ****1234
            Comercio: SUPERMERCADO NACIONAL
            Fecha: 15/03/2026 23:33
            Referencia: XYZ-9
            """,
            new DateTime(2026, 3, 16, 3, 38, 0, DateTimeKind.Utc));

        IngestReport segunda = await Ingestor(db).IngestAsync(segundoCafe, default);

        Assert.Equal(IngestOutcome.DuplicateProbable, segunda.Outcome);

        Transaction tx = await db.Transactions
            .AsNoTracking()
            .SingleAsync(t => t.Id == segunda.TransactionId);

        Assert.Equal(TxStatus.Duplicate, tx.Status);
        Assert.NotNull(tx.DuplicateOfTransactionId);

        // Marcado como duplicado, no cuenta para el gasto.
        Assert.False(tx.AffectsSpending);
    }

    [Fact]
    public async Task un_correo_que_ningun_parser_reconoce_no_crea_nada()
    {
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        var ajeno = new RawEmail(
            "<x@otro.do>",
            "boletin@otro.do",
            "Nuestras ofertas de la semana",
            "Aproveche nuestras promociones.",
            DateTime.UtcNow);

        IngestReport report = await Ingestor(db).IngestAsync(ajeno, default);

        Assert.Equal(IngestOutcome.Unrecognized, report.Outcome);
        Assert.Equal(0, await db.Transactions.CountAsync());

        // El correo sí se guarda: sin el original no hay reproceso posible.
        IncomingEmail guardado = await db.IncomingEmails.AsNoTracking().SingleAsync();
        Assert.Equal(EmailStatus.Unrecognized, guardado.Status);
        Assert.NotNull(guardado.FailureReason);
    }

    [Fact]
    public async Task un_correo_al_que_le_falta_el_monto_no_crea_nada()
    {
        // Fallo cerrado: no se asume cero.
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        var incompleto = new RawEmail(
            "<y@banco-de-muestra.do>",
            "alertas@banco-de-muestra.do",
            "Compra aprobada",
            "Tarjeta: ****1234\nComercio: X\nFecha: 15/03/2026 23:30",
            DateTime.UtcNow);

        IngestReport report = await Ingestor(db).IngestAsync(incompleto, default);

        Assert.Equal(IngestOutcome.NeedsReview, report.Outcome);
        Assert.Equal(0, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task un_correo_de_una_cuenta_desconocida_no_crea_una_cuenta()
    {
        // Una cuenta creada al vuelo tendría un saldo inicial inventado, y ese
        // saldo entra directo en el líquido de la fórmula del dinero seguro.
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        IngestReport report = await Ingestor(db)
            .IngestAsync(Correo(tarjeta: "9999"), default);

        Assert.Equal(IngestOutcome.NeedsReview, report.Outcome);
        Assert.Equal(0, await db.Transactions.CountAsync());
        Assert.Equal(3, await db.Accounts.CountAsync());
    }

    [Fact]
    public async Task el_reproceso_reinterpreta_lo_que_quedo_sin_reconocer_sin_duplicar()
    {
        // El escenario que justifica guardar el cuerpo original: un correo que
        // el parser de entonces no supo leer y el de ahora sí.
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        // Llega cuando no hay ningún parser que lo reconozca.
        var vacio = new ParserRegistry([]);
        var sinParser = new EmailIngestor(db, vacio, TimeProvider.System);

        IngestReport primera = await sinParser.IngestAsync(Correo(), default);
        Assert.Equal(IngestOutcome.Unrecognized, primera.Outcome);
        Assert.Equal(0, await db.Transactions.CountAsync());

        // Ahora existe el parser. Se reprocesa el correo guardado.
        IncomingEmail guardado = await db.IncomingEmails.SingleAsync();
        IngestReport segunda = await Ingestor(db).ProcessAsync(guardado, default);

        Assert.Equal(IngestOutcome.Created, segunda.Outcome);
        Assert.Equal(1, await db.Transactions.CountAsync());

        // Y reprocesarlo otra vez no añade un segundo movimiento.
        IngestReport tercera = await Ingestor(db).ProcessAsync(guardado, default);

        Assert.Equal(IngestOutcome.DuplicateExact, tercera.Outcome);
        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task un_parser_que_revienta_deja_el_correo_para_reprocesar()
    {
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        var explosivo = new ParserRegistry([new ParserQueRevienta()]);
        var ingestor = new EmailIngestor(db, explosivo, TimeProvider.System);

        IngestReport report = await ingestor.IngestAsync(Correo(), default);

        Assert.Equal(IngestOutcome.Failed, report.Outcome);

        IncomingEmail guardado = await db.IncomingEmails.AsNoTracking().SingleAsync();
        Assert.Equal(EmailStatus.Failed, guardado.Status);

        // El cuerpo sigue ahí: el banco no lo manda dos veces.
        Assert.Contains("Monto", guardado.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task una_compra_de_las_23_30_cae_en_el_dia_local_al_calcular_la_huella()
    {
        // Con el día UTC, esta compra y otra idéntica del día siguiente local
        // compartirían huella y la segunda se rechazaría siendo real.
        await ArrangeAsync();
        await using MargenDbContext db = Db();

        await Ingestor(db).IngestAsync(
            Correo(messageId: "<a@x>", fecha: "15/03/2026 23:30"),
            default);

        IngestReport segunda = await Ingestor(db).IngestAsync(
            Correo(messageId: "<b@x>", fecha: "16/03/2026 23:30"),
            default);

        Assert.Equal(IngestOutcome.Created, segunda.Outcome);
        Assert.Equal(2, await db.Transactions.CountAsync());
    }

    private sealed class ParserQueRevienta : IEmailParser
    {
        public string Name => "el-que-revienta";

        public int Version => 1;

        public bool CanHandle(RawEmail email) => true;

        public ParseResult Parse(RawEmail email) =>
            throw new InvalidOperationException("formato inesperado");
    }
}

public sealed class MailboxOptionsTests
{
    [Fact]
    public void sin_lista_blanca_no_esta_configurado()
    {
        // Sin lista, leerlo todo convertiría el buzón en una entrada abierta:
        // cualquiera que sepa la dirección podría crear movimientos.
        var options = new MailboxOptions
        {
            Host = "imap.ejemplo.do",
            User = "finanzas@ejemplo.do",
        };

        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void admite_la_direccion_completa_y_el_dominio()
    {
        var options = new MailboxOptions
        {
            Host = "imap.ejemplo.do",
            User = "finanzas@ejemplo.do",
            AllowedSenders = "alertas@banco.do, otrobanco.do",
        };

        Assert.True(options.IsConfigured);
        Assert.True(options.Allows("alertas@banco.do"));
        Assert.True(options.Allows("ALERTAS@BANCO.DO"));
        Assert.True(options.Allows("cualquiera@otrobanco.do"));
    }

    [Fact]
    public void un_remitente_de_fuera_no_pasa()
    {
        var options = new MailboxOptions { AllowedSenders = "banco.do" };

        Assert.False(options.Allows("boletin@otro.do"));
        Assert.False(options.Allows(""));
        Assert.False(options.Allows("   "));
    }

    [Fact]
    public void el_dominio_va_anclado_a_la_arroba()
    {
        // Sin el ancla, `banco.do` dejaría entrar `banco.do.atacante.com`, que
        // es un dominio que cualquiera puede registrar.
        var options = new MailboxOptions { AllowedSenders = "banco.do" };

        Assert.False(options.Allows("alertas@banco.do.atacante.com"));
        Assert.False(options.Allows("alertas@nobanco.do"));
    }
}
