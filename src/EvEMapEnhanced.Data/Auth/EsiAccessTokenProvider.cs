namespace EvEMapEnhanced.Data.Auth;

/// <summary>
/// Turns a signed-in character's stored (encrypted) refresh token into a short-lived access
/// token, refreshing and caching as needed. Every refresh rotates the stored refresh token per
/// EVE SSO's rules, so callers must go through this instead of caching tokens themselves.
/// </summary>
public sealed class EsiAccessTokenProvider
{
    private readonly EsiOAuthClient _oauthClient;
    private readonly AuthenticatedCharacterRepository _repository;
    private readonly Dictionary<long, (string Token, DateTime ExpiresUtc)> _cache = new();

    public EsiAccessTokenProvider(EsiOAuthClient oauthClient, AuthenticatedCharacterRepository repository)
    {
        _oauthClient = oauthClient;
        _repository = repository;
    }

    public async Task<string> GetAccessTokenAsync(long characterId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(characterId, out var cached) && cached.ExpiresUtc > DateTime.UtcNow.AddSeconds(30))
        {
            return cached.Token;
        }

        string? refreshToken = _repository.GetRefreshToken(characterId);
        if (refreshToken is null) throw new InvalidOperationException("Character is not signed in.");

        var result = await _oauthClient.RefreshAsync(refreshToken, ct);
        _repository.UpdateRefreshToken(characterId, result.RefreshToken);
        _cache[characterId] = (result.AccessToken, DateTime.UtcNow.AddSeconds(result.ExpiresIn));
        return result.AccessToken;
    }
}
