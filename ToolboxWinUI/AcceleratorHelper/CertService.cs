using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Caching.Memory;

namespace AcceleratorHelper;

public class CertService
{
    private readonly string _certDir;
    private readonly ILogger<CertService> _logger;
    private readonly IMemoryCache _certCache;

    private X509Certificate2? _caCert;

    private const string CaCertFile = "ToolboxWinCA.pfx";
    private const string CaCertPassword = "ToolboxWinCA2026!";

    public CertService(string certDir, ILogger<CertService> logger)
    {
        _certDir = certDir;
        _logger = logger;
        _certCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 200
        });

        Directory.CreateDirectory(_certDir);
    }

    public void CreateCaCertIfNotExists()
    {
        var caPath = Path.Combine(_certDir, CaCertFile);
        if (File.Exists(caPath))
        {
            var pfxBytes = File.ReadAllBytes(caPath);
            _caCert = X509CertificateLoader.LoadPkcs12(pfxBytes, CaCertPassword);
            _logger.LogInformation("已加载现有根证书: {Thumbprint}",
                _caCert.Thumbprint);
        }
        else
        {
            _caCert = CertGenerator.CreateCACertificate(
                DateTimeOffset.UtcNow.AddYears(-1),
                DateTimeOffset.UtcNow.AddYears(10));

            var pfxBytes = _caCert.Export(
                X509ContentType.Pfx, CaCertPassword);
            File.WriteAllBytes(caPath, pfxBytes);

            var pemPath = Path.Combine(_certDir, "ToolboxWinCA.pem");
            File.WriteAllText(pemPath, _caCert.ExportCertificatePem());

            _logger.LogInformation("已生成根证书: {Thumbprint}",
                _caCert.Thumbprint);
        }
    }

    public void InstallCaCert()
    {
        if (_caCert == null) return;

        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);

            var existing = store.Certificates.Find(
                X509FindType.FindBySubjectName, "ToolboxWinCA", false);
            foreach (var cert in existing)
            {
                if (cert.Thumbprint != _caCert.Thumbprint)
                    store.Remove(cert);
            }

            if (store.Certificates.Find(
                X509FindType.FindByThumbprint, _caCert.Thumbprint, false).Count == 0)
            {
                store.Add(_caCert);
                _logger.LogInformation("已安装根证书到系统信任存储");
            }

            store.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "安装根证书失败");
            throw;
        }
    }

    public void UninstallCaCert()
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);

            var certs = store.Certificates.Find(
                X509FindType.FindBySubjectName, "ToolboxWinCA", false);
            foreach (var cert in certs)
            {
                store.Remove(cert);
                _logger.LogInformation("已卸载根证书: {Thumbprint}", cert.Thumbprint);
            }

            store.Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "卸载根证书失败");
            throw;
        }
    }

    public bool IsCaCertInstalled()
    {
        if (_caCert == null) return false;

        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadOnly);

            var found = store.Certificates.Find(
                X509FindType.FindByThumbprint, _caCert.Thumbprint, false);
            store.Close();

            return found.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public X509Certificate2 GetOrCreateServerCert(string domain)
    {
        return _certCache.GetOrCreate($"Cert:{domain}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(365);
            entry.Size = 1;

            var extraDns = new List<string> { "localhost" };
            var cert = CertGenerator.CreateEndCertificate(
                _caCert!,
                domain,
                extraDns,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddYears(1));

            _logger.LogDebug("已生成域名证书: {Domain}", domain);
            return cert;
        });
    }

    public void Dispose()
    {
        _certCache.Dispose();
        _caCert?.Dispose();
    }
}
