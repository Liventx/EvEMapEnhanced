using System.Net.Sockets;
using System.Text;
using EvEMapEnhanced.Data.Auth;

namespace EvEMapEnhanced.Data.Tests.Auth;

public class LoopbackListenerTests
{
    [Fact]
    public async Task WaitForCallbackAsync_ParsesCodeAndStateFromOAuthRedirect()
    {
        const int port = 28787;
        using var listener = new LoopbackListener(port);
        listener.Start();

        var waitTask = listener.WaitForCallbackAsync();

        await Task.Delay(50);
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        string request =
            "GET /callback?code=abc123&state=xyz789 HTTP/1.1\r\n" +
            "Host: localhost\r\n" +
            "\r\n";
        byte[] bytes = Encoding.ASCII.GetBytes(request);
        using var stream = client.GetStream();
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        var buffer = new byte[4096];
        await stream.ReadAsync(buffer);

        var (code, state) = await waitTask;
        Assert.Equal("abc123", code);
        Assert.Equal("xyz789", state);
    }

    [Fact]
    public async Task WaitForCallbackAsync_IgnoresUnrelatedRequestsUntilOAuthRedirect()
    {
        const int port = 28788;
        using var listener = new LoopbackListener(port);
        listener.Start();

        var waitTask = listener.WaitForCallbackAsync();

        await Task.Delay(50);
        await SendRawRequestAsync(port, "GET /favicon.ico HTTP/1.1\r\nHost: localhost\r\n\r\n");
        await SendRawRequestAsync(port, "GET /callback?code=real&state=ok HTTP/1.1\r\nHost: localhost\r\n\r\n");

        var (code, state) = await waitTask;
        Assert.Equal("real", code);
        Assert.Equal("ok", state);
    }

    private static async Task SendRawRequestAsync(int port, string request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", port);
        byte[] bytes = Encoding.ASCII.GetBytes(request);
        using var stream = client.GetStream();
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        var buffer = new byte[4096];
        try
        {
            await stream.ReadAsync(buffer);
        }
        catch
        {
            // Server may close immediately after responding.
        }
    }
}
