using System.Net;
using System.Net.Http.Json;
using Margen.Api.Auth;
using Margen.Api.Endpoints;
using Margen.Api.Tests.Infra;
using Margen.Domain;

namespace Margen.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class AuthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task el_recorrido_completo_entrega_un_token_de_alcance_total()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        TokenResponse token = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);

        Assert.NotEmpty(token.Token);
        Assert.Contains(Scopes.Full, token.Scopes);
        Assert.True(token.ExpiresAt > DateTime.UtcNow);

        client.UseToken(token.Token);
        HttpResponseMessage whoami = await client.GetAsync(new Uri("/auth/whoami", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, whoami.StatusCode);
    }

    [Fact]
    public async Task una_firma_de_otra_clave_no_abre_sesion()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();
        using var impostora = new TestDeviceKey();

        Guid deviceId = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, key);
        ChallengeResponse challenge = await AuthFlow.ChallengeAsync(client, deviceId);

        HttpResponseMessage response = await AuthFlow.RedeemAsync(
            client,
            deviceId,
            challenge.Nonce,
            impostora.Sign(deviceId, challenge.Nonce));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task una_firma_de_otro_reto_no_abre_sesion()
    {
        // Firmar bien, pero un reto distinto del que se presenta. Sin esta
        // comprobación, una firma vieja capturada valdría para siempre.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        Guid deviceId = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, key);
        ChallengeResponse primero = await AuthFlow.ChallengeAsync(client, deviceId);
        ChallengeResponse segundo = await AuthFlow.ChallengeAsync(client, deviceId);

        HttpResponseMessage response = await AuthFlow.RedeemAsync(
            client,
            deviceId,
            segundo.Nonce,
            key.Sign(deviceId, primero.Nonce));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task el_mismo_reto_no_se_canjea_dos_veces()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        Guid deviceId = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, key);
        ChallengeResponse challenge = await AuthFlow.ChallengeAsync(client, deviceId);
        string firma = key.Sign(deviceId, challenge.Nonce);

        HttpResponseMessage primera = await AuthFlow.RedeemAsync(
            client, deviceId, challenge.Nonce, firma);
        HttpResponseMessage repetida = await AuthFlow.RedeemAsync(
            client, deviceId, challenge.Nonce, firma);

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, repetida.StatusCode);
    }

    [Fact]
    public async Task la_firma_de_un_dispositivo_no_vale_para_otro()
    {
        // El identificador del dispositivo va dentro del mensaje firmado. Sin
        // él, una firma válida podría presentarse en nombre de otro.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var propia = new TestDeviceKey();
        using var ajena = new TestDeviceKey();

        Guid mio = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, propia, "mío");
        Guid otro = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, ajena, "otro");

        ChallengeResponse challenge = await AuthFlow.ChallengeAsync(client, mio);

        // Firma correcta de la clave correcta, pero sobre el identificador del
        // otro dispositivo.
        HttpResponseMessage response = await AuthFlow.RedeemAsync(
            client,
            mio,
            challenge.Nonce,
            propia.Sign(otro, challenge.Nonce));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task un_reto_inventado_no_se_canjea()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        Guid deviceId = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, key);
        const string inventado = "reto-que-nunca-emitio-el-servidor";

        HttpResponseMessage response = await AuthFlow.RedeemAsync(
            client,
            deviceId,
            inventado,
            key.Sign(deviceId, inventado));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task sin_codigo_de_alta_no_se_registra_un_dispositivo()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/devices")
        {
            Content = JsonContent.Create(
                new RegisterDeviceRequest("intruso", key.PublicKeySpkiBase64)),
        };
        request.Headers.Add(AuthEndpoints.EnrollmentHeader, "codigo-equivocado");

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task un_servidor_sin_codigo_configurado_no_registra_a_nadie()
    {
        // Fallo cerrado. Con la variable de entorno sin poner, el alta no se
        // vuelve libre: se apaga.
        await using TestApp app = TestApp.WithoutEnrollmentCode(postgres.ConnectionString);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/devices")
        {
            Content = JsonContent.Create(
                new RegisterDeviceRequest("cualquiera", key.PublicKeySpkiBase64)),
        };
        request.Headers.Add(AuthEndpoints.EnrollmentHeader, string.Empty);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task una_clave_que_no_es_p256_no_se_admite()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        string spki = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());

        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/devices")
        {
            Content = JsonContent.Create(new RegisterDeviceRequest("con RSA", spki)),
        };
        request.Headers.Add(AuthEndpoints.EnrollmentHeader, app.EnrollmentCode);

        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task sin_token_no_se_entra()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/auth/whoami", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task un_token_inventado_no_se_acepta()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        client.UseToken("este-token-no-lo-emitio-nadie");

        HttpResponseMessage response = await client.GetAsync(new Uri("/auth/whoami", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task un_token_revocado_deja_de_servir_de_inmediato()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        TokenResponse token = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(token.Token);

        HttpResponseMessage antes = await client.GetAsync(new Uri("/auth/whoami", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, antes.StatusCode);

        HttpResponseMessage revocacion = await client.DeleteAsync(
            new Uri($"/auth/tokens/{token.TokenId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, revocacion.StatusCode);

        HttpResponseMessage despues = await client.GetAsync(new Uri("/auth/whoami", UriKind.Relative));

        // Es la diferencia práctica con un JWT: revocar surte efecto ahora, no
        // cuando caduque.
        Assert.Equal(HttpStatusCode.Unauthorized, despues.StatusCode);
    }

    [Fact]
    public async Task el_mismo_telefono_dado_de_alta_dos_veces_es_el_mismo_dispositivo()
    {
        // Reinstalar la app no puede dejar al usuario fuera ni crear un
        // dispositivo fantasma por cada instalación.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        Guid primera = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, key);
        Guid segunda = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, key);

        Assert.Equal(primera, segunda);
    }

    [Fact]
    public async Task el_reto_de_un_dispositivo_que_no_existe_no_se_emite()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/challenges",
            new ChallengeRequest(Guid.CreateVersion7()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
    [Fact]
    public async Task una_firma_del_navegador_tambien_vale()
    {
        // **La PWA es el único cliente que existe desde ADR-001**, y WebCrypto
        // no firma en DER: produce los dos enteros concatenados en crudo.
        //
        // La verificación solo aceptaba DER —el formato de `SecKeyCreateSignature`
        // de iOS, de cuando la app iba a ser nativa—, así que ninguna firma del
        // navegador validaba jamás. El servidor respondía «firma inválida», que
        // era justo lo que no pasaba: la firma era correcta y solo venía
        // envuelta de otra manera.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();

        Guid deviceId = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, key);
        ChallengeResponse challenge = await AuthFlow.ChallengeAsync(client, deviceId);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/tokens",
            new RedeemRequest(
                deviceId,
                challenge.Nonce,
                key.SignAsBrowser(deviceId, challenge.Nonce)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        TokenResponse token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.NotEmpty(token.Token);
    }

    [Fact]
    public async Task una_firma_de_otra_clave_sigue_sin_valer()
    {
        // Aceptar dos formatos no puede aceptar dos claves. Es la prueba de que
        // la comprobación de arriba no aflojó la verificación.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();
        using var key = new TestDeviceKey();
        using var intrusa = new TestDeviceKey();

        Guid deviceId = await AuthFlow.RegisterAsync(client, app.EnrollmentCode, key);
        ChallengeResponse challenge = await AuthFlow.ChallengeAsync(client, deviceId);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/tokens",
            new RedeemRequest(
                deviceId,
                challenge.Nonce,
                intrusa.SignAsBrowser(deviceId, challenge.Nonce)));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

}
