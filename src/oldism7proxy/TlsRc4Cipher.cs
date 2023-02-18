using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;
using Org.BouncyCastle.Tls.Crypto.Impl;

namespace oldism7proxy;

/// <summary>A generic SSL 3.0 RC4 cipher.</summary>
public class TlsRc4Cipher
    :TlsCipher
{
    private readonly TlsSuiteHmac m_readMac, m_writeMac;
    private readonly RC4Engine m_encryptionCipher;
    private readonly RC4Engine m_decryptionCipher;

    public TlsRc4Cipher(TlsCryptoParameters cryptoParams, int cipherKeySize, TlsHmac clientMac, TlsHmac serverMac)
    {
        if (cipherKeySize < 1 || cipherKeySize > 256)
        {
            throw new TlsFatalAlert(AlertDescription.internal_error);
        }
        m_encryptionCipher = new RC4Engine();
        m_decryptionCipher = new RC4Engine();
            
        IStreamCipher clientCipher, serverCipher;
        if (cryptoParams.IsServer)
        {
            clientCipher = m_decryptionCipher;
            serverCipher = m_encryptionCipher;
        }
        else
        {
            clientCipher = m_encryptionCipher;
            serverCipher = m_decryptionCipher;
        }
        int keyBlockSize = 2 * cipherKeySize + clientMac.MacLength + serverMac.MacLength;
        
        Span<byte> keyBlock = keyBlockSize <= 512
            ? stackalloc byte[keyBlockSize]
            : new byte[keyBlockSize];
        TlsImplUtilities.CalculateKeyBlock(cryptoParams, keyBlock);

        clientMac.SetKey(keyBlock[..clientMac.MacLength]); keyBlock = keyBlock[clientMac.MacLength..];
        serverMac.SetKey(keyBlock[..serverMac.MacLength]); keyBlock = keyBlock[serverMac.MacLength..];
        clientCipher.Init(true, new KeyParameter(keyBlock[..cipherKeySize])); keyBlock = keyBlock[cipherKeySize..];
        serverCipher.Init(false, new KeyParameter(keyBlock[..cipherKeySize])); keyBlock = keyBlock[cipherKeySize..];
        if (!keyBlock.IsEmpty)
            throw new TlsFatalAlert(AlertDescription.internal_error);
        if (cryptoParams.IsServer)
        {
            this.m_writeMac = new TlsSuiteHmac(cryptoParams, serverMac);
            this.m_readMac = new TlsSuiteHmac(cryptoParams, clientMac);
        }
        else
        {
            this.m_writeMac = new TlsSuiteHmac(cryptoParams, clientMac);
            this.m_readMac = new TlsSuiteHmac(cryptoParams, serverMac);
        }
    }

    public int GetCiphertextDecodeLimit(int plaintextLimit)
    {
        return plaintextLimit + m_readMac.Size;
    }

    public int GetCiphertextEncodeLimit(int plaintextLength, int plaintextLimit)
    {
        return plaintextLength + m_writeMac.Size;
    }

    public int GetPlaintextLimit(int ciphertextLimit)
    {
        return ciphertextLimit - m_writeMac.Size;
    }

    public TlsEncodeResult EncodePlaintext(long seqNo, short contentType, ProtocolVersion recordVersion, int headerAllocation,
        byte[] plaintext, int offset, int len)
    {
        throw new NotImplementedException();
    }

    public TlsEncodeResult EncodePlaintext(long seqNo, short contentType, ProtocolVersion recordVersion, int headerAllocation,
        ReadOnlySpan<byte> plaintext)
    {
        byte[] mac = m_writeMac.CalculateMac(seqNo, contentType, plaintext);
        byte[] ciphertext = new byte[headerAllocation + plaintext.Length + mac.Length];
        var encrypted = ciphertext.AsSpan(headerAllocation);
        m_encryptionCipher.ProcessBytes(plaintext, encrypted);
        m_encryptionCipher.ProcessBytes(mac, encrypted.Slice(plaintext.Length));
        return new TlsEncodeResult(ciphertext, 0, ciphertext.Length, contentType);
    }

    public TlsDecodeResult DecodeCiphertext(long seqNo, short recordType, ProtocolVersion recordVersion, byte[] ciphertext,
        int offset, int len)
    {
        int macSize = m_readMac.Size;
        if (len < macSize)
            throw new TlsFatalAlert(AlertDescription.decode_error);

        int macInputLen = len - macSize;

        var plaintext = new byte[len];
        m_decryptionCipher.ProcessBytes(ciphertext.AsSpan(offset, len), plaintext);

        byte[] expectedMac = m_readMac.CalculateMac(seqNo, recordType, plaintext.AsSpan(0, macInputLen));
        bool badMac = !TlsUtilities.ConstantTimeAreEqual(macSize, expectedMac, 0, plaintext, macInputLen);
        if (badMac)
            throw new TlsFatalAlert(AlertDescription.bad_record_mac);

        return new TlsDecodeResult(plaintext, 0, macInputLen, recordType);
    }

    public void RekeyDecoder()
    {
        throw new NotImplementedException();
    }

    public void RekeyEncoder()
    {
        throw new NotImplementedException();
    }

    public bool UsesOpaqueRecordType => false;
}