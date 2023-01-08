using Org.BouncyCastle.Tls;

namespace oldism7proxy;

class Ism7TlsAuthentication : TlsAuthentication
{
    private readonly TlsCredentialedSigner _signer;
    
    public Ism7TlsAuthentication(TlsCredentialedSigner signer)
    {
        _signer = signer;
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
        //accept all
    }

    public TlsCredentials GetClientCredentials(CertificateRequest certificateRequest)
    {
        return _signer;
    }
}