using AcceleratorHelper;

var services = new List<string>();
var statusPort = 20800;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--services" && i + 1 < args.Length)
    {
        services.AddRange(args[i + 1].Split(','));
    }
    else if (args[i] == "--status-port" && i + 1 < args.Length)
    {
        int.TryParse(args[i + 1], out statusPort);
    }
}

if (services.Count == 0)
{
    services.AddRange(DomainConfig.Services.Keys);
}

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<AcceleratorService>();
var certLogger = loggerFactory.CreateLogger<CertService>();
var hostsLogger = loggerFactory.CreateLogger<HostsManager>();
var dnsLogger = loggerFactory.CreateLogger<DnsResolver>();

var certDir = Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory, "certs");

var certService = new CertService(certDir, certLogger);
var hostsManager = new HostsManager(hostsLogger);
var dnsResolver = new DnsResolver(dnsLogger);

var domains = DomainConfig.GetEnabledDomains(services.ToArray());

var accelerator = new AcceleratorService(
    domains, certService, hostsManager, dnsResolver, logger);

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    accelerator.StopAsync().GetAwaiter().GetResult();
    Environment.Exit(0);
};

AppDomain.CurrentDomain.ProcessExit += (_, _) =>
{
    accelerator.StopAsync().GetAwaiter().GetResult();
};

try
{
    await accelerator.StartAsync();
    Console.WriteLine("加速服务已启动，按 Ctrl+C 停止...");
    await Task.Delay(Timeout.Infinite);
}
catch (Exception ex)
{
    Console.WriteLine($"启动失败: {ex.Message}");
    await accelerator.StopAsync();
    Environment.Exit(1);
}
