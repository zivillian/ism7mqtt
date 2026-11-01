using System.Text.Json.Nodes;
using System.Xml.Serialization;
using ism7mqtt.HomeAssistant;
using ism7mqtt.ISM7;
using ism7mqtt.ISM7.Protocol;
using Xunit;
using Xunit.Abstractions;

namespace ism7mqtt.Tests;

public class FaultTests
{
    private const string FaultListRequest = "<tbreq bn=\"3\" gw=\"1\" ae=\"false\" ty=\"push\"><erd se=\"3\" li=\"{0}\" /></tbreq>";

    private readonly ITestOutputHelper _output;

    public FaultTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static ErrorStateResp Ers(string ba, int index, int code, bool active, string? start = "01.11.2026 13:05:00", string? stop = null) => new()
    {
        Seq = "3",
        BusAddress = ba,
        Index = index,
        Code = code,
        Active = active,
        Start = start,
        Stop = stop,
    };

    private static TelegramBundleResp FaultBundle(params ErrorStateResp[] entries) => new()
    {
        Timestamp = DateTime.UtcNow.ToString("O"),
        GatewayId = "1",
        BundleId = "3",
        State = TelegrResponseState.OK,
        ErrorStates = entries.ToList(),
    };

    private static List<InfonumberReadResp> RespondZero(TelegramBundleReq request) => request.InfoReadTelegrams.Select(ir => new InfonumberReadResp
    {
        BusAddress = ir.BusAddress,
        InfoNumber = (ushort)ir.InfoNumber,
        State = TelegrResponseState.OK,
        DBLow = "0x05",
        DBHigh = "0x00",
    }).ToList();

    private static async Task WaitUntilAsync(Func<bool> condition, int seconds = 10)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "condition not met in time");
            await Task.Delay(20);
        }
    }

    private static List<TelegramBundleReq> BundleRequests(FakeIsm7Server server) => server.ReceivedRequests
        .Where(x => x.Type == PayloadType.TgrBundleReq)
        .Select(x => (TelegramBundleReq)new XmlSerializer(typeof(TelegramBundleReq)).Deserialize(new StringReader(x.Xml))!)
        .ToList();

    /// <summary>Runs a client with --faults against the fake server and collects the latest published value per topic.</summary>
    private sealed class FaultsRun : IAsyncDisposable
    {
        private readonly Dictionary<string, string> _published = new();
        private readonly CancellationTokenSource _cts = new();
        private readonly string _parameterPath;

        public FaultsRun(FakeIsm7Server server, string fixture = "faults.json", bool enableFaults = true, int startupTimeout = 5, string language = "DEU")
        {
            _parameterPath = TestFixtures.WriteParameterFile(fixture, server.Port);
            Client = new Ism7Client((config, _) =>
            {
                lock (_published)
                {
                    foreach (var message in config.MqttMessages)
                    {
                        _published[message.Path] = message.Content;
                    }
                }
                return Task.CompletedTask;
            }, _parameterPath, "127.0.0.1", new Ism7Localizer(language))
            {
                StartupTimeout = startupTimeout,
                SerializePushSubscribes = true,
                EnableFaults = enableFaults,
                OnInitializationFinishedAsync = (_, _) =>
                {
                    InitFinished.TrySetResult(true);
                    return Task.CompletedTask;
                }
            };
            RunTask = Client.RunAsync("test-password", _cts.Token);
        }

        public Ism7Client Client { get; }

        public Task RunTask { get; }

        public TaskCompletionSource<bool> InitFinished { get; } = new();

        public string? Get(string topic)
        {
            lock (_published) return _published.TryGetValue(topic, out var value) ? value : null;
        }

        public IReadOnlyList<string> Topics
        {
            get { lock (_published) return _published.Keys.ToList(); }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            await Task.WhenAny(RunTask, Task.Delay(TimeSpan.FromSeconds(5)));
            _cts.Dispose();
            File.Delete(_parameterPath);
        }
    }

    [Fact]
    public async Task FaultProbe_SendsSystemconfigThenOnlyTheFaultListRequest()
    {
        var pulls = 0;
        await using var server = new FakeIsm7Server(request =>
        {
            pulls++;
            return RespondZero(request);
        }, faultListResponder: _ => new[]
        {
            FaultBundle(Ers("0x76", 1, 63, true), Ers("0x76", 2, 63, false, "31.10.2026 07:12:00", "31.10.2026 09:40:00")),
        });
        var parameterPath = TestFixtures.WriteParameterFile("faults.json", server.Port);
        var originalOut = Console.Out;
        var console = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(console));
        try
        {
            var handlerInvoked = false;
            var client = new Ism7Client((_, _) =>
            {
                handlerInvoked = true;
                return Task.CompletedTask;
            }, parameterPath, "127.0.0.1", new Ism7Localizer("DEU"))
            {
                StartupTimeout = 5,
                FaultProbeAddresses = "0x76",
            };
            using var cts = new CancellationTokenSource();
            var runTask = client.RunAsync("test-password", cts.Token);
            await WaitUntilAsync(() => console.ToString().Contains("index=2 code=63"));
            Assert.False(runTask.IsCompleted);
            cts.Cancel();
            await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));

            var requests = server.ReceivedRequests;
            Assert.Equal(new[] { PayloadType.DirectLogonReq, PayloadType.SystemconfigReq, PayloadType.TgrBundleReq }, requests.Select(x => x.Type));
            Assert.Contains("<read-systemconfig-request sid=\"1\" />", requests[1].Xml);
            Assert.EndsWith(String.Format(FaultListRequest, "0x76"), requests[2].Xml);
            Assert.DoesNotContain(requests, x => x.Xml.Contains("<ird") || x.Xml.Contains("<iwr"));
            Assert.Equal(0, pulls);
            Assert.False(handlerInvoked);

            var log = console.ToString();
            Assert.Contains("fault probe: systemconfig with 1 bus devices: ba=0x76 did=0x0B sv=0x01 sr=0x02", log);
            Assert.Contains("fault probe: --faults would request li=\"0x76\"", log);
            Assert.Contains("fault probe: tbres bn=3 st=OK emsg='' ers=2", log);
            Assert.Contains("fault probe: ers se=3 ba=0x76 index=1 code=63 active=True start='01.11.2026 13:05:00' stop='' dtid=60000 text='Funktionskontrolle Schwerkraftbremse'", log);
            Assert.Contains("< <?xml", log);
            Assert.DoesNotContain("test-password", log);
            _output.WriteLine(log);
        }
        finally
        {
            Console.SetOut(originalOut);
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task FaultProbe_WithoutSystemconfigAnswer_StillSendsTheFaultListRequest()
    {
        await using var server = new FakeIsm7Server(RespondZero, answerSystemconfig: false);
        var parameterPath = TestFixtures.WriteParameterFile("faults.json", server.Port);
        var originalOut = Console.Out;
        var console = new StringWriter();
        Console.SetOut(TextWriter.Synchronized(console));
        try
        {
            var client = new Ism7Client((_, _) => Task.CompletedTask, parameterPath, "127.0.0.1", new Ism7Localizer("DEU"))
            {
                StartupTimeout = 1,
                FaultProbeAddresses = "0x76,0x08",
            };
            using var cts = new CancellationTokenSource();
            var runTask = client.RunAsync("test-password", cts.Token);
            await WaitUntilAsync(() => server.ReceivedRequests.Any(x => x.Type == PayloadType.TgrBundleReq));
            cts.Cancel();
            await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));

            var requests = server.ReceivedRequests;
            Assert.Equal(new[] { PayloadType.DirectLogonReq, PayloadType.SystemconfigReq, PayloadType.TgrBundleReq }, requests.Select(x => x.Type));
            Assert.EndsWith(String.Format(FaultListRequest, "0x76,0x08"), requests[2].Xml);
            Assert.Contains("fault probe: no systemconfig answer within 1s", console.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            File.Delete(parameterPath);
        }
    }

    /// <summary>Captures the console output for the duration of a test.</summary>
    private sealed class ConsoleCapture : IDisposable
    {
        private readonly TextWriter _original = Console.Out;
        private readonly StringWriter _writer = new();

        public ConsoleCapture()
        {
            Console.SetOut(TextWriter.Synchronized(_writer));
        }

        public string Log => _writer.ToString();

        public void Dispose() => Console.SetOut(_original);
    }

    [Fact]
    public async Task Faults_RequestsTheFaultListOnceLikeWolf_AndPublishesPerDevice()
    {
        using var console = new ConsoleCapture();
        // 0x75 is configured, but not on the bus
        await using var server = new FakeIsm7Server(RespondZero, faultListResponder: _ => new[]
        {
            FaultBundle(Ers("0x76", 1, 63, true), Ers("0x08", 1, 4, false, "30.10.2026 08:00:00", "30.10.2026 08:05:00")),
        }, systemconfigBusAddresses: new[] { "0x35", "0x76", "0x8" });
        await using var run = new FaultsRun(server);
        await WaitUntilAsync(() => run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldung") is not null);
        Assert.True(run.InitFinished.Task.IsCompleted);

        // like Wolf: the systemconfig first, then the fault list request for the devices on the bus
        var requests = server.ReceivedRequests.ToList();
        var faultRequestIndex = requests.FindIndex(x => x.Xml.Contains("<erd"));
        Assert.Equal(PayloadType.SystemconfigReq, requests[faultRequestIndex - 1].Type);
        Assert.Single(requests, x => x.Type == PayloadType.SystemconfigReq);
        var bundles = BundleRequests(server);
        var faultRequest = Assert.Single(bundles, x => x.EStRead is not null);
        Assert.Equal("3", faultRequest.BundleId);
        Assert.Equal("3", faultRequest.EStRead.Seq);
        Assert.Equal("0x35,0x76,0x8", faultRequest.EStRead.VisibleDeviceAdresses);
        Assert.Equal(TelegramBundleType.push, faultRequest.TelegramBundleType);
        Assert.False(faultRequest.AbortOnError);
        Assert.Empty(faultRequest.InfoReadTelegrams ?? new List<InfoRead>());
        Assert.Empty(faultRequest.InfoWriteTelegrams ?? new List<InfoWrite>());
        Assert.EndsWith(String.Format(FaultListRequest, "0x35,0x76,0x8"), requests[faultRequestIndex].Xml);
        Assert.Contains("fault list: requesting li=\"0x35,0x76,0x8\" (configured 0x35,0x75,0x76,0x8, systemconfig 0x35,0x76,0x8)", console.Log);
        // our own pull and push bundles never use Wolf's fault list bundle id
        Assert.All(bundles.Where(x => x.EStRead is null), x => Assert.True(Int32.Parse(x.BundleId) >= 10, x.BundleId));

        Assert.Equal("63 Funktionskontrolle Schwerkraftbremse", run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldung"));
        var sm1 = JsonNode.Parse(run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldungen")!)!.AsArray();
        var entry = Assert.Single(sm1)!;
        Assert.Equal(63, entry["code"]!.GetValue<int>());
        Assert.Equal("Funktionskontrolle Schwerkraftbremse", entry["text"]!.GetValue<string>());
        Assert.True(entry["active"]!.GetValue<bool>());
        Assert.Equal("01.11.2026 13:05:00", entry["start"]!.GetValue<string>());
        Assert.Null(entry["stop"]);
        Assert.Equal(1, entry["index"]!.GetValue<int>());

        Assert.Equal("Keine", run.Get("Wolf/127.0.0.1/CGB_0x08/Stoermeldung"));
        var cgb = Assert.Single(JsonNode.Parse(run.Get("Wolf/127.0.0.1/CGB_0x08/Stoermeldungen")!)!.AsArray())!;
        Assert.Equal("Keine Flammenbildung", cgb["text"]!.GetValue<string>());
        Assert.False(cgb["active"]!.GetValue<bool>());
        Assert.Equal("30.10.2026 08:05:00", cgb["stop"]!.GetValue<string>());

        Assert.Equal("Keine", run.Get("Wolf/127.0.0.1/BM_0x35/Stoermeldung"));
        Assert.Equal("[]", run.Get("Wolf/127.0.0.1/BM_0x35/Stoermeldungen"));
        // not on the bus, so not requested and nothing published for it
        Assert.DoesNotContain(run.Topics, x => x.StartsWith("Wolf/127.0.0.1/BM_0x75/Stoermeldung"));
        Assert.DoesNotContain(run.Topics, x => x.StartsWith("Wolf/127.0.0.1/DHK_BM_0x35/Stoermeldung"));
        Assert.DoesNotContain(run.Topics, x => x.StartsWith("Wolf/127.0.0.1/ISM_0x00/Stoermeldung"));
        // the normal values are still there
        Assert.Equal("5", run.Get("Wolf/127.0.0.1/SM1_0x76/Softwarerevision"));
    }

    [Fact]
    public async Task Faults_EveryAnswerIsAFullSnapshot()
    {
        await using var server = new FakeIsm7Server(RespondZero, faultListResponder: _ => new[] { FaultBundle(Ers("0x76", 1, 63, true)) },
            systemconfigBusAddresses: new[] { "0x35", "0x76" });
        await using var run = new FaultsRun(server);
        await WaitUntilAsync(() => run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldung") == "63 Funktionskontrolle Schwerkraftbremse");

        // the active entry is missing from the next answer: like Wolf it stays in the list, no longer active
        await server.SendBundleResponseAsync(FaultBundle(Ers("0x35", 1, 15, false, "01.11.2026 10:00:00", "01.11.2026 10:01:00")));
        await WaitUntilAsync(() => run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldung") == "Keine");
        var kept = Assert.Single(JsonNode.Parse(run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldungen")!)!.AsArray())!;
        Assert.Equal(63, kept["code"]!.GetValue<int>());
        Assert.False(kept["active"]!.GetValue<bool>());
        Assert.Equal("01.11.2026 13:05:00", kept["start"]!.GetValue<string>());
        Assert.Null(kept["stop"]);
        Assert.Contains("\"code\":15", run.Get("Wolf/127.0.0.1/BM_0x35/Stoermeldungen"));

        // the module reports it again, now as history with its stop time: it replaces the kept entry
        await server.SendBundleResponseAsync(FaultBundle(Ers("0x76", 1, 63, false, "01.11.2026 13:05:00", "01.11.2026 14:00:00")));
        await WaitUntilAsync(() => run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldungen")!.Contains("14:00:00"));
        Assert.Equal("Keine", run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldung"));
        var entry = Assert.Single(JsonNode.Parse(run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldungen")!)!.AsArray())!;
        Assert.False(entry["active"]!.GetValue<bool>());
        Assert.Equal("01.11.2026 14:00:00", entry["stop"]!.GetValue<string>());
        // the BM's history entry is kept as well
        Assert.Contains("\"code\":15", run.Get("Wolf/127.0.0.1/BM_0x35/Stoermeldungen"));
        Assert.False(run.RunTask.IsCompleted);
    }

    [Fact]
    public async Task FaultListAnswerWithError_DoesNotBreakTheConnection()
    {
        var error = new TelegramBundleResp { GatewayId = "1", BundleId = "3", State = TelegrResponseState.ER, Errormsg = "not supported" };
        await using var server = new FakeIsm7Server(RespondZero, faultListResponder: _ => new[] { error }, bundleAfterLogin: error);
        await using var run = new FaultsRun(server);
        await WaitUntilAsync(() => server.ReceivedRequests.Any(x => x.Xml.Contains("<erd")));
        Assert.True(run.InitFinished.Task.IsCompleted);
        await Task.Delay(200);
        Assert.False(run.RunTask.IsCompleted);
        Assert.DoesNotContain(run.Topics, x => x.Contains("Stoermeldung"));
        Assert.Equal("5", run.Get("Wolf/127.0.0.1/SM1_0x76/Softwarerevision"));
    }

    [Fact]
    public async Task Faults_WithoutSystemconfigAnswer_RequestsNoFaultList_AndSaysSo()
    {
        using var console = new ConsoleCapture();
        await using var server = new FakeIsm7Server(RespondZero, answerSystemconfig: false);
        await using var run = new FaultsRun(server, startupTimeout: 1);
        await WaitUntilAsync(() => console.Log.Contains("fault list: no answer to the read-systemconfig-request within 1s, the fault lists are not requested"));
        await WaitUntilAsync(() => run.InitFinished.Task.IsCompleted);
        Assert.False(run.RunTask.IsCompleted);
        Assert.DoesNotContain(server.ReceivedRequests, x => x.Xml.Contains("<erd"));
        Assert.DoesNotContain(run.Topics, x => x.Contains("Stoermeldung"));
    }

    [Fact]
    public async Task Faults_WithoutFaultListAnswer_SaysSo()
    {
        using var console = new ConsoleCapture();
        await using var server = new FakeIsm7Server(RespondZero);
        await using var run = new FaultsRun(server, startupTimeout: 1);
        await WaitUntilAsync(() => console.Log.Contains("fault list: no answer within 1s"));
        Assert.Contains(server.ReceivedRequests, x => x.Xml.Contains("<erd"));
        Assert.False(run.RunTask.IsCompleted);
        Assert.DoesNotContain(run.Topics, x => x.Contains("Stoermeldung"));
    }

    [Fact]
    public async Task Faults_FollowTheLanguage()
    {
        await using var server = new FakeIsm7Server(RespondZero, faultListResponder: _ => new[] { FaultBundle(Ers("0x76", 1, 63, true)) },
            systemconfigBusAddresses: new[] { "0x76", "0x8" });
        await using var run = new FaultsRun(server, language: "GBR");
        await WaitUntilAsync(() => run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldung") is not null);
        Assert.Equal("63 Function check, gravity brake", run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldung"));
        Assert.Contains("\"text\":\"Function check, gravity brake\"", run.Get("Wolf/127.0.0.1/SM1_0x76/Stoermeldungen"));
        Assert.Equal("None", run.Get("Wolf/127.0.0.1/CGB_0x08/Stoermeldung"));
    }

    [Fact]
    public async Task UnsolicitedBundle3_DoesNotBreakTheStartup_WithoutFaults()
    {
        var unsolicited = FaultBundle(Ers("0x76", 1, 63, true));
        unsolicited.Errormsg = "unexpected";
        await using var server = new FakeIsm7Server(RespondZero, bundleAfterLogin: unsolicited);
        await using var run = new FaultsRun(server, enableFaults: false);
        var completed = await Task.WhenAny(run.InitFinished.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(run.InitFinished.Task, completed);
        Assert.False(run.RunTask.IsCompleted);
        Assert.Equal("5", run.Get("Wolf/127.0.0.1/SM1_0x76/Softwarerevision"));
        Assert.DoesNotContain(run.Topics, x => x.Contains("Stoermeldung"));
        var bundles = BundleRequests(server);
        Assert.DoesNotContain(bundles, x => x.EStRead is not null);
        Assert.All(bundles, x => Assert.True(Int32.Parse(x.BundleId) >= 10, x.BundleId));
    }

    [Fact]
    public void BundleIds_NeverUseTheFaultListId()
    {
        Assert.True(IdGenerator.GetNextBundleId() >= 10);
    }

    private static Ism7Config LoadFaultsConfig(out string path, string fixture = "faults.json", string language = "DEU", params string[] systemconfig)
    {
        path = TestFixtures.WriteParameterFile(fixture, 9092);
        var config = new Ism7Config(path, new Ism7Localizer(language));
        config.AddAllDevices("host").ToList();
        config.SelectFaultBusAddresses(systemconfig.Length > 0 ? systemconfig : new[] { "0x35", "0x75", "0x76", "0x8" });
        return config;
    }

    [Fact]
    public void FaultLists_OnlyForPhysicalDevices_CwlOn0x8B_NoFirstMmWithKm()
    {
        var config = LoadFaultsConfig(out var path, "faults-mk-cwl.json", "DEU", "0x35", "0x3C", "0x15", "0x51", "0x52");
        try
        {
            Assert.Equal(new[] { "0x35", "0x8B", "0x15", "0x52" }, config.FaultBusAddresses);
            // the virtual MK_BM comes first at 0x35, but the BM owns the fault list
            Assert.False(config.Devices.Single(x => x.Name == "MK_BM").ReportsFaults);
            Assert.True(config.Devices.Single(x => x.Name == "BM").ReportsFaults);
            Assert.Equal(30000, config.GetFaultDeviceTemplateId("0x35"));
            Assert.Equal(230000, config.GetFaultDeviceTemplateId("0x8B"));
            Assert.Null(config.GetFaultDeviceTemplateId("0x3C"));
            Assert.Null(config.GetFaultDeviceTemplateId("0x51"));

            Assert.True(config.ProcessFaults(new[] { Ers("0x8B", 1, 100, true), Ers("0x3C", 2, 101, true) }));
            var cwl = config.Devices.Single(x => x.Name == "CWL");
            Assert.Equal("100 Druck Zuluft", cwl.ActiveFault);
            Assert.Single(cwl.Faults);
            Assert.Null(config.Devices.Single(x => x.Name == "MK_BM").Faults);
            Assert.Empty(config.Devices.Single(x => x.Name == "BM").Faults);
            Assert.Null(config.Devices.Single(x => x.MqttTopic == "Wolf/host/MM_0x51").Faults);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SelectFaultBusAddresses_OnlyDevicesInTheSystemconfig()
    {
        var config = LoadFaultsConfig(out var path, "faults.json", "DEU", "0x35", "0x76", "0x08", "0x00");
        try
        {
            Assert.Equal(new[] { "0x35", "0x75", "0x76", "0x8" }, config.FaultBusAddresses);
            Assert.Equal(new[] { "0x35", "0x76", "0x8" }, config.SelectFaultBusAddresses(new[] { "0x35", "0x76", "0x08", "0x00" }));
            Assert.Empty(config.SelectFaultBusAddresses(new[] { "0x00" }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFaults_UnknownCodeHasNoText_InvalidCodesAndAddressesAreDropped()
    {
        var config = LoadFaultsConfig(out var path);
        using var console = new ConsoleCapture();
        try
        {
            var answer = new[]
            {
                Ers("0x76", 1, 200, true),
                Ers("0x76", 2, 0, true),
                Ers("0x76", 3, 300, true),
                Ers("0x99", 1, 63, true),
                Ers("0x00", 1, 63, true),
            };
            Assert.True(config.ProcessFaults(answer));
            // repeated in every answer, logged once
            Assert.False(config.ProcessFaults(answer));
            Assert.Equal(2, console.Log.Split("unknown bus address '0x99'").Length);
            var sm1 = config.Devices.Single(x => x.Name == "SM1");
            var entry = Assert.Single(sm1.Faults);
            Assert.Equal(200, entry.Code);
            Assert.Null(entry.Text);
            Assert.Equal("200", sm1.ActiveFault);
            Assert.Contains("\"text\":null", sm1.MqttMessages.Single(x => x.Path.EndsWith("/Stoermeldungen")).Content);
            Assert.Null(config.Devices.Single(x => x.Name == "ISM").Faults);
            Assert.Null(config.Devices.Single(x => x.Name == "DHK_BM").Faults);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFaults_NewestActiveEntryWins_AtMostTenEntries_RepeatsAreNoChange()
    {
        var config = LoadFaultsConfig(out var path);
        try
        {
            var entries = new List<ErrorStateResp>
            {
                Ers("0x76", 1, 63, true, "31.10.2026 07:00:00"),
                Ers("0x8", 2, 8, true, "01.11.2026 07:00:00"),
                Ers("0x8", 3, 4, true, "01.11.2026 09:00:00"),
            };
            entries.AddRange(Enumerable.Range(4, 10).Select(i => Ers("0x08", i, 4, false, "01.10.2026 07:00:00", "01.10.2026 08:00:00")));
            Assert.True(config.ProcessFaults(entries));
            var cgb = config.Devices.Single(x => x.Name == "CGB");
            var sm1 = config.Devices.Single(x => x.Name == "SM1");
            Assert.Equal("4 Keine Flammenbildung", cgb.ActiveFault);
            // at most 10 entries per device, the active ones are kept
            Assert.Equal(10, cgb.Faults.Count);
            Assert.Equal(2, cgb.Faults.Count(x => x.Active));
            Assert.Single(sm1.Faults);
            Assert.False(config.ProcessFaults(entries));

            // a numeric match, whatever spelling the module uses
            Assert.True(config.ProcessFaults(new[] { Ers("0x08", 1, 8, true) }));
            Assert.Equal("8", cgb.ActiveFault);
            Assert.Equal(10, cgb.Faults.Count);
            Assert.Single(cgb.Faults, x => x.Active);
            // missing from the answer: kept, but no longer active
            Assert.Equal("Keine", sm1.ActiveFault);
            var kept = Assert.Single(sm1.Faults);
            Assert.Equal(63, kept.Code);
            Assert.False(kept.Active);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFaults_HistoryOfOneDevice_DoesNotHideAnActiveFault()
    {
        var config = LoadFaultsConfig(out var path);
        try
        {
            // 11 history entries of the SM1 come first, then an active fault of the CGB
            var entries = Enumerable.Range(1, 11)
                .Select(i => Ers("0x76", i, 63, false, $"{i:00}.10.2026 07:00:00", $"{i:00}.10.2026 08:00:00"))
                .ToList();
            entries.Add(Ers("0x8", 1, 4, true, "01.11.2026 07:00:00"));
            Assert.True(config.ProcessFaults(entries));
            var cgb = config.Devices.Single(x => x.Name == "CGB");
            var sm1 = config.Devices.Single(x => x.Name == "SM1");
            Assert.Equal("4 Keine Flammenbildung", cgb.ActiveFault);
            Assert.Equal(10, sm1.Faults.Count);

            // within one device, an active entry behind 10 history entries is kept as well
            entries.Add(Ers("0x76", 12, 63, true, "01.11.2026 07:00:00"));
            Assert.True(config.ProcessFaults(entries));
            Assert.Equal(10, sm1.Faults.Count);
            Assert.Equal("63 Funktionskontrolle Schwerkraftbremse", sm1.ActiveFault);
            Assert.Equal(12, sm1.Faults.Last().Index);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ProcessFaults_BmListsHeatGeneratorFault_TextIsBorrowedFromTheHeatGenerator()
    {
        var config = LoadFaultsConfig(out var path);
        try
        {
            // live answer of FW 3.00: the BM at 0x35 lists the burner faults 5 and 4 (texts only under the CGB, 80000)
            Assert.True(config.ProcessFaults(new[]
            {
                Ers("0x35", 1, 5, true, "14.09.2026 23:10:41"),
                Ers("0x35", 2, 4, false, "14.09.2026 23:10:34", "14.09.2026 23:10:40"),
                Ers("0x35", 3, 15, false, "14.09.2026 23:10:30", "14.09.2026 23:10:31"),
                Ers("0x76", 1, 4, true, "14.09.2026 23:10:41"),
            }));
            var bm = config.Devices.Single(x => x.ReportsFaults && x.FaultBusAddress == 0x35);
            Assert.Equal("5 Flammenausfall im Betrieb", bm.ActiveFault);
            Assert.Equal(80000, bm.Faults[0].TextSource);
            Assert.Equal("Keine Flammenbildung", bm.Faults[1].Text);
            // the BM's own text is not borrowed
            Assert.Null(bm.Faults[2].TextSource);
            Assert.NotNull(bm.Faults[2].Text);
            var json = bm.MqttMessages.Single(x => x.Path.EndsWith("/Stoermeldungen")).Content;
            Assert.Contains("\"text_source\":80000", json);
            // only a BM borrows texts: the SM1 has none for code 4
            var sm1 = config.Devices.Single(x => x.Name == "SM1");
            Assert.Equal("4", sm1.ActiveFault);
            Assert.Null(sm1.Faults[0].TextSource);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FaultTexts_AreLookedUpByCodeAndDevice()
    {
        Assert.Equal("Funktionskontrolle Schwerkraftbremse", FaultMessageTexts.Default.Get(63, 60000));
        Assert.Equal("Störung Befeuchter", FaultMessageTexts.Default.Get(63, 660000));
        Assert.Null(FaultMessageTexts.Default.Get(63, 30000));
    }

    [Fact]
    public void JsonPayload_ContainsTheFaultsOnceReceived()
    {
        var config = LoadFaultsConfig(out var path);
        try
        {
            Assert.Empty(config.JsonMessages);
            config.ProcessFaults(new[] { Ers("0x76", 1, 63, true) });
            var sm1 = Assert.Single(config.JsonMessages, x => x.Path == "Wolf/host/SM1_0x76");
            Assert.Equal("63 Funktionskontrolle Schwerkraftbremse", sm1.Content["Stoermeldung"]!.GetValue<string>());
            Assert.Single(sm1.Content["Stoermeldungen"]!.AsArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HaDiscovery_AnnouncesAFaultSensorPerDevice_OnlyWithFaults()
    {
        var config = LoadFaultsConfig(out var path);
        try
        {
            var discovery = new HaDiscovery(config, null!, "wolf", new Ism7Localizer("DEU")) { EnableFaults = true };
            var sensors = discovery.GetDiscoveryInfo().Where(x => x.Path.EndsWith("_Stoermeldung/config")).ToList();
            Assert.Equal(4, sensors.Count);
            var sm1 = Assert.Single(sensors, x => x.Path == "homeassistant/sensor/wolf_SM1_0x71_Stoermeldung/config");
            Assert.Equal("Wolf/host/SM1_0x76/Stoermeldung", sm1.Content["state_topic"]!.GetValue<string>());
            Assert.Equal("Wolf/host/SM1_0x76/Stoermeldungen", sm1.Content["json_attributes_topic"]!.GetValue<string>());
            Assert.Equal("{{ {'Stoermeldungen': value_json} | tojson }}", sm1.Content["json_attributes_template"]!.GetValue<string>());
            Assert.Equal("Störmeldung", sm1.Content["name"]!.GetValue<string>());

            discovery.EnableFaults = false;
            Assert.DoesNotContain(discovery.GetDiscoveryInfo(), x => x.Path.EndsWith("_Stoermeldung/config"));

            var english = new HaDiscovery(config, null!, "wolf", new Ism7Localizer("GBR")) { EnableFaults = true };
            Assert.Equal("Fault message", english.GetDiscoveryInfo().Single(x => x.Path == "homeassistant/sensor/wolf_SM1_0x71_Stoermeldung/config").Content["name"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
