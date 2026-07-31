using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Margen.Infrastructure;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Registra el <see cref="MargenDbContext"/> con la cadena de conexión de
    /// la configuración.
    /// </summary>
    /// <remarks>
    /// Sin cadena de conexión la aplicación no arranca. La alternativa —caer a
    /// una base en memoria o a localhost— produce un servidor que responde 200
    /// sin datos y que nadie nota hasta que faltan movimientos. Fallar al
    /// arrancar es ruidoso, y eso es exactamente lo que hace falta.
    /// </remarks>
    public static IServiceCollection AddMargenDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Falta la cadena de conexión '{ConnectionStringName}'. "
                + "Se pasa por la variable de entorno "
                + $"ConnectionStrings__{ConnectionStringName}.");

        services.AddDbContext<MargenDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(MargenDbContext).Assembly.FullName);

                // Un corte de red de un segundo no debe tumbar una petición.
                npgsql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(2),
                    errorCodesToAdd: null);
            }));

        return services;
    }
}
