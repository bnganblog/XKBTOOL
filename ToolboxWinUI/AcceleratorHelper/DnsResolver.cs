using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;

namespace AcceleratorHelper;

public class DnsResolver
{
    private readonly ILogger<DnsResolver> _logger;

    private static readonly Dictionary<string, string> DohServers = new()
    {
        ["阿里"] = "https://dns.alidns.com/resolve",
        ["Cloudflare"] = "https://cloudflare-dns.com/resolve",
        ["又拍云"] = "https://doh.pub/resolve",
        ["Google"] = "https://dns.google/resolve"
    };

    private static readonly Dictionary<string, string> PresetHosts = new()
    {
        ["i.scdn.co"] = "117.18.232.151",
        ["p.scdn.co"] = "117.18.232.151",
        ["r.scdn.co"] = "117.18.232.151",
        ["t.scdn.co"] = "117.18.232.151",
        ["u.scdn.co"] = "117.18.232.151",
        ["audio-ec.spotify.com"] = "117.18.232.151",
    };

    private readonly Dictionary<string, (IPAddress Ip, long Ms, string Server)> _cache = new();
    private readonly object _cacheLock = new();

    public DnsResolver(ILogger<DnsResolver> logger)
    {
        _logger = logger;
    }

    public async Task<IPAddress> ResolveAsync(string domain)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(domain, out var cached))
            {
                _logger.LogDebug("DNS 缓存命中: {Domain} → {Ip} ({Ms}ms, {Server})",
                    domain, cached.Ip, cached.Ms, cached.Server);
                return cached.Ip;
            }
        }

        if (PresetHosts.TryGetValue(domain, out var presetIp))
        {
            var ip = IPAddress.Parse(presetIp);
            lock (_cacheLock) { _cache[domain] = (ip, 0, "预设Hosts"); }
            _logger.LogInformation("DNS 预设Hosts: {Domain} → {Ip}", domain, ip);
            return ip;
        }

        var results = new List<(IPAddress Ip, long Ms, string Server)>();

        foreach (var (serverName, serverUrl) in DohServers)
        {
            try
            {
                var (ip, ms) = await QueryDohAsync(domain, serverUrl, serverName);
                if (ip != null)
                {
                    results.Add((ip, ms, serverName));
                    _logger.LogInformation("DNS 解析: {Domain} → {Ip} ({Ms}ms, {Server})",
                        domain, ip, ms, serverName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DNS 解析失败: {Domain} ({Server})",
                    domain, serverName);
            }
        }

        if (results.Count == 0)
        {
            _logger.LogWarning("所有 DNS 服务器解析失败: {Domain}，使用系统 DNS", domain);
            return await ResolveSystemAsync(domain);
        }

        var best = results.OrderBy(r => r.Ms).First();

        lock (_cacheLock)
        {
            _cache[domain] = best;
        }

        return best.Ip;
    }

    public async Task<string> ResolveFastIpAsync(string domain)
    {
        var ip = await ResolveAsync(domain);
        return ip.ToString();
    }

    public async Task<Dictionary<string, string>> ResolveAllAsync(string[] domains)
    {
        var results = new Dictionary<string, string>();
        var tasks = domains.Select(async domain =>
        {
            var ip = await ResolveFastIpAsync(domain);
            lock (results)
            {
                results[domain] = ip;
            }
        });
        await Task.WhenAll(tasks);
        return results;
    }

    private async Task<(IPAddress? Ip, long Ms)> QueryDohAsync(
        string domain, string serverUrl, string serverName)
    {
        var url = $"{serverUrl}?name={domain}&type=A";
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(3);

        var sw = Stopwatch.StartNew();
        var response = await client.GetStringAsync(url);
        sw.Stop();

        using var doc = JsonDocument.Parse(response);
        var root = doc.RootElement;

        if (root.TryGetProperty("Answer", out var answers) &&
            answers.GetArrayLength() > 0)
        {
            foreach (var answer in answers.EnumerateArray())
            {
                if (answer.TryGetProperty("data", out var dataProp))
                {
                    var ipStr = dataProp.GetString();
                    if (IPAddress.TryParse(ipStr, out var ip))
                    {
                        return (ip, sw.ElapsedMilliseconds);
                    }
                }
            }
        }

        return (null, sw.ElapsedMilliseconds);
    }

    private async Task<IPAddress> ResolveSystemAsync(string domain)
    {
        var addresses = await Dns.GetHostAddressesAsync(domain);
        if (addresses.Length == 0)
            throw new InvalidOperationException($"无法解析域名: {domain}");

        var best = addresses[0];
        var bestMs = long.MaxValue;

        foreach (var ip in addresses)
        {
            try
            {
                using var client = new TcpClient();
                var sw = Stopwatch.StartNew();
                await client.ConnectAsync(ip, 443)
                    .WaitAsync(TimeSpan.FromSeconds(2));
                sw.Stop();

                if (sw.ElapsedMilliseconds < bestMs)
                {
                    bestMs = sw.ElapsedMilliseconds;
                    best = ip;
                }
            }
            catch
            {
                // 连接失败，跳过
            }
        }

        _logger.LogInformation("系统 DNS 解析: {Domain} → {Ip} ({Ms}ms)",
            domain, best, bestMs);
        return best;
    }

    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _cache.Clear();
        }
    }
}
