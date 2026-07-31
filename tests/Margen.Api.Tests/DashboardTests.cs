using System.Net;
using System.Net.Http.Json;
using Margen.Api.Budget;
using Margen.Api.Tests.Infra;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Tests;

/// <summary>
/// El panel de punta a punta: base real, agregador real, motor real.
/// </summary>
/// <remarks>
/// Las pruebas de esta clase no comparten estado con las demás por accidente:
/// <see cref="Seed.PlantAsync"/> borra y vuelve a sembrar. Se ejecutan en serie
/// dentro de la colección de Postgres, que es lo que lo hace seguro.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class DashboardTests(PostgresFixture postgres)
{
    /// <summary>Base sembrada, aplicación levantada y cliente con sesión abierta.</summary>
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

    [Fact]
    public async Task el_panel_responde_las_cifras_ya_resueltas()
    {
        await using Scenario s = await ArrangeAsync();
        HttpClient client = s.Client;
        Seed seed = s.Seed;

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/dashboard", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        DashboardView panel = (await response.Content.ReadFromJsonAsync<DashboardView>())!;

        // Líquido = 60,000 de nómina + 5,000 de efectivo. La tarjeta no suma:
        // su saldo es deuda, no dinero disponible.
        Assert.Equal(6_500_000, panel.LiquidCents);

        // Restas: alquiler pendiente 25,000 + tarjeta 12,000 + ahorro 5,000
        // + fondo 10,000 + retenciones 0 = 52,000. Seguro = 13,000.
        Assert.Equal(5_200_000, panel.Deductions.Sum(d => d.Cents));
        Assert.Equal(1_300_000, panel.SafeToSpendCents);
        Assert.False(panel.IsOverdrawn);

        // El desglose completo, para que el usuario pueda discutir la cifra.
        Assert.Equal(5, panel.Deductions.Count);

        Assert.Equal(seed.Start, panel.Period.Start);
        Assert.Equal(seed.End, panel.Period.End);
        Assert.Equal(30, panel.Period.TotalDays);
    }

    [Fact]
    public async Task el_liquido_menos_las_restas_es_exactamente_el_dinero_seguro()
    {
        // La invariante que impide que una resta se pierda entre la base y el
        // cable.
        await using Scenario s = await ArrangeAsync();
        HttpClient client = s.Client;

        DashboardView panel = (await client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        Assert.Equal(
            panel.LiquidCents - panel.Deductions.Sum(d => d.Cents),
            panel.SafeToSpendCents);
    }

    [Fact]
    public async Task una_compra_de_las_23_30_cuenta_en_el_dia_local_y_no_en_el_utc()
    {
        // La prueba central de esta fase. El último día del ciclo, a las 23:30
        // hora de Santo Domingo, es ya el día siguiente en UTC. Si el
        // agregador recortara el ciclo en UTC, esta compra quedaría fuera y el
        // gasto del período saldría menor de lo real —y el dinero seguro,
        // mayor—.
        await using Scenario s = await ArrangeAsync();
        HttpClient client = s.Client;
        Seed seed = s.Seed;

        DateTime ultimaNoche = LocalTime.StartOfLocalDay(seed.End).AddHours(23).AddMinutes(30);

        // Comprobación de que el escenario es el que se cree: el instante UTC
        // cae el día siguiente al cierre del ciclo.
        Assert.Equal(seed.End.AddDays(1), DateOnly.FromDateTime(ultimaNoche));
        Assert.Equal(seed.End, LocalTime.LocalDateOf(ultimaNoche));

        await Seed.AddTransactionAsync(
            postgres.ConnectionString,
            seed.CheckingId,
            seed.FoodId,
            245_000,
            ultimaNoche);

        DashboardView panel = (await client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        Assert.Equal(245_000, panel.SpentSoFarCents);
        Assert.Equal(245_000, panel.Categories.Single(c => c.Name == "Comida").SpentCents);
    }

    [Fact]
    public async Task una_compra_de_las_00_30_del_dia_siguiente_al_cierre_queda_fuera()
    {
        // El simétrico de la anterior: media hora más tarde ya es otro ciclo.
        // Sin esta prueba, un agregador que se pasara de generoso con el rango
        // pasaría la de arriba y seguiría estando mal.
        await using Scenario s = await ArrangeAsync();
        HttpClient client = s.Client;
        Seed seed = s.Seed;

        DateTime primeraNocheFuera = LocalTime
            .StartOfLocalDay(seed.End.AddDays(1)).AddMinutes(30);

        await Seed.AddTransactionAsync(
            postgres.ConnectionString,
            seed.CheckingId,
            seed.FoodId,
            999_000,
            primeraNocheFuera);

        DashboardView panel = (await client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        Assert.Equal(0, panel.SpentSoFarCents);
    }

    [Fact]
    public async Task una_devolucion_descuenta_de_la_categoria_de_la_compra_original()
    {
        await using Scenario s = await ArrangeAsync();
        HttpClient client = s.Client;
        Seed seed = s.Seed;

        DateTime cuando = LocalTime.StartOfLocalDay(seed.Start).AddHours(12);

        Guid compra = await Seed.AddTransactionAsync(
            postgres.ConnectionString, seed.CheckingId, seed.FunId, 500_000, cuando);

        // La devolución llega categorizada como Comida, que es lo que suele
        // hacer el banco. Tiene que descontarse de Ocio igual.
        await Seed.AddTransactionAsync(
            postgres.ConnectionString,
            seed.CheckingId,
            seed.FoodId,
            200_000,
            cuando.AddDays(1),
            kind: Domain.TxKind.Refund,
            refunds: compra);

        DashboardView panel = (await client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        Assert.Equal(300_000, panel.SpentSoFarCents);
        Assert.Equal(300_000, panel.Categories.Single(c => c.Name == "Ocio").SpentCents);
        Assert.Equal(0, panel.Categories.Single(c => c.Name == "Comida").SpentCents);
    }

    [Fact]
    public async Task un_pago_de_tarjeta_no_aparece_como_gasto()
    {
        await using Scenario s = await ArrangeAsync();
        HttpClient client = s.Client;
        Seed seed = s.Seed;

        await Seed.AddTransactionAsync(
            postgres.ConnectionString,
            seed.CreditId,
            null,
            1_500_000,
            LocalTime.StartOfLocalDay(seed.Start).AddHours(12),
            kind: Domain.TxKind.Payment);

        DashboardView panel = (await client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        Assert.Equal(0, panel.SpentSoFarCents);
    }

    [Fact]
    public async Task un_movimiento_pendiente_se_retiene_del_liquido()
    {
        // El saldo que reporta el banco todavía no lo descuenta. Si no se
        // restara, el dinero seguro sería mayor de lo real durante los días que
        // el banco tarda en asentar, que es justo cuando el usuario decide si
        // puede gastar.
        await using Scenario s = await ArrangeAsync();
        HttpClient client = s.Client;
        Seed seed = s.Seed;

        DashboardView antes = (await client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        await Seed.AddTransactionAsync(
            postgres.ConnectionString,
            seed.CheckingId,
            seed.FoodId,
            300_000,
            LocalTime.StartOfLocalDay(seed.Start).AddHours(12),
            status: Domain.TxStatus.Pending);

        DashboardView despues = (await client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        Assert.Equal(antes.SafeToSpendCents - 300_000, despues.SafeToSpendCents);
        Assert.Equal(
            300_000,
            despues.Deductions.Single(d => d.Kind == "Withholdings").Cents);
    }

    [Fact]
    public async Task sin_periodo_abierto_responde_409_y_no_ceros()
    {
        // Un 200 con todo a cero sería una cifra inventada con código de éxito.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await CloseThePeriodAsync(seed.PeriodId);

        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();
        Auth.TokenResponse token = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(token.Token);

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/dashboard", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task sin_historia_la_base_historica_viaja_nula_con_su_motivo()
    {
        // Y no como cero, que la pantalla enseñaría como una cifra.
        await using Scenario s = await ArrangeAsync();
        HttpClient client = s.Client;

        DashboardView panel = (await client.GetFromJsonAsync<DashboardView>("/dashboard"))!;

        Assert.Null(panel.HistoricalBaseline.Cents);
        Assert.NotNull(panel.HistoricalBaseline.Unavailable);
        Assert.Equal(0, panel.HistoricalPeriodsConsidered);
    }

    [Fact]
    public async Task el_panel_exige_token()
    {
        Seed _ = await Seed.PlantAsync(postgres.ConnectionString);
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/dashboard", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task el_token_del_atajo_no_alcanza_el_panel()
    {
        await using Scenario s = await ArrangeAsync();
        HttpClient client = s.Client;

        HttpResponseMessage emision = await client.PostAsJsonAsync(
            "/auth/shortcut-tokens",
            new Auth.ShortcutTokenRequest("Atajo"));
        emision.EnsureSuccessStatusCode();

        Auth.TokenResponse atajo =
            (await emision.Content.ReadFromJsonAsync<Auth.TokenResponse>())!;

        using HttpClient conAtajo = s.App.CreateClient();
        conAtajo.UseToken(atajo.Token);

        HttpResponseMessage response = await conAtajo.GetAsync(
            new Uri("/dashboard", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task CloseThePeriodAsync(Guid periodId)
    {
        var options = new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options;

        await using var db = new MargenDbContext(options);

        await db.BudgetPeriods
            .Where(p => p.Id == periodId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.IsClosed, true));
    }
}
