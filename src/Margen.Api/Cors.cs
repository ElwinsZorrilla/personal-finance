namespace Margen.Api;

/// <summary>
/// Permiso para que la PWA hable con su API.
/// </summary>
/// <remarks>
/// Existe desde que ADR-001 se decidió por PWA. Una app nativa habla con el API
/// sin que el concepto de «origen» exista; una servida en un navegador desde
/// `margen.dominio` y que llama a `margen-api.dominio` hace **peticiones entre
/// orígenes distintos**, y sin esto el navegador las bloquea todas.
///
/// El fallo que evita es de los caros: la app carga bien, la pantalla se pinta,
/// y cada petición muere en el navegador. El error sale en la consola del
/// navegador y **en ningún registro del servidor**, así que desde el lado del
/// servidor parece que nadie ha entrado.
/// </remarks>
public static class Cors
{
    public const string PolicyName = "pwa";

    /// <summary>
    /// De dónde salen los orígenes permitidos: `Cors__AllowedOrigins`, separados
    /// por comas.
    /// </summary>
    public const string ConfigurationKey = "Cors:AllowedOrigins";

    /// <summary>
    /// Registra la política.
    /// </summary>
    /// <remarks>
    /// **Lista explícita, nunca comodín.** Este API se autentica con un token en
    /// una cabecera; abrirlo a cualquier origen dejaría que una página
    /// cualquiera hiciera peticiones con el token de quien la visite. Con lista
    /// vacía no se permite ninguno, que es el estado correcto de un servidor al
    /// que todavía no se le ha dicho quién es su cliente.
    /// </remarks>
    public static IServiceCollection AddMargenCors(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string[] origins =
        [
            .. (configuration[ConfigurationKey] ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
        ];

        return services.AddCors(options => options.AddPolicy(
            PolicyName,
            policy =>
            {
                if (origins.Length == 0) return;

                policy
                    .WithOrigins(origins)
                    .WithMethods("GET", "POST", "PUT", "DELETE")

                    // `Authorization` porque el token viaja ahí, y `Content-Type`
                    // porque sin él el navegador no deja mandar JSON. Enumerarlas
                    // en vez de permitir cualquiera hace que añadir una cabecera
                    // nueva sea una decisión y no un descuido.
                    .WithHeaders("Authorization", "Content-Type")

                    // La respuesta del preflight se guarda diez minutos. Sin
                    // esto, cada petición del panel son dos viajes.
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            }));
    }
}
