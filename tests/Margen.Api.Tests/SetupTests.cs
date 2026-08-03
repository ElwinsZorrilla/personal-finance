using System.Net;
using System.Net.Http.Json;
using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Api.Endpoints;
using Margen.Api.Tests.Infra;
using Margen.Classify;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Tests;

/// <summary>
/// El arranque en frío de una instalación recién desplegada.
/// </summary>
/// <remarks>
/// Estas pruebas existen porque el hueco solo se vio al desplegar de verdad:
/// todas las demás siembran sus propios datos, y producción nacía vacía.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class SetupTests(PostgresFixture postgres)
{
    private MargenDbContext Db() => new(
        new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task<(TestApp App, HttpClient Client)> VaciaAsync()
    {
        // Base limpia de verdad: es el estado de una instalación nueva.
        await using (MargenDbContext db = Db())
        {
            await db.Alerts.ExecuteDeleteAsync();
            await db.Transactions.ExecuteDeleteAsync();
            await db.CategoryBudgets.ExecuteDeleteAsync();
            await db.MerchantRules.ExecuteDeleteAsync();
            await db.RecurringPayments.ExecuteDeleteAsync();
            await db.BudgetPeriods.ExecuteDeleteAsync();
            await db.Categories.ExecuteDeleteAsync();
            await db.StatementProfiles.ExecuteDeleteAsync();
            await db.IncomingEmails.ExecuteDeleteAsync();
            await db.Accounts.ExecuteDeleteAsync();
        }

        var app = new TestApp(postgres.ConnectionString, null);
        HttpClient client = app.CreateClient();

        using var key = new TestDeviceKey();
        TokenResponse token = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(token.Token);

        return (app, client);
    }

    [Fact]
    public async Task una_instalacion_nueva_dice_exactamente_que_le_falta()
    {
        // «No está lista» sin decir por qué obliga a adivinar.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        SetupStatusView estado =
            (await client.GetFromJsonAsync<SetupStatusView>("/setup/status"))!;

        Assert.False(estado.IsReady);
        Assert.Equal(3, estado.Missing.Count);
        Assert.Contains(estado.Missing, m => m.Contains("cuenta", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(estado.Missing, m => m.Contains("categor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task las_categorias_predeterminadas_son_las_que_el_clasificador_reconoce()
    {
        // El clasificador local devuelve un **nombre**, y alguien tiene que
        // haber creado la categoría con ese nombre exacto o la cascada nunca
        // acierta.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsync(new Uri("/setup/categories/defaults", UriKind.Relative), null);

        await using MargenDbContext db = Db();
        List<string> nombres = await db.Categories.Select(c => c.Name).ToListAsync();

        foreach (string esperado in new[]
        {
            CategoryNames.Supermercado,
            CategoryNames.Restaurantes,
            CategoryNames.Transporte,
            CategoryNames.Suscripciones,
            CategoryNames.Salud,
            CategoryNames.Servicios,
            CategoryNames.Efectivo,
        })
        {
            Assert.Contains(esperado, nombres, StringComparer.Ordinal);
        }
    }

    [Fact]
    public async Task sembrar_dos_veces_no_duplica_categorias()
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsync(new Uri("/setup/categories/defaults", UriKind.Relative), null);
        await client.PostAsync(new Uri("/setup/categories/defaults", UriKind.Relative), null);

        await using MargenDbContext db = Db();
        Assert.Equal(7, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task una_cuenta_se_da_de_alta_con_sus_cuatro_digitos()
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/setup/accounts",
            new CreateAccountRequest("Cuenta de nómina", "1234", "Checking", 500_000, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        AccountView cuenta = (await response.Content.ReadFromJsonAsync<AccountView>())!;
        Assert.Equal("1234", cuenta.LastFour);
        Assert.Equal(500_000, cuenta.BalanceCents);
    }

    [Fact]
    public async Task dar_de_alta_la_misma_cuenta_dos_veces_devuelve_la_que_hay()
    {
        // Con dos cuentas terminadas en lo mismo, un movimiento iría a una de
        // las dos según el orden de la consulta.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        var peticion = new CreateAccountRequest("Nómina", "1234", "Checking", 500_000, null);

        await client.PostAsJsonAsync("/setup/accounts", peticion);
        HttpResponseMessage segunda = await client.PostAsJsonAsync("/setup/accounts", peticion);

        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);

        await using MargenDbContext db = Db();
        Assert.Equal(1, await db.Accounts.CountAsync());
    }

    [Theory]
    [InlineData("123")]
    [InlineData("12345")]
    [InlineData("12a4")]
    [InlineData("")]
    public async Task unos_digitos_que_no_son_cuatro_digitos_se_rechazan(string digitos)
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/setup/accounts",
            new CreateAccountRequest("Algo", digitos, "Checking", 0, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task el_periodo_se_abre_alrededor_del_dia_de_cobro()
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest(25, 6_000_000, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        PeriodOpenedView periodo = (await response.Content.ReadFromJsonAsync<PeriodOpenedView>())!;
        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);

        Assert.True(
            periodo.StartDate <= hoy && periodo.EndDate >= hoy,
            "el período abierto tiene que contener hoy");
        Assert.True(periodo.Created);
    }

    [Fact]
    public async Task abrir_el_periodo_dos_veces_no_crea_dos()
    {
        // Dos períodos solapados harían que el panel eligiera uno de los dos
        // según el orden de la consulta.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest(25, 6_000_000, null, null));

        HttpResponseMessage segunda = await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest(25, 6_000_000, null, null));

        PeriodOpenedView periodo = (await segunda.Content.ReadFromJsonAsync<PeriodOpenedView>())!;

        Assert.False(periodo.Created);

        await using MargenDbContext db = Db();
        Assert.Equal(1, await db.BudgetPeriods.CountAsync());
    }

    [Fact]
    public void un_cobro_el_31_no_deja_sin_periodo_los_meses_de_treinta()
    {
        // El error que aparece una vez al año y parece magia negra.
        foreach (DateOnly dia in new[]
        {
            new DateOnly(2026, 2, 15),
            new DateOnly(2026, 4, 10),
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 3, 1),
        })
        {
            (DateOnly inicio, DateOnly fin) = SetupEndpoints.CycleAround(dia, 31);

            Assert.True(
                inicio <= dia && fin >= dia,
                $"el ciclo del {dia} con cobro el 31 no contiene ese día");
        }
    }

    [Fact]
    public async Task cuando_esta_todo_puesto_dice_que_esta_lista()
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsync(new Uri("/setup/categories/defaults", UriKind.Relative), null);

        await client.PostAsJsonAsync(
            "/setup/accounts",
            new CreateAccountRequest("Nómina", "1234", "Checking", 500_000, null));

        await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest(25, 6_000_000, null, null));

        SetupStatusView estado =
            (await client.GetFromJsonAsync<SetupStatusView>("/setup/status"))!;

        Assert.True(estado.IsReady);
        Assert.Empty(estado.Missing);
    }
}
