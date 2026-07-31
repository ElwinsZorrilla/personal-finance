namespace Margen.Api.Auth;

/// <summary>Alta de dispositivo. El código de alta va en la cabecera, no aquí.</summary>
public sealed record RegisterDeviceRequest(string Name, string PublicKeySpki);

public sealed record RegisterDeviceResponse(Guid DeviceId);

public sealed record ChallengeRequest(Guid DeviceId);

public sealed record ChallengeResponse(string Nonce, DateTime ExpiresAt);

public sealed record RedeemRequest(Guid DeviceId, string Nonce, string Signature);

/// <summary>
/// El token en claro viaja una sola vez, aquí. No se puede volver a consultar:
/// en la base solo queda su hash.
/// </summary>
public sealed record TokenResponse(
    Guid TokenId,
    string Token,
    DateTime ExpiresAt,
    IReadOnlyList<string> Scopes);

public sealed record ShortcutTokenRequest(string Label);

public sealed record WhoAmIResponse(
    Guid DeviceId,
    Guid TokenId,
    IReadOnlyList<string> Scopes);
