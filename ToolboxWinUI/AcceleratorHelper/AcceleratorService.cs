using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Forwarder;

namespace AcceleratorHelper;

public class AcceleratorService
{
    private readonly string[] _domains;
    private readonly CertService _certService;
    private readonly HostsManager _hostsManager;
    private readonly DnsResolver _dnsResolver;
    private readonly ILogger<AcceleratorService> _logger;
    private readonly List<string> _logs = new();
    private readonly object _logLock = new();

    private IHost? _host;
    private bool _isRunning;

    public bool IsRunning => _isRunning;
    public int HttpPort { get; private set; }
    public int HttpsPort { get; private set; }
    public int DomainCount => _domains.Length;
    public List<string> Logs
    {
        get
        {
            lock (_logLock) return _logs.ToList();
        }
    }

    public AcceleratorService(
        string[] domains,
        CertService certService,
        HostsManager hostsManager,
        DnsResolver dnsResolver,
        ILogger<AcceleratorService> logger)
    {
        _domains = domains;
        _certService = certService;
        _hostsManager = hostsManager;
        _dnsResolver = dnsResolver;
        _logger = logger;
    }

    public async Task StartAsync()
    {
        if (_isRunning) return;

        AddLog("INFO", "正在启动加速服务...");

        HttpPort = GetAvailablePort(80);
        HttpsPort = GetAvailablePort(443);

        if (HttpPort != 80)
            AddLog("WARN", $"HTTP 端口 80 不可用，使用 {HttpPort}");
        if (HttpsPort != 443)
            AddLog("WARN", $"HTTPS 端口 443 不可用，使用 {HttpsPort}");

        _certService.CreateCaCertIfNotExists();
        _certService.InstallCaCert();
        AddLog("INFO", "已安装根证书到系统信任存储");

        AddLog("INFO", $"正在解析 {_domains.Length} 个域名的最优 IP...");
        var dnsResults = await _dnsResolver.ResolveAllAsync(_domains);
        AddLog("INFO", $"DNS 解析完成: {dnsResults.Count} 个域名");

        _hostsManager.BackupHosts();

        var nonGitHubDomains = _domains.Where(d => !d.Contains("github")).ToArray();
        _hostsManager.AddEntries(nonGitHubDomains);
        AddLog("INFO", $"已添加 {nonGitHubDomains.Length} 条代理 hosts 条目");

        var githubDomains = _domains.Where(d => d.Contains("github")).ToArray();
        if (githubDomains.Length > 0)
        {
            try
            {
                AddLog("INFO", "正在获取 GitHub Hosts...");
                var ghEntries = await FetchGitHubHostsAsync();
                if (ghEntries.Count > 0)
                {
                    _hostsManager.AddGitHubHosts(ghEntries);
                    AddLog("INFO", $"已添加 {ghEntries.Count} 条 GitHub Hosts");
                }
                else
                {
                    _hostsManager.AddEntries(githubDomains);
                    AddLog("WARN", "获取 GitHub Hosts 失败，使用代理模式");
                }
            }
            catch
            {
                _hostsManager.AddEntries(githubDomains);
                AddLog("WARN", "获取 GitHub Hosts 异常，使用代理模式");
            }
        }

        _host = CreateHost();
        await _host.StartAsync();

        _isRunning = true;
        AddLog("INFO", "加速服务已启动");
        AddLog("INFO", $"HTTP 监听 127.0.0.1:{HttpPort}");
        AddLog("INFO", $"HTTPS 监听 127.0.0.1:{HttpsPort}");
        AddLog("INFO", $"已加速 {_domains.Length} 个域名");
    }

    public async Task StopAsync()
    {
        if (!_isRunning) return;

        AddLog("INFO", "正在停止加速服务...");

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }

        _hostsManager.RemoveEntries();
        HostsManager.FlushDnsCache();

        _isRunning = false;
        AddLog("INFO", "加速服务已停止");
    }

    private IHost CreateHost()
    {
        var certService = _certService;
        var logger = _logger;
        var domains = _domains;
        var dnsResolver = _dnsResolver;

        return Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel(kestrel =>
                {
                    kestrel.Limits.MaxRequestBodySize = null;
                    kestrel.Limits.MinResponseDataRate = null;
                    kestrel.Limits.MinRequestBodyDataRate = null;

                    // HTTP
                    if (HttpPort == 80)
                        kestrel.ListenLocalhost(HttpPort);
                    else
                        kestrel.Listen(IPAddress.Loopback, HttpPort);

                    // HTTPS with dynamic SNI
                    if (HttpsPort == 443)
                        kestrel.ListenLocalhost(HttpsPort, ConfigureHttps);
                    else
                        kestrel.Listen(IPAddress.Loopback, HttpsPort, ConfigureHttps);

                    void ConfigureHttps(ListenOptions listen)
                    {
                        var defaultCert = certService.GetOrCreateServerCert("localhost");
                        listen.UseHttps(defaultCert);
                    }
                });
                webBuilder.Configure(app =>
                {
                    app.UseMiddleware<ReverseProxyMiddleware>(
                        domains, dnsResolver, logger);
                });
                webBuilder.ConfigureServices(services =>
                {
                    services.AddHttpForwarder();
                });
            })
            .Build();
    }

    private void AddLog(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] [{level,-5}] {message}";
        lock (_logLock)
        {
            _logs.Add(logEntry);
            if (_logs.Count > 500)
                _logs.RemoveAt(0);
        }
        _logger.LogInformation("[{Level}] {Message}", level, message);
    }

    private static async Task<List<(string Ip, string Domain)>> FetchGitHubHostsAsync()
    {
        var entries = new List<(string Ip, string Domain)>();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var urls = new[]
        {
            "https://raw.githubusercontent.com/maxiaof/github-hosts/master/hosts",
            "https://ghfast.top/https://raw.githubusercontent.com/maxiaof/github-hosts/master/hosts"
        };
        string? text = null;
        foreach (var url in urls)
        {
            try { text = await client.GetStringAsync(url); break; }
            catch { }
        }
        if (text == null) return entries;

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#") || string.IsNullOrEmpty(trimmed)) continue;
            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && System.Net.IPAddress.TryParse(parts[0], out _))
                entries.Add((parts[0], parts[1]));
        }
        return entries;
    }

    private static int GetAvailablePort(int preferredPort)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, preferredPort);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
        catch
        {
            return preferredPort;
        }
    }
}

public class ReverseProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string[] _domains;
    private readonly DnsResolver _dnsResolver;
    private readonly ILogger _logger;

    private readonly Dictionary<string, string> _ipCache = new();
    private readonly object _cacheLock = new();

    public ReverseProxyMiddleware(
        RequestDelegate next,
        string[] domains,
        DnsResolver dnsResolver,
        ILogger logger)
    {
        _next = next;
        _domains = domains;
        _dnsResolver = dnsResolver;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;

        if (Array.IndexOf(_domains, host) < 0)
        {
            await _next(context);
            return;
        }

        var fastIp = await GetOrResolveIpAsync(host);

        var destinationPrefix = $"{context.Request.Scheme}://{host}/";

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (ctx, ct) =>
            {
                var socket = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp);
                var port = ctx.InitialRequestMessage.RequestUri?.Port ?? 443;
                await socket.ConnectAsync(IPAddress.Parse(fastIp), port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true
            }
        };

        var httpClient = new HttpClient(handler);

        try
        {
            var httpForwarder = context.RequestServices.GetRequiredService<IHttpForwarder>();

            var error = await httpForwarder.SendAsync(
                context,
                destinationPrefix,
                httpClient,
                new ForwarderRequestConfig(),
                HttpTransformer.Empty);

            if (error != ForwarderError.None && !context.Response.HasStarted)
            {
                var feature = context.GetForwarderErrorFeature();
                _logger.LogWarning("转发失败: {Host} - {Error}: {Message}",
                    host, error, feature?.Exception?.Message);
                context.Response.StatusCode = 502;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = error.ToString(),
                    message = feature?.Exception?.Message
                });
            }
        }
        finally
        {
            httpClient.Dispose();
            handler.Dispose();
        }
    }

    private async Task<string> GetOrResolveIpAsync(string domain)
    {
        lock (_cacheLock)
        {
            if (_ipCache.TryGetValue(domain, out var cached))
                return cached;
        }

        var ip = await _dnsResolver.ResolveFastIpAsync(domain);
        lock (_cacheLock)
        {
            _ipCache[domain] = ip;
        }
        return ip;
    }
}
