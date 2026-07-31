using Margen.Domain;
using Microsoft.AspNetCore.Authorization;

namespace Margen.Api.Auth;

/// <summary>Políticas de autorización por alcance.</summary>
public static class ScopePolicies
{
    /// <summary>Todo lo que hace la app. El token del Atajo no la cumple.</summary>
    public const string Full = "alcance:total";

    /// <summary>Registrar efectivo. La cumplen el Atajo y la app.</summary>
    public const string CashCreate = "alcance:efectivo";

    public static AuthorizationBuilder AddMargenPolicies(this AuthorizationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddPolicy(Full, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(TokenAuthenticationHandler.ScopeClaimType, Scopes.Full));

        // El alcance total incluye el de efectivo: la app tiene que poder
        // registrar un gasto en efectivo. Lo que no ocurre al revés —y es todo
        // el punto del token del Atajo— es que el de efectivo alcance lo demás.
        builder.AddPolicy(CashCreate, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(
                TokenAuthenticationHandler.ScopeClaimType,
                Scopes.CashCreate,
                Scopes.Full));

        // Sin política por defecto no hay endpoint que quede abierto por
        // olvido: lo que no lleve `AllowAnonymous` explícito exige token.
        builder.SetFallbackPolicy(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build());

        return builder;
    }
}
