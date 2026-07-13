using ism7mqtt.ISM7.Protocol;
using Xunit;

// FakeIsm7Server redirects Console.Out/Error in some tests below - keep this collection's tests
// (all of them, this is the only class today) from running concurrently with each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ism7mqtt.Tests;

public class Ism7ClientStartupTests
{
    // Telegram number -> raw wire bytes for the parameters used by Fixtures/happy-path.json.
    private static readonly Dictionary<ushort, (string Low, string High)> HappyPathTelegrams = new()
    {
        [12] = ("0x55", "0x00"),    // 270005 Außentemperatur (SS10): 85 -> 8.5°C
        [367] = ("0x2A", "0x00"),   // 270010 Aktuelle Leistungsvorgabe Verdichter (US): 42%
        [12409] = ("0x01", "0x00"), // 270011 Heizkreispumpe (binary bit0): 1 -> "Ein"
        [10164] = ("0xD5", "0x00"), // 360000 Raumtemperatur (SS10): 213 -> 21.3°C
        [10100] = ("0x01", "0x00"), // 360051 Programmwahl (US, list): 1 -> "Auto"
        [5266] = ("0x02", "0x00"),  // 360066 Reglertyp (US): 2
    };

    private static List<InfonumberReadResp> RespondFromTable(TelegramBundleReq request, IReadOnlyDictionary<ushort, (string Low, string High)> table)
    {
        return request.InfoReadTelegrams.Select(ir =>
        {
            var (low, high) = table[(ushort)ir.InfoNumber];
            return new InfonumberReadResp
            {
                BusAddress = ir.BusAddress,
                InfoNumber = (ushort)ir.InfoNumber,
                State = TelegrResponseState.OK,
                DBLow = low,
                DBHigh = high,
            };
        }).ToList();
    }

    [Fact]
    public async Task HappyPath_PublishesBothDevicesWithExpectedValues()
    {
        await using var server = new FakeIsm7Server(request => RespondFromTable(request, HappyPathTelegrams));
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var captured = new List<JsonMessage>();
            var initFinished = new TaskCompletionSource<bool>();
            var client = new Ism7Client((config, _) =>
            {
                foreach (var message in config.JsonMessages)
                {
                    captured.Add(message);
                }
                return Task.CompletedTask;
            }, parameterPath, "127.0.0.1", new Ism7Localizer("DEU"))
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

            cts.Cancel();
            await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Equal(2, captured.Count);

            var cha = Assert.Single(captured, m => m.Path == "Wolf/127.0.0.1/CHA_0x29");
            Assert.Equal("8.5", cha.Content["Außentemperatur"]!.ToJsonString());
            Assert.Equal("42", cha.Content["Aktuelle Leistungsvorgabe Verdichter"]!.ToJsonString());
            Assert.Equal("Ein", cha.Content["Heizkreispumpe"]!["text"]!.GetValue<string>());
            Assert.Equal("1", cha.Content["Heizkreispumpe"]!["value"]!.ToJsonString());

            var mkBm2 = Assert.Single(captured, m => m.Path == "Wolf/127.0.0.1/MK_BM-2_0x85");
            Assert.Equal("21.3", mkBm2.Content["Raumtemperatur"]!.ToJsonString());
            Assert.Equal("Auto", mkBm2.Content["Programmwahl"]!["text"]!.GetValue<string>());
            Assert.Equal("1", mkBm2.Content["Programmwahl"]!["value"]!.ToJsonString());
            Assert.Equal("2", mkBm2.Content["Reglertyp"]!.ToJsonString());
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task HappyPath_ChunksLargeDeviceIntoThreePullBundles()
    {
        // Fixtures/chunked-pull.json has one device with 42 single-telegram parameters. They should be
        // packed into bundles of <=20 info reads each (20 + 20 + 2), i.e. 3 pull requests instead of 1 -
        // see Ism7Config.GetBundlesForDevice, added for FW5.x devices that choke on overly large pulls.
        var telegramValues = new Dictionary<ushort, (string Low, string High)>
        {
            [5266] = ("0x00", "0x00"),
            [10165] = ("0x00", "0x00"),
            [10179] = ("0x00", "0x00"),
            [10109] = ("0x00", "0x00"),
            [10164] = ("0x00", "0x00"),
        };
        var pullBundleCount = 0;

        await using var server = new FakeIsm7Server(request =>
        {
            pullBundleCount++;
            return RespondFromTable(request, telegramValues);
        });
        var parameterPath = TestFixtures.WriteParameterFile("chunked-pull.json", server.Port);
        try
        {
            var handlerInvocationCount = 0;
            var initFinished = new TaskCompletionSource<bool>();
            var client = new Ism7Client((_, _) =>
            {
                handlerInvocationCount++;
                return Task.CompletedTask;
            }, parameterPath, "127.0.0.1", new Ism7Localizer("DEU"))
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

            cts.Cancel();
            await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Equal(3, pullBundleCount);
            Assert.Equal(3, handlerInvocationCount);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task InvalidLoginState_LogsErrorAndReturnsWithoutStartingUp()
    {
        await using var server = new FakeIsm7Server(_ => new List<InfonumberReadResp>(), loginState: LoginState.busy);
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
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
            };

            var originalOut = Console.Out;
            var consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);
            Task runTask;
            try
            {
                using var cts = new CancellationTokenSource();
                runTask = client.RunAsync("test-password", cts.Token);
                var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(10)));
                Assert.Same(runTask, completed);
                cts.Cancel();
                await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            Assert.Contains("invalid login state", consoleOutput.ToString());
            Assert.False(handlerInvoked);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task DroppedFifthOfSixPullBundleResponses_LogsTimeoutAndNeverFinishesStartup()
    {
        await using var server = new FakeIsm7Server(
            request => RespondFromTable(request, new Dictionary<ushort, (string Low, string High)> { [10164] = ("0x00", "0x00") }),
            dropPullBundleOrdinal: 5);
        var parameterPath = TestFixtures.WriteParameterFile("pull-timeout.json", server.Port);
        try
        {
            var publishedDeviceCount = 0;
            var initFinished = new TaskCompletionSource<bool>();
            var client = new Ism7Client((_, _) =>
            {
                publishedDeviceCount++;
                return Task.CompletedTask;
            }, parameterPath, "127.0.0.1", new Ism7Localizer("DEU"))
            {
                StartupTimeout = 2,
                OnInitializationFinishedAsync = (_, _) =>
                {
                    initFinished.TrySetResult(true);
                    return Task.CompletedTask;
                }
            };

            var originalOut = Console.Out;
            var consoleOutput = new StringWriter();
            Console.SetOut(consoleOutput);
            Task runTask;
            try
            {
                using var cts = new CancellationTokenSource();
                runTask = client.RunAsync("test-password", cts.Token);
                var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(8)));
                Assert.Same(runTask, completed);
                cts.Cancel();
                await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            Assert.Contains("Shutdown ism7mqtt due to timeout during startup.", consoleOutput.ToString());
            // bundles 1-4 succeeded and were published before bundle 5 stalled; startup never completed.
            Assert.Equal(4, publishedDeviceCount);
            Assert.False(initFinished.Task.IsCompleted);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }
}
