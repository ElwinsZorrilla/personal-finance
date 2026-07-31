using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Health;

/// <summary>
/// Aplica las migraciones pendientes al arrancar.
/// </summary>
/// <remarks>
/// No bloquea el arranque ni tumba el proceso si falla, y eso es deliberado.
/// Con la base caída, un servidor que no arranca no puede decir por qué; uno
/// que arranca y responde 503 en <c>/health/ready</c> sí, y el proxy no le
/// manda tráfico igual. Cuando la base vuelve, la migración se reintenta.
/// </remarks>
public sealed class MigrationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<MigrationHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    private const int MaxAttempts = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        for (int attempt = 1; attempt <= MaxAttempts && !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                using IServiceScope scope = scopeFactory.CreateScope();
                MargenDbContext db = scope.ServiceProvider.GetRequiredService<MargenDbContext>();

                await db.Database.MigrateAsync(stoppingToken).ConfigureAwait(false);

                Logs.MigrationsApplied(logger, attempt);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Logs.MigrationAttemptFailed(logger, ex, attempt, MaxAttempts);

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        Logs.MigrationsGaveUp(logger, MaxAttempts);
    }
}
