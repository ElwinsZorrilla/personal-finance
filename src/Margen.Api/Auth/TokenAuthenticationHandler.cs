using System.Security.Claims;
using System.Text.Encodings.Web;
using Margen.Domain.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Margen.Api.Auth;

/// <summary>
/// Esquema <c>Bearer</c> sobre el token opaco. Cada alcance del token se
/// convierte en una reclamación <c>scope</c>, que es lo que miran las políticas.
/// </summary>
public sealed class TokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    TokenService tokens)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "Bearer";

    public const string ScopeClaimType = "scope";

    public const string TokenIdClaimType = "token_id";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? header = Request.Headers.Authorization;
        if (string.IsNullOrWhiteSpace(header))
        {
            return AuthenticateResult.NoResult();
        }

        const string prefix = "Bearer ";
        if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        string plaintext = header[prefix.Length..].Trim();

        AccessToken? token = await tokens
            .ResolveAsync(plaintext, Context.RequestAborted)
            .ConfigureAwait(false);

        if (token is null)
        {
            // El motivo no se detalla. «Caducado» y «no existe» son la misma
            // respuesta para quien pregunta: distinguirlos convierte el
            // endpoint en un oráculo que dice qué tokens existieron.
            return AuthenticateResult.Fail("Token no válido.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, token.DeviceId.ToString("D")),
            new(TokenIdClaimType, token.Id.ToString("D")),
        };

        foreach (string scope in token.Scopes.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(ScopeClaimType, scope));
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}
