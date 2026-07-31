using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Api.Tests.Infra;

namespace Margen.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class EndpointTests(PostgresFixture postgres)
{
    private sealed class Scenario(TestApp app, HttpClient client, Seed seed) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public TestApp App => app;

        public Seed Seed => seed;

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }

    private async Task<Scenario> ArrangeAsync()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        var app = new TestApp(postgres.ConnectionString, null);
        HttpClient client = app.CreateClient();

        using var key = new TestDeviceKey();
        Auth.TokenResponse token = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(token.Token);

        return new Scenario(app, client, seed);
    }

    // ---------- Movimientos ----------

    [Fact]
    public async Task los_movimientos_se_listan_del_mas_reciente_al_mas_viejo()
    {
        await using Scenario s = await ArrangeAsync();

        DateTime baseUtc = LocalTime.StartOfLocalDay(s.Seed.Start).AddHours(12);

        await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, s.Seed.FoodId, 100_000, baseUtc);
        await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, s.Seed.FoodId, 200_000,
            baseUtc.AddDays(2), merchant: "PANADERIA");

        Page<TransactionView> page =
            (await s.Client.GetFromJsonAsync<Page<TransactionView>>("/transactions"))!;

        Assert.Equal(2, page.Count);
        Assert.Equal("PANADERIA", page.Items[0].Merchant);
    }

    [Fact]
    public async Task el_filtro_por_dia_local_incluye_las_compras_de_la_noche()
    {
        // Filtrar `to = día` comparando en UTC dejaría fuera las cuatro últimas
        // horas de ese día local, que son las de más gasto.
        await using Scenario s = await ArrangeAsync();

        DateOnly dia = s.Seed.Start.AddDays(3);
        DateTime nocheUtc = LocalTime.StartOfLocalDay(dia).AddHours(23).AddMinutes(30);

        await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, s.Seed.FoodId, 145_000, nocheUtc);

        Page<TransactionView> page = (await s.Client
            .GetFromJsonAsync<Page<TransactionView>>(
                $"/transactions?from={dia:yyyy-MM-dd}&to={dia:yyyy-MM-dd}"))!;

        Assert.Equal(1, page.Count);
        Assert.Equal(dia, page.Items[0].OccurredOn);
    }

    [Fact]
    public async Task un_estado_desconocido_en_el_filtro_es_400_y_no_una_lista_vacia()
    {
        await using Scenario s = await ArrangeAsync();

        HttpResponseMessage response = await s.Client.GetAsync(
            new Uri("/transactions?status=inventado", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task corregir_la_categoria_saca_el_movimiento_de_revision()
    {
        await using Scenario s = await ArrangeAsync();

        Guid id = await Seed.AddTransactionAsync(
            postgres.ConnectionString,
            s.Seed.CheckingId,
            null,
            300_000,
            LocalTime.StartOfLocalDay(s.Seed.Start).AddHours(12),
            status: Domain.TxStatus.NeedsReview);

        HttpResponseMessage response = await s.Client.PutAsJsonAsync(
            $"/transactions/{id}",
            new PutTransactionRequest(s.Seed.FoodId, null, null, CreateRule: true));

        response.EnsureSuccessStatusCode();
        TransactionView view = (await response.Content.ReadFromJsonAsync<TransactionView>())!;

        Assert.Equal("Posted", view.Status);
        Assert.Equal(s.Seed.FoodId, view.CategoryId);
        Assert.Equal(10000, view.ConfidenceBasisPoints);

        // La corrección generó la regla, así que la siguiente compra en el
        // mismo comercio no vuelve a preguntar.
        Page<RuleView> rules = (await s.Client.GetFromJsonAsync<Page<RuleView>>("/rules"))!;
        Assert.Contains(rules.Items, r => r.CategoryId == s.Seed.FoodId && r.IsUserDefined);
    }

    [Fact]
    public async Task corregir_dos_veces_el_mismo_comercio_no_deja_dos_reglas()
    {
        await using Scenario s = await ArrangeAsync();

        DateTime cuando = LocalTime.StartOfLocalDay(s.Seed.Start).AddHours(12);

        Guid uno = await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, null, 100_000, cuando,
            status: Domain.TxStatus.NeedsReview);
        Guid dos = await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, null, 200_000, cuando.AddDays(1),
            status: Domain.TxStatus.NeedsReview);

        await s.Client.PutAsJsonAsync($"/transactions/{uno}",
            new PutTransactionRequest(s.Seed.FoodId, null, null, CreateRule: true));
        await s.Client.PutAsJsonAsync($"/transactions/{dos}",
            new PutTransactionRequest(s.Seed.FunId, null, null, CreateRule: true));

        Page<RuleView> rules = (await s.Client.GetFromJsonAsync<Page<RuleView>>("/rules"))!;

        Assert.Single(rules.Items);
        Assert.Equal(s.Seed.FunId, rules.Items[0].CategoryId);
    }

    [Fact]
    public async Task corregir_a_una_categoria_que_no_existe_es_400()
    {
        await using Scenario s = await ArrangeAsync();

        Guid id = await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, null, 100_000,
            LocalTime.StartOfLocalDay(s.Seed.Start).AddHours(12));

        HttpResponseMessage response = await s.Client.PutAsJsonAsync(
            $"/transactions/{id}",
            new PutTransactionRequest(Guid.CreateVersion7(), null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Efectivo ----------

    [Fact]
    public async Task el_registro_de_efectivo_crea_el_gasto_y_baja_el_saldo()
    {
        await using Scenario s = await ArrangeAsync();

        HttpResponseMessage response = await s.Client.PostAsJsonAsync(
            "/transactions/cash",
            new CreateCashRequest(45_000, "Almuerzo", s.Seed.FoodId, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        TransactionView view = (await response.Content.ReadFromJsonAsync<TransactionView>())!;
        Assert.Equal(45_000, view.AmountCents);
        Assert.Equal("Cash", view.Kind);
        Assert.Equal("Shortcut", view.Source);

        DashboardView panel = (await s.Client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        // 5,000 de efectivo menos 450 = 4,550. Con la nómina, 64,550.
        Assert.Equal(6_455_000, panel.LiquidCents);
        Assert.Equal(45_000, panel.SpentSoFarCents);
    }

    [Fact]
    public async Task un_reintento_del_atajo_no_crea_el_gasto_dos_veces()
    {
        // El Atajo reintenta cuando la red falla a medio camino. Sin la huella,
        // el usuario vería el doble de lo que gastó.
        await using Scenario s = await ArrangeAsync();

        var body = new CreateCashRequest(45_000, "Almuerzo", s.Seed.FoodId, null, null);

        HttpResponseMessage primera = await s.Client.PostAsJsonAsync("/transactions/cash", body);
        HttpResponseMessage segunda = await s.Client.PostAsJsonAsync("/transactions/cash", body);

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);

        DashboardView panel = (await s.Client.GetFromJsonAsync<DashboardView>("/dashboard"))!;
        Assert.Equal(45_000, panel.SpentSoFarCents);
    }

    [Fact]
    public async Task un_gasto_en_efectivo_sin_categoria_cae_en_revision()
    {
        // Fallo cerrado: no se le adivina una categoría.
        await using Scenario s = await ArrangeAsync();

        HttpResponseMessage response = await s.Client.PostAsJsonAsync(
            "/transactions/cash",
            new CreateCashRequest(45_000, "Algo", null, null, null));

        TransactionView view = (await response.Content.ReadFromJsonAsync<TransactionView>())!;

        Assert.Equal("NeedsReview", view.Status);
        Assert.Equal(0, view.ConfidenceBasisPoints);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(10_000_001)]
    public async Task un_monto_fuera_de_rango_se_rechaza(long cents)
    {
        await using Scenario s = await ArrangeAsync();

        HttpResponseMessage response = await s.Client.PostAsJsonAsync(
            "/transactions/cash",
            new CreateCashRequest(cents, "Algo", null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task dos_registros_de_efectivo_a_la_vez_descuentan_los_dos_del_saldo()
    {
        // Leer el saldo, restar en memoria y escribirlo pierde uno de los dos
        // descuentos: los dos leen el mismo saldo y el segundo pisa al primero.
        // El gasto quedaría registrado y el saldo habría bajado una sola vez.
        await using Scenario s = await ArrangeAsync();

        HttpResponseMessage[] responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(i => s.Client.PostAsJsonAsync(
                "/transactions/cash",
                new CreateCashRequest(10_000 + i, $"Gasto {i}", s.Seed.FoodId, null, null))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        // 8 gastos: 100.00 + 100.01 + ... + 100.07 = 80,028 centavos.
        const long total = (10_000 * 8) + 28;

        DashboardView panel = (await s.Client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        Assert.Equal(total, panel.SpentSoFarCents);

        // 5,000 de efectivo + 60,000 de nómina, menos lo gastado. Si algún
        // descuento se hubiera perdido, el líquido sería mayor.
        Assert.Equal(6_500_000 - total, panel.LiquidCents);
    }

    [Fact]
    public async Task el_reemplazo_de_un_movimiento_aplica_los_campos_tal_como_llegan()
    {
        // Una sola regla para todo el cuerpo. La primera versión mezclaba dos
        // —la categoría nula borraba y las notas nulas se conservaban— y quien
        // llamara para cambiar solo el estado habría descategorizado el
        // movimiento sin pedirlo.
        await using Scenario s = await ArrangeAsync();

        Guid id = await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, s.Seed.FoodId, 100_000,
            LocalTime.StartOfLocalDay(s.Seed.Start).AddHours(12));

        HttpResponseMessage conNota = await s.Client.PutAsJsonAsync(
            $"/transactions/{id}",
            new PutTransactionRequest(s.Seed.FunId, "Posted", "cena con amigos"));

        conNota.EnsureSuccessStatusCode();

        HttpResponseMessage sinNada = await s.Client.PutAsJsonAsync(
            $"/transactions/{id}",
            new PutTransactionRequest(null, "Posted", null));

        sinNada.EnsureSuccessStatusCode();
        TransactionView view = (await sinNada.Content.ReadFromJsonAsync<TransactionView>())!;

        // Los dos campos ausentes se borran, y los dos igual.
        Assert.Null(view.CategoryId);
        Assert.Equal("Posted", view.Status);
    }

    // ---------- Presupuesto ----------

    [Fact]
    public async Task el_presupuesto_lista_las_categorias_con_lo_disponible()
    {
        await using Scenario s = await ArrangeAsync();

        BudgetView budget = (await s.Client.GetFromJsonAsync<BudgetView>("/budget"))!;

        Assert.Equal(3, budget.Lines.Count);
        Assert.Equal(5_100_000, budget.TotalAllocatedCents);

        // Recortable: Comida 20,000 + Ocio 6,000. El alquiler no cuenta.
        Assert.Equal(2_600_000, budget.TrimmableCents);
    }

    [Fact]
    public async Task recortar_el_alquiler_desde_el_endpoint_falla()
    {
        // El criterio de la Fase 3, comprobado ahora a través de HTTP: la regla
        // vive en el motor y el endpoint no la puede sortear.
        await using Scenario s = await ArrangeAsync();

        HttpResponseMessage response = await s.Client.PostAsJsonAsync(
            "/budget/redistribute",
            new RedistributeRequest(s.Seed.RentId, s.Seed.FoodId, 100_000));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task mover_entre_dos_flexibles_cuadra_por_los_dos_lados()
    {
        await using Scenario s = await ArrangeAsync();

        HttpResponseMessage response = await s.Client.PostAsJsonAsync(
            "/budget/redistribute",
            new RedistributeRequest(s.Seed.FunId, s.Seed.FoodId, 200_000));

        response.EnsureSuccessStatusCode();
        BudgetView budget = (await response.Content.ReadFromJsonAsync<BudgetView>())!;

        Assert.Equal(
            2_200_000,
            budget.Lines.Single(l => l.CategoryName == "Comida").EffectiveCents);
        Assert.Equal(
            400_000,
            budget.Lines.Single(l => l.CategoryName == "Ocio").EffectiveCents);

        // El total no cambia: lo que sale de uno entra en el otro.
        Assert.Equal(5_100_000, budget.TotalAllocatedCents);
    }

    [Fact]
    public async Task el_ajuste_se_guarda_aparte_de_lo_asignado()
    {
        // Para que quede el rastro de qué se movió y por qué la categoría tiene
        // hoy menos de lo que se le asignó al abrir el período.
        await using Scenario s = await ArrangeAsync();

        await s.Client.PostAsJsonAsync(
            "/budget/redistribute",
            new RedistributeRequest(s.Seed.FunId, s.Seed.FoodId, 200_000));

        BudgetView budget = (await s.Client.GetFromJsonAsync<BudgetView>("/budget"))!;
        BudgetLineView ocio = budget.Lines.Single(l => l.CategoryName == "Ocio");

        Assert.Equal(600_000, ocio.AllocatedCents);
        Assert.Equal(-200_000, ocio.AdjustmentCents);
        Assert.Equal(400_000, ocio.EffectiveCents);
    }

    [Fact]
    public async Task no_se_puede_mover_mas_de_lo_disponible()
    {
        await using Scenario s = await ArrangeAsync();

        await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, s.Seed.FunId, 550_000,
            LocalTime.StartOfLocalDay(s.Seed.Start).AddHours(12));

        HttpResponseMessage response = await s.Client.PostAsJsonAsync(
            "/budget/redistribute",
            new RedistributeRequest(s.Seed.FunId, s.Seed.FoodId, 200_000));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---------- Revisión, avisos, reglas, correos, conciliación ----------

    [Fact]
    public async Task la_revision_trae_lo_que_no_esta_clasificado()
    {
        await using Scenario s = await ArrangeAsync();

        await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, null, 100_000,
            LocalTime.StartOfLocalDay(s.Seed.Start).AddHours(12),
            status: Domain.TxStatus.NeedsReview);

        using JsonDocument body = JsonDocument.Parse(
            await s.Client.GetStringAsync(new Uri("/review", UriKind.Relative)));

        Assert.Equal(1, body.RootElement.GetProperty("transactions").GetArrayLength());
    }

    [Fact]
    public async Task una_regla_con_expresion_regular_invalida_se_rechaza()
    {
        // Guardarla sin protestar la haría reventar dentro del worker, sobre un
        // correo real y lejos de quien la escribió.
        await using Scenario s = await ArrangeAsync();

        HttpResponseMessage response = await s.Client.PostAsJsonAsync(
            "/rules",
            new CreateRuleRequest("([sin cerrar", "Regex", s.Seed.FoodId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task borrar_una_regla_la_desactiva_y_conserva_su_historia()
    {
        await using Scenario s = await ArrangeAsync();

        HttpResponseMessage creada = await s.Client.PostAsJsonAsync(
            "/rules",
            new CreateRuleRequest("SUPERMERCADO NACIONAL", "Exact", s.Seed.FoodId));
        creada.EnsureSuccessStatusCode();

        Guid id = await creada.Content.ReadFromJsonAsync<Guid>();

        HttpResponseMessage borrada = await s.Client.DeleteAsync(
            new Uri($"/rules/{id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, borrada.StatusCode);

        Page<RuleView> rules = (await s.Client.GetFromJsonAsync<Page<RuleView>>("/rules"))!;
        RuleView rule = rules.Items.Single(r => r.Id == id);

        Assert.False(rule.IsActive);
    }

    [Fact]
    public async Task los_correos_entrantes_y_la_conciliacion_responden_vacios_sin_datos()
    {
        // Vacío no es error: todavía no hay worker que llene el buzón.
        await using Scenario s = await ArrangeAsync();

        Page<IncomingEmailView> emails =
            (await s.Client.GetFromJsonAsync<Page<IncomingEmailView>>("/emails"))!;
        Assert.Equal(0, emails.Count);

        ReconciliationSummaryView reconciliation = (await s.Client
            .GetFromJsonAsync<ReconciliationSummaryView>("/reconciliation"))!;
        Assert.Empty(reconciliation.Outstanding);
    }

    [Fact]
    public async Task la_conciliacion_lista_lo_que_falta_por_cuadrar()
    {
        await using Scenario s = await ArrangeAsync();

        await Seed.AddTransactionAsync(
            postgres.ConnectionString, s.Seed.CheckingId, s.Seed.FoodId, 100_000,
            LocalTime.StartOfLocalDay(s.Seed.Start).AddHours(12),
            status: Domain.TxStatus.Pending);

        ReconciliationSummaryView view = (await s.Client
            .GetFromJsonAsync<ReconciliationSummaryView>("/reconciliation"))!;

        Assert.Equal(1, view.Pending);
        Assert.Single(view.Outstanding);
    }

    [Fact]
    public async Task todos_los_endpoints_de_lectura_exigen_token()
    {
        Seed _ = await Seed.PlantAsync(postgres.ConnectionString);
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        foreach (string ruta in new[]
        {
            "/dashboard", "/transactions", "/budget", "/review",
            "/rules", "/emails", "/reconciliation", "/notifications",
        })
        {
            HttpResponseMessage response = await client.GetAsync(new Uri(ruta, UriKind.Relative));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
