using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using ism7mqtt;
using ism7mqtt.ISM7.Protocol;
using ism7ssl;

namespace ism7mqtt.Tests;

/// <summary>
/// Minimal ISM7 device stand-in: speaks the same TLS handshake (reusing the embedded client/server
/// certificate, exactly like <c>ism7proxy</c>'s accept side) and the same 4-byte-length + 2-byte-type +
/// XML wire framing as the real device, so a real <see cref="Ism7Client"/> can be pointed at it.
/// </summary>
public sealed class FakeIsm7Server : IAsyncDisposable
{
    public delegate List<InfonumberReadResp> PullBundleResponder(TelegramBundleReq request);

    private readonly TcpListener _listener;
    private readonly LoginState _loginState;
    private readonly PullBundleResponder _pullResponder;
    private readonly int? _dropPullBundleOrdinal;
    private readonly Task _acceptTask;
    private TcpClient? _client;
    private SslStream? _sslStream;

    public FakeIsm7Server(PullBundleResponder pullResponder, LoginState loginState = LoginState.ok, int? dropPullBundleOrdinal = null)
    {
        _pullResponder = pullResponder;
        _loginState = loginState;
        _dropPullBundleOrdinal = dropPullBundleOrdinal;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = AcceptLoopAsync();
    }

    public int Port { get; }

    private async Task AcceptLoopAsync()
    {
        try
        {
            _client = await _listener.AcceptTcpClientAsync();
            _sslStream = new SslStream(_client.GetStream(), false, (_, _, _, _) => true);
            await _sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = Ism7SslStream.Certificate,
                ClientCertificateRequired = true,
            });
            await HandleConnectionAsync(_sslStream);
        }
        catch
        {
            // connection torn down by the test (disposal/cancellation) - nothing to assert on here
        }
    }

    private async Task HandleConnectionAsync(Stream stream)
    {
        var pullOrdinal = 0;
        var header = new byte[6];
        while (true)
        {
            if (!await ReadExactAsync(stream, header, 6)) return;
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            var type = (PayloadType)BinaryPrimitives.ReadInt16BigEndian(header.AsSpan(4));
            var body = length > 0 ? new byte[length] : Array.Empty<byte>();
            if (length > 0 && !await ReadExactAsync(stream, body, length)) return;

            switch (type)
            {
                case PayloadType.DirectLogonReq:
                    await SendAsync(stream, PayloadType.DirectLogonResp, BuildLoginResp());
                    break;
                case PayloadType.TgrBundleReq:
                    var request = DeserializeXml<TelegramBundleReq>(body);
                    if (request.TelegramBundleType == TelegramBundleType.pull)
                    {
                        pullOrdinal++;
                        if (pullOrdinal != _dropPullBundleOrdinal)
                        {
                            await SendAsync(stream, PayloadType.TgrBundleResp, BuildBundleResp(request));
                        }
                    }
                    // push/write bundles and keep-alives are not needed by the current test cases.
                    break;
            }
        }
    }

    private LoginResp BuildLoginResp() => new LoginResp
    {
        State = _loginState,
        Sid = "test-session",
        InstallationName = "FakeIsm7Server",
        Serialnumber = "0",
        DateTime = DateTime.UtcNow.ToString("O"),
        IsmSoftwareVersion = "1.0",
        IsmHardwareVersion = "1.0",
        IsWLanConnected = false,
        Type = "ISM7i",
    };

    private TelegramBundleResp BuildBundleResp(TelegramBundleReq request) => new TelegramBundleResp
    {
        Timestamp = DateTime.UtcNow.ToString("O"),
        GatewayId = request.GatewayId,
        BundleId = request.BundleId,
        State = TelegrResponseState.OK,
        Telegrams = _pullResponder(request),
    };

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int count)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, count - offset));
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    private static async Task SendAsync<T>(Stream stream, PayloadType type, T payload)
    {
        var body = SerializeXml(payload);
        var frame = new byte[body.Length + 6];
        BinaryPrimitives.WriteInt32BigEndian(frame, body.Length);
        BinaryPrimitives.WriteInt16BigEndian(frame.AsSpan(4), (short)type);
        Buffer.BlockCopy(body, 0, frame, 6, body.Length);
        await stream.WriteAsync(frame);
        await stream.FlushAsync();
    }

    private static byte[] SerializeXml<T>(T obj)
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("", "");
        using var sw = new Utf8StringWriter();
        using var xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings { Indent = false });
        new XmlSerializer(typeof(T)).Serialize(xmlWriter, obj, namespaces);
        xmlWriter.Flush();
        return Encoding.UTF8.GetBytes(sw.ToString());
    }

    private static T DeserializeXml<T>(byte[] body)
    {
        using var ms = new MemoryStream(body);
        return (T)new XmlSerializer(typeof(T)).Deserialize(ms)!;
    }

    public async ValueTask DisposeAsync()
    {
        try { _listener.Stop(); } catch { /* already stopped */ }
        _sslStream?.Dispose();
        _client?.Dispose();
        try { await _acceptTask; } catch { /* observed above via the accept loop's own catch */ }
    }
}
