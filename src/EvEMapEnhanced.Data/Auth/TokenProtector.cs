using System.Security.Cryptography;
using System.Text;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>
/// Encrypts refresh tokens at rest using Windows DPAPI (current-user scope), so a copy of the
/// local SQLite file alone isn't enough to hijack a signed-in character. DPAPI is Windows-only,
/// but this whole app targets Windows Desktop, so the platform-compatibility warning is
/// suppressed locally rather than threading <c>SupportedOSPlatform</c> attributes through every
/// caller up to <see cref="AuthenticatedCharacterRepository"/>.
/// </summary>
internal static class TokenProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EvEMapEnhanced.RefreshToken.v1");

#pragma warning disable CA1416 // DPAPI is Windows-only; this app is Windows Desktop-only.
    public static byte[] Protect(string plainText) =>
        ProtectedData.Protect(Encoding.UTF8.GetBytes(plainText), Entropy, DataProtectionScope.CurrentUser);

    public static string Unprotect(byte[] protectedBytes) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser));
#pragma warning restore CA1416
}
