using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ism7mqtt.ISM7;
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
        private short _lastKeepAlive = 0;
        private Stream _sslStream;

        public int Interval { get; set; }

        public int StartupTimeout { get; set; } = 90;

        private TimeSpan StartupTimeoutSpan => TimeSpan.FromSeconds(StartupTimeout);

        public bool EnableDebug { get; set; }

        public bool SerializePushSubscribes { get; set; } = false;

        public Func<Ism7Config, CancellationToken, Task> OnInitializationFinishedAsync { get; set; }

        /// <summary>Request the fault lists of all configured devices and publish them.</summary>
        public bool EnableFaults { get; set; }

        /// <summary>
        /// Diagnostic mode: when set (comma separated read bus addresses), only the systemconfig and the fault list
        /// of these addresses are requested and every answer is logged. No values are read and nothing is written.
        /// </summary>
        public string FaultProbeAddresses { get; set; }

        private bool IsFaultProbe => !String.IsNullOrWhiteSpace(FaultProbeAddresses);

        /// <summary>Wolf requests the fault list with this fixed bundle id (and sequence).</summary>
        public const string FaultBundleId = "3";

        public Ism7Client(Func<Ism7Config, CancellationToken, Task> messageHandler, string parameterPath, string host, Ism7Localizer localizer, string parameterXmlOverridePath = null)
        {
            _messageHandler = messageHandler;
            _host = host;
            _config = new Ism7Config(parameterPath, localizer, parameterXmlOverridePath);
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
            if (IsFaultProbe) return;
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
                            if (done.IsFaulted)
                                await done;
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
                        if (EnableDebug || IsFaultProbe)
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
                            IResponse response = null;
                            try
                            {
                                response = Deserialize(type, new ReadOnlySequenceStream(xmlBuffer));
                            }
                            catch (Exception ex) when (IsFaultProbe)
                            {
                                // the probe logs whatever arrives and keeps going
                                Console.WriteLine($"fault probe: cannot parse message of type '{type}': {ex.Message}");
                            }
                            if (response is not null)
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

        private async Task SubscribeAsync(string busAddress, string bundleId, SemaphoreSlim semaphore, CancellationToken cancellationToken)
        {
            var infoReads = _config.GetBundle(bundleId);
            bundleId = NextBundleId();
            _dispatcher.Subscribe(x => x.MessageType == PayloadType.TgrBundleResp && ((TelegramBundleResp) x).BundleId == bundleId, OnPushResponseAsync);
            _dispatcher.SubscribeOnce(x => x.MessageType == PayloadType.TgrBundleResp && ((TelegramBundleResp) x).BundleId == bundleId, (_, _) =>
            {
                semaphore.Release();
                return Task.CompletedTask;
            });
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
            var pendingSubscriptions = new List<(string busAddress, string bundleId)>();

            // Phase 1: pull all bundles, one at a time as FW5.1+ has problems with too many pull requests in parallel
            foreach (var busAddress in _config.AddAllDevices(_host))
            {
                var bundles = _config.GetBundlesForDevice(busAddress);
                foreach (var (bundleId, infoReads) in bundles)
                {
                    _dispatcher.SubscribeOnce(
                        x => x.MessageType == PayloadType.TgrBundleResp && ((TelegramBundleResp)x).BundleId == bundleId,
                        async (r, c) =>
                        {
                            try
                            {
                                await OnInitialValuesAsync(r, pendingSubscriptions, c);
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }
                    );

                    foreach (var infoRead in infoReads)
                    {
                        infoRead.BusAddress = busAddress;
                        infoRead.Seq = NextSequenceId();
                    }

                    await WaitForBundleResponseAsync(semaphore, StartupTimeoutSpan, cancellationToken);
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

            // Wait for the last pull response to be fully processed before subscribing.
            await WaitForBundleResponseAsync(semaphore, StartupTimeoutSpan, cancellationToken);

            // Phase 2: send push subscribes after all pulls are complete. Some FW5.20 devices stop
            // responding if these arrive in parallel; SerializePushSubscribes opts into sending them
            // one at a time, waiting for each response, like the pull phase above. Defaults to false
            // (burst, the long-standing behavior) since most devices don't need this.
            // Older ISM7 firmware (e.g. 2.10) rejects all writes once too many datapoints are subscribed,
            // so log the count to make this visible.
            Console.WriteLine($"Subscribing to {pendingSubscriptions.Sum(x => _config.GetBundle(x.bundleId).Count)} datapoints");
            foreach (var (busAddress, bundleId) in pendingSubscriptions)
            {
                if (SerializePushSubscribes)
                {
                    await SubscribeAsync(busAddress, bundleId, semaphore, cancellationToken);
                    await WaitForBundleResponseAsync(semaphore, StartupTimeoutSpan, cancellationToken);
                }
                else
                {
                    await SubscribeAsync(busAddress, bundleId, cancellationToken);
                }
            }

            // Wolf requests the fault lists before the value subscriptions, right after the systemconfig. We do it
            // once all devices are set up and subscribed, so the proven startup sequence stays unchanged and the
            // startup does not wait for the answers. Without SerializePushSubscribes the push answers may still be
            // outstanding, so the requests then go out together with the subscriptions.
            if (EnableFaults)
            {
                await SubscribeFaultListAsync(cancellationToken);
            }

            if (OnInitializationFinishedAsync is not null)
            {
                await OnInitializationFinishedAsync(_config, cancellationToken);
            }
        }

        private volatile bool _faultSystemconfigReceived;
        private volatile bool _faultListReceived;

        /// <summary>
        /// Like Wolf, reads the systemconfig first and then requests the fault lists of the configured devices
        /// that are on the bus. Nothing waits for the answers, a missing answer is only logged.
        /// </summary>
        private async Task SubscribeFaultListAsync(CancellationToken cancellationToken)
        {
            if (!_config.FaultBusAddresses.Any())
            {
                Console.WriteLine("fault list: no configured device has a fault list");
                return;
            }
            _dispatcher.SubscribeOnce(x => x.MessageType == PayloadType.SystemconfigResp, OnFaultSystemconfigAsync);
            _dispatcher.Subscribe(IsFaultListResponse, OnFaultListAsync);
            await SendAsync(new SystemconfigReq { Sid = "1" }, cancellationToken);
            _ = LogIfNoAnswerAsync(() => _faultSystemconfigReceived,
                $"fault list: no answer to the read-systemconfig-request within {StartupTimeout}s, the fault lists are not requested", cancellationToken);
        }

        private async Task OnFaultSystemconfigAsync(IResponse response, CancellationToken cancellationToken)
        {
            _faultSystemconfigReceived = true;
            var busDevices = ((SystemconfigResp)response).BusConfig?.Devices ?? new List<SystemconfigResp.BusDevice>();
            var addresses = _config.SelectFaultBusAddresses(busDevices.Select(x => x.Ba));
            Console.WriteLine($"fault list: requesting li=\"{String.Join(",", addresses)}\" (configured {String.Join(",", _config.FaultBusAddresses)}, systemconfig {String.Join(",", busDevices.Select(x => x.Ba))})");
            if (addresses.Count == 0) return;
            await SendFaultListRequestAsync(String.Join(",", addresses), cancellationToken);
            _ = LogIfNoAnswerAsync(() => _faultListReceived,
                $"fault list: no answer within {StartupTimeout}s", cancellationToken);
        }

        private async Task LogIfNoAnswerAsync(Func<bool> answered, string message, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(StartupTimeoutSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!answered())
                Console.WriteLine(message);
        }

        private static bool IsFaultListResponse(IResponse response)
        {
            return response.MessageType == PayloadType.TgrBundleResp && ((TelegramBundleResp)response).BundleId == FaultBundleId;
        }

        /// <summary>
        /// Requests the fault lists like Wolf does: a push bundle with the fixed id 3 and only an error state read.
        /// The module answers with the lists and again whenever they change.
        /// </summary>
        private ValueTask SendFaultListRequestAsync(string busAddresses, CancellationToken cancellationToken)
        {
            return SendAsync(new TelegramBundleReq
            {
                AbortOnError = false,
                BundleId = FaultBundleId,
                GatewayId = "1",
                TelegramBundleType = TelegramBundleType.push,
                EStRead = new TelegramBundleReq.ErrorStateRead
                {
                    Seq = FaultBundleId,
                    VisibleDeviceAdresses = busAddresses
                }
            }, cancellationToken);
        }

        private async Task OnFaultListAsync(IResponse response, CancellationToken cancellationToken)
        {
            var resp = (TelegramBundleResp)response;
            _faultListReceived = true;
            if (!String.IsNullOrEmpty(resp.Errormsg) || resp.State != TelegrResponseState.OK)
            {
                Console.WriteLine($"fault list: unexpected answer, state '{resp.State}' {resp.Errormsg}");
                return;
            }
            bool changed;
            try
            {
                changed = _config.ProcessFaults(resp.ErrorStates ?? new List<ErrorStateResp>());
            }
            catch (Exception ex)
            {
                // a fault list we do not understand must never take down the connection
                Console.WriteLine($"fault list: cannot process answer: {ex.Message}");
                return;
            }
            if (changed)
            {
                foreach (var device in _config.Devices.Where(x => x.ReportsFaults && x.Faults is not null))
                {
                    Console.WriteLine($"fault list: {device.MqttTopic}: {device.ActiveFault} ({device.Faults.Count} entries)");
                }
                await _messageHandler(_config, cancellationToken);
            }
        }

        private async Task RunFaultProbeAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine($"fault probe: read-only, sending only read-systemconfig-request and the fault list request for '{FaultProbeAddresses}'");
            var systemconfig = new TaskCompletionSource<SystemconfigResp>(TaskCreationOptions.RunContinuationsAsynchronously);
            _dispatcher.SubscribeOnce(x => x.MessageType == PayloadType.SystemconfigResp, (r, _) =>
            {
                systemconfig.TrySetResult((SystemconfigResp)r);
                return Task.CompletedTask;
            });
            _dispatcher.Subscribe(x => x.MessageType == PayloadType.TgrBundleResp, OnFaultProbeResponseAsync);

            // Wolf sends the same gateway id as sid and as gw of its bundles
            await SendAsync(new SystemconfigReq { Sid = "1" }, cancellationToken);
            var completed = await Task.WhenAny(systemconfig.Task, Task.Delay(StartupTimeoutSpan, cancellationToken));
            if (completed == systemconfig.Task)
            {
                var devices = systemconfig.Task.Result.BusConfig?.Devices ?? new List<SystemconfigResp.BusDevice>();
                Console.WriteLine($"fault probe: systemconfig with {devices.Count} bus devices: {String.Join(", ", devices.Select(x => $"ba={x.Ba} did={x.DeviceId} sv={x.SoftwareVersion} sr={x.SoftwareRevision}"))}");
                Console.WriteLine($"fault probe: --faults would request li=\"{String.Join(",", _config.SelectFaultBusAddresses(devices.Select(x => x.Ba)))}\"");
            }
            else
            {
                await completed;
                Console.WriteLine($"fault probe: no systemconfig answer within {StartupTimeout}s, sending the fault list request anyway");
            }
            await SendFaultListRequestAsync(FaultProbeAddresses, cancellationToken);
            Console.WriteLine("fault probe: fault list request sent, logging every answer until stopped");
        }

        private Task OnFaultProbeResponseAsync(IResponse response, CancellationToken cancellationToken)
        {
            var resp = (TelegramBundleResp)response;
            var entries = resp.ErrorStates ?? new List<ErrorStateResp>();
            Console.WriteLine($"fault probe: tbres bn={resp.BundleId} st={resp.State} emsg='{resp.Errormsg}' ers={entries.Count}");
            foreach (var entry in entries)
            {
                var dtid = _config.GetFaultDeviceTemplateId(entry.BusAddress);
                var text = dtid.HasValue ? FaultMessageTexts.Default.Get(entry.Code, dtid.Value) : null;
                Console.WriteLine($"fault probe: ers se={entry.Seq} ba={entry.BusAddress} index={entry.Index} code={entry.Code} active={entry.Active} start='{entry.Start}' stop='{entry.Stop}' dtid={dtid?.ToString() ?? "-"} text='{text ?? "-"}'");
            }
            return Task.CompletedTask;
        }

        // We observed pull bundel request were not answered by the ISM7 which caused the startup to hang.
        // With a timeout for each pull request we make sure the startup is interrupted and the user can see the problem in the logs.
        private static async Task WaitForBundleResponseAsync(SemaphoreSlim semaphore, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await semaphore.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Shutdown ism7mqtt due to timeout during startup.");
            }
        }

        private async Task OnInitialValuesAsync(IResponse response, List<(string busAddress, string bundleId)> pendingSubscriptions, CancellationToken cancellationToken)
        {
            var resp = (TelegramBundleResp) response;
            if (!String.IsNullOrEmpty(resp.Errormsg))
                throw new InvalidDataException(resp.Errormsg);
            if (resp.State != TelegrResponseState.OK)
                throw new InvalidDataException($"unexpected state '{resp.State}'");
            if (resp.Telegrams.Any())
            {
                var hasDatapoints = _config.ProcessData(resp.Telegrams.Where(x => x.State == TelegrResponseState.OK));
                if (hasDatapoints)
                {
                    await _messageHandler(_config, cancellationToken);
                    var busAddress = resp.Telegrams.Select(x => x.BusAddress).First();
                    pendingSubscriptions.Add((busAddress, resp.BundleId));
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
            if (IsFaultProbe)
                return RunFaultProbeAsync(cancellationToken);
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
            // debug output and the probe log what is sent - never the login, it carries the password
            if (EnableDebug || IsFaultProbe)
                Console.WriteLine(payload is LoginReq ? "> (login request, password not logged)" : $"> {Encoding.UTF8.GetString(data)}");
            var buffer = new byte[data.Length + 6];
            BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
            BinaryPrimitives.WriteInt16BigEndian(buffer.AsSpan(4), (short) payload.Type);
            Buffer.BlockCopy(data, 0, buffer, 6, data.Length);
            return _sslStream.WriteAsync(buffer, cancellationToken);
        }

        private static string NextBundleId()
        {
            return IdGenerator.GetNextBundleIdString();
        }

        private static string NextSequenceId()
        {
            var id = IdGenerator.GetNextSequenceId();
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
