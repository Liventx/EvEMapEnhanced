using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>Result of an EVE SSO token exchange/refresh: the access token plus who it belongs to.</summary>
public sealed record EsiTokenResult(string AccessToken, string RefreshToken, int ExpiresIn, long CharacterId, string CharacterName);

/// <summary>
/// EVE SSO (login.eveonline.com) OAuth2 client using the Authorization Code + PKCE flow -- the
/// flow CCP recommends for installed/native apps that can't hold a client secret. Talks directly
/// to CCP's endpoints over HTTPS.
/// </summary>
public sealed class EsiOAuthClient
{
    private const string AuthorizeUrl = "https://login.eveonline.com/v2/oauth/authorize/";
    private const string TokenUrl = "https://login.eveonline.com/v2/oauth/token";

    private readonly string _clientId;
    private readonly HttpClient _httpClient;

    public EsiOAuthClient(string clientId, HttpClient? httpClient = null)
    {
        _clientId = clientId;
        _httpClient = httpClient ?? new HttpClient();
    }

    public string BuildAuthorizeUrl(string redirectUri, string state, string codeChallenge, IEnumerable<string> scopes)
    {
        var query = new (string Key, string Value)[]
        {
            ("response_type", "code"),
            ("redirect_uri", redirectUri),
            ("client_id", _clientId),
            ("scope", string.Join(' ', scopes)),
            ("state", state),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", "S256"),
        };
        string qs = string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return $"{AuthorizeUrl}?{qs}";
    }

    public Task<EsiTokenResult> ExchangeCodeAsync(string code, string codeVerifier, string redirectUri, CancellationToken ct = default) =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = _clientId,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
        }, ct);

    public Task<EsiTokenResult> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = _clientId,
        }, ct);

    private async Task<EsiTokenResult> RequestTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
        {
            Content = new FormUrlEncodedContent(form),
        };
        request.Headers.Host = "login.eveonline.com";

        using var response = await _httpClient.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"EVE SSO token request failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        string accessToken = doc.RootElement.GetProperty("access_token").GetString()!;
        string refreshToken = doc.RootElement.GetProperty("refresh_token").GetString()!;
        int expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();

        var (characterId, characterName) = DecodeAccessToken(accessToken);
        return new EsiTokenResult(accessToken, refreshToken, expiresIn, characterId, characterName);
    }

    /// <summary>
    /// Decodes the JWT access token's payload (no signature verification -- this app talks
    /// directly to CCP's official token endpoint over TLS, so the token is trusted as-issued) to
    /// pull out the character id/name without an extra round trip.
    /// </summary>
    private static (long CharacterId, string CharacterName) DecodeAccessToken(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2) throw new InvalidOperationException("Malformed ESI access token.");

        string payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
        using var doc = JsonDocument.Parse(payloadJson);

        string sub = doc.RootElement.GetProperty("sub").GetString() ?? throw new InvalidOperationException("ESI access token missing 'sub'.");
        long characterId = long.Parse(sub.Split(':')[^1]);
        string name = doc.RootElement.GetProperty("name").GetString() ?? $"Character {characterId}";
        return (characterId, name);
    }

    private static byte[] Base64UrlDecode(string input)
    {
        string s = input.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch
        {
            2 => s + "==",
            3 => s + "=",
            _ => s,
        };
        return Convert.FromBase64String(s);
    }
}
