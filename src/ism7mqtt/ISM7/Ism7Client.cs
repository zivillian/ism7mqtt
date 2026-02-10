using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ism7mqtt.ISM7.Protocol;
using ism7ssl;

namespace ism7mqtt
{
    public class Ism7Client
    {
        private readonly Func<Ism7Config, CancellationToken, Task> _messageHandler;
        private readonly string _host;
        private readonly ConcurrentDictionary<Type, XmlSerializer> _serializers = new ConcurrentDictionary<Type, XmlSerializer>();
        private readonly Ism7Config _config;
        private readonly Pipe _pipe;
        private readonly ResponseDispatcher _dispatcher = new ResponseDispatcher();
        private int _nextBundleId = 0;
        private short _lastKeepAlive = 0;
        private Stream _sslStream;
        private int _nextSequenceId = 1;

        public int Interval { get; set; }

        public bool EnableDebug { get; set; }

        public Func<Ism7Config, CancellationToken, Task> OnInitializationFinishedAsync { get; set; }

        public Ism7Client(Func<Ism7Config, CancellationToken, Task> messageHandler, string parameterPath, string host, Ism7Localizer localizer)
        {
            _messageHandler = messageHandler;
            _host = host;
            _config = new Ism7Config(parameterPath, localizer);
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

        private async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(_host, _config.TcpPort, cancellationToken);
            var ssl = new Ism7SslStream(tcp.Client);
            await ssl.AuthenticateAsClientAsync(cancellationToken);
            return ssl;
        }

        public Task OnCommandAsync(string mqttTopic, JsonObject data, CancellationToken cancellationToken)
        {
            try
            {
                var writeRequests = _config.GetWriteRequest(mqttTopic, data).ToList();
                if (writeRequests.Count == 0)
                {
                    Console.WriteLine($"nothing to send for topic '{mqttTopic}' with payload '{data.ToJsonString()}'");
                    return Task.CompletedTask;
                }
                return OnCommandAsync(writeRequests, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error for topic '{mqttTopic}' with payload '{data.ToJsonString()}'\n{ex.Message}");
                return Task.CompletedTask;
            }
        }

        public Task OnCommandAsync(string mqttTopic, ReadOnlyMemory<string> propertyParts, string value, CancellationToken cancellationToken)
        {
            try
            {
                var writeRequests = _config.GetWriteRequest(mqttTopic, propertyParts, value).ToList();
                if (writeRequests.Count == 0)
                {
                    Console.WriteLine($"nothing to send for topic '{mqttTopic}' with payload '{value}'");
                    return Task.CompletedTask;
                }
                return OnCommandAsync(writeRequests, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"error for topic '{mqttTopic}' with payload '{value}'\n{ex.Message}");
                return Task.CompletedTask;
            }
        }

        private async Task OnCommandAsync(List<InfoWrite> writeRequests, CancellationToken cancellationToken)
        {
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
            
            
            var hasDatapoints = _config.ProcessData(resp.WriteTelegrams.Where(x => x.State == TelegrResponseState.OK));
            if (hasDatapoints)
            {
                await _messageHandler(_config, cancellationToken);
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
                var tasks = new List<Task>();
                while (!cancellationToken.IsCancellationRequested)
                {
                    var readValueTask = source.ReadAsync(cancellationToken);
                    ReadResult result;
                    if (readValueTask.IsCompleted)
                    {
                        result = await readValueTask;
                    }
                    else
                    {
                        var readTask = readValueTask.AsTask();
                        tasks.Add(readTask);
                        while (true)
                        {
                            var done = await Task.WhenAny(tasks);
                            tasks.Remove(done);
                            if (done == readTask) break;
                        }

                        result = await readTask;
                    }

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
                            tasks.Add(_dispatcher.DispatchAsync(response, cancellationToken));
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

        private async Task SubscribeAsync(string busAddress, string bundleId, CancellationToken cancellationToken)
        {
            var infoReads = _config.GetBundle(bundleId);
            bundleId = NextBundleId();
            _dispatcher.Subscribe(x => x.MessageType == PayloadType.TgrBundleResp && ((TelegramBundleResp) x).BundleId == bundleId, OnPushResponseAsync);
            foreach (var infoRead in infoReads)
            {
                infoRead.BusAddress = busAddress;
                infoRead.Intervall = Interval;
                infoRead.Seq = NextSequenceId();
            }
            await SendAsync(new TelegramBundleReq
            {
                AbortOnError = false,
                BundleId = bundleId,
                GatewayId = "1",
                TelegramBundleType = TelegramBundleType.push,
                InfoReadTelegrams = infoReads
            }, cancellationToken);
        }

        private async Task OnPushResponseAsync(IResponse response, CancellationToken cancellationToken)
        {
            var resp = (TelegramBundleResp) response;
            if (!String.IsNullOrEmpty(resp.Errormsg))
                throw new InvalidDataException(resp.Errormsg);
            if (resp.State != TelegrResponseState.OK)
            {
                await Console.Error.WriteLineAsync($"unexpected state '{resp.State}");
            }
            else
            {
                var hasDatapoints = _config.ProcessData(resp.Telegrams.Where(x => x.State == TelegrResponseState.OK));
                if (hasDatapoints)
                {
                    await _messageHandler(_config, cancellationToken);
                }
            }
        }

        private async Task LoadInitialValuesAsync(CancellationToken cancellationToken)
        {
            var semaphore = new SemaphoreSlim(1, 1);
            foreach (var busAddress in _config.AddAllDevices(_host))
            {
                var bundles = _config.GetBundlesForDevice(busAddress);
                foreach (var (bundleId, infoReads) in bundles)
                {
                    NextBundleId();
                    _dispatcher.SubscribeOnce(
                        x => x.MessageType == PayloadType.TgrBundleResp && ((TelegramBundleResp)x).BundleId == bundleId,
                        (r, c) =>
                        {
                            semaphore.Release();
                            return OnInitialValuesAsync(r, c);
                        });
                    foreach (var infoRead in infoReads)
                    {
                        infoRead.BusAddress = busAddress;
                        infoRead.Seq = NextSequenceId();
                    }

                    await semaphore.WaitAsync(cancellationToken);
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
            if (OnInitializationFinishedAsync is not null)
            {
                await OnInitializationFinishedAsync(_config, cancellationToken);
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
                var hasDatapoints = _config.ProcessData(resp.Telegrams.Where(x => x.State == TelegrResponseState.OK));
                if (hasDatapoints)
                {
                    await _messageHandler(_config, cancellationToken);
                    var busAddress = resp.Telegrams.Select(x => x.BusAddress).First();
                    await SubscribeAsync(busAddress, resp.BundleId, cancellationToken);
                }
            }
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
            return LoadInitialValuesAsync(cancellationToken);
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

        private string NextSequenceId()
        {
            var id = Interlocked.Increment(ref _nextSequenceId);
            return $"A;{id}";
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
