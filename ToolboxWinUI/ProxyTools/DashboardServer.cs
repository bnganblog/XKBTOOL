using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ToolboxWinUI.ProxyTools;

public class DashboardServer : IDisposable
{
    private TcpListener? _listener;
    private readonly string _dashboardPath;
    private bool _running;
    private CancellationTokenSource? _cts;

    public int Port { get; } = 19090;
    public bool IsRunning => _running;

    public DashboardServer()
    {
        _dashboardPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ToolboxWinUI", "proxy", "dashboard");
    }

    public void Start()
    {
        if (_running) return;
        if (!System.IO.Directory.Exists(_dashboardPath)) return;

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
            _running = true;
            _ = ListenLoopAsync(_cts.Token);
        }
        catch { }
    }

    public void Stop()
    {
        _running = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    public void Dispose() => Stop();

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync();
                _ = HandleClientAsync(client);
            }
            catch (ObjectDisposedException) { break; }
            catch (InvalidOperationException) { break; }
            catch { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                var buffer = new byte[8192];
                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) return;

                var request = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                var requestLine = request.Split('\r')[0];
                var parts = requestLine.Split(' ');
                var path = parts.Length > 1 ? parts[1] : "/";
                if (path == "/") path = "/index.html";

                var filePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(_dashboardPath, path.TrimStart('/')));
                var basePath = System.IO.Path.GetFullPath(_dashboardPath);

                if (!filePath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(filePath))
                {
                    await SendResponseAsync(stream, 404, "Not Found", "text/plain", Encoding.UTF8.GetBytes("404 Not Found"));
                    return;
                }

                var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                var mime = ext switch
                {
                    ".html" => "text/html",
                    ".css" => "text/css",
                    ".js" => "application/javascript",
                    ".json" => "application/json",
                    ".png" => "image/png",
                    ".svg" => "image/svg+xml",
                    ".ico" => "image/x-icon",
                    ".webmanifest" => "application/manifest+json",
                    _ => "application/octet-stream"
                };

                var content = await System.IO.File.ReadAllBytesAsync(filePath);
                await SendResponseAsync(stream, 200, "OK", mime, content);
            }
            catch { }
        }
    }

    private static async Task SendResponseAsync(NetworkStream stream, int statusCode, string statusText, string contentType, byte[] body)
    {
        var header = $"HTTP/1.1 {statusCode} {statusText}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nAccess-Control-Allow-Origin: *\r\nConnection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
        await stream.WriteAsync(body, 0, body.Length);
    }
}
