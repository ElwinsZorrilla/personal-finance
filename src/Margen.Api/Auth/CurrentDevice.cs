using System.Security.Claims;

namespace Margen.Api.Auth;

/// <summary>
/// El dispositivo de la petición en curso.
/// </summary>
/// <remarks>
/// Paga la deuda m12 de CR-002. Antes, cada endpoint hacía
/// <c>Guid.Parse(user.FindFirstValue(...)!)</c>: cuatro copias de la misma
/// suposición, y una reclamación ausente habría sido un 500 con traza en vez de
/// un 401. Aquí la ausencia se trata como lo que es —no autenticado— y hay un
/// solo sitio que cambiar el día que la reclamación se llame de otra forma.
/// </remarks>
public static class CurrentDevice
{
    public static bool TryGetId(ClaimsPrincipal user, out Guid deviceId)
    {
        ArgumentNullException.ThrowIfNull(user);

        deviceId = Guid.Empty;
        string? raw = user.FindFirstValue(ClaimTypes.NameIdentifier);

        return raw is not null && Guid.TryParse(raw, out deviceId);
    }

    public static bool TryGetTokenId(ClaimsPrincipal user, out Guid tokenId)
    {
        ArgumentNullException.ThrowIfNull(user);

        tokenId = Guid.Empty;
        string? raw = user.FindFirstValue(TokenAuthenticationHandler.TokenIdClaimType);

        return raw is not null && Guid.TryParse(raw, out tokenId);
    }

    public static IReadOnlyList<string> ScopesOf(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return [.. user
            .FindAll(TokenAuthenticationHandler.ScopeClaimType)
            .Select(c => c.Value)];
    }
}
