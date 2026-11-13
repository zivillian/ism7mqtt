using ism7mqtt.HomeAssistant;
using ism7mqtt.ISM7.Protocol;
using ism7mqtt.ISM7.Xml;
using Xunit;

namespace ism7mqtt.Tests;

public class TimeprogTests
{
    // 360000 Raumtemperatur (SS10, telegram 10164) keeps the regular pull/push path busy
    private static List<InfonumberReadResp> RoomTemperature(TelegramBundleReq request) =>
        request.InfoReadTelegrams.Select(ir => new InfonumberReadResp
        {
            BusAddress = ir.BusAddress,
            InfoNumber = (ushort)ir.InfoNumber,
            State = TelegrResponseState.OK,
            DBLow = "0xD5",
            DBHigh = "0x00",
        }).ToList();

    // Fixtures/timeprog-read.json:
    //   0x85 / 360000 (MK): 320063-320069 = heating program 1 (ty 0, hk 1), Mo..Sa, So
    //   0x35 / 350000 (WW): 320021 = hot water program 1 Mo, 320028 = hot water program 2 Mo
    private static string? Respond(TimeprogRead read) => (read.ProgramType, read.HeatprogNumber, read.DayOfWeek) switch
    {
        (0, 1, _) => "2C019F0500000000000000000000000000000000",   // 05:00-23:59
        (1, 1, 1) => "4A01D4030000000000000000FFFFFFFFFFFFFFFF",   // 05:30-16:20
        (1, 2, 1) => null,                                         // error response
        _ => "0000000000000000000000000000000000000000",
    };

    [Theory]
    [InlineData("2C01FE0100000000000000000000000000000000", "05:00-08:30")]
    [InlineData("2C01680100000000000000000000000000000000", "05:00-06:00")]
    [InlineData("FD021A040000000000000000FFFFFFFFFFFFFFFF", "12:45-17:30")]
    [InlineData("2C01A4018403EC040000000000000000FFFFFFFF", "05:00-07:00, 15:00-21:00")]
    [InlineData("4A01A00500000000000000000000000000000000", "05:30-24:00")]
    [InlineData("0000000000000000000000000000000000000000", "-")]
    [InlineData("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "-")]
    public void Decode_FormatsSwitchTimes(string data, string expected)
    {
        var switchTimes = Timeprog03F1ConverterTemplate.Decode(data);
        Assert.Equal(expected, Timeprog03F1ConverterTemplate.Format(switchTimes));
    }

    [Fact]
    public async Task TimeprogReads_ArePulledAndPublished_WhenEnabled()
    {
        var published = new System.Collections.Concurrent.ConcurrentQueue<JsonMessage>();
        await using var server = new FakeIsm7Server(RoomTemperature, timeprogResponder: Respond);
        var parameterPath = TestFixtures.WriteParameterFile("timeprog-read.json", server.Port);
        try
        {
            var timeprogsPublished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new Ism7Client((config, _) =>
            {
                foreach (var message in config.JsonMessages)
                {
                    published.Enqueue(message);
                    if (message.Path == "Wolf/127.0.0.1/WWSystem_BM-2_0x35")
                        timeprogsPublished.TrySetResult(true);
                }
                return Task.CompletedTask;
            }, parameterPath, "127.0.0.1", new Ism7Localizer("DEU"))
            {
                StartupTimeout = 5,
                TimeprogInterval = 60,
                TimeprogBundlePause = TimeSpan.Zero,
                TimeprogStartupDelay = TimeSpan.Zero,
            };

            using var cts = new CancellationTokenSource();
            var runTask = client.RunAsync("test-password", cts.Token);
            var completed = await Task.WhenAny(timeprogsPublished.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(timeprogsPublished.Task, completed);
            cts.Cancel();
            await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));

            var requests = server.TimeprogRequests.ToList();
            Assert.Equal(9, requests.Count);
            var mk = requests.Where(x => x.BusAddress == "0x85").ToList();
            Assert.Equal(7, mk.Count);
            Assert.All(mk, x => Assert.Equal((1, 0, 1), (x.HeatprogNumber, x.ProgramType, x.HzkInstance)));
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, mk.Select(x => x.DayOfWeek).OrderBy(x => x));
            var ww = requests.Where(x => x.BusAddress == "0x35").OrderBy(x => x.HeatprogNumber).ToList();
            Assert.Equal(new[] { (1, 1, 0, 1), (2, 1, 0, 1) }, ww.Select(x => (x.HeatprogNumber, x.ProgramType, x.HzkInstance, x.DayOfWeek)));
            Assert.All(requests, x => Assert.False(String.IsNullOrEmpty(x.Seq)));

            // time programs must never be push-subscribed - every subscription occupies a slot in the ISM7
            Assert.All(server.PushRequests, x => Assert.True(x.TimeprogReadTelegrams is null or { Count: 0 }));

            var mkMessage = published.Last(x => x.Path == "Wolf/127.0.0.1/MK_BM-2_0x85");
            Assert.Equal("05:00-23:59", mkMessage.Content["Mo"]!.GetValue<string>());
            Assert.Equal("05:00-23:59", mkMessage.Content["So"]!.GetValue<string>());
            // the regular datapoints are not affected - values are published once after each read
            Assert.Contains(published, x => x.Path == "Wolf/127.0.0.1/MK_BM-2_0x85"
                                            && x.Content.ContainsKey("Raumtemperatur")
                                            && x.Content["Raumtemperatur"]!.ToJsonString() == "21.3");

            var wwMessage = published.Last(x => x.Path == "Wolf/127.0.0.1/WWSystem_BM-2_0x35");
            // program 1 Mo answered, program 2 Mo failed and must stay without value
            Assert.Equal("05:30-16:20", wwMessage.Content["Mo"]!.AsObject()["320021"]!.GetValue<string>());
            Assert.False(wwMessage.Content["Mo"]!.AsObject().ContainsKey("320028"));
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task TimeprogReads_LateResponse_DoesNotBreakTheConnection()
    {
        // the first bundle is answered after the client stopped waiting (StartupTimeout = 1s)
        await using var server = new FakeIsm7Server(RoomTemperature, timeprogResponder: Respond, timeprogResponseDelay: TimeSpan.FromSeconds(1.5));
        var parameterPath = TestFixtures.WriteParameterFile("timeprog-read.json", server.Port);
        var errors = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(errors);
        try
        {
            var client = new Ism7Client((_, _) => Task.CompletedTask, parameterPath, "127.0.0.1", new Ism7Localizer("DEU"))
            {
                StartupTimeout = 1,
                TimeprogInterval = 60,
                TimeprogBundlePause = TimeSpan.Zero,
                TimeprogStartupDelay = TimeSpan.Zero,
            };
            using var cts = new CancellationTokenSource();
            var runTask = client.RunAsync("test-password", cts.Token);

            // timeout after 1s, late answer after 1.5s - the connection must survive both
            var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(4)));
            Assert.NotSame(runTask, completed);
            Assert.Contains("no response for time program bundle", errors.ToString());

            cts.Cancel();
            await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            Console.SetError(originalError);
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task TimeprogReads_AreNotSent_ByDefault()
    {
        await using var server = new FakeIsm7Server(RoomTemperature, timeprogResponder: Respond);
        var parameterPath = TestFixtures.WriteParameterFile("timeprog-read.json", server.Port);
        try
        {
            var initFinished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new Ism7Client((_, _) => Task.CompletedTask, parameterPath, "127.0.0.1", new Ism7Localizer("DEU"))
            {
                StartupTimeout = 5,
                OnInitializationFinishedAsync = (_, _) =>
                {
                    initFinished.TrySetResult(true);
                    return Task.CompletedTask;
                }
            };
            using var cts = new CancellationTokenSource();
            var runTask = client.RunAsync("test-password", cts.Token);
            var completed = await Task.WhenAny(initFinished.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.Same(initFinished.Task, completed);
            // give a (wrongly) started refresh a chance to show up
            await Task.Delay(500);
            cts.Cancel();
            await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Empty(server.TimeprogRequests);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public void Discovery_LocalizesNames_ButKeepsLanguageIndependentIds()
    {
        var parameterPath = TestFixtures.WriteParameterFile("timeprog-read.json", 0);
        try
        {
            var localizer = new Ism7Localizer("GBR");
            var config = new Ism7Config(parameterPath, localizer);
            _ = config.AddAllDevices("127.0.0.1").ToList();
            var discovery = new HaDiscovery(config, null!, "Wolf", localizer) { IncludeTimeprogs = true };
            var mkMonday = Assert.Single(discovery.GetDiscoveryInfo(),
                x => x.Content["unique_id"]!.GetValue<string>() == "Wolf_MK_BM2_0x80_320063_Zeitprogramm_Heizen_1_Mo");
            Assert.Equal("Time program Heating 1 Mo", mkMonday.Content["name"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Discovery_PublishesReadOnlySensors_OnlyIfEnabled(bool includeTimeprogs)
    {
        var parameterPath = TestFixtures.WriteParameterFile("timeprog-read.json", 0);
        try
        {
            var localizer = new Ism7Localizer("DEU");
            var config = new Ism7Config(parameterPath, localizer);
            _ = config.AddAllDevices("127.0.0.1").ToList();
            var discovery = new HaDiscovery(config, null!, "Wolf", localizer) { IncludeTimeprogs = includeTimeprogs };
            var timeprogs = discovery.GetDiscoveryInfo()
                .Where(x => x.Content["name"]!.GetValue<string>().StartsWith("Zeitprogramm"))
                .ToList();

            if (!includeTimeprogs)
            {
                Assert.Empty(timeprogs);
                return;
            }
            Assert.Equal(9, timeprogs.Count);
            Assert.All(timeprogs, x =>
            {
                Assert.StartsWith("homeassistant/sensor/", x.Path);
                Assert.False(x.Content.ContainsKey("command_topic"));
                Assert.Equal("{{ value_json }}", x.Content["value_template"]!.GetValue<string>());
            });
            var mkMonday = Assert.Single(timeprogs, x => x.Content["name"]!.GetValue<string>() == "Zeitprogramm Heizen 1 Mo");
            Assert.Equal("Wolf/127.0.0.1/MK_BM-2_0x85/Mo", mkMonday.Content["state_topic"]!.GetValue<string>());
            Assert.Equal("Wolf_MK_BM2_0x80_320063_Zeitprogramm_Heizen_1_Mo", mkMonday.Content["unique_id"]!.GetValue<string>());
            var wwMonday = Assert.Single(timeprogs, x => x.Content["name"]!.GetValue<string>() == "Zeitprogramm Warmwasser 2 Mo");
            Assert.Equal("Wolf/127.0.0.1/WWSystem_BM-2_0x35/Mo/320028", wwMonday.Content["state_topic"]!.GetValue<string>());
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }
}
