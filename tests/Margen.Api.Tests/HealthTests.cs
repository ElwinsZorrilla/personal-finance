using System.Net;
using Margen.Api.Tests.Infra;

namespace Margen.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class HealthTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ready_responde_200_con_la_base_conectada()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ready_responde_503_sin_la_base()
    {
        // Un puerto donde no hay nadie escuchando, con expiración corta para
        // que la prueba no se quede esperando el sistema operativo.
        const string muerta =
            "Host=127.0.0.1;Port=1;Database=margen;Username=margen;Timeout=1;Command Timeout=2";

        await using var app = new TestApp(muerta, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task live_responde_200_aunque_la_base_este_caida()
    {
        // Vivo y listo son preguntas distintas. Si fueran la misma, el
        // supervisor reiniciaría el contenedor cada vez que la base tarda en
        // arrancar, y así nunca llegaría a arrancar.
        const string muerta =
            "Host=127.0.0.1;Port=1;Database=margen;Username=margen;Timeout=1;Command Timeout=2";

        await using var app = new TestApp(muerta, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task la_salud_no_exige_token()
    {
        // La política por defecto exige autenticación. Los dos endpoints de
        // salud llevan AllowAnonymous porque el proxy los consulta sin
        // credenciales; esta prueba es lo que impide que un cambio en la
        // política los cierre en silencio y el contenedor quede marcado como
        // enfermo para siempre.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));
        HttpResponseMessage live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.Unauthorized, ready.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, live.StatusCode);
    }
}
