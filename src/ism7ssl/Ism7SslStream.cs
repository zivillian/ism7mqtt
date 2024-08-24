using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Org.BouncyCastle.Tls;

namespace ism7ssl;

public class Ism7SslStream : Stream
{
    private readonly Socket _socket;
    private readonly TlsClientProtocol _protocol;

    public Ism7SslStream(Socket socket)
    {
        _socket = socket;
        _protocol = new TlsClientProtocol();
    }

    public static X509Certificate2 Certificate => new(Resources.client);

    public async Task AuthenticateAsClientAsync(CancellationToken cancellationToken)
    {
        var tlsClient = new Ism7TlsClient();
        _protocol.Connect(tlsClient);
        while (!tlsClient.HandshakeComplete)
        {
            var bytes = _protocol.GetAvailableOutputBytes();
            if (bytes > 0)
            {
                await WriteAsync(Array.Empty<byte>(), cancellationToken);
            }
            else
            {
                var buffer = ArrayPool<byte>.Shared.Rent(8192);
                bytes = await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
                if (bytes == 0)
                {
                    if (_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0)
                        throw new SocketException(10057);
                }
                _protocol.OfferInput(buffer, 0, bytes);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    public override void Flush()
    {
        throw new NotSupportedException("use async");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("use async");
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException("use async");
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("use async");
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _socket.Dispose();
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        if (!MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> array))
        {
            throw new NotSupportedException("could not get array from buffer");
        }

        while (true)
        {
            var bytes = _protocol.GetAvailableInputBytes();
            if (bytes > 0)
            {
                return _protocol.ReadInput(array.Array, array.Offset, array.Count);
            }
            bytes = await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
            if (bytes == 0)
            {
                if (_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0)
                    throw new SocketException(10057);
                return 0;
            }
            _protocol.OfferInput(array.Array, array.Offset, bytes);
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
    {
        if (!buffer.IsEmpty)
        {
            _protocol.WriteApplicationData(buffer.Span);
        }
        var bytes = _protocol.GetAvailableOutputBytes();
        var sendBuffer = ArrayPool<byte>.Shared.Rent(bytes);
        bytes = _protocol.ReadOutput(sendBuffer, 0, sendBuffer.Length);
        var data = sendBuffer.AsMemory(0, bytes);
        while (!data.IsEmpty)
        {
            bytes = await _socket.SendAsync(data, SocketFlags.None, cancellationToken);
            data = data.Slice(bytes);
        }
    }
}