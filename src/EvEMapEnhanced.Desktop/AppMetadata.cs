using System.Reflection;

namespace EvEMapEnhanced.Desktop;

public static class AppMetadata
{
    public const string ProductName = "EvE Map Enhanced";
    public const string GitHubRepositoryUrl = "https://github.com/Liventx/EvEMapEnhanced";
    public const string GitHubReleasesUrl = "https://github.com/Liventx/EvEMapEnhanced/releases";
    internal const string GitHubLatestReleaseApiUrl =
        "https://api.github.com/repos/Liventx/EvEMapEnhanced/releases/latest";

    public static string CurrentVersion
    {
        get
        {
            var info = typeof(AppMetadata).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                int plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }

            return typeof(AppMetadata).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}
