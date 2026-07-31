using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Margen.Api.Health;

/// <summary>
/// Listo significa dos cosas: la base responde y el esquema está al día.
/// </summary>
/// <remarks>
/// Comprobar solo la conexión deja pasar el caso peor: un contenedor nuevo
/// contra una base vieja, que responde a todo y falla en la primera consulta
/// que toque una columna que todavía no existe. El proxy lo daría por sano y
/// mandaría tráfico.
/// </remarks>
public sealed class DatabaseReadyCheck(MargenDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IEnumerable<string> pending = await db.Database
                .GetPendingMigrationsAsync(cancellationToken)
                .ConfigureAwait(false);

            var pendingList = pending.ToList();
            if (pendingList.Count > 0)
            {
                return HealthCheckResult.Unhealthy(
                    $"Faltan {pendingList.Count} migraciones por aplicar: "
                    + string.Join(", ", pendingList));
            }

            return HealthCheckResult.Healthy("Base conectada y esquema al día.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El mensaje de la excepción puede traer la cadena de conexión. Se
            // registra en el servidor; a quien pregunta le llega solo que no.
            return HealthCheckResult.Unhealthy("La base de datos no responde.", ex);
        }
    }
}
