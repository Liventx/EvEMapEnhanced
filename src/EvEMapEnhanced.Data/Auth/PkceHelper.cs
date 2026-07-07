using System.Security.Cryptography;
using System.Text;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>
/// PKCE (RFC 7636) helpers for the EVE SSO "authorization code + PKCE" flow used by installed
/// apps that can't safely hold a client secret.
/// </summary>
public static class PkceHelper
{
    public static string GenerateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string GenerateCodeChallenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    public static string GenerateState() => Base64UrlEncode(RandomNumberGenerator.GetBytes(16));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
