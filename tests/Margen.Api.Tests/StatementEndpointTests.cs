using System.Net;
using System.Net.Http.Json;
using System.Text;
using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Api.Tests.Infra;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Tests;

/// <summary>
/// Importar el estado de cuenta y cerrar el período, contra el servidor y la
/// base de verdad.
/// </summary>
/// <remarks>
/// La lectura del CSV, el emparejamiento y la recomendación están probados sin
/// servidor en `Margen.Ingest.Tests` y `Margen.Budget.Tests`. Aquí se comprueba
/// lo que solo se ve montado: qué se escribe, qué no se escribe y qué pasa al
/// importar dos veces.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class StatementEndpointTests(PostgresFixture postgres)
{
    private MargenDbContext Db() => new(
        new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task<(TestApp App, HttpClient Client, Seed Seed)> ArrangeAsync()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        var app = new TestApp(postgres.ConnectionString, null);
        HttpClient client = app.CreateClient();

        using var key = new TestDeviceKey();
        TokenResponse token = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(token.Token);

        return (app, client, seed);
    }

    private static CreateStatementProfileRequest Perfil(Guid cuenta) => new(
        "Popular, cuenta de nómina", cuenta, ",", 1, 0, "dd/MM/yyyy", 1, 3, null, null, "Point", false);

    private static async Task<Guid> CrearPerfilAsync(
        HttpClient client, CreateStatementProfileRequest request)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync("/statements/profiles", request);
        response.EnsureSuccessStatusCode();

        StatementProfileView view =
            (await response.Content.ReadFromJsonAsync<StatementProfileView>())!;

        return view.Id;
    }

    private static async Task<HttpResponseMessage> SubirAsync(
        HttpClient client, Guid perfil, string csv, string ruta)
    {
        // El contenido se desecha **después** de que termine la petición. Con
        // `using` sobre un `Task` devuelto sin esperar, el cuerpo se cierra a
        // media escritura.
        using var content = new StringContent(csv, Encoding.UTF8, "text/csv");

        return await client.PostAsync(
            new Uri($"/statements/{perfil}/{ruta}", UriKind.Relative), content);
    }

    private static string CsvDe(params (DateOnly Dia, string Que, long Centavos)[] filas)
    {
        var b = new StringBuilder("Fecha,Descripcion,Referencia,Monto\n");

        foreach ((DateOnly dia, string que, long centavos) in filas)
        {
            b.Append(System.Globalization.CultureInfo.InvariantCulture, $"{dia:dd/MM/yyyy},{que},A1,");
            // En negativo: es un cargo. Escribirlos en positivo los convierte en
            // ingresos, y con la dirección al revés no cuadran con nada.
            b.Append(System.Globalization.CultureInfo.InvariantCulture, $"-{centavos / 100m:0.00}\n");
        }

        return b.ToString();
    }

    // ---------- El perfil ----------

    [Fact]
    public async Task un_perfil_sin_columna_de_monto_no_se_guarda()
    {
        // Guardarlo dejaría una opción en la lista que falla al usarla.
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/statements/profiles",
            Perfil(seed.CheckingId) with { AmountColumn = null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task un_perfil_sin_formato_de_fecha_no_se_guarda()
    {
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/statements/profiles",
            Perfil(seed.CheckingId) with { DateFormat = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task dos_perfiles_con_el_mismo_nombre_no_se_pueden_distinguir()
    {
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        await CrearPerfilAsync(client, Perfil(seed.CheckingId));

        HttpResponseMessage segunda = await client.PostAsJsonAsync(
            "/statements/profiles", Perfil(seed.CheckingId));

        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }

    [Fact]
    public async Task un_perfil_por_banco_convive_con_los_demas()
    {
        // Con varios bancos hay un perfil por cada uno, y añadir el tercero no
        // toca a los dos que ya funcionan.
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        await CrearPerfilAsync(client, Perfil(seed.CheckingId));
        await CrearPerfilAsync(
            client,
            Perfil(seed.CreditId) with
            {
                Name = "Otro banco, tarjeta",
                Delimiter = ";",
                DateFormat = "yyyy-MM-dd",
                AmountColumn = null,
                DebitColumn = 2,
                CreditColumn = 3,
                Decimals = "Comma",
            });

        Page<StatementProfileView>? lista = await client
            .GetFromJsonAsync<Page<StatementProfileView>>("/statements/profiles");

        Assert.Equal(2, lista!.Count);
    }

    // ---------- La vista previa no escribe ----------

    [Fact]
    public async Task la_vista_previa_no_escribe_nada()
    {
        // Es la mitad del valor del mapeo: se ve lo que se entendió antes de
        // tocar la base.
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        Guid perfil = await CrearPerfilAsync(client, Perfil(seed.CheckingId));
        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);

        HttpResponseMessage response = await SubirAsync(
            client, perfil, CsvDe((hoy, "SM NACIONAL", 123_456)), "preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        StatementPreviewView view =
            (await response.Content.ReadFromJsonAsync<StatementPreviewView>())!;

        Assert.Single(view.Lines);
        Assert.Equal("Missing", view.Lines[0].State);

        await using MargenDbContext db = Db();
        Assert.Equal(0, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task las_filas_que_no_se_entienden_salen_con_su_motivo()
    {
        // Un importador que se come en silencio lo que no entiende deja un
        // estado de cuenta que parece cuadrado y no lo está.
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        Guid perfil = await CrearPerfilAsync(client, Perfil(seed.CheckingId));
        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);

        string csv = CsvDe((hoy, "SM NACIONAL", 123_456)) + "SALDO ANTERIOR,,,\n";

        HttpResponseMessage response = await SubirAsync(client, perfil, csv, "preview");
        StatementPreviewView view =
            (await response.Content.ReadFromJsonAsync<StatementPreviewView>())!;

        Assert.Single(view.Rejected);
        Assert.Contains("fecha", view.Rejected[0].Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task un_archivo_vacio_se_rechaza()
    {
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        Guid perfil = await CrearPerfilAsync(client, Perfil(seed.CheckingId));

        HttpResponseMessage response = await SubirAsync(client, perfil, "", "preview");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- La importación ----------

    [Fact]
    public async Task lo_que_falta_en_la_aplicacion_se_crea()
    {
        // Es el motivo entero de conciliar: el efectivo que nadie registró y
        // los correos que no llegaron aparecen aquí.
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        Guid perfil = await CrearPerfilAsync(client, Perfil(seed.CheckingId));
        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);

        HttpResponseMessage response = await SubirAsync(
            client, perfil, CsvDe((hoy, "COLMADO LA ESQUINA", 45_000)), "import");

        ImportReportView report = (await response.Content.ReadFromJsonAsync<ImportReportView>())!;

        Assert.Equal(1, report.Created);

        await using MargenDbContext db = Db();
        Transaction creado = await db.Transactions.AsNoTracking().SingleAsync();

        Assert.Equal(new Money(45_000), creado.Amount);
        Assert.Equal(TxSource.Statement, creado.Source);
        Assert.Equal(TxStatus.NeedsReview, creado.Status);
        Assert.Equal(TxDirection.Outflow, creado.Direction);
        Assert.Null(creado.CategoryId);
    }

    [Fact]
    public async Task lo_que_ya_estaba_se_marca_conciliado_y_no_se_duplica()
    {
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        Guid perfil = await CrearPerfilAsync(client, Perfil(seed.CheckingId));
        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);

        await using (MargenDbContext db = Db())
        {
            db.Transactions.Add(Movimiento(seed, "SM NACIONAL CHARLES", 123_456, hoy));
            await db.SaveChangesAsync();
        }

        HttpResponseMessage response = await SubirAsync(
            client, perfil, CsvDe((hoy, "SM NACIONAL", 123_456)), "import");

        ImportReportView report = (await response.Content.ReadFromJsonAsync<ImportReportView>())!;

        Assert.Equal(1, report.Reconciled);
        Assert.Equal(0, report.Created);

        await using MargenDbContext db2 = Db();
        Transaction row = await db2.Transactions.AsNoTracking().SingleAsync();
        Assert.Equal(TxStatus.Reconciled, row.Status);
    }

    [Fact]
    public async Task importar_dos_veces_el_mismo_archivo_no_duplica_nada()
    {
        // El estado de cuenta llega todos los meses y se solapa con el
        // anterior. Sin esto, cada importación duplicaría el mes entero.
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        Guid perfil = await CrearPerfilAsync(client, Perfil(seed.CheckingId));
        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);
        string csv = CsvDe((hoy, "COLMADO LA ESQUINA", 45_000));

        await SubirAsync(client, perfil, csv, "import");
        HttpResponseMessage segunda = await SubirAsync(client, perfil, csv, "import");

        ImportReportView report = (await segunda.Content.ReadFromJsonAsync<ImportReportView>())!;

        Assert.Equal(0, report.Created);
        Assert.Equal(1, report.Reconciled);

        await using MargenDbContext db = Db();
        Assert.Equal(1, await db.Transactions.CountAsync());
    }

    [Fact]
    public async Task un_cargo_con_otro_monto_no_se_toca_y_sale_como_discrepante()
    {
        // Hay dos cifras y elegir una sin preguntar es justo lo que este
        // sistema no hace.
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        Guid perfil = await CrearPerfilAsync(client, Perfil(seed.CheckingId));
        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);

        await using (MargenDbContext db = Db())
        {
            db.Transactions.Add(Movimiento(seed, "SM NACIONAL CHARLES", 100_000, hoy));
            await db.SaveChangesAsync();
        }

        HttpResponseMessage response = await SubirAsync(
            client, perfil, CsvDe((hoy, "SM NACIONAL", 120_000)), "import");

        ImportReportView report = (await response.Content.ReadFromJsonAsync<ImportReportView>())!;

        Assert.Equal(1, report.Discrepant);
        Assert.Equal(0, report.Created);

        await using MargenDbContext db2 = Db();
        Transaction row = await db2.Transactions.AsNoTracking().SingleAsync();
        Assert.Equal(new Money(100_000), row.Amount);
        Assert.NotEqual(TxStatus.Reconciled, row.Status);
    }

    [Fact]
    public async Task un_perfil_que_no_existe_responde_404()
    {
        (TestApp app, HttpClient client, _) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await SubirAsync(
            client, Guid.CreateVersion7(), "a,b\n1,2", "preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- El cierre de período ----------

    [Fact]
    public async Task sin_periodos_cerrados_no_hay_recomendacion()
    {
        // Proponer ceros sería inventarse un presupuesto y presentarlo como si
        // saliera de algún sitio.
        (TestApp app, HttpClient client, _) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/periods/recommendation", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task cerrar_un_periodo_devuelve_lo_que_asignar_en_el_siguiente()
    {
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);

        await using (MargenDbContext db = Db())
        {
            Transaction gasto = Movimiento(seed, "SM NACIONAL", 1_400_000, hoy);
            gasto.CategoryId = seed.FoodId;
            db.Transactions.Add(gasto);
            await db.SaveChangesAsync();
        }

        HttpResponseMessage response = await client.PostAsync(
            new Uri($"/periods/{seed.PeriodId}/close", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RecommendedBudgetView view =
            (await response.Content.ReadFromJsonAsync<RecommendedBudgetView>())!;

        // Sale de lo gastado, no de lo asignado.
        Assert.Equal(1_400_000, view.Categories.Single(c => c.CategoryId == seed.FoodId)
            .RecommendedCents);
    }

    [Fact]
    public async Task cerrar_dos_veces_no_cambia_nada()
    {
        // Un doble toque en la pantalla o un reintento de red no pueden alterar
        // el resultado de un período cerrado.
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);

        await using (MargenDbContext db = Db())
        {
            Transaction gasto = Movimiento(seed, "SM NACIONAL", 1_400_000, hoy);
            gasto.CategoryId = seed.FoodId;
            db.Transactions.Add(gasto);
            await db.SaveChangesAsync();
        }

        var uri = new Uri($"/periods/{seed.PeriodId}/close", UriKind.Relative);

        HttpResponseMessage primera = await client.PostAsync(uri, null);
        DateTime? cerradoEn;

        await using (MargenDbContext db = Db())
        {
            cerradoEn = await db.BudgetPeriods.AsNoTracking()
                .Where(p => p.Id == seed.PeriodId).Select(p => p.ClosedAt).SingleAsync();
        }

        HttpResponseMessage segunda = await client.PostAsync(uri, null);

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);

        await using MargenDbContext db2 = Db();
        DateTime? despues = await db2.BudgetPeriods.AsNoTracking()
            .Where(p => p.Id == seed.PeriodId).Select(p => p.ClosedAt).SingleAsync();

        Assert.Equal(cerradoEn, despues);
    }

    private static Transaction Movimiento(Seed seed, string comercio, long centavos, DateOnly dia) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            AccountId = seed.CheckingId,
            MerchantRaw = comercio,
            MerchantNormalized = comercio,
            Amount = new Money(centavos),
            OccurredAt = LocalTime.StartOfLocalDay(dia).AddHours(12),
            Kind = TxKind.Purchase,
            Status = TxStatus.Posted,
            Source = TxSource.Email,
            Direction = TxDirection.Outflow,
            Fingerprint = $"est-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
}
