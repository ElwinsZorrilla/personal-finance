using System.Security.Claims;
using Margen.Api.Auth;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Margen.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// Cabecera que lleva el código de alta. Va en cabecera y no en el cuerpo
    /// para que no aparezca en un registro de peticiones que vuelque el cuerpo.
    /// </summary>
    public const string EnrollmentHeader = "X-Margen-Enrollment";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/auth").WithTags("Autenticación");

        group.MapPost("/devices", RegisterDeviceAsync).AllowAnonymous();
        group.MapPost("/challenges", CreateChallengeAsync).AllowAnonymous();
        group.MapPost("/tokens", RedeemAsync).AllowAnonymous();

        group.MapGet("/whoami", WhoAmI).RequireAuthorization();

        group.MapPost("/shortcut-tokens", IssueShortcutTokenAsync)
            .RequireAuthorization(ScopePolicies.Full);

        group.MapGet("/tokens", ListTokensAsync)
            .RequireAuthorization(ScopePolicies.Full);

        group.MapDelete("/tokens/{tokenId:guid}", RevokeTokenAsync)
            .RequireAuthorization(ScopePolicies.Full);

        return app;
    }

    private static async Task<IResult> RegisterDeviceAsync(
        [FromBody] RegisterDeviceRequest request,
        [FromServices] DeviceService devices,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.PublicKeySpki))
        {
            return Problem(StatusCodes.Status400BadRequest, "Faltan el nombre o la clave pública.");
        }

        string code = http.Request.Headers[EnrollmentHeader].ToString();

        DeviceRegistration result = await devices
            .RegisterAsync(code, request.Name, request.PublicKeySpki, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            RegistrationOutcome.Ok =>
                Results.Ok(new RegisterDeviceResponse(result.DeviceId)),

            // Sin código configurado el servidor no está en condiciones de dar
            // de alta a nadie. Es un 503 y no un 401: el problema es del
            // servidor, y decir «código incorrecto» invitaría a seguir
            // probando códigos.
            RegistrationOutcome.NotConfigured =>
                Problem(
                    StatusCodes.Status503ServiceUnavailable,
                    "El alta de dispositivos no está habilitada en este servidor."),

            RegistrationOutcome.BadKey =>
                Problem(StatusCodes.Status400BadRequest, result.Problem!),

            RegistrationOutcome.Revoked =>
                Problem(StatusCodes.Status403Forbidden, "Este dispositivo está revocado."),

            _ => Problem(StatusCodes.Status401Unauthorized, "Código de alta incorrecto."),
        };
    }

    private static async Task<IResult> CreateChallengeAsync(
        [FromBody] ChallengeRequest request,
        [FromServices] DeviceService devices,
        CancellationToken cancellationToken)
    {
        DeviceChallenge? challenge = await devices
            .CreateChallengeAsync(request.DeviceId, cancellationToken)
            .ConfigureAwait(false);

        // Mismo 401 para «no existe» y «revocado»: con respuestas distintas,
        // este endpoint diría qué identificadores de dispositivo son reales.
        return challenge is null
            ? Problem(StatusCodes.Status401Unauthorized, "Dispositivo no reconocido.")
            : Results.Ok(new ChallengeResponse(challenge.Nonce, challenge.ExpiresAt));
    }

    private static async Task<IResult> RedeemAsync(
        [FromBody] RedeemRequest request,
        [FromServices] DeviceService devices,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Nonce)
            || string.IsNullOrWhiteSpace(request.Signature))
        {
            return Problem(StatusCodes.Status400BadRequest, "Faltan el reto o la firma.");
        }

        TokenRedemption result = await devices
            .RedeemAsync(request.DeviceId, request.Nonce, request.Signature, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            // Un solo mensaje para reto vencido, reto ya usado y firma
            // inválida. Distinguirlos le diría a quien prueba cuál de los tres
            // pasos acertó.
            return Problem(StatusCodes.Status401Unauthorized, "No se pudo canjear el reto.");
        }

        IssuedToken token = result.Token!;
        return Results.Ok(new TokenResponse(
            token.Id,
            token.Plaintext,
            token.ExpiresAt,
            token.Scopes));
    }

    private static Ok<WhoAmIResponse> WhoAmI(ClaimsPrincipal user)
    {
        Guid deviceId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
        Guid tokenId = Guid.Parse(
            user.FindFirstValue(TokenAuthenticationHandler.TokenIdClaimType)!);

        string[] scopes = [.. user
            .FindAll(TokenAuthenticationHandler.ScopeClaimType)
            .Select(c => c.Value)];

        return TypedResults.Ok(new WhoAmIResponse(deviceId, tokenId, scopes));
    }

    private static async Task<IResult> IssueShortcutTokenAsync(
        [FromBody] ShortcutTokenRequest request,
        [FromServices] TokenService tokens,
        [FromServices] MargenDbContext db,
        [FromServices] IOptions<AuthOptions> options,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Guid deviceId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        Device? device = await db.Devices
            .FirstOrDefaultAsync(d => d.Id == deviceId, cancellationToken)
            .ConfigureAwait(false);

        if (device is null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "Dispositivo no reconocido.");
        }

        string label = string.IsNullOrWhiteSpace(request.Label) ? "Atajo de iOS" : request.Label;

        // Un solo alcance, escrito aquí y no tomado del que pide. Dejar que el
        // cliente elija los alcances de un token que emite el servidor es
        // dejarle elegir sus propios permisos.
        IssuedToken issued = await tokens.IssueAsync(
            device,
            [Scopes.CashCreate],
            label,
            options.Value.ShortcutTokenLifetime,
            cancellationToken).ConfigureAwait(false);

        return Results.Ok(new TokenResponse(
            issued.Id,
            issued.Plaintext,
            issued.ExpiresAt,
            issued.Scopes));
    }

    private static async Task<IResult> ListTokensAsync(
        [FromServices] MargenDbContext db,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Guid deviceId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var tokens = await db.AccessTokens
            .Where(t => t.DeviceId == deviceId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                t.Id,
                t.Label,
                t.Scopes,
                t.CreatedAt,
                t.ExpiresAt,
                t.LastUsedAt,
                t.RevokedAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(tokens);
    }

    private static async Task<IResult> RevokeTokenAsync(
        Guid tokenId,
        [FromServices] TokenService tokens,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        Guid deviceId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await tokens.RevokeAsync(deviceId, tokenId, cancellationToken).ConfigureAwait(false);

        // 204 tanto si estaba vivo como si ya estaba revocado. Revocar es
        // idempotente y el resultado que importa —ese token no sirve— es el
        // mismo en los dos casos.
        return Results.NoContent();
    }

    private static IResult Problem(int status, string detail) =>
        Results.Problem(detail: detail, statusCode: status);
}
