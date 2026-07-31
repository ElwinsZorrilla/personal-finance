using System.Net.Http.Headers;
using System.Net.Http.Json;
using Margen.Api.Auth;
using Margen.Api.Endpoints;

namespace Margen.Api.Tests.Infra;

/// <summary>
/// El recorrido completo de autenticación, tal como lo hará la app: alta, reto,
/// firma, token.
/// </summary>
internal static class AuthFlow
{
    public static async Task<Guid> RegisterAsync(
        HttpClient client,
        string enrollmentCode,
        TestDeviceKey key,
        string name = "iPhone de prueba")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/devices")
        {
            Content = JsonContent.Create(
                new RegisterDeviceRequest(name, key.PublicKeySpkiBase64)),
        };
        request.Headers.Add(AuthEndpoints.EnrollmentHeader, enrollmentCode);

        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        RegisterDeviceResponse body =
            (await response.Content.ReadFromJsonAsync<RegisterDeviceResponse>())!;

        return body.DeviceId;
    }

    public static async Task<ChallengeResponse> ChallengeAsync(HttpClient client, Guid deviceId)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/auth/challenges",
            new ChallengeRequest(deviceId));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChallengeResponse>())!;
    }

    public static async Task<HttpResponseMessage> RedeemAsync(
        HttpClient client,
        Guid deviceId,
        string nonce,
        string signature) =>
        await client.PostAsJsonAsync(
            "/auth/tokens",
            new RedeemRequest(deviceId, nonce, signature));

    /// <summary>Alta, reto, firma y canje en una llamada. Devuelve el token en claro.</summary>
    public static async Task<TokenResponse> SignInAsync(
        HttpClient client,
        string enrollmentCode,
        TestDeviceKey key)
    {
        Guid deviceId = await RegisterAsync(client, enrollmentCode, key);
        ChallengeResponse challenge = await ChallengeAsync(client, deviceId);

        HttpResponseMessage response = await RedeemAsync(
            client,
            deviceId,
            challenge.Nonce,
            key.Sign(deviceId, challenge.Nonce));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
    }

    public static void UseToken(this HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
}
