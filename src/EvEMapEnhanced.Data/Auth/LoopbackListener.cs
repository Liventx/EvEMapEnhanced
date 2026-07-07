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

    /// <summary>
    /// Starts the listener synchronously so a failure (port already in use, no permission to
    /// bind, ...) throws immediately to the caller instead of being deferred into the Task
    /// returned by <see cref="WaitForCallbackAsync"/> -- which the caller wouldn't observe until
    /// after it has already opened the browser, leaving it pointed at a port nothing is
    /// listening on (browser shows "connection refused").
    /// </summary>
    public void Start()
    {
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Could not start the local sign-in listener on http://localhost:{Port}/. " +
                "Another application may already be using that port. Close it and try again.", ex);
        }
    }

    /// <summary>Blocks until the SSO redirect arrives (call <see cref="Start"/> first) and returns its "code" and "state" query parameters.</summary>
    public async Task<(string Code, string State)> WaitForCallbackAsync(CancellationToken ct = default)
    {
        bool lingering = false;
        try
        {
            using var registration = ct.Register(() =>
            {
                try { _listener.Stop(); } catch { /* already stopped */ }
            });

            while (true)
            {
                var context = await _listener.GetContextAsync();
                var query = context.Request.QueryString;
                string? code = query["code"];
                string? error = query["error"];
                string state = query["state"] ?? string.Empty;

                // Ignore requests that aren't the OAuth redirect itself -- e.g. a browser's
                // automatic favicon.ico fetch for the freshly-opened localhost origin -- rather
                // than treating the first request of any kind as "the callback" and shutting the
                // listener down before the real redirect (which may arrive a moment later) has a
                // chance to connect, which would otherwise surface to the user as the browser's
                // "connection refused" page.
                if (code is null && error is null)
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                await RespondAsync(context, ok: code is not null, ct);

                if (code is null) throw new InvalidOperationException($"EVE SSO redirect reported an error: {error}.");

                // Keep the listener alive a little longer instead of tearing it down the instant
                // this one request is answered: some browsers race a second connection against
                // the real redirect (e.g. an automatic HTTPS-upgrade probe that then falls back to
                // plain HTTP a moment later, or a duplicate preconnect), and if that late arrival
                // finds nothing listening it shows the user a scary "connection refused" page even
                // though sign-in already succeeded underneath it.
                lingering = true;
                LingerThenStop(TimeSpan.FromSeconds(2));
                return (code, state);
            }
        }
        finally
        {
            if (!lingering)
            {
                try { _listener.Stop(); } catch { /* already stopped */ }
            }
        }
    }

    /// <summary>Answers any further requests with the same "signed in" page for a grace period, then stops the listener.</summary>
    private void LingerThenStop(TimeSpan grace)
    {
        _ = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(grace);
            using var registration = cts.Token.Register(() =>
            {
                try { _listener.Stop(); } catch { /* already stopped */ }
            });
            try
            {
                while (true)
                {
                    var context = await _listener.GetContextAsync();
                    await RespondAsync(context, ok: true, CancellationToken.None);
                }
            }
            catch
            {
                // Listener stopped once the grace period elapsed (or was disposed by the caller
                // in the meantime) -- nothing left to clean up.
            }
        });
    }

    private static async Task RespondAsync(HttpListenerContext context, bool ok, CancellationToken ct)
    {
        string html = ok
            ? "<html><body style=\"font-family:sans-serif\"><h2>EvE Map Enhanced</h2><p>Signed in. You can close this window.</p></body></html>"
            : "<html><body style=\"font-family:sans-serif\"><h2>EvE Map Enhanced</h2><p>Sign-in failed or was cancelled. You can close this window.</p></body></html>";

        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, ct);
        context.Response.Close();
    }

    public void Dispose() => ((IDisposable)_listener).Dispose();
}
