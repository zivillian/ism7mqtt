using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;

namespace ism7mqtt
{
    public class Ism7Client
    {
        private readonly Func<MqttMessage, CancellationToken, Task> _messageHandler;
        private readonly ConcurrentDictionary<Type, XmlSerializer> _serializers = new ConcurrentDictionary<Type, XmlSerializer>();
        private readonly ConcurrentDictionary<string, SystemconfigResp.BusDevice> _devices = new ConcurrentDictionary<string, SystemconfigResp.BusDevice>();
        private readonly Ism7Config _config = new Ism7Config();

        public Ism7Client(Func<MqttMessage, CancellationToken, Task> messageHandler)
        {
            _messageHandler = messageHandler;
        }

        public async Task RunAsync(IPAddress ipAddress, string password, CancellationToken cancellationToken)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(ipAddress, 9092, cancellationToken);
            var certificate = new X509Certificate2(Resources.client);
            using (var ssl = new SslStream(tcp.GetStream(), false, (a, b, c, d) => true))
            {
                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = "ism7.server",
                    ClientCertificates = new X509Certificate2Collection(certificate),
                };
                if (!OperatingSystem.IsWindows())
                {
                    sslOptions.CipherSuitesPolicy = new CipherSuitesPolicy(new[]
                    {
                        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256
                    });
                }
                await ssl.AuthenticateAsClientAsync(sslOptions, cancellationToken);
                var session = await AuthenticateAsync(ssl, password, cancellationToken);
                await GetConfigAsync(ssl, session, cancellationToken);
                await LoadInitialValuesAsync(ipAddress.ToString(), ssl, cancellationToken);
                await SubscribeAsync(ssl, cancellationToken);
                await ReadEventsAsync(ssl, cancellationToken);
            }
        }

        private async Task ReadEventsAsync(Stream connection, CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await ReadAsync(connection, cancellationToken);
                if (result.MessageType != PayloadType.TgrBundleResp)
                    throw new InvalidDataException("invalid response");
                var resp = (TelegramBundleResp) result;
                if (!String.IsNullOrEmpty(resp.Errormsg))
                    throw new InvalidDataException(resp.Errormsg);
                if (resp.State != TelegrResponseState.OK)
                    throw new InvalidDataException($"unexpected stat '{resp.State}");
                var datapoints = _config.ProcessData(resp.InfonumberReadResponseTelegrams.Where(x => x.State == TelegrResponseState.OK));
                foreach (var datapoint in datapoints)
                {
                    await _messageHandler(datapoint, cancellationToken);
                }
            }
        }

        private async Task SubscribeAsync(Stream connection, CancellationToken cancellationToken)
        {
            foreach (var device in _devices.Values)
            {
                var ids = _config.GetTelegramIdsForDevice(device.Ba);
                await SendAsync(connection, new TelegramBundleReq
                {
                    AbortOnError = false,
                    BundleId = device.Ba,
                    GatewayId = "1",
                    TelegramBundleType = TelegramBundleType.push,
                    InfoReadTelegrams = ids.Select(x=>new InfoRead
                    {
                        BusAddress = device.Ba,
                        InfoNumber = x,
                        Intervall = 60
                        
                    }).ToList()
                }, cancellationToken);
                var result = await ReadAsync(connection, cancellationToken);
                if (result.MessageType != PayloadType.TgrBundleResp)
                    throw new InvalidDataException("invalid response");
                var resp = (TelegramBundleResp) result;
                if (!String.IsNullOrEmpty(resp.Errormsg))
                    throw new InvalidDataException(resp.Errormsg);
                if (resp.State != TelegrResponseState.OK)
                    throw new InvalidDataException($"unexpected stat '{resp.State}");
            }
        }

        private async Task LoadInitialValuesAsync(string ip, Stream connection, CancellationToken cancellationToken)
        {
            foreach (var device in _devices.Values)
            {
                _config.AddDevice(ip, device.Ba, device.DeviceId, device.SoftwareNumber);
                var ids = _config.GetTelegramIdsForDevice(device.Ba);
                await SendAsync(connection, new TelegramBundleReq
                {
                    AbortOnError = false,
                    BundleId = "1",
                    GatewayId = "1",
                    TelegramBundleType = TelegramBundleType.pull,
                    InfoReadTelegrams = ids.Select(x=>new InfoRead
                    {
                        BusAddress = device.Ba,
                        InfoNumber = x,
                    }).ToList()
                }, cancellationToken);
                var result = await ReadAsync(connection, cancellationToken);
                if (result.MessageType != PayloadType.TgrBundleResp)
                    throw new InvalidDataException("invalid response");
                var resp = (TelegramBundleResp) result;
                if (!String.IsNullOrEmpty(resp.Errormsg))
                    throw new InvalidDataException(resp.Errormsg);
                if (resp.State != TelegrResponseState.OK)
                    throw new InvalidDataException($"unexpected stat '{resp.State}");
                var datapoints = _config.ProcessData(resp.InfonumberReadResponseTelegrams.Where(x => x.State == TelegrResponseState.OK));
                foreach (var datapoint in datapoints)
                {
                    await _messageHandler(datapoint, cancellationToken);
                }
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
            Console.WriteLine($"> {data}");
            var length = Encoding.UTF8.GetByteCount(data);
            var buffer = new byte[length + 6];
            BinaryPrimitives.WriteInt32BigEndian(buffer, length);
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(4), (short) payload.Type);
            Encoding.UTF8.GetBytes(data, buffer.AsSpan(6));
            return connection.WriteAsync(buffer, cancellationToken);
        }

        private async Task<IResponse> ReadAsync(Stream connection, CancellationToken cancellationToken)
        {
            var buffer = new byte[6];
            await ReadExactAsync(connection, buffer, 6, cancellationToken);
            var length = BinaryPrimitives.ReadInt32BigEndian(buffer);
            var type = (PayloadType) BinaryPrimitives.ReadInt16BigEndian(buffer.AsSpan(4));
            buffer = new byte[length];
            await ReadExactAsync(connection, buffer, length, cancellationToken);
            Console.WriteLine($"< {Encoding.UTF8.GetString(buffer.AsSpan(0, length))}");
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
                case PayloadType.TgrBundleResp:
                    return (IResponse) GetSerializer<TelegramBundleResp>().Deserialize(data);
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
            var xmlWriter = XmlWriter.Create(sw, new XmlWriterSettings {Indent = false});
            var serializer = GetSerializer<T>();
            serializer.Serialize(xmlWriter, request);
            sw.Flush();
            return sw.ToString();
        }

        private XmlSerializer GetSerializer<T>()
        {
            return _serializers.GetOrAdd(typeof(T), x => new XmlSerializer(x));
        }

    }
}