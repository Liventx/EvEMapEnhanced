using EvEMapEnhanced.Core.Auth;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>
/// Orchestrates a full "Sign in with EVE Online" round trip: builds the PKCE authorize URL,
/// waits for the local loopback redirect, exchanges the code for tokens, fetches the character's
/// jump-relevant skills, and persists everything.
/// </summary>
public sealed class EsiSignInFlow
{
    private readonly EsiAuthSettings _settings;
    private readonly AuthenticatedCharacterRepository _repository;
    private readonly EsiOAuthClient _oauthClient;
    private readonly EsiCharacterSkillsClient _skillsClient;

    public EsiSignInFlow(EsiAuthSettings settings, AuthenticatedCharacterRepository repository, HttpClient? httpClient = null)
    {
        _settings = settings;
        _repository = repository;
        _oauthClient = new EsiOAuthClient(settings.ClientId, httpClient);
        _skillsClient = new EsiCharacterSkillsClient(httpClient);
    }

    /// <summary>
    /// Runs one full sign-in round trip. <paramref name="openBrowser"/> is invoked with the
    /// authorize URL once the local callback listener is ready to receive the redirect (the
    /// caller is responsible for actually opening it, e.g. via <c>Process.Start</c>).
    /// </summary>
    public async Task<AuthenticatedCharacter> SignInAsync(Action<string> openBrowser, CancellationToken ct = default)
    {
        string verifier = PkceHelper.GenerateCodeVerifier();
        string challenge = PkceHelper.GenerateCodeChallenge(verifier);
        string state = PkceHelper.GenerateState();

        using var listener = new LoopbackListener(_settings.CallbackPort);
        listener.Start();
        string authorizeUrl = _oauthClient.BuildAuthorizeUrl(listener.RedirectUri, state, challenge, EsiAuthSettings.Scopes);

        var callbackTask = listener.WaitForCallbackAsync(ct);
        openBrowser(authorizeUrl);
        var (code, returnedState) = await callbackTask;
        if (returnedState != state)
        {
            throw new InvalidOperationException("OAuth state mismatch - sign-in aborted for safety.");
        }

        var token = await _oauthClient.ExchangeCodeAsync(code, verifier, listener.RedirectUri, ct);
        _repository.Upsert(token.CharacterId, token.CharacterName, token.RefreshToken, EsiAuthSettings.Scopes);

        var skills = await _skillsClient.GetSkillsAsync(token.CharacterId, token.AccessToken, ct);
        _repository.UpdateSkills(token.CharacterId, skills);

        return new AuthenticatedCharacter
        {
            CharacterId = token.CharacterId,
            Name = token.CharacterName,
            Skills = skills,
            SkillsUpdatedUtc = DateTime.UtcNow,
        };
    }
}
