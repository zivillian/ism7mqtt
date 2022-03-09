using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;
using Newtonsoft.Json.Linq;

namespace ism7mqtt
{
    public class Ism7Client
    {
        private readonly Func<MqttMessage, CancellationToken, Task> _messageHandler;
        private readonly IPAddress _ipAddress;
        private readonly ConcurrentDictionary<Type, XmlSerializer> _serializers = new ConcurrentDictionary<Type, XmlSerializer>();
        private readonly ConcurrentDictionary<string, SystemconfigResp.BusDevice> _devices = new ConcurrentDictionary<string, SystemconfigResp.BusDevice>();
        private readonly Ism7Config _config;
        private readonly Pipe _pipe;
        private readonly ResponseDispatcher _dispatcher = new ResponseDispatcher();
        private int _nextBundleId = 0;
        private short _lastKeepAlive = 0;
        private Stream _sslStream;

        public bool EnableDebug { get; set; }

        public Ism7Client(Func<MqttMessage, CancellationToken, Task> messageHandler, string parameterPath, IPAddress ipAddress)
        {
            _messageHandler = messageHandler;
            _ipAddress = ipAddress;
            _config = new Ism7Config(parameterPath);
            _pipe = new Pipe();
        }

        public async Task RunAsync(string password, CancellationToken cancellationToken)
        {
            using (_sslStream = await ConnectAsync(cancellationToken))
            {
                var fillPipeTask = FillPipeAsync(_pipe.Writer, cancellationToken);
                var readPipeTask = ReadPipeAsync(_pipe.Reader, cancellationToken);
                await AuthenticateAsync(password, cancellationToken);
                var keepAlive = KeepAliveAsync(cancellationToken);
                await Task.WhenAny(fillPipeTask, readPipeTask, keepAlive);
            }
        }

        private async Task<SslStream> ConnectAsync(CancellationToken cancellationToken)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(_ipAddress, 9092, cancellationToken);
            var certificate = new X509Certificate2(Resources.client);
            var ssl = new SslStream(tcp.GetStream(), false, (a, b, c, d) => true);

            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "ism7.server",
                ClientCertificates = new X509Certificate2Collection(certificate),
            };
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    sslOptions.CipherSuitesPolicy = new CipherSuitesPolicy(new[]
                    {
                        TlsCipherSuite.TLS_RSA_WITH_AES_256_CBC_SHA256
                    });
                }
                catch (PlatformNotSupportedException)
                {
                    //older linux or mac https://github.com/dotnet/runtime/issues/33649
                }
            }
            await ssl.AuthenticateAsClientAsync(sslOptions, cancellationToken);
            return ssl;
        }

        public async Task OnCommandAsync(string mqttTopic, JObject data, CancellationToken cancellationToken)
        {
            var writeRequests = _config.GetWriteRequest(mqttTopic, data).ToList();
            if (writeRequests.Count == 0) return;
            var request = new TelegramBundleReq
            {
                AbortOnError = true,
                BundleId = NextBundleId(),
                GatewayId = "1",
                TelegramBundleType = TelegramBundleType.write,
                InfoWriteTelegrams = writeRequests
            };
            _dispatcher.SubscribeOnce(x=>x.MessageType == PayloadType.TgrBundleResp && ((TelegramBundleResp)x).BundleId == request.BundleId, OnWriteResponse);
            await SendAsync(request, cancellationToken);
        }

        private async Task OnWriteResponse(IResponse response, CancellationToken cancellationToken)
        {
            var resp = (TelegramBundleResp) response;
            if (!String.IsNullOrEmpty(resp.Errormsg))
                throw new InvalidDataException(resp.Errormsg);
            if (resp.State != TelegrResponseState.OK)
                throw new InvalidDataException($"unexpected state '{resp.State}");
            
            
            var datapoints = _config.ProcessData(resp.WriteTelegrams.Where(x => x.State == TelegrResponseState.OK));
            foreach (var datapoint in datapoints)
            {
                await _messageHandler(datapoint, cancellationToken);
            }
        }

        private async Task FillPipeAsync(PipeWriter target, CancellationToken cancellationToken)
        {
            const int bufferSize = 512;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var buffer = target.GetMemory(bufferSize);
                    var read = await _sslStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    target.Advance(read);
                    var result = await target.FlushAsync(cancellationToken);
                    if (result.IsCanceled || result.IsCompleted)
                        break;
                }
                await target.CompleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                await target.CompleteAsync(ex);
            }
        }

        private async Task ReadPipeAsync(PipeReader source, CancellationToken cancellationToken)
        {
            try
            {
                var header = new byte[6];
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = await source.ReadAsync(cancellationToken);
                    var buffer = result.Buffer;
                    while (buffer.Length >= 6)
                    {
                        var size = buffer.Slice(0, 6);
                        size.CopyTo(header);
                        var length = BinaryPrimitives.ReadInt32BigEndian(header);
                        if (buffer.Length < length + 6) break;
                        var type = (PayloadType)BinaryPrimitives.ReadInt16BigEndian(header.AsSpan(4));
                        var xmlBuffer = buffer.Slice(6, length);
                        if (EnableDebug)
                        {
                            var xml = Encoding.UTF8.GetString(xmlBuffer);
                            Console.WriteLine($"< {xml}");
                        }
                        if (xmlBuffer.Length == 0)
                        {
                            Console.WriteLine($"Received empty xml of type '{type}' - skipping dispatch (raw '{HexEncode(header)}')");
                        }
                        else
                        {
                            var response = Deserialize(type, new ReadOnlySequenceStream(xmlBuffer));
                            await _dispatcher.DispatchAsync(response, cancellationToken);
                        }
                        buffer = buffer.Slice(xmlBuffer.End);
                        
                    }
                    source.AdvanceTo(buffer.Start, buffer.End);
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                await source.CompleteAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                await source.CompleteAsync(ex);
            }
        }

        private static string HexEncode(ReadOnlySpan<byte> data)
        {
            StringBuilder s = new StringBuilder(data.Length*2);
            foreach (byte b in data)
                s.Append(b.ToString("x2"));
            return s.ToString();
        }

        private async Task SubscribeAsync(string busAddress, CancellationToken cancellationToken)
        {
            var device = _devices[busAddress];
            var ids = _config.GetTelegramIdsForDevice(device.Ba);
            var bundleId = NextBundleId();
            _dispatcher.Subscribe(x => x.MessageType == PayloadType.TgrBundleResp && ((TelegramBundleResp) x).BundleId == bundleId, OnPushResponseAsync);
            await SendAsync(new TelegramBundleReq
            {
                AbortOnError = false,
                BundleId = bundleId,
                GatewayId = "1",
                TelegramBundleType = TelegramBundleType.push,
                InfoReadTelegrams = ids.Select(x => new InfoRead
                {
                    BusAddress = device.Ba,
                    InfoNumber = x,
                    Intervall = 60
                }).ToList()
            }, cancellationToken);
        }

        private async Task OnPushResponseAsync(IResponse response, CancellationToken cancellationToken)
        {
            var resp = (TelegramBundleResp) response;
            if (!String.IsNullOrEmpty(resp.Errormsg))
                throw new InvalidDataException(resp.Errormsg);
            if (resp.State != TelegrResponseState.OK)
                throw new InvalidDataException($"unexpected state '{resp.State}");
            
            var datapoints = _config.ProcessData(resp.Telegrams.Where(x => x.State == TelegrResponseState.OK));
            foreach (var datapoint in datapoints)
            {
                await _messageHandler(datapoint, cancellationToken);
            }
        }

        private async Task LoadInitialValuesAsync(CancellationToken cancellationToken)
        {
            foreach (var device in _devices.Values)
            {
                if (!_config.AddDevice(_ipAddress.ToString(), device.Ba)) continue;
                var ids = _config.GetTelegramIdsForDevice(device.Ba);
                var bundleId = NextBundleId();
                _dispatcher.SubscribeOnce(
                    x => x.MessageType == PayloadType.TgrBundleResp && ((TelegramBundleResp) x).BundleId == bundleId,
                    OnInitialValuesAsync);
                var infoReads = ids.Select(x=>new InfoRead
                {
                    BusAddress = device.Ba,
                    InfoNumber = x,
                }).ToList();
                if (infoReads.Count == 0)
                {
                    //device without any valid parameter
                    continue;
                }
                await SendAsync(new TelegramBundleReq
                {
                    AbortOnError = false,
                    BundleId = bundleId,
                    GatewayId = "1",
                    TelegramBundleType = TelegramBundleType.pull,
                    InfoReadTelegrams = infoReads
                }, cancellationToken);
            }
        }

        private async Task OnInitialValuesAsync(IResponse response, CancellationToken cancellationToken)
        {
            var resp = (TelegramBundleResp) response;
            if (!String.IsNullOrEmpty(resp.Errormsg))
                throw new InvalidDataException(resp.Errormsg);
            if (resp.State != TelegrResponseState.OK)
                throw new InvalidDataException($"unexpected state '{resp.State}");
            if (resp.Telegrams.Any())
            {
                var datapoints = _config.ProcessData(resp.Telegrams.Where(x => x.State == TelegrResponseState.OK));
                foreach (var datapoint in datapoints)
                {
                    await _messageHandler(datapoint, cancellationToken);
                }
                var busAddress = resp.Telegrams.Select(x => x.BusAddress).First();
                await SubscribeAsync(busAddress, cancellationToken);
            }
        }

        private async Task GetConfigAsync(LoginResp session, CancellationToken cancellationToken)
        {
            _dispatcher.SubscribeOnce(x => x.MessageType == PayloadType.SystemconfigResp,
                OnSystemConfigAsync);
            await SendAsync(new SystemconfigReq {Sid = session.Sid}, cancellationToken);
        }

        private Task OnSystemConfigAsync(IResponse response, CancellationToken cancellationToken)
        {
            var resp = (SystemconfigResp)response;
            foreach (var device in resp.BusConfig.Devices)
            {
                _devices.AddOrUpdate(device.Ba, device, (k, o) => device);
            }
            return LoadInitialValuesAsync(cancellationToken);
        }

        private ValueTask AuthenticateAsync(string password, CancellationToken cancellationToken)
        {
            _dispatcher.SubscribeOnce(x => x.MessageType == PayloadType.DirectLogonResp,
                OnAuthenticateAsync);
            return SendAsync(new LoginReq {Password = password}, cancellationToken);
        }

        private Task OnAuthenticateAsync(IResponse response, CancellationToken cancellationToken)
        {
            var resp = (LoginResp)response;
            if (resp.State != LoginState.ok)
                throw new InvalidDataException("invalid login state");
            return GetConfigAsync(resp, cancellationToken);
        }

        private async Task KeepAliveAsync(CancellationToken cancellationToken)
        {
            _dispatcher.Subscribe(x=>x.MessageType == PayloadType.KeepAlive, OnKeepAliveAsync);
            short currentKeepAlive = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                if (currentKeepAlive != _lastKeepAlive)
                    throw new TimeoutException("No keepalive response within the last 60 seconds");
                currentKeepAlive++;
                await SendAsync(new KeepAliveReq(currentKeepAlive), cancellationToken);
            }
        }

        private Task OnKeepAliveAsync(IResponse response, CancellationToken cancellationToken)
        {
            var resp = (KeepAliveResp) response;
            _lastKeepAlive = resp.Seq;
            return Task.CompletedTask;
        }

        private ValueTask SendAsync<T>(T payload, CancellationToken cancellationToken) where T:IPayload
        {
            var data = payload.Serialize();
            if (EnableDebug)
                Console.WriteLine($"> {Encoding.UTF8.GetString(data)}");
            var buffer = new byte[data.Length + 6];
            BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(4), (short) payload.Type);
            Buffer.BlockCopy(data, 0, buffer, 6, data.Length);
            return _sslStream.WriteAsync(buffer, cancellationToken);
        }

        private string NextBundleId()
        {
            var id = Interlocked.Increment(ref _nextBundleId);
            return id.ToString();
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
                case PayloadType.KeepAlive:
                    using (var reader = new BinaryReader(data))
                    {
                        var buffer = reader.ReadBytes(2);
                        return new KeepAliveResp(BinaryPrimitives.ReadInt16BigEndian(buffer));
                    }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "unsupported payload type");
            }
        }

        private XmlSerializer GetSerializer<T>()
        {
            return _serializers.GetOrAdd(typeof(T), x => new XmlSerializer(x));
        }
    }
}