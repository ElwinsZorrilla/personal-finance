using Margen.Api;
using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Endpoints;
using Margen.Api.Health;
using Margen.Infrastructure;
using Margen.Infrastructure.Classification;
using Margen.Infrastructure.Statements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Qué se va a hacer, antes de montar nada. Regenerar el contrato lee el mapa de
// rutas y no toca una sola fila, así que exigirle una cadena de conexión lo
// hacía fallar por algo que no usa. Mismo error que tenía la captura de
// muestras del worker, mismo arreglo: decidir el modo primero.
bool generarContrato = args.Contains("--generar-contrato", StringComparer.Ordinal);

if (generarContrato)
{
    // Se registra el contexto **sin proveedor**: el contrato sale de recorrer el
    // mapa de rutas y nunca se abre una conexión. Resolverlo lanzaría, y por eso
    // es la forma correcta de decirlo —si algún día generar el contrato acabara
    // tocando la base, esto falla en vez de conectarse a algo a escondidas—.
    //
    // Y no una cadena de mentira en el fuente: un marcador con forma de
    // credencial es lo que alguien rellena con la de verdad sin pensarlo.
    builder.Services.AddDbContext<MargenDbContext>();
}
else
{
    builder.Services.AddMargenDatabase(builder.Configuration);
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<DeviceService>();
builder.Services.AddScoped<BudgetAssembler>();

// Clasificación. Sin ICategorySuggester registrado, la cascada corre sus cuatro
// escalones locales y se salta el modelo: no hay proveedor decidido, y enchufar
// uno a escondidas sería tomar esa decisión sin que nadie la vea.
builder.Services.AddScoped<RuleWriter>();
builder.Services.AddScoped<TransactionClassifier>();
builder.Services.AddScoped<AnomalyScanner>();
builder.Services.AddScoped<StatementService>();

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

builder.Services.AddMargenCors(builder.Configuration);
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

// Tampoco el migrador: generar el contrato arrancaba el servicio de migración,
// que intentaba conectarse y dejaba una excepción en la salida. El archivo se
// escribía igual, que es la peor forma de fallar —parece que fue bien—.
if (!generarContrato)
{
    builder.Services.AddHostedService<MigrationHostedService>();
}

// Toda respuesta de error sale como ProblemDetails. Sin esto, una excepción sin
// atrapar devuelve la página de error de ASP.NET Core con la traza dentro.
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

// CORS **antes** del límite de peticiones y de la autorización, y no es
// preferencia de estilo: el preflight `OPTIONS` que manda el navegador no lleva
// token, así que puesto después lo rechazaría la autorización con un 401 y el
// navegador cancelaría la petición de verdad sin llegar a hacerla. Y contar los
// preflight contra el límite gastaría la mitad del presupuesto en peticiones
// que no piden nada.
app.UseCors(Cors.PolicyName);

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
app.MapStatementEndpoints();
app.MapPeriodEndpoints();

// El documento se sirve para poder generarlo y versionarlo, no para publicar
// una consola interactiva: una interfaz de exploración es superficie expuesta
// sin dueño y este servidor solo lo consume una app.
app.MapOpenApi("/openapi/v1.json").AllowAnonymous();

// Regenera `docs/api/openapi.json` y sale, sin escuchar en ningún puerto:
//   dotnet run --project src/Margen.Api -- --generar-contrato
// Se hace desde la aplicación real y no desde una herramienta aparte para que
// el archivo versionado no pueda divergir de lo que el servidor sirve.
if (generarContrato)
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
