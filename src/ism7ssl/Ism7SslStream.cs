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
        while (true)
        {
            var bytes = _protocol.GetAvailableInputBytes();
            if (bytes > 0)
            {
                return _protocol.ReadInput(buffer, offset, count);
            }

            var readBuffer = ArrayPool<byte>.Shared.Rent(8192);
            bytes = _socket.Receive(readBuffer, SocketFlags.None);
            if (bytes == 0)
            {
                if (_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0)
                    throw new SocketException(10057);
                return 0;
            }
            _protocol.OfferInput(readBuffer, 0, bytes);
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
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

    public override int ReadTimeout
    {
        get => _socket.ReceiveTimeout;
        set => _socket.ReceiveTimeout = value;
    }

    public override bool CanTimeout => true;

    public override int WriteTimeout
    {
        get => _socket.SendTimeout;
        set => _socket.SendTimeout = value;
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
            var readBuffer = ArrayPool<byte>.Shared.Rent(8192);
            bytes = await _socket.ReceiveAsync(readBuffer, SocketFlags.None, cancellationToken);
            if (bytes == 0)
            {
                if (_socket.Poll(0, SelectMode.SelectRead) && _socket.Available == 0)
                    throw new SocketException(10057);
                return 0;
            }
            _protocol.OfferInput(readBuffer, 0, bytes);
            ArrayPool<byte>.Shared.Return(readBuffer);
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
        ArrayPool<byte>.Shared.Return(sendBuffer);
    }

    public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        return AsApm(WriteAsync(buffer.AsMemory(offset, count)).AsTask(), callback, state);
    }

    public override void EndWrite(IAsyncResult asyncResult)
    {
        ((Task)asyncResult).Wait();
    }

    public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
    {
        return AsApm(ReadAsync(buffer.AsMemory(offset, count)).AsTask(), callback, state);
    }

    public override int EndRead(IAsyncResult asyncResult)
    {
        return ((Task<int>)asyncResult).Result;
    }

    //https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/interop-with-other-asynchronous-patterns-and-types#from-tap-to-apm
    private static IAsyncResult AsApm(Task task, AsyncCallback callback, object state)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        var tcs = new TaskCompletionSource(state);
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                tcs.TrySetException(t.Exception.InnerExceptions);
            else if (t.IsCanceled)
                tcs.TrySetCanceled();
            else
                tcs.TrySetResult();

            if (callback != null)
                callback(tcs.Task);
        }, TaskScheduler.Default);
        return tcs.Task;
    }

    //https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/interop-with-other-asynchronous-patterns-and-types#from-tap-to-apm
    private static IAsyncResult AsApm<T>(Task<T> task, AsyncCallback callback, object state)
    {
        if (task == null)
            throw new ArgumentNullException(nameof(task));

        var tcs = new TaskCompletionSource<T>(state);
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                tcs.TrySetException(t.Exception.InnerExceptions);
            else if (t.IsCanceled)
                tcs.TrySetCanceled();
            else
                tcs.TrySetResult(t.Result);

            if (callback != null)
                callback(tcs.Task);
        }, TaskScheduler.Default);
        return tcs.Task;
    }
}