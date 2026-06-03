using System.Diagnostics;
using System.Text;
using Path = System.IO.Path;

namespace ToolboxWinUI.ProxyTools;

public class ProxyEngine
{
    private Process? _process;
    private readonly string _mihomoPath;
    private readonly string _configDir;
    private readonly string _configPath;

    public bool IsRunning => _process is { HasExited: false };
    public string ApiBaseUrl => "http://127.0.0.1:9097";

    public enum Stage { Stopped, Preparing, FreeingPort, CopyingGeoData, FixingConfig, StartingProcess, WaitingForApi, Running, Error }
    public Stage CurrentStage { get; private set; } = Stage.Stopped;
    public string? StageMessage { get; private set; }
    public string? LastError { get; private set; }

    public event Action? StatusChanged;
    public event Action<Stage, string?>? StageChanged;

    public ProxyEngine()
    {
        _mihomoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProxyTools", "mihomo", "mihomo.exe");
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ToolboxWinUI", "proxy");
        _configDir = appData;
        _configPath = Path.Combine(appData, "config.yaml");
        Directory.CreateDirectory(appData);
    }

    private void SetStage(Stage stage, string? msg = null)
    {
        CurrentStage = stage;
        StageMessage = msg;
        StageChanged?.Invoke(stage, msg);
        StatusChanged?.Invoke();
    }

    public static Process[] FindKernelProcesses()
    {
        try { return Process.GetProcessesByName("mihomo"); }
        catch { return []; }
    }

    public static bool IsKernelRunning => FindKernelProcesses().Length > 0;

    public void KillExternalKernels()
    {
        var ourPid = _process?.Id;
        foreach (var p in FindKernelProcesses())
        {
            if (p.Id != ourPid)
            {
                try { p.Kill(); p.WaitForExit(3000); p.Dispose(); } catch { }
            }
        }
    }

    public void KillAllKernels()
    {
        foreach (var p in FindKernelProcesses())
        {
            try { p.Kill(); p.WaitForExit(3000); p.Dispose(); } catch { }
        }
        _process = null;
    }

    public string GetKernelStatus()
    {
        if (IsRunning) return "本机内核运行中";
        if (IsKernelRunning) return "外部内核运行中";
        return CurrentStage == Stage.Error ? $"启动失败: {LastError}" : "内核未运行";
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;
        LastError = null;

        SetStage(Stage.Preparing, "准备启动...");
        KillExternalKernels();

        SetStage(Stage.FreeingPort, "释放端口...");
        FreePort(9097);
        await Task.Delay(1000);

        SetStage(Stage.CopyingGeoData, "复制地理数据...");
        CopyGeoData();

        SetStage(Stage.FixingConfig, "处理配置文件...");
        if (!await EnsureConfigAsync()) return;

        SetStage(Stage.StartingProcess, "启动内核进程...");
        if (!StartProcess()) return;

        SetStage(Stage.WaitingForApi, "等待 API 响应...");
        SetStage(Stage.Running, "运行中");
    }

    private void CopyGeoData()
    {
        var mihomoDir = Path.GetDirectoryName(_mihomoPath)!;
        foreach (var file in new[] { "Country.mmdb", "geoip.dat", "geosite.dat" })
        {
            var src = Path.Combine(mihomoDir, file);
            var dst = Path.Combine(_configDir, file);
            if (File.Exists(src) && !File.Exists(dst))
            {
                try { File.Copy(src, dst); } catch { }
            }
        }
    }

    private async Task<bool> EnsureConfigAsync()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                await File.WriteAllTextAsync(_configPath, GenerateDefaultConfig());
                return true;
            }
            var cfg = await File.ReadAllTextAsync(_configPath);
            if (cfg.Trim().Length < 30)
            {
                await File.WriteAllTextAsync(_configPath, GenerateDefaultConfig());
                return true;
            }
            cfg = cfg.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = cfg.Split('\n').ToList();
            lines.RemoveAll(l => l.TrimStart().StartsWith("external-controller:") ||
                                 l.TrimStart().StartsWith("external-ui:"));
            var output = string.Join("\r\n", lines.Where(l => l != null));
            output = output.TrimEnd() + "\r\n\r\nexternal-controller: 0.0.0.0:9097\r\nexternal-ui: dashboard\r\nlog-file: logs/run.log\r\n";
            await File.WriteAllTextAsync(_configPath, output);
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"配置处理失败: {ex.Message}";
            SetStage(Stage.Error, LastError);
            return false;
        }
    }

    private bool StartProcess()
    {
        try
        {
            if (!File.Exists(_mihomoPath))
            {
                LastError = $"内核文件不存在: {_mihomoPath}";
                SetStage(Stage.Error, LastError);
                return false;
            }
            var psi = new ProcessStartInfo
            {
                FileName = _mihomoPath,
                Arguments = $"-d \"{_configDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.Exited += (_, _) =>
            {
                _process?.Dispose();
                _process = null;
                SetStage(Stage.Stopped, "进程已退出");
            };
            if (!_process.Start())
            {
                LastError = "进程启动返回 false";
                _process.Dispose();
                _process = null;
                SetStage(Stage.Error, LastError);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            _process?.Dispose();
            _process = null;
            LastError = $"进程启动异常: {ex.Message}";
            SetStage(Stage.Error, LastError);
            return false;
        }
    }

    public void Stop()
    {
        if (_process is { HasExited: false })
        {
            try { _process.Kill(); } catch { }
            _process.Dispose();
            _process = null;
        }
        SetStage(Stage.Stopped, "已停止");
    }

    public async Task RestartAsync()
    {
        Stop();
        await Task.Delay(500);
        await StartAsync();
    }

    public string GetConfig()
    {
        try { return File.Exists(_configPath) ? File.ReadAllText(_configPath) : ""; }
        catch { return ""; }
    }

    public async Task SaveConfigAsync(string content)
    {
        try { await File.WriteAllTextAsync(_configPath, content); }
        catch { }
    }

    private static void FreePort(int port)
    {
        try
        {
            var lines = Process.Start(new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            })?.StandardOutput.ReadToEnd();
            if (lines == null) return;
            foreach (var line in lines.Split('\n'))
            {
                if (!line.Contains($":{port}") || !line.Contains("LISTEN")) continue;
                var parts = line.TrimEnd().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 4 && int.TryParse(parts[^1], out var pid) && pid > 0)
                {
                    try { Process.GetProcessById(pid)?.Kill(); } catch { }
                }
            }
        }
        catch { }
    }

    private string GenerateDefaultConfig()
    {
        return @"mixed-port: 7890
socks-port: 7891
allow-lan: true
mode: Rule
log-level: info
ipv6: true
external-controller: 0.0.0.0:9097
external-ui: dashboard
log-file: logs/run.log

dns:
  enable: true
  default-nameserver:
    - 223.5.5.5
    - 114.114.114.114
  nameserver:
    - https://doh.pub/dns-query
    - https://dns.alidns.com/dns-query
  fallback:
    - https://dns.google/dns-query
    - https://cloudflare-dns.com/dns-query
  fallback-filter:
    geoip: false
    geoip-code: CN

proxies: []

proxy-groups:
  - name: Proxy
    type: select
    proxies:
      - DIRECT

rules:
  - MATCH,DIRECT
";
    }
}
