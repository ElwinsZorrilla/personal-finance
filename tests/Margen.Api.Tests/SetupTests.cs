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
    [Fact]
    public async Task corregir_el_calendario_recalcula_las_fechas()
    {
        // El caso real: el período de producción se abrió con un solo cobro el
        // 15, antes de que existieran los dos. `POST /setup/periods` es
        // idempotente y devuelve el que hay, así que sin esto no había forma de
        // arreglarlo hasta que terminara.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest([15], 3_500_000, null, null));

        HttpResponseMessage respuesta = await client.PutAsJsonAsync(
            "/setup/periods/current",
            new OpenPeriodRequest([15, 31], 4_250_000, null, null));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        PeriodOpenedView periodo =
            (await respuesta.Content.ReadFromJsonAsync<PeriodOpenedView>())!;

        int dias = periodo.EndDate.DayNumber - periodo.StartDate.DayNumber + 1;

        // **Las fechas se recalculan.** Un período que dice cobrar dos veces al
        // mes y abarca treinta días no es una etiqueta mal puesta: es una cifra
        // de gasto diario equivocada.
        Assert.InRange(dias, 13, 17);
        Assert.Equal(4_250_000, periodo.ExpectedIncomeCents);

        await using MargenDbContext db = Db();
        Margen.Domain.Entities.BudgetPeriod guardado =
            await db.BudgetPeriods.SingleAsync();

        Assert.Equal([15, 31], guardado.PayDays);
    }

    [Fact]
    public async Task corregir_sin_periodo_abierto_da_404()
    {
        // Fallo cerrado: no se crea uno por las buenas. Crear un período al
        // intentar corregir otro que no existe es inventar un ciclo.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage respuesta = await client.PutAsJsonAsync(
            "/setup/periods/current",
            new OpenPeriodRequest([15, 31], 4_250_000, null, null));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    [Fact]
    public async Task corregir_con_un_calendario_invalido_no_toca_nada()
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest([15], 3_500_000, null, null));

        HttpResponseMessage respuesta = await client.PutAsJsonAsync(
            "/setup/periods/current",
            new OpenPeriodRequest([45], 4_250_000, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);

        await using MargenDbContext db = Db();
        Margen.Domain.Entities.BudgetPeriod intacto =
            await db.BudgetPeriods.SingleAsync();

        Assert.Equal([15], intacto.PayDays);
        Assert.Equal(3_500_000, intacto.ExpectedIncome.Cents);
    }

    [Fact]
    public async Task se_puede_consultar_el_periodo_abierto()
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsJsonAsync(
            "/setup/periods", new OpenPeriodRequest([15, 31], 4_250_000, null, null));

        PeriodOpenedView periodo =
            (await client.GetFromJsonAsync<PeriodOpenedView>("/setup/periods/current"))!;

        Assert.Equal(4_250_000, periodo.ExpectedIncomeCents);
        Assert.False(periodo.Created);
    }
    [Fact]
    public async Task las_cuentas_se_pueden_listar()
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsJsonAsync(
            "/setup/accounts",
            new CreateAccountRequest("Popular corriente", "4821", "Checking", 1_250_050, null));
        await client.PostAsJsonAsync(
            "/setup/accounts",
            new CreateAccountRequest("Visa", "9876", "Credit", -450_000, 10_000_000));

        List<AccountView> cuentas =
            (await client.GetFromJsonAsync<List<AccountView>>("/setup/accounts"))!;

        Assert.Equal(2, cuentas.Count);
        Assert.Equal("Popular corriente", cuentas[0].Name);
        Assert.Equal(1_250_050, cuentas[0].BalanceCents);
        Assert.Equal(10_000_000, cuentas[1].CreditLimitCents);
    }

    [Fact]
    public async Task corregir_el_saldo_de_una_cuenta()
    {
        // Es lo que más se va a usar: el saldo es la única cifra que escribe una
        // persona, y se desvía en cuanto un movimiento no llega por correo.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage creada = await client.PostAsJsonAsync(
            "/setup/accounts",
            new CreateAccountRequest("Popular", "4821", "Checking", 1_000_000, null));

        AccountView cuenta = (await creada.Content.ReadFromJsonAsync<AccountView>())!;

        HttpResponseMessage respuesta = await client.PutAsJsonAsync(
            $"/setup/accounts/{cuenta.Id}",
            new UpdateAccountRequest("Popular corriente", 1_777_725, null));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);

        AccountView corregida = (await respuesta.Content.ReadFromJsonAsync<AccountView>())!;

        Assert.Equal("Popular corriente", corregida.Name);
        Assert.Equal(1_777_725, corregida.BalanceCents);

        // Los cuatro dígitos no se tocan: son la identidad frente a los correos.
        Assert.Equal("4821", corregida.LastFour);
    }

    [Fact]
    public async Task dar_de_baja_una_cuenta_no_la_borra()
    {
        // Borrarla se llevaría sus movimientos, y con ellos el gasto de los
        // períodos cerrados: la base histórica pondera los tres anteriores y
        // pasaría a calcularse sobre un pasado que cambió.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage creada = await client.PostAsJsonAsync(
            "/setup/accounts",
            new CreateAccountRequest("Vieja", "1111", "Checking", 100_000, null));
        await client.PostAsJsonAsync(
            "/setup/accounts",
            new CreateAccountRequest("Nueva", "2222", "Checking", 200_000, null));

        AccountView vieja = (await creada.Content.ReadFromJsonAsync<AccountView>())!;

        HttpResponseMessage baja = await client.DeleteAsync(
            new Uri($"/setup/accounts/{vieja.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, baja.StatusCode);

        List<AccountView> activas =
            (await client.GetFromJsonAsync<List<AccountView>>("/setup/accounts"))!;

        Assert.Single(activas);
        Assert.Equal("Nueva", activas[0].Name);

        await using MargenDbContext db = Db();

        // Sigue en la base, inactiva.
        Assert.Equal(2, await db.Accounts.CountAsync());
    }

    [Fact]
    public async Task no_se_puede_dar_de_baja_la_ultima_cuenta()
    {
        // Sin ninguna cuenta activa el panel no puede calcular, y la app
        // volvería a la puesta en marcha sin que nadie lo hubiera pedido.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage creada = await client.PostAsJsonAsync(
            "/setup/accounts",
            new CreateAccountRequest("Única", "4821", "Checking", 100_000, null));

        AccountView unica = (await creada.Content.ReadFromJsonAsync<AccountView>())!;

        HttpResponseMessage baja = await client.DeleteAsync(
            new Uri($"/setup/accounts/{unica.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, baja.StatusCode);

        List<AccountView> activas =
            (await client.GetFromJsonAsync<List<AccountView>>("/setup/accounts"))!;

        Assert.Single(activas);
    }

    [Fact]
    public async Task corregir_una_cuenta_que_no_existe_da_404()
    {
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage respuesta = await client.PutAsJsonAsync(
            $"/setup/accounts/{Guid.NewGuid()}",
            new UpdateAccountRequest("Fantasma", 100, null));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }
    [Fact]
    public async Task las_categorias_se_pueden_listar_con_su_identificador()
    {
        // Sin los identificadores, la pantalla de movimientos puede enseñar el
        // nombre que le llega pero no ofrecer otro: era lo que la dejaba en
        // solo lectura.
        (TestApp app, HttpClient client) = await VaciaAsync();
        await using var _1 = app;
        using var _2 = client;

        await client.PostAsync(
            new Uri("/setup/categories/defaults", UriKind.Relative), null);

        List<CategoryView> categorias =
            (await client.GetFromJsonAsync<List<CategoryView>>("/setup/categories"))!;

        Assert.NotEmpty(categorias);
        Assert.All(categorias, c => Assert.NotEqual(Guid.Empty, c.Id));
        Assert.All(categorias, c => Assert.False(string.IsNullOrWhiteSpace(c.Name)));

        // Ordenadas por prioridad: arriba lo que no se puede recortar.
        List<string> prioridades = [.. categorias.Select(c => c.Priority)];
        Assert.Equal("Essential", prioridades[0]);
    }




}
