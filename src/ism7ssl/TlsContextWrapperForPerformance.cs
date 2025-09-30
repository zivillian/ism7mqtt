using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;

namespace ism7ssl;

class TlsContextWrapperForPerformance : TlsClientContext
{
    private readonly TlsClientContext _inner;

    public TlsContextWrapperForPerformance(TlsClientContext inner)
    {
        _inner = inner;
        SecurityParameters = _inner.SecurityParameters;
    }

    public TlsCrypto Crypto => _inner.Crypto;

    public TlsNonceGenerator NonceGenerator => _inner.NonceGenerator;

    public SecurityParameters SecurityParameters { get; }

    public bool IsServer => _inner.IsServer;

    public ProtocolVersion[] ClientSupportedVersions => _inner.ClientSupportedVersions;

    public ProtocolVersion ClientVersion => _inner.ClientVersion;

    public ProtocolVersion RsaPreMasterSecretVersion => _inner.RsaPreMasterSecretVersion;

    public ProtocolVersion ServerVersion => _inner.ServerVersion;

    public TlsSession ResumableSession => _inner.ResumableSession;

    public TlsSession Session => _inner.Session;

    public object UserObject
    {
        get => _inner.UserObject;
        set => _inner.UserObject = value;
    }

    public byte[] ExportChannelBinding(int channelBinding)
    {
        return _inner.ExportChannelBinding(channelBinding);
    }

    public byte[] ExportEarlyKeyingMaterial(string asciiLabel, byte[] context_value, int length)
    {
        return _inner.ExportEarlyKeyingMaterial(asciiLabel, context_value, length);
    }

    public byte[] ExportKeyingMaterial(string asciiLabel, byte[] context_value, int length)
    {
        return _inner.ExportKeyingMaterial(asciiLabel, context_value, length);
    }
}