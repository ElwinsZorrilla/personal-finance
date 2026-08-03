using System.Net;
using Margen.Api.Tests.Infra;

namespace Margen.Api.Tests;

/// <summary>
/// La PWA vive en otro origen que su API, y el navegador lo comprueba.
/// </summary>
/// <remarks>
/// Estas pruebas existen desde que ADR-001 se decidió por PWA. Con app nativa el
/// concepto de origen no existe; servida en un navegador, **sin CORS la app
/// carga bien y cada petición muere**, con el error solo en la consola del
/// navegador y en ningún registro del servidor.
///
/// Se descubrió desplegando: el preflight devolvía 401 porque la autorización lo
/// atendía antes que nada.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class CorsTests(PostgresFixture postgres)
{
    private const string Permitido = "https://margen.ejemplo.do";

    [Fact]
    public async Task el_preflight_no_necesita_token()
    {
        // El `OPTIONS` que manda el navegador **no lleva el token**. Si la
        // autorización lo atiende primero responde 401, el navegador cancela y
        // la petición de verdad no llega a hacerse nunca.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        using var peticion = new HttpRequestMessage(HttpMethod.Options, "/dashboard");
        peticion.Headers.Add("Origin", Permitido);
        peticion.Headers.Add("Access-Control-Request-Method", "GET");
        peticion.Headers.Add("Access-Control-Request-Headers", "authorization");

        HttpResponseMessage response = await client.SendAsync(peticion);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(
            response.Headers.Contains("Access-Control-Allow-Origin"),
            "el preflight no autorizó el origen");
    }

    [Fact]
    public async Task el_origen_de_la_pwa_se_permite()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        peticion.Headers.Add("Origin", Permitido);

        HttpResponseMessage response = await client.SendAsync(peticion);

        Assert.Equal(
            Permitido,
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task un_origen_cualquiera_no_se_permite()
    {
        // **Nunca comodín.** Este API se autentica con un token en una cabecera;
        // abrirlo a cualquier origen dejaría que una página cualquiera hiciera
        // peticiones con el token de quien la visite.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        peticion.Headers.Add("Origin", "https://sitio-cualquiera.example");

        HttpResponseMessage response = await client.SendAsync(peticion);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task la_cabecera_de_autorizacion_esta_permitida()
    {
        // Es donde viaja el token. Sin permitirla, el preflight autoriza el
        // método y el navegador bloquea igual.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        using var peticion = new HttpRequestMessage(HttpMethod.Options, "/dashboard");
        peticion.Headers.Add("Origin", Permitido);
        peticion.Headers.Add("Access-Control-Request-Method", "GET");
        peticion.Headers.Add("Access-Control-Request-Headers", "authorization");

        HttpResponseMessage response = await client.SendAsync(peticion);

        string permitidas = string.Join(
            ",",
            response.Headers.TryGetValues("Access-Control-Allow-Headers", out var v)
                ? v
                : []);

        Assert.Contains("Authorization", permitidas, StringComparison.OrdinalIgnoreCase);
    }
}
