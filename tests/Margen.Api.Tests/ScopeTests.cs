using System.Net;
using System.Net.Http.Json;
using Margen.Api.Auth;
using Margen.Api.Contracts;
using Margen.Api.Tests.Infra;
using Margen.Domain;

namespace Margen.Api.Tests;

/// <summary>
/// El token del Atajo vive en una automatización del iPhone que cualquiera con
/// el teléfono desbloqueado puede abrir y leer. Todo lo que lo hace tolerable
/// es que no alcance para nada más que registrar un gasto en efectivo.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ScopeTests(PostgresFixture postgres)
{
    /// <summary>
    /// El registro de efectivo necesita una cuenta de efectivo activa donde
    /// anotarlo. Sembrarla aquí es lo que permite afirmar un 201 exacto en vez
    /// de «cualquier cosa que no sea 403», que pasaría también con el endpoint
    /// roto.
    /// </summary>
    private Task<Seed> PlantAsync() => Seed.PlantAsync(postgres.ConnectionString);

    [Fact]
    public async Task el_token_del_atajo_lleva_un_solo_alcance()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        TokenResponse atajo = await EmitirTokenDeAtajoAsync(client, app.EnrollmentCode, key);

        Assert.Equal([Scopes.CashCreate], atajo.Scopes);
        Assert.DoesNotContain(Scopes.Full, atajo.Scopes);
    }

    [Fact]
    public async Task el_token_del_atajo_no_alcanza_mas_que_efectivo()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        TokenResponse atajo = await EmitirTokenDeAtajoAsync(client, app.EnrollmentCode, key);
        client.UseToken(atajo.Token);

        // Autenticado sí; autorizado no. 403 y no 401 es la respuesta correcta:
        // el token es válido, lo que no tiene es permiso.
        HttpResponseMessage emitirOtro = await client.PostAsJsonAsync(
            "/auth/shortcut-tokens",
            new ShortcutTokenRequest("otro más"));
        Assert.Equal(HttpStatusCode.Forbidden, emitirOtro.StatusCode);

        HttpResponseMessage listar = await client.GetAsync(new Uri("/auth/tokens", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, listar.StatusCode);

        HttpResponseMessage revocar = await client.DeleteAsync(
            new Uri($"/auth/tokens/{Guid.CreateVersion7()}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Forbidden, revocar.StatusCode);
    }

    [Fact]
    public async Task el_token_del_atajo_si_alcanza_el_registro_de_efectivo()
    {
        await PlantAsync();
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        TokenResponse atajo = await EmitirTokenDeAtajoAsync(client, app.EnrollmentCode, key);
        client.UseToken(atajo.Token);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/transactions/cash",
            new CreateCashRequest(45_000, "Almuerzo", null, null, null));

        // 201 y no 403. Si aquí saliera 403, el criterio de «alcance solo de
        // creación de efectivo» estaría cumplido por accidente —el token no
        // podría hacer nada— y no por diseño.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task el_token_de_la_app_tambien_alcanza_el_efectivo()
    {
        await PlantAsync();
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        TokenResponse completo = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(completo.Token);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/transactions/cash",
            new CreateCashRequest(51_000, "Cena", null, null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task sin_token_el_registro_de_efectivo_responde_401()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/transactions/cash",
            new CreateCashRequest(45_000, "Almuerzo", null, null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task el_token_del_atajo_se_puede_revocar_desde_la_app()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        TokenResponse completo = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(completo.Token);

        HttpResponseMessage emision = await client.PostAsJsonAsync(
            "/auth/shortcut-tokens",
            new ShortcutTokenRequest("Atajo de iOS"));
        emision.EnsureSuccessStatusCode();
        TokenResponse atajo = (await emision.Content.ReadFromJsonAsync<TokenResponse>())!;

        HttpResponseMessage revocacion = await client.DeleteAsync(
            new Uri($"/auth/tokens/{atajo.TokenId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, revocacion.StatusCode);

        using HttpClient conAtajo = app.CreateClient();
        conAtajo.UseToken(atajo.Token);

        HttpResponseMessage response = await conAtajo.PostAsJsonAsync(
            "/transactions/cash",
            new CreateCashRequest(45_000, "Almuerzo", null, null, null));

        // El teléfono se perdió y el token muere hoy, no dentro de un año.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task un_dispositivo_no_puede_revocar_el_token_de_otro()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var mia = new TestDeviceKey();
        using var ajena = new TestDeviceKey();

        TokenResponse mio = await AuthFlow.SignInAsync(client, app.EnrollmentCode, mia);

        using HttpClient otroCliente = app.CreateClient();
        TokenResponse ajeno = await AuthFlow.SignInAsync(otroCliente, app.EnrollmentCode, ajena);

        client.UseToken(mio.Token);
        HttpResponseMessage intento = await client.DeleteAsync(
            new Uri($"/auth/tokens/{ajeno.TokenId}", UriKind.Relative));

        // La revocación responde 204 siempre —es idempotente— pero no toca lo
        // que no es suyo.
        Assert.Equal(HttpStatusCode.NoContent, intento.StatusCode);

        otroCliente.UseToken(ajeno.Token);
        HttpResponseMessage sigueVivo = await otroCliente.GetAsync(
            new Uri("/auth/whoami", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, sigueVivo.StatusCode);
    }

    private static async Task<TokenResponse> EmitirTokenDeAtajoAsync(
        HttpClient client,
        string enrollmentCode,
        TestDeviceKey key)
    {
        TokenResponse completo = await AuthFlow.SignInAsync(client, enrollmentCode, key);
        client.UseToken(completo.Token);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/shortcut-tokens",
            new ShortcutTokenRequest("Atajo de iOS"));

        response.EnsureSuccessStatusCode();
        TokenResponse atajo = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;

        client.DefaultRequestHeaders.Authorization = null;
        return atajo;
    }
}
