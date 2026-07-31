using System.Net;
using System.Net.Http.Json;
using Margen.Api.Auth;
using Margen.Api.Contracts;
using Margen.Api.Tests.Infra;

namespace Margen.Api.Tests;

/// <summary>
/// Paga la deuda m10 de CR-002.
/// </summary>
/// <remarks>
/// Cada prueba levanta su propia aplicación, así que su ventana de límite es
/// suya: no hereda las peticiones de las demás ni se las deja.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class RateLimitTests(PostgresFixture postgres)
{
    [Fact]
    public async Task el_canje_de_reto_se_corta_a_las_diez_por_minuto()
    {
        // Es el endpoint que un atacante martillearía: cada intento es una
        // firma distinta contra el mismo dispositivo.
        Seed _ = await Seed.PlantAsync(postgres.ConnectionString);
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        var codigos = new List<HttpStatusCode>();

        for (int i = 0; i < 14; i++)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/auth/tokens",
                new RedeemRequest(Guid.CreateVersion7(), "reto", "ZmlybWE="));

            codigos.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, codigos);

        // Las primeras diez sí pasan: el límite corta, no cierra.
        Assert.Equal(10, codigos.Count(c => c == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task el_alta_de_dispositivo_tambien_esta_limitada()
    {
        Seed _ = await Seed.PlantAsync(postgres.ConnectionString);
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        var codigos = new List<HttpStatusCode>();

        for (int i = 0; i < 14; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/devices")
            {
                Content = JsonContent.Create(
                    new RegisterDeviceRequest("intruso", "no-es-una-clave")),
            };
            request.Headers.Add(Endpoints.AuthEndpoints.EnrollmentHeader, "codigo-equivocado");

            HttpResponseMessage response = await client.SendAsync(request);
            codigos.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, codigos);
    }

    [Fact]
    public async Task el_registro_rapido_se_corta_a_las_treinta_por_minuto()
    {
        // Una persona no llega a treinta gastos en efectivo por minuto; un
        // Atajo en bucle, sí.
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        using var key = new TestDeviceKey();
        TokenResponse token = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(token.Token);

        var codigos = new List<HttpStatusCode>();

        for (int i = 0; i < 34; i++)
        {
            HttpResponseMessage response = await client.PostAsJsonAsync(
                "/transactions/cash",
                new CreateCashRequest(1_000 + i, $"Gasto {i}", seed.FoodId, null, null));

            codigos.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, codigos);
        Assert.Equal(30, codigos.Count(c => c == HttpStatusCode.Created));
    }

    [Fact]
    public async Task el_limite_no_alcanza_a_los_endpoints_de_lectura()
    {
        // Limitar el panel castigaría a la app por refrescar, que es lo que
        // tiene que hacer. El límite protege lo que se puede martillear desde
        // fuera, no lo que el dueño consulta.
        Seed _ = await Seed.PlantAsync(postgres.ConnectionString);
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        using var key = new TestDeviceKey();
        TokenResponse token = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
        client.UseToken(token.Token);

        for (int i = 0; i < 25; i++)
        {
            HttpResponseMessage response = await client.GetAsync(
                new Uri("/dashboard", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
