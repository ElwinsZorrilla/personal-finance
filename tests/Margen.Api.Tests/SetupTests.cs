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
            "/setup/periods", new OpenPeriodRequest([25], 6_000_000, null, null));

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
            "/setup/periods", new OpenPeriodRequest([25], 6_000_000, null, null));

        HttpResponseMessage segunda = await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest([25], 6_000_000, null, null));

        PeriodOpenedView periodo = (await segunda.Content.ReadFromJsonAsync<PeriodOpenedView>())!;

        Assert.False(periodo.Created);

        await using MargenDbContext db = Db();
        Assert.Equal(1, await db.BudgetPeriods.CountAsync());
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
            "/setup/periods", new OpenPeriodRequest([25], 6_000_000, null, null));

        SetupStatusView estado =
            (await client.GetFromJsonAsync<SetupStatusView>("/setup/status"))!;

        Assert.True(estado.IsReady);
        Assert.Empty(estado.Missing);
    }
    [Fact]
    public async Task dos_cobros_abren_un_periodo_de_media_quincena()
    {
        // Quien cobra quincena y fin de mes tiene **dos ciclos por mes**. Con un
        // solo día, el período duraba un mes y el reparto diario dividía el
        // dinero de una quincena entre treinta días: la mitad de lo que se puede
        // gastar, todos los días, sin que nada fallara.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage respuesta = await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest([15, 31], 4_250_000, null, null));

        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);

        PeriodOpenedView periodo =
            (await respuesta.Content.ReadFromJsonAsync<PeriodOpenedView>())!;

        int dias = periodo.EndDate.DayNumber - periodo.StartDate.DayNumber + 1;

        Assert.InRange(dias, 13, 17);
    }

    [Fact]
    public async Task un_dia_de_cobro_que_no_existe_se_rechaza()
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage respuesta = await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest([0], 4_250_000, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task sin_ningun_dia_de_cobro_no_se_abre_nada()
    {
        // Fallo cerrado: sin calendario no hay ciclo, y un ciclo inventado es
        // una cifra de gasto diario inventada.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage respuesta = await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest([], 4_250_000, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
    }

    [Fact]
    public async Task el_periodo_recuerda_con_que_calendario_nacio()
    {
        // Se guarda en el período y no en unos ajustes globales: si mañana
        // cambian los días de cobro, los períodos cerrados tienen que seguir
        // contando su propia historia. La base histórica pondera los tres
        // anteriores, y un ajuste global la calcularía sobre ciclos que nunca
        // existieron.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest([15, 31], 4_250_000, null, null));

        await using MargenDbContext db = Db();

        Margen.Domain.Entities.BudgetPeriod periodo =
            await db.BudgetPeriods.SingleAsync();

        Assert.Equal([15, 31], periodo.PayDays);
    }

}
