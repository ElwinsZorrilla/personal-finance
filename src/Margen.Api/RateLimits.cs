using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Margen.Api;

/// <summary>
/// Límite de peticiones. Paga la deuda m10 de CR-002.
/// </summary>
/// <remarks>
/// La Fase 2 lo dejó anotado en vez de escribirlo, porque un limitador sobre
/// endpoints sin tráfico es código que no se puede comprobar. Ahora hay dos
/// superficies que proteger y las dos tienen prueba de que devuelven 429.
///
/// La partición es por dirección de origen y no por dispositivo: la
/// autenticación se limita **antes** de saber quién llama, que es justo cuando
/// hay que limitarla.
/// </remarks>
public static class RateLimits
{
    /// <summary>Alta de dispositivo, reto y canje de firma.</summary>
    public const string Authentication = "auth";

    /// <summary>Registro rápido de efectivo desde el Atajo.</summary>
    public const string QuickEntry = "quick-entry";

    public static IServiceCollection AddMargenRateLimiting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRateLimiter(options =>
        {
            // 429 y no 503: el cliente puede reintentar más tarde y la
            // diferencia le dice si el problema es suyo o del servidor.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(Authentication, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(http),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),

                        // Sin cola. Encolar intentos de autenticación solo
                        // retrasa el rechazo y mantiene abierta la conexión de
                        // quien está probando códigos.
                        QueueLimit = 0,
                    }));

            options.AddPolicy(QuickEntry, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    PartitionKey(http),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        // Treinta gastos en efectivo por minuto. Una persona no
                        // llega ahí; un Atajo en bucle, sí.
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    /// <summary>
    /// La dirección de origen, o una constante si no se puede leer.
    /// </summary>
    /// <remarks>
    /// Detrás de Nginx Proxy Manager, <c>RemoteIpAddress</c> es la del proxy si
    /// no se procesan las cabeceras reenviadas. El servidor las procesa —ver
    /// <c>Program.cs</c>— pero aun así el peor caso de esta función es que
    /// todos caigan en la misma partición, que limita de más y nunca de menos.
    /// </remarks>
    private static string PartitionKey(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "sin-origen";
}
