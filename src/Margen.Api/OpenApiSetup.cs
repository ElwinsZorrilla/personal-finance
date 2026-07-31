using Microsoft.OpenApi;

namespace Margen.Api;

public static class OpenApiSetup
{
    public static IServiceCollection AddMargenOpenApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Margen",
                Version = "v1",
                Description =
                    "Asistente financiero personal. Todos los montos viajan como "
                    + "entero de centavos; el servidor no emite decimales en ningún "
                    + "camino monetario.",
            };

            // La lista de servidores la rellena ASP.NET Core con la dirección
            // por la que llegó la petición. En un documento versionado eso es
            // ruido —cambia con el puerto efímero de cada ejecución— y además
            // es una URL incrustada, que es precisamente lo que este proyecto
            // no admite en el código. La dirección del servidor la sabe el
            // cliente por `--dart-define`, no por el contrato.
            document.Servers?.Clear();

            return Task.CompletedTask;
        }));

        return services;
    }
}
