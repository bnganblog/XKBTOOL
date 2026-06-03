namespace AcceleratorHelper;

public class HostsManager
{
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "System32", "drivers", "etc", "hosts");

    private const string MarkerBegin = "# >>> ToolboxWin Begin >>>";
    private const string MarkerEnd = "# <<< ToolboxWin End <<<";

    private readonly ILogger<HostsManager> _logger;
    private readonly string _backupPath;

    public HostsManager(ILogger<HostsManager> logger)
    {
        _logger = logger;
        _backupPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ToolboxWinUI", "hosts.backup");
    }

    public void BackupHosts()
    {
        try
        {
            File.Copy(HostsPath, _backupPath, overwrite: true);
            _logger.LogInformation("已备份 hosts 文件: {Path}", _backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "备份 hosts 文件失败");
        }
    }

    public void RestoreHosts()
    {
        try
        {
            if (File.Exists(_backupPath))
            {
                File.Copy(_backupPath, HostsPath, overwrite: true);
                _logger.LogInformation("已还原 hosts 文件");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "还原 hosts 文件失败");
        }
    }

    public void AddEntries(string[] domains, string ipAddress = "127.0.0.1")
    {
        try
        {
            var lines = File.ReadAllLines(HostsPath).ToList();
            RemoveOurEntries(lines);

            lines.Add("");
            lines.Add(MarkerBegin);
            foreach (var domain in domains)
            {
                lines.Add($"{ipAddress} {domain}");
            }
            lines.Add(MarkerEnd);
            lines.Add("");

            File.WriteAllLines(HostsPath, lines);
            _logger.LogInformation("已添加 {Count} 条 hosts 条目", domains.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加 hosts 条目失败");
            throw;
        }
    }

    public void AddGitHubHosts(List<(string Ip, string Domain)> entries)
    {
        try
        {
            var lines = File.ReadAllLines(HostsPath).ToList();
            RemoveOurEntries(lines);

            lines.Add("");
            lines.Add(MarkerBegin);
            lines.Add("# Github Hosts Start");
            lines.Add("# Project Address: https://github.com/maxiaof/github-hosts");
            foreach (var (ip, domain) in entries)
            {
                lines.Add($"{ip} {domain}");
            }
            lines.Add("# Github Hosts End");
            lines.Add(MarkerEnd);
            lines.Add("");

            File.WriteAllLines(HostsPath, lines);
            _logger.LogInformation("已添加 {Count} 条 GitHub Hosts 条目", entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加 GitHub Hosts 条目失败");
            throw;
        }
    }

    public void AddSteamHosts(List<(string Ip, string Domain)> entries)
    {
        try
        {
            var lines = File.ReadAllLines(HostsPath).ToList();
            RemoveOurEntries(lines);

            lines.Add("");
            lines.Add(MarkerBegin);
            lines.Add("# Steam Hosts Start");
            foreach (var (ip, domain) in entries)
            {
                lines.Add($"{ip} {domain}");
            }
            lines.Add("# Steam Hosts End");
            lines.Add(MarkerEnd);
            lines.Add("");

            File.WriteAllLines(HostsPath, lines);
            _logger.LogInformation("已添加 {Count} 条 Steam Hosts 条目", entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加 Steam Hosts 条目失败");
            throw;
        }
    }

    public void RemoveEntries()
    {
        try
        {
            var lines = File.ReadAllLines(HostsPath).ToList();
            var removed = RemoveOurEntries(lines);

            if (removed > 0)
            {
                File.WriteAllLines(HostsPath, lines);
                _logger.LogInformation("已移除 {Count} 条 hosts 条目", removed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除 hosts 条目失败");
            throw;
        }
    }

    private int RemoveOurEntries(List<string> lines)
    {
        int beginIdx = lines.FindIndex(l => l.Trim() == MarkerBegin);
        int endIdx = lines.FindIndex(l => l.Trim() == MarkerEnd);

        if (beginIdx >= 0 && endIdx >= 0 && endIdx > beginIdx)
        {
            int count = endIdx - beginIdx + 1;
            lines.RemoveRange(beginIdx, count);
            return count;
        }

        return 0;
    }

    public static void FlushDnsCache()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch
        {
            // 非关键操作，忽略错误
        }
    }
}
