using System.Net;
using System.Text;
using System.Text.Json;

namespace AcceleratorHelper;

public class StatusServer
{
    private readonly AcceleratorService _service;
    private readonly ILogger<StatusServer> _logger;
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public StatusServer(
        AcceleratorService service,
        ILogger<StatusServer> logger,
        int port)
    {
        _service = service;
        _logger = logger;
        _port = port;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();

        _ = Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener!.GetContextAsync()
                    .WaitAsync(ct);
                _ = HandleRequestAsync(context);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "请求处理失败"); }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        var method = context.Request.HttpMethod;

        context.Response.ContentType = "application/json; charset=utf-8";

        try
        {
            switch (path)
            {
                case "/status":
                    await WriteJsonAsync(context.Response, new
                    {
                        running = _service.IsRunning,
                        httpPort = _service.HttpPort,
                        httpsPort = _service.HttpsPort,
                        domainCount = _service.DomainCount
                    });
                    break;
                case "/logs":
                    await WriteJsonAsync(context.Response, _service.Logs);
                    break;
                case "/stop" when method == "POST":
                    await _service.StopAsync();
                    await WriteJsonAsync(context.Response, new { stopped = true });
                    _ = Task.Delay(500).ContinueWith(_ => Environment.Exit(0));
                    break;
                default:
                    context.Response.StatusCode = 404;
                    await WriteJsonAsync(context.Response, new { error = "Not Found" });
                    break;
            }
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            await WriteJsonAsync(context.Response, new { error = ex.Message });
        }
    }

    private static async Task WriteJsonAsync(
        HttpListenerResponse response, object data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}
