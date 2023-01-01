using System.Text;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace oldism7proxy;

class Ism7TlsClient : DefaultTlsClient
{
    private static readonly int[] _cipherSuites =
    {
        CipherSuite.TLS_RSA_WITH_RC4_128_MD5,
        CipherSuite.TLS_RSA_WITH_RC4_128_SHA,
        CipherSuite.TLS_RSA_EXPORT_WITH_RC4_40_MD5,
        CipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256,
        CipherSuite.TLS_RSA_WITH_NULL_MD5,
    };

    public Ism7TlsClient() : base(new BcTlsCrypto())
    {
    }

    public override TlsAuthentication GetAuthentication()
    {
        var store = new Pkcs12StoreBuilder().Build();
        store.Load(new MemoryStream(Resources.client), Array.Empty<char>());
        var alias = store.Aliases.First();
        var cert = store.GetCertificate(alias);
        var key = store.GetKey(alias);
        var signer = new BcDefaultTlsCredentialedSigner(new TlsCryptoParameters(m_context),
            new BcTlsCrypto(Crypto.SecureRandom), key.Key,
            new Certificate(CertificateType.X509, null, new[]
            {
                new CertificateEntry(Crypto.CreateCertificate(cert.Certificate.GetEncoded()), null)
            }),
            SignatureAndHashAlgorithm.GetInstance(HashAlgorithm.sha256, SignatureAlgorithm.rsa)
        );
        return new Ism7TlsAuthentication(signer);
    }

    protected override int[] GetSupportedCipherSuites()
    {
        return _cipherSuites;
    }

    protected override ProtocolVersion[] GetSupportedVersions()
    {
        return new[] { ProtocolVersion.TLSv12, ProtocolVersion.SSLv3 };
    }

    public override TlsKeyExchangeFactory GetKeyExchangeFactory()
    {
        return new DefaultTlsKeyExchangeFactory();
    }

    protected override IList<ServerName> GetSniServerNames()
    {
        return new List<ServerName>
        {
            new ServerName(NameType.host_name, Encoding.UTF8.GetBytes("ism7.server"))
        };
    }

    public override void NotifySecureRenegotiation(bool secureRenegotiation)
    {
    }

    public override void NotifySelectedCipherSuite(int selectedCipherSuite)
    {
    }
}