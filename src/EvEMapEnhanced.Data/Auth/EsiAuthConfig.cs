using System.Text.Json;
using System.Text.Json.Serialization;
using EvEMapEnhanced.Data.Paths;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>
/// Client ID + callback port for the CCP developer application used to sign in with EVE Online.
/// This is deliberately not a secret (the flow is PKCE, no client secret involved), but it's
/// still per-user (everyone runs the app against their own registered application), so it lives
/// in a local config file rather than being baked into source control.
/// </summary>
public sealed record EsiAuthSettings(string ClientId, int CallbackPort)
{
    /// <summary>Scopes requested at sign-in: jump-relevant skills plus live location for pilot tracking.</summary>
    public static readonly string[] Scopes =
    {
        "esi-skills.read_skills.v1",
        "esi-location.read_location.v1",
        "esi-location.read_online.v1",
    };
}

/// <summary>
/// Reads/writes the local ESI application config. See the setup notes for how to register your
/// own CCP developer application at https://developers.eveonline.com/applications (Connection
/// Type: "Authentication &amp; API Access"; Callback URL must exactly match
/// <c>http://localhost:&lt;CallbackPort&gt;/callback</c> -- EVE SSO matches redirect URIs
/// byte-for-byte, including the presence/absence of a trailing slash).
/// </summary>
public static class EsiAuthConfig
{
    public static string ConfigPath => Path.Combine(AppPaths.AppDataDir, "esi-client.json");

    /// <summary>Shipped next to the executable by the installer / portable publish folder.</summary>
    public static string BundledConfigPath => Path.Combine(AppContext.BaseDirectory, "esi-client.json");

    public static EsiAuthSettings? TryLoad()
    {
        var fromAppData = TryLoadFromPath(ConfigPath);
        if (fromAppData is not null) return fromAppData;

        var fromBundled = TryLoadFromPath(BundledConfigPath);
        if (fromBundled is null) return null;

        TrySeedAppData(fromBundled);
        return fromBundled;
    }

    public static void Save(string clientId, int callbackPort = 8787)
    {
        var dto = new ConfigDto { ClientId = clientId, CallbackPort = callbackPort };
        Directory.CreateDirectory(AppPaths.AppDataDir);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static EsiAuthSettings? TryLoadFromPath(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<ConfigDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto is null || string.IsNullOrWhiteSpace(dto.ClientId)) return null;
            return new EsiAuthSettings(dto.ClientId, dto.CallbackPort > 0 ? dto.CallbackPort : 8787);
        }
        catch
        {
            return null;
        }
    }

    private static void TrySeedAppData(EsiAuthSettings settings)
    {
        if (File.Exists(ConfigPath)) return;
        try
        {
            Save(settings.ClientId, settings.CallbackPort);
        }
        catch
        {
            // Bundled config still works for this session even if AppData is not writable.
        }
    }

    private sealed class ConfigDto
    {
        [JsonPropertyName("clientId")]
        public string ClientId { get; set; } = string.Empty;

        [JsonPropertyName("callbackPort")]
        public int CallbackPort { get; set; } = 8787;
    }
}
