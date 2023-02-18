using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace oldism7proxy;

class Ism7TlsCrypto : BcTlsCrypto
{
    public override TlsCipher CreateCipher(TlsCryptoParameters cryptoParams, int encryptionAlgorithm, int macAlgorithm)
    {
        switch (encryptionAlgorithm)
        {
            case EncryptionAlgorithm.RC4_128:
                return CreateCipher_RC4(cryptoParams, 16, macAlgorithm);
            case EncryptionAlgorithm.RC4_40:
                return CreateCipher_RC4(cryptoParams, 5, macAlgorithm);
            default:
                return base.CreateCipher(cryptoParams, encryptionAlgorithm, macAlgorithm);
        }
    }

    public override bool HasEncryptionAlgorithm(int encryptionAlgorithm)
    {
        switch (encryptionAlgorithm)
        {
            case EncryptionAlgorithm.RC4_128:
            case EncryptionAlgorithm.RC4_40:
                return true;
            default:
                return base.HasEncryptionAlgorithm(encryptionAlgorithm);
        }
    }

    private TlsCipher CreateCipher_RC4(TlsCryptoParameters cryptoParams, int cipherKeySize, int macAlgorithm)
    {
        return new TlsRc4Cipher(cryptoParams, cipherKeySize, CreateMac(cryptoParams, macAlgorithm),
            CreateMac(cryptoParams, macAlgorithm));
    }
}