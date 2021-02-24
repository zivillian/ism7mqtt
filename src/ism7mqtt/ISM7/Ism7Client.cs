using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt
{
    public class Ism7Client
    {
        private readonly ConcurrentDictionary<Type, XmlSerializer> _serializers = new ConcurrentDictionary<Type, XmlSerializer>();
        private readonly ConcurrentDictionary<string, SystemconfigResp.BusDevice> _devices = new ConcurrentDictionary<string, SystemconfigResp.BusDevice>();

        public async Task RunAsync(IPAddress ipAddress, string password, CancellationToken cancellationToken)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(ipAddress, 9092, cancellationToken);
            var certificate = new X509Certificate2(Resources.client);
            using (var ssl = new SslStream(tcp.GetStream(), false, (a, b, c, d) => true))
            {
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "ism7.server",
                    ClientCertificates = new X509Certificate2Collection(certificate)
                }, cancellationToken);
                var session = await AuthenticateAsync(ssl, password, cancellationToken);
                await GetConfigAsync(ssl, session, cancellationToken);
            }
        }

        private async Task GetConfigAsync(SslStream connection, LoginResp session, CancellationToken cancellationToken)
        {
            await SendAsync(connection, new SystemconfigReq {Sid = session.Sid}, cancellationToken);
            var result = await ReadAsync(connection, cancellationToken);
            if (result.MessageType != PayloadType.SystemconfigResp)
                throw new InvalidDataException("invalid response");
            var resp = (SystemconfigResp)result;
            foreach (var device in resp.BusConfig.Devices)
            {
                _devices.AddOrUpdate(device.Ba, device, (k, o) => device);
            }
        }

        private async Task<LoginResp> AuthenticateAsync(Stream connection, string password, CancellationToken cancellationToken)
        {
            await SendAsync(connection, new LoginReq {Password = password}, cancellationToken);
            var result = await ReadAsync(connection, cancellationToken);
            if (result.MessageType != PayloadType.DirectLogonResp)
                throw new InvalidDataException("invalid response");
            var resp = (LoginResp)result;
            if (resp.State != LoginState.ok)
                throw new InvalidDataException("invalid login state");
            return resp;
        }

        private ValueTask SendAsync<T>(Stream connection, T payload, CancellationToken cancellationToken) where T:IPayload
        {
            var data = Serialize(payload);
            var length = Encoding.UTF8.GetByteCount(data);
            var buffer = new byte[length + 6];
            BinaryPrimitives.WriteInt32BigEndian(buffer, length);
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(4), (short) payload.Type);
            Encoding.UTF8.GetBytes(data, buffer.AsSpan(6));
            return connection.WriteAsync(buffer, cancellationToken);
        }

        private async Task<IResponse> ReadAsync(Stream connection, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            await ReadExactAsync(connection, buffer, 6, cancellationToken);
            var length = BinaryPrimitives.ReadInt32BigEndian(buffer);
            var type = (PayloadType) BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(4));
            await ReadExactAsync(connection, buffer, length, cancellationToken);
            using var stream = new MemoryStream(buffer, 0, length);
            return Deserialize(type, stream);
        }

        private IResponse Deserialize(PayloadType type, Stream data)
        {
            switch (type)
            {
                case PayloadType.DirectLogonResp:
                    return (IResponse) GetSerializer<LoginResp>().Deserialize(data);
                case PayloadType.SystemconfigResp:
                    return (IResponse) GetSerializer<SystemconfigResp>().Deserialize(data);
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        private async Task ReadExactAsync(Stream connection, Memory<byte> buffer, int count, CancellationToken cancellationToken)
        {
            var read = 0;
            buffer = buffer.Slice(0, count);
            while (count > read)
            {
                var current = await connection.ReadAsync(buffer, cancellationToken);
                if (current < 0)
                    throw new EndOfStreamException();
                buffer = buffer.Slice(current);
                read += current;
            }
        }

        private string Serialize<T>(T request)
        {
            using var sw = new StringWriter();
            var serializer = GetSerializer<T>();
            serializer.Serialize(sw, request);
            sw.Flush();
            return sw.ToString();
        }

        private XmlSerializer GetSerializer<T>()
        {
            return _serializers.GetOrAdd(typeof(T), x => new XmlSerializer(x));
        }

    }
}