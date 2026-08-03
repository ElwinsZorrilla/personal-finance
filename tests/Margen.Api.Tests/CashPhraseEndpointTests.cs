using System.Net;
using System.Net.Http.Json;
using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Api.Tests.Infra;
using Margen.Classify;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Tests;

/// <summary>
/// La puerta de la frase, contra el servidor y la base de verdad.
/// </summary>
/// <remarks>
/// `CashPhrase` está probado a fondo sin servidor en `Margen.Ingest.Tests`.
/// Aquí se comprueba lo que solo se ve con las dos cosas montadas: qué se
/// guarda, qué saldo queda, qué clasifica la cascada y qué pasa cuando el
/// Atajo reintenta.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CashPhraseEndpointTests(PostgresFixture postgres)
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

    private static Task<HttpResponseMessage> DecirAsync(HttpClient client, string frase) =>
        client.PostAsJsonAsync("/transactions/cash/phrase", new CreateCashPhraseRequest(frase));

    // ---------- El camino feliz ----------

    [Fact]
    public async Task una_frase_crea_el_gasto_con_el_monto_exacto()
    {
        (TestApp app, HttpClient client, _) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await DecirAsync(client, "Gasté 450 pesos en almuerzo");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        TransactionView? view = await response.Content.ReadFromJsonAsync<TransactionView>();

        Assert.NotNull(view);
        Assert.Equal(45_000, view.AmountCents);
        Assert.Equal("almuerzo", view.Merchant, ignoreCase: true);
        Assert.Equal(nameof(TxSource.Shortcut), view.Source);
        Assert.Equal(nameof(TxKind.Cash), view.Kind);
        Assert.Equal("Egreso", view.DirectionLabel);
    }

    [Fact]
    public async Task el_gasto_baja_el_saldo_de_la_cuenta_de_efectivo()
    {
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        await using MargenDbContext db = Db();
        Money antes = await db.Accounts
            .AsNoTracking().Where(a => a.Id == seed.CashId).Select(a => a.Balance).SingleAsync();

        await DecirAsync(client, "Gasté 450 pesos en almuerzo");

        await using MargenDbContext db2 = Db();
        Money despues = await db2.Accounts
            .AsNoTracking().Where(a => a.Id == seed.CashId).Select(a => a.Balance).SingleAsync();

        Assert.Equal(antes - new Money(45_000), despues);
    }

    [Fact]
    public async Task ayer_registra_el_gasto_en_el_dia_local_de_ayer()
    {
        // Un gasto de las once de la noche tiene que caer en el día que ve el
        // usuario, no en el siguiente de UTC.
        (TestApp app, HttpClient client, _) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await DecirAsync(client, "Gasté 450 ayer en almuerzo");
        TransactionView view = (await response.Content.ReadFromJsonAsync<TransactionView>())!;

        DateOnly hoy = LocalTime.LocalDateOf(DateTime.UtcNow);

        Assert.Equal(hoy.AddDays(-1), view.OccurredOn);
    }

    // ---------- La cascada clasifica lo que entra por aquí ----------

    [Fact]
    public async Task una_regla_del_usuario_clasifica_el_gasto_de_efectivo()
    {
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        await using (MargenDbContext db = Db())
        {
            db.MerchantRules.Add(new MerchantRule
            {
                Id = Guid.CreateVersion7(),
                Pattern = "ALMUERZO",
                MatchKind = MatchKind.Exact,
                CategoryId = seed.FoodId,
                Weight = Corrections.UserWeight,
                IsUserDefined = true,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        HttpResponseMessage response = await DecirAsync(client, "Gasté 450 pesos en almuerzo");
        TransactionView view = (await response.Content.ReadFromJsonAsync<TransactionView>())!;

        Assert.Equal(seed.FoodId, view.CategoryId);
        Assert.Equal(nameof(TxStatus.Posted), view.Status);
        Assert.Equal(nameof(ClassifiedBy.UserRule), view.ClassificationSource);
    }

    [Fact]
    public async Task sin_nada_que_lo_reconozca_el_gasto_queda_en_revision()
    {
        // Fallo cerrado, igual que en la ingesta de correo: no se inventa una
        // categoría para poder cerrar el movimiento.
        (TestApp app, HttpClient client, _) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await DecirAsync(client, "Gasté 450 en chichigua");
        TransactionView view = (await response.Content.ReadFromJsonAsync<TransactionView>())!;

        Assert.Null(view.CategoryId);
        Assert.Equal(nameof(TxStatus.NeedsReview), view.Status);
        Assert.False(view.IsCategoryConfirmed);
    }

    // ---------- Lo que no se entiende no crea nada ----------

    [Theory]
    [InlineData("gasté en almuerzo", "monto")]
    [InlineData("Gasté mil pesos en almuerzo", "monto")]
    [InlineData("gasté 450 pesos", "gastaste")]
    [InlineData("Gasté 20 dólares en el aeropuerto", "pesos dominicanos")]
    [InlineData("", "")]
    public async Task una_frase_que_no_se_entiende_responde_400_y_no_guarda_nada(
        string frase, string pista)
    {
        (TestApp app, HttpClient client, _) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await DecirAsync(client, frase);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // El mensaje se enseña en el teléfono: la persona está de pie en la
        // calle y necesita saber si se registró o no, y qué arreglar.
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains(pista, body, StringComparison.OrdinalIgnoreCase);

        await using MargenDbContext db = Db();
        Assert.Equal(0, await db.Transactions.CountAsync(t => t.Source == TxSource.Shortcut));
    }

    [Fact]
    public async Task un_gasto_rechazado_no_toca_el_saldo()
    {
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        await using MargenDbContext db = Db();
        Money antes = await db.Accounts
            .AsNoTracking().Where(a => a.Id == seed.CashId).Select(a => a.Balance).SingleAsync();

        await DecirAsync(client, "Gasté 20 dólares en el aeropuerto");

        await using MargenDbContext db2 = Db();
        Money despues = await db2.Accounts
            .AsNoTracking().Where(a => a.Id == seed.CashId).Select(a => a.Balance).SingleAsync();

        Assert.Equal(antes, despues);
    }

    [Fact]
    public async Task un_monto_por_encima_del_tope_se_rechaza()
    {
        // El tope no es una regla de negocio: es un cortafuegos. El token vive
        // en el teléfono y el peor caso tiene que ser molesto, no caro.
        (TestApp app, HttpClient client, _) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage response = await DecirAsync(client, "Gasté 500,000 pesos en un carro");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- El reintento del Atajo ----------

    [Fact]
    public async Task el_mismo_gasto_dos_veces_no_se_registra_dos_veces()
    {
        // El Atajo reintenta cuando la red falla a medio camino. Sin esto, la
        // persona vería el doble de lo que gastó.
        (TestApp app, HttpClient client, _) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage primera = await DecirAsync(client, "Gasté 450 pesos en almuerzo");
        HttpResponseMessage segunda = await DecirAsync(client, "Gasté 450 pesos en almuerzo");

        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);

        await using MargenDbContext db = Db();
        Assert.Equal(1, await db.Transactions.CountAsync(t => t.Source == TxSource.Shortcut));
    }

    [Fact]
    public async Task el_reintento_tampoco_descuenta_el_saldo_dos_veces()
    {
        // Es lo que de verdad hace daño: el movimiento repetido se ve en la
        // lista, un saldo descontado dos veces no se ve en ninguna parte.
        (TestApp app, HttpClient client, Seed seed) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        await using MargenDbContext db = Db();
        Money antes = await db.Accounts
            .AsNoTracking().Where(a => a.Id == seed.CashId).Select(a => a.Balance).SingleAsync();

        await DecirAsync(client, "Gasté 450 pesos en almuerzo");
        await DecirAsync(client, "Gasté 450 pesos en almuerzo");

        await using MargenDbContext db2 = Db();
        Money despues = await db2.Accounts
            .AsNoTracking().Where(a => a.Id == seed.CashId).Select(a => a.Balance).SingleAsync();

        Assert.Equal(antes - new Money(45_000), despues);
    }

    [Fact]
    public async Task dos_gastos_distintos_el_mismo_dia_si_entran_los_dos()
    {
        (TestApp app, HttpClient client, _) = await ArrangeAsync();
        await using var _1 = app;
        using var _2 = client;

        HttpResponseMessage almuerzo = await DecirAsync(client, "Gasté 450 pesos en almuerzo");
        HttpResponseMessage pasaje = await DecirAsync(client, "Gasté 50 pesos en pasaje");

        Assert.Equal(HttpStatusCode.Created, almuerzo.StatusCode);
        Assert.Equal(HttpStatusCode.Created, pasaje.StatusCode);
    }

    // ---------- El alcance del token ----------

    [Fact]
    public async Task el_token_del_atajo_alcanza_la_frase()
    {
        // Si no alcanzara, el Atajo no serviría; y si alcanzara de más, el
        // token dejaría de ser tolerable en un teléfono.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        TokenResponse completo = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(completo.Token);

        HttpResponseMessage emitido = await client.PostAsJsonAsync(
            "/auth/shortcut-tokens", new ShortcutTokenRequest("iPhone"));
        TokenResponse atajo = (await emitido.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.UseToken(atajo.Token);

        HttpResponseMessage response = await DecirAsync(client, "Gasté 450 pesos en almuerzo");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal([Scopes.CashCreate], atajo.Scopes);
    }

    [Fact]
    public async Task sin_token_la_frase_no_entra()
    {
        await Seed.PlantAsync(postgres.ConnectionString);
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await DecirAsync(client, "Gasté 450 pesos en almuerzo");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
