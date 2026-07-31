using System.Text.Json;

namespace Margen.Api;

/// <summary>
/// Vuelca el documento OpenAPI a <c>docs/api/openapi.json</c>.
/// </summary>
/// <remarks>
/// Arranca la aplicación en un puerto efímero de bucle local, pide el documento
/// por su propia ruta y escribe el resultado. Da un rodeo a propósito: generar
/// el documento por otro camino produciría un archivo que *se parece* al que
/// sirve el servidor, y la prueba que los compara dejaría de significar nada.
/// </remarks>
internal static class ContractWriter
{
    /// <summary>
    /// Con sangría y sin escapar los acentos. El codificador por defecto
    /// convierte «ningún» en «ningún», que es correcto y ilegible: este
    /// archivo se lee en un diff cuando alguien cambia el contrato.
    /// </summary>
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task WriteAsync(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");

        await app.StartAsync();

        try
        {
            string origin = app.Urls.First();

            using var client = new HttpClient { BaseAddress = new Uri(origin) };
            string json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative));

            // Se reescribe con sangría para que el diff de un cambio de
            // contrato se pueda leer línea a línea en vez de como una sola.
            using JsonDocument document = JsonDocument.Parse(json);
            string pretty = JsonSerializer.Serialize(document.RootElement, Indented);

            string path = Path.Combine(RepositoryRoot(), "docs", "api", "openapi.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Salto de línea LF y uno final: es lo que dice `.gitattributes`.
            await File.WriteAllTextAsync(
                path,
                pretty.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n");

            Console.WriteLine($"Contrato escrito en {path}");
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

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
            "No se encontró la raíz del repositorio desde "
            + Directory.GetCurrentDirectory());
    }
}
