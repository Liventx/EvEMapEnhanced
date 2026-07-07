using System.Net;
using System.Text;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>
/// Short-lived local HTTP listener that captures the EVE SSO redirect (authorization code +
/// state) for the installed-app OAuth flow, then shuts itself down. This is the standard
/// "loopback redirect" pattern for native/desktop apps that can't register a custom URI scheme
/// reliably across platforms.
/// </summary>
public sealed class LoopbackListener : IDisposable
{
    private readonly HttpListener _listener = new();

    public int Port { get; }

    public LoopbackListener(int port)
    {
        Port = port;
        // Listen on the whole loopback port rather than registering the exact callback path:
        // the redirect_uri advertised to EVE SSO must be a byte-for-byte match of whatever
        // callback URL is registered on the CCP developer application (which may or may not
        // have a trailing slash), but HttpListener's own prefix only matches requests whose
        // path starts with the registered prefix -- registering just the port avoids that
        // mismatch entirely regardless of how the app's callback URL is spelled.
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    /// <summary>
    /// The redirect URI advertised to EVE SSO in the authorize request and token exchange.
    /// Must exactly match a callback URL registered on the CCP developer application.
    /// </summary>
    public string RedirectUri => $"http://localhost:{Port}/callback";

    /// <summary>Starts listening, blocks until the SSO redirect arrives, and returns its "code" and "state" query parameters.</summary>
    public async Task<(string Code, string State)> WaitForCallbackAsync(CancellationToken ct = default)
    {
        _listener.Start();
        try
        {
            using var registration = ct.Register(() =>
            {
                try { _listener.Stop(); } catch { /* already stopped */ }
            });

            var context = await _listener.GetContextAsync();
            var query = context.Request.QueryString;
            string? code = query["code"];
            string state = query["state"] ?? string.Empty;

            bool ok = code is not null;
            string html = ok
                ? "<html><body style=\"font-family:sans-serif\"><h2>EvE Map Enhanced</h2><p>Signed in. You can close this window.</p></body></html>"
                : "<html><body style=\"font-family:sans-serif\"><h2>EvE Map Enhanced</h2><p>Sign-in failed or was cancelled. You can close this window.</p></body></html>";

            var buffer = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, ct);
            context.Response.Close();

            if (code is null) throw new InvalidOperationException("EVE SSO redirect did not include an authorization code.");
            return (code, state);
        }
        finally
        {
            try { _listener.Stop(); } catch { /* already stopped */ }
        }
    }

    public void Dispose() => ((IDisposable)_listener).Dispose();
}
