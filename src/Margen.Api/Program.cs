using Margen.Api;
using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Endpoints;
using Margen.Api.Health;
using Margen.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddMargenDatabase(builder.Configuration);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<DeviceService>();
builder.Services.AddScoped<BudgetAssembler>();

// El único camino de entrada es Nginx Proxy Manager. Sin procesar las cabeceras
// reenviadas, la dirección de origen de toda petición sería la del proxy y el
// límite de peticiones metería a todo el mundo en la misma partición.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // El proxy es de confianza y está en la red interna del stack; la lista
    // vacía desactiva la comprobación de red conocida, que en Docker no acierta
    // porque la dirección del proxy cambia con cada despliegue.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddMargenRateLimiting();
builder.Services.AddMargenOpenApi();

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

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseRateLimiter();
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
app.MapDashboardEndpoints();
app.MapTransactionEndpoints();
app.MapCashEndpoints();
app.MapBudgetEndpoints();
app.MapReviewEndpoints();
app.MapNotificationEndpoints();
app.MapRuleEndpoints();
app.MapEmailEndpoints();
app.MapReconciliationEndpoints();

// El documento se sirve para poder generarlo y versionarlo, no para publicar
// una consola interactiva: una interfaz de exploración es superficie expuesta
// sin dueño y este servidor solo lo consume una app.
app.MapOpenApi("/openapi/v1.json").AllowAnonymous();

// Regenera `docs/api/openapi.json` y sale, sin escuchar en ningún puerto:
//   dotnet run --project src/Margen.Api -- --generar-contrato
// Se hace desde la aplicación real y no desde una herramienta aparte para que
// el archivo versionado no pueda divergir de lo que el servidor sirve.
if (args.Contains("--generar-contrato", StringComparer.Ordinal))
{
    await ContractWriter.WriteAsync(app);
    return;
}

await app.RunAsync();

/// <summary>
/// Visible para que las pruebas de integración puedan levantar la aplicación
/// en proceso con <c>WebApplicationFactory</c>.
/// </summary>
public partial class Program;
