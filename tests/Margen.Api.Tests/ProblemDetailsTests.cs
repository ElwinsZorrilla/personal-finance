using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Margen.Api.Contracts;
using Margen.Api.Tests.Infra;

namespace Margen.Api.Tests;

/// <summary>
/// Todo error sale como ProblemDetails y ninguno lleva traza.
/// </summary>
/// <remarks>
/// La aplicación de prueba corre en entorno de producción a propósito
/// (<see cref="TestApp"/>): en desarrollo, ASP.NET Core añade la traza a la
/// respuesta, y una prueba que corriera en desarrollo diría que todo está bien
/// mientras el servidor real filtra rutas de archivos y nombres de método.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ProblemDetailsTests(PostgresFixture postgres)
{
    private static readonly string[] Forbidden =
    [
        "StackTrace", "stackTrace", "at Margen.", "Npgsql.", "C:\\\\",
        "Microsoft.EntityFrameworkCore", "ConnectionString", "Password",
    ];

    [Fact]
    public async Task un_401_sale_como_problem_details()
    {
        Seed _ = await Seed.PlantAsync(postgres.ConnectionString);
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/dashboard", UriKind.Relative));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task un_404_sale_como_problem_details()
    {
        await using Fixture f = await Fixture.CreateAsync(postgres.ConnectionString);

        HttpResponseMessage response = await f.Client.GetAsync(
            new Uri($"/transactions/{Guid.CreateVersion7()}", UriKind.Relative));

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task un_400_sale_como_problem_details()
    {
        await using Fixture f = await Fixture.CreateAsync(postgres.ConnectionString);

        HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            "/transactions/cash",
            new CreateCashRequest(0, "Algo", null, null, null));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task un_409_sale_como_problem_details()
    {
        await using Fixture f = await Fixture.CreateAsync(postgres.ConnectionString);

        HttpResponseMessage response = await f.Client.PostAsJsonAsync(
            "/budget/redistribute",
            new RedistributeRequest(f.Seed.RentId, f.Seed.FoodId, 100_000));

        await AssertProblemAsync(response, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task una_ruta_que_no_existe_sale_como_problem_details_y_no_como_pagina()
    {
        await using Fixture f = await Fixture.CreateAsync(postgres.ConnectionString);

        HttpResponseMessage response = await f.Client.GetAsync(
            new Uri("/no-existe-esta-ruta", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertNoLeakAsync(response);
    }

    [Fact]
    public async Task un_cuerpo_json_ilegible_no_devuelve_una_traza()
    {
        // Es el 500 más fácil de provocar desde fuera y el que más filtra:
        // la excepción de deserialización lleva el nombre del tipo y la ruta
        // del archivo donde se declaró.
        await using Fixture f = await Fixture.CreateAsync(postgres.ConnectionString);

        using var content = new StringContent(
            "{ esto no es json",
            System.Text.Encoding.UTF8,
            "application/json");

        HttpResponseMessage response = await f.Client.PostAsync(
            new Uri("/transactions/cash", UriKind.Relative),
            content);

        Assert.True(
            (int)response.StatusCode >= 400,
            $"Se esperaba un error y llegó {(int)response.StatusCode}.");

        await AssertNoLeakAsync(response);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(body);

        // Los campos que ProblemDetails obliga a llevar. Sin `status`, el
        // cliente tendría que fiarse del código HTTP y del cuerpo por separado.
        Assert.True(document.RootElement.TryGetProperty("title", out _));
        Assert.True(document.RootElement.TryGetProperty("status", out JsonElement status));
        Assert.Equal((int)expected, status.GetInt32());

        AssertNoLeak(body);
    }

    private static async Task AssertNoLeakAsync(HttpResponseMessage response) =>
        AssertNoLeak(await response.Content.ReadAsStringAsync());

    private static void AssertNoLeak(string body)
    {
        foreach (string needle in Forbidden)
        {
            Assert.DoesNotContain(needle, body, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class Fixture(TestApp app, HttpClient client, Seed seed) : IAsyncDisposable
    {
        public HttpClient Client => client;

        public Seed Seed => seed;

        public static async Task<Fixture> CreateAsync(string connectionString)
        {
            Seed seed = await Seed.PlantAsync(connectionString);
            var app = new TestApp(connectionString, null);
            HttpClient client = app.CreateClient();

            using var key = new TestDeviceKey();
            Auth.TokenResponse token = await AuthFlow.SignInAsync(client, app.EnrollmentCode, key);
            client.UseToken(token.Token);

            return new Fixture(app, client, seed);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await app.DisposeAsync();
        }
    }
}
