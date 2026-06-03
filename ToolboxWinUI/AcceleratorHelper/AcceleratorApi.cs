using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AcceleratorHelper;

public class AcceleratorApi
{
    private readonly CertService _certService;
    private readonly HostsManager _hostsManager;
    private readonly DnsResolver _dnsResolver;
    private readonly AcceleratorService? _acceleratorService;

    private IHost? _webHost;
    private bool _isRunning;
    private readonly List<string> _logs = new();
    private readonly object _logLock = new();

    public bool IsRunning => _isRunning;
    public List<string> Logs
    {
        get { lock (_logLock) return _logs.ToList(); }
    }

    public AcceleratorApi(string certDir)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
        });

        _certService = new CertService(certDir, loggerFactory.CreateLogger<CertService>());
        _hostsManager = new HostsManager(loggerFactory.CreateLogger<HostsManager>());
        _dnsResolver = new DnsResolver(loggerFactory.CreateLogger<DnsResolver>());
    }

    public void CreateCaCertIfNotExists()
    {
        _certService.CreateCaCertIfNotExists();
    }

    public void InstallCaCert()
    {
        _certService.InstallCaCert();
    }

    public void UninstallCaCert()
    {
        _certService.UninstallCaCert();
    }

    public bool IsCaCertInstalled()
    {
        return _certService.IsCaCertInstalled();
    }

    public async Task StartAsync(string[] services)
    {
        if (_isRunning) return;

        var domains = DomainConfig.GetEnabledDomains(services);

        AddLog("INFO", "正在启动加速服务...");
        _hostsManager.BackupHosts();
        _hostsManager.AddEntries(domains);
        AddLog("INFO", $"已添加 {domains.Length} 条 hosts 条目");

        var acceleratorLogger = LoggerFactory.Create(b => b.AddConsole())
            .CreateLogger<AcceleratorService>();

        var accelerator = new AcceleratorService(
            domains, _certService, _hostsManager, _dnsResolver, acceleratorLogger);

        await accelerator.StartAsync();

        _isRunning = true;
        AddLog("INFO", "加速服务已启动");
    }

    public async Task StopAsync()
    {
        if (!_isRunning) return;

        AddLog("INFO", "正在停止加速服务...");
        _hostsManager.RemoveEntries();
        HostsManager.FlushDnsCache();

        _isRunning = false;
        AddLog("INFO", "加速服务已停止");
    }

    public void StopSync()
    {
        if (!_isRunning) return;

        AddLog("INFO", "正在停止加速服务...");
        _hostsManager.RemoveEntries();
        HostsManager.FlushDnsCache();

        _isRunning = false;
        AddLog("INFO", "加速服务已停止");
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
    }
}
