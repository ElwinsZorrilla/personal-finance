using Margen.Api.Auth;
using Margen.Api.Endpoints;
using Margen.Api.Health;
using Margen.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddMargenDatabase(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<DeviceService>();

builder.Services.AddOptions<AuthOptions>()
    .Bind(builder.Configuration.GetSection(AuthOptions.SectionName));

builder.Services
    .AddAuthentication(TokenAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, TokenAuthenticationHandler>(
        TokenAuthenticationHandler.SchemeName,
        configureOptions: null);

builder.Services.AddAuthorizationBuilder().AddMargenPolicies();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseReadyCheck>("base", tags: ["ready"]);

builder.Services.AddHostedService<MigrationHostedService>();

// Toda respuesta de error sale como ProblemDetails. Sin esto, una excepción sin
// atrapar devuelve la página de error de ASP.NET Core con la traza dentro.
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

// Vivo es «el proceso responde». Se separa de listo a propósito: si el proxy
// reiniciara el contenedor cada vez que la base tarda en arrancar, nunca
// llegaría a arrancar.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous();

// Listo es «puedo atender». Sin base o con el esquema atrasado, 503.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResultStatusCodes = new Dictionary<HealthStatus, int>
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
}).AllowAnonymous();

app.MapAuthEndpoints();
app.MapCashEndpoints();

await app.RunAsync();

/// <summary>
/// Visible para que las pruebas de integración puedan levantar la aplicación
/// en proceso con <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program;
