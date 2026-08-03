using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Margen.Api.Tests.Infra;

/// <summary>La aplicación real, levantada en proceso contra la base del contenedor.</summary>
public sealed class TestApp(string connectionString, string? enrollmentCode)
    : WebApplicationFactory<Program>
{
    /// <summary>
    /// El código de alta de las pruebas. Se genera aquí y solo existe en
    /// memoria: no hay ninguno escrito en el árbol.
    /// </summary>
    public string EnrollmentCode { get; } = enrollmentCode ?? Guid.NewGuid().ToString("N");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting($"Auth:{nameof(Api.Auth.AuthOptions.EnrollmentCode)}", EnrollmentCode);
        builder.UseSetting(Api.Cors.ConfigurationKey, "https://margen.ejemplo.do");

        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }

    /// <summary>
    /// Aplicación sin código de alta configurado: es el servidor recién
    /// desplegado al que se le olvidó la variable de entorno.
    /// </summary>
    public static TestApp WithoutEnrollmentCode(string connectionString) =>
        new(connectionString, string.Empty);
}
