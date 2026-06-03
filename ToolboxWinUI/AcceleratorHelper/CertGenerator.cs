using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AcceleratorHelper;

public static class CertGenerator
{
    private const string CaSubjectName = "ToolboxWinCA";
    private const int RsaKeySize = 2048;
    private static readonly HashAlgorithmName HashAlgorithm = HashAlgorithmName.SHA256;

    public static X509Certificate2 CreateCACertificate(
        DateTimeOffset notBefore,
        DateTimeOffset notAfter)
    {
        using var rsa = RSA.Create(RsaKeySize);
        var subjectName = new X500DistinguishedName($"CN={CaSubjectName}");

        var request = new CertificateRequest(
            subjectName, rsa, HashAlgorithm, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, true, 0, true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.CrlSign |
                X509KeyUsageFlags.KeyCertSign, true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new("1.3.6.1.5.5.7.3.1"),
                    new("1.3.6.1.5.5.7.3.2")
                }, true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        return request.CreateSelfSigned(notBefore, notAfter);
    }

    public static X509Certificate2 CreateEndCertificate(
        X509Certificate2 issuerCertificate,
        string domain,
        IEnumerable<string>? extraDnsNames = null,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        notBefore ??= DateTimeOffset.UtcNow;
        notAfter ??= notBefore.Value.AddYears(1);

        using var rsa = RSA.Create(RsaKeySize);
        var subjectName = new X500DistinguishedName($"CN={domain}");

        var request = new CertificateRequest(
            subjectName, rsa, HashAlgorithm, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.KeyEncipherment, true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new("1.3.6.1.5.5.7.3.1"),
                    new("1.3.6.1.5.5.7.3.2")
                }, true));

        request.CertificateExtensions.Add(
            GetAuthorityKeyIdentifierExtension(issuerCertificate));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(domain);

        if (extraDnsNames != null)
        {
            foreach (var name in extraDnsNames)
            {
                if (System.Net.IPAddress.TryParse(name, out var addr))
                    sanBuilder.AddIpAddress(addr);
                else
                    sanBuilder.AddDnsName(name);
            }
        }

        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        var serial = BitConverter.GetBytes(Random.Shared.NextInt64());
        using var certOnly = request.Create(
            issuerCertificate, notBefore.Value, notAfter.Value, serial);
        return certOnly.CopyWithPrivateKey(rsa);
    }

    private static X509Extension GetAuthorityKeyIdentifierExtension(
        X509Certificate2 issuerCertificate)
    {
        // .NET 10 中 X509AuthorityKeyIdentifierExtension 构造函数已更改
        // 直接使用 issuer 的 SubjectKeyIdentifier
        var issuerSubjectKeyExt = issuerCertificate.Extensions
            .OfType<X509SubjectKeyIdentifierExtension>()
            .FirstOrDefault();

        if (issuerSubjectKeyExt != null)
        {
            return new X509SubjectKeyIdentifierExtension(
                issuerSubjectKeyExt.SubjectKeyIdentifier, false);
        }

        return new X509SubjectKeyIdentifierExtension(
            issuerCertificate.PublicKey, false);
    }
}
