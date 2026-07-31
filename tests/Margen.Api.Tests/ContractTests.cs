using System.Net;
using System.Text.Json;
using Margen.Api.Tests.Infra;

namespace Margen.Api.Tests;

/// <summary>
/// El contrato de la API, versionado en el repositorio.
/// </summary>
/// <remarks>
/// Un contrato que solo existe en tiempo de ejecución no se puede comparar
/// entre versiones. Con el archivo commiteado, cualquier cambio en la superficie
/// aparece en el diff y hay que aceptarlo a propósito; sin él, la Fase 5 se
/// entera de que el contrato cambió cuando la app deja de funcionar.
///
/// Regenerar tras un cambio deliberado:
///   dotnet run --project src/Margen.Api -- --generar-contrato
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ContractTests(PostgresFixture postgres)
{
    [Fact]
    public async Task el_contrato_versionado_coincide_con_el_generado()
    {
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string committedPath = Path.Combine(RepositoryRoot(), "docs", "api", "openapi.json");

        Assert.True(
            File.Exists(committedPath),
            $"Falta el contrato versionado en {committedPath}. "
            + "Se genera con: dotnet run --project src/Margen.Api -- --generar-contrato");

        // La comparación es del contenido, no del texto. Sangría, orden de
        // escritura y escapes son decisiones del serializador; lo que no puede
        // cambiar sin que alguien lo acepte es qué rutas, qué campos y qué
        // tipos expone el servidor.
        string generated = Canonical(await response.Content.ReadAsStringAsync());
        string committed = Canonical(await File.ReadAllTextAsync(committedPath));

        Assert.Equal(committed, generated);
    }

    [Fact]
    public async Task ninguna_cifra_monetaria_del_contrato_es_decimal()
    {
        // Un `number` con coma en un camino monetario sería un double en
        // JavaScript, y el Atajo de iOS serializa desde JavaScript. Todo lo que
        // acaba en `Cents` tiene que ser entero.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        using JsonDocument document = JsonDocument.Parse(
            await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative)));

        var offenders = new List<string>();
        Walk(document.RootElement, string.Empty, offenders);

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task el_contrato_no_exige_token_para_leerse()
    {
        // Lo consume la generación del cliente, que corre antes de que exista
        // ningún dispositivo dado de alta.
        await using var app = new TestApp(postgres.ConnectionString, null);
        using HttpClient client = app.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Recorre el esquema buscando propiedades cuyo nombre acaba en «Cents» y
    /// cuyo tipo no es entero.
    /// </summary>
    private static void Walk(JsonElement element, string path, List<string> offenders)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Name.EndsWith("Cents", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Object
                    && property.Value.TryGetProperty("type", out JsonElement type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() is string declared
                    && declared != "integer")
                {
                    offenders.Add($"{path}.{property.Name} es {declared}");
                }

                Walk(property.Value, $"{path}.{property.Name}", offenders);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                Walk(item, $"{path}[{index++}]", offenders);
            }
        }
    }

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Reduce el documento a una forma única: sin sangría, sin saltos y con los
    /// acentos sin escapar. Dos textos distintos que digan lo mismo dan la
    /// misma cadena.
    /// </summary>
    private static string Canonical(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(document.RootElement, CanonicalOptions);
    }

    /// <summary>
    /// Sube desde el binario hasta encontrar la solución. Es lo que permite que
    /// la prueba encuentre `docs/` sin depender de la profundidad de
    /// `bin/Debug/netX`.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Margen.slnx"))
                || File.Exists(Path.Combine(directory.FullName, "Margen.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "No se encontró la raíz del repositorio desde " + AppContext.BaseDirectory);
    }
}
