using System.Net;
using System.Net.Sockets;
using System.Text;

namespace EvEMapEnhanced.Data.Auth;

/// <summary>
/// Short-lived local TCP listener that captures the EVE SSO redirect (authorization code +
/// state) for the installed-app OAuth flow. Uses raw TCP + minimal HTTP parsing instead of
/// <see cref="HttpListener"/> so sign-in does not depend on Windows http.sys URL reservations
/// (a common failure on locked-down or freshly imaged PCs).
/// </summary>
public sealed class LoopbackListener : IDisposable
{
    private TcpListener? _listener;

    public int Port { get; }

    public LoopbackListener(int port) => Port = port;

    /// <summary>
    /// The redirect URI advertised to EVE SSO in the authorize request and token exchange.
    /// Must exactly match a callback URL registered on the CCP developer application.
    /// </summary>
    public string RedirectUri => $"http://localhost:{Port}/callback";

    /// <summary>
    /// Starts the listener synchronously so binding failures throw before the browser opens.
    /// </summary>
    public void Start()
    {
        _listener = new TcpListener(IPAddress.Loopback, Port);
        try
        {
            _listener.Start();
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(
                $"Could not start the local sign-in listener on http://localhost:{Port}/. " +
                "Another application may already be using that port — close it and try again.", ex);
        }
    }

    /// <summary>Blocks until the SSO redirect arrives (call <see cref="Start"/> first).</summary>
    public async Task<(string Code, string State)> WaitForCallbackAsync(CancellationToken ct = default)
    {
        if (_listener is null)
            throw new InvalidOperationException("Call Start() before waiting for the OAuth callback.");

        bool lingering = false;
        try
        {
            while (true)
            {
                using var client = await AcceptTcpClientAsync(_listener, ct);
                var request = await ReadHttpRequestAsync(client, ct);
                string? code = request.Query.GetValueOrDefault("code");
                string? error = request.Query.GetValueOrDefault("error");
                string state = request.Query.GetValueOrDefault("state") ?? string.Empty;

                if (code is null && error is null)
                {
                    await WriteHttpResponseAsync(client, 404, "Not Found", SignInHtml(ok: false), ct);
                    continue;
                }

                await WriteHttpResponseAsync(client, 200, "OK", SignInHtml(ok: code is not null), ct);

                if (code is null)
                    throw new InvalidOperationException($"EVE SSO redirect reported an error: {error}.");

                lingering = true;
                LingerThenStop(TimeSpan.FromSeconds(2));
                return (code, state);
            }
        }
        finally
        {
            if (!lingering)
                StopListener();
        }
    }

    private void LingerThenStop(TimeSpan grace)
    {
        var listener = _listener;
        if (listener is null) return;

        _ = Task.Run(async () =>
        {
            using var cts = new CancellationTokenSource(grace);
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    using var client = await AcceptTcpClientAsync(listener, cts.Token);
                    await WriteHttpResponseAsync(client, 200, "OK", SignInHtml(ok: true), CancellationToken.None);
                }
            }
            catch (OperationCanceledException)
            {
                // Grace period elapsed.
            }
            catch
            {
                // Listener stopped or disposed.
            }
            finally
            {
                StopListener();
            }
        });
    }

    private static async Task<TcpClient> AcceptTcpClientAsync(TcpListener listener, CancellationToken ct)
    {
        var client = await listener.AcceptTcpClientAsync(ct);
        client.ReceiveTimeout = 5000;
        return client;
    }

    private static async Task<HttpRequestLine> ReadHttpRequestAsync(TcpClient client, CancellationToken ct)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync(ct);
        if (string.IsNullOrWhiteSpace(requestLine))
            return new HttpRequestLine(string.Empty, new Dictionary<string, string>());

        // Consume headers until the blank line.
        while (true)
        {
            string? header = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(header))
                break;
        }

        return ParseRequestLine(requestLine);
    }

    private static HttpRequestLine ParseRequestLine(string requestLine)
    {
        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        string target = parts.Length >= 2 ? parts[1] : string.Empty;
        int queryStart = target.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
            return new HttpRequestLine(target, new Dictionary<string, string>());

        string query = target[(queryStart + 1)..];
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq < 0)
            {
                values[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            string key = Uri.UnescapeDataString(pair[..eq]);
            string value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            values[key] = value;
        }

        return new HttpRequestLine(target, values);
    }

    private static async Task WriteHttpResponseAsync(
        TcpClient client,
        int statusCode,
        string statusText,
        string html,
        CancellationToken ct)
    {
        try
        {
            if (!client.Connected)
                return;

            byte[] body = Encoding.UTF8.GetBytes(html);
            string headers =
                $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n" +
                "\r\n";
            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            using var stream = client.GetStream();
            await stream.WriteAsync(headerBytes, ct);
            await stream.WriteAsync(body, ct);
            await stream.FlushAsync(ct);
        }
        catch (IOException)
        {
            // Browser closed the connection early (common for favicon probes).
        }
        catch (SocketException)
        {
            // Client disconnected before the response was fully sent.
        }
        catch (InvalidOperationException)
        {
            // TcpClient already torn down by the remote side.
        }
    }

    private static string SignInHtml(bool ok) => ok
        ? "<html><body style=\"font-family:sans-serif\"><h2>EvE Map Enhanced</h2><p>Signed in. You can close this window.</p></body></html>"
        : "<html><body style=\"font-family:sans-serif\"><h2>EvE Map Enhanced</h2><p>Sign-in failed or was cancelled. You can close this window.</p></body></html>";

    private void StopListener()
    {
        try { _listener?.Stop(); } catch { /* already stopped */ }
    }

    public void Dispose()
    {
        StopListener();
        _listener = null;
    }

    private sealed record HttpRequestLine(string Target, Dictionary<string, string> Query);
}
