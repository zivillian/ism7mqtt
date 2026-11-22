using System.Net;
using System.Net.Sockets;
using ism7mqtt.ISM7.Protocol;
using Xunit;

namespace ism7mqtt.Tests;

/// <summary>
/// Reconnect behavior for issue #245: the ism7 drops its connection (router reconnect, device reset) or refuses
/// the first attempt afterwards, and ism7mqtt has to reconnect by itself instead of ending the process.
/// The delay function is replaced so the tests don't wait for the real backoff.
/// </summary>
public class Ism7SupervisorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static Ism7Client CreateClient(string parameterPath, Func<Task>? onInitializationFinished = null, int startupTimeout = 5, TimeSpan? keepAliveInterval = null)
    {
        return new Ism7Client((_, _) => Task.CompletedTask, parameterPath, "127.0.0.1", new Ism7Localizer("DEU"))
        {
            StartupTimeout = startupTimeout,
            KeepAliveInterval = keepAliveInterval ?? TimeSpan.FromMinutes(1),
            // deterministic: initialization only finishes after every response has been received
            SerializePushSubscribes = true,
            OnInitializationFinishedAsync = (_, _) => onInitializationFinished?.Invoke() ?? Task.CompletedTask,
        };
    }

    private static async Task<string> CaptureConsoleAsync(Func<Task> action)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var output = new StringWriter();
        Console.SetOut(output);
        Console.SetError(output);
        try
        {
            await action();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
        return output.ToString();
    }

    [Fact]
    public async Task ConnectionDroppedAfterInitialization_ReconnectsWithFreshClient()
    {
        await using var server = new FakeIsm7Server(request => Ism7ClientStartupTests.RespondFromTable(request, Ism7ClientStartupTests.HappyPathTelegrams));
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var clients = new List<Ism7Client>();
            var firstInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() =>
            {
                var initialization = clients.Count == 0 ? firstInitialization : secondInitialization;
                var client = CreateClient(parameterPath, () =>
                {
                    initialization.TrySetResult();
                    return Task.CompletedTask;
                });
                clients.Add(client);
                return client;
            }, (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

            using var cts = new CancellationTokenSource();
            Task runTask = null!;
            var output = await CaptureConsoleAsync(async () =>
            {
                runTask = supervisor.RunAsync("test-password", cts.Token);

                Assert.Same(firstInitialization.Task, await Task.WhenAny(firstInitialization.Task, Task.Delay(TestTimeout)));
                Assert.Same(clients[0], supervisor.Current);

                server.DropConnection();

                Assert.Same(secondInitialization.Task, await Task.WhenAny(secondInitialization.Task, Task.Delay(TestTimeout)));
                // mqtt commands are routed to the client of the new connection
                Assert.Same(clients[1], supervisor.Current);

                cts.Cancel();
                Assert.Same(runTask, await Task.WhenAny(runTask, Task.Delay(TestTimeout)));
            });

            // cancellation ends the loop cleanly instead of reconnecting or throwing
            Assert.True(runTask.IsCompletedSuccessfully);
            Assert.Null(supervisor.Current);
            Assert.Equal(2, clients.Count);
            Assert.NotSame(clients[0], clients[1]);
            Assert.Equal(2, server.ConnectionCount);
            Assert.Equal(new[] { supervisor.MinBackoff }, delays);
            // the reason is part of the reconnect line
            Assert.Contains("ism7 connection failed: SocketException", output);
            Assert.Contains("- reconnecting in 10 seconds", output);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task FirstConnectionAttemptRefused_IsRetriedWithoutUnhandledException()
    {
        // the ism7 closes the first connection after a disconnect - observed as "Socket not connected"
        await using var server = new FakeIsm7Server(request => Ism7ClientStartupTests.RespondFromTable(request, Ism7ClientStartupTests.HappyPathTelegrams), rejectConnections: 1);
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var initialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath, () =>
            {
                initialization.TrySetResult();
                return Task.CompletedTask;
            }), (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

            using var cts = new CancellationTokenSource();
            Task runTask = null!;
            var output = await CaptureConsoleAsync(async () =>
            {
                runTask = supervisor.RunAsync("test-password", cts.Token);
                Assert.Same(initialization.Task, await Task.WhenAny(initialization.Task, Task.Delay(TestTimeout)));
                cts.Cancel();
                Assert.Same(runTask, await Task.WhenAny(runTask, Task.Delay(TestTimeout)));
            });

            Assert.True(runTask.IsCompletedSuccessfully);
            Assert.Equal(2, server.ConnectionCount);
            Assert.Equal(new[] { supervisor.MinBackoff }, delays);
            Assert.Contains("ism7 connection failed: ", output);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task LoginRefused_IsNeverRetried()
    {
        // a wrong password must not be repeated - the ism7 might lock the client out
        await using var server = new FakeIsm7Server(_ => new List<InfonumberReadResp>(), loginState: LoginState.invalidCredentials);
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath), (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

            using var cts = new CancellationTokenSource();
            Exception? error = null;
            var output = await CaptureConsoleAsync(async () =>
            {
                error = await Record.ExceptionAsync(() => supervisor.RunAsync("test-password", cts.Token).WaitAsync(TestTimeout));
            });

            var login = Assert.IsType<Ism7LoginException>(error);
            Assert.Equal(LoginState.invalidCredentials, login.State);
            Assert.Contains("invalid login state 'invalidCredentials'", login.Message);
            Assert.Equal(1, server.ConnectionCount);
            Assert.Empty(delays);
            Assert.DoesNotContain("reconnecting", output);
            Assert.Null(supervisor.Current);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Theory]
    [InlineData(LoginState.busy)]
    [InlineData(LoginState.pending)]
    public async Task LoginBusyOrPending_IsRetriedLikeAConnectionError(LoginState state)
    {
        // pending: the ism7 still holds the previous session (#245) - transient, so retried within the reconnect timeout
        await using var server = new FakeIsm7Server(_ => new List<InfonumberReadResp>(), loginState: state);
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var clock = new ManualTimeProvider();
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath), (delay, _) =>
            {
                delays.Add(delay);
                clock.Advance(delay);
                return Task.CompletedTask;
            }, clock);

            using var cts = new CancellationTokenSource();
            Exception? error = null;
            var output = await CaptureConsoleAsync(async () =>
            {
                error = await Record.ExceptionAsync(() => supervisor.RunAsync("test-password", cts.Token).WaitAsync(TestTimeout));
            });

            Assert.Equal(new[] { 10, 20, 40, 60, 60, 60, 50 }, delays.Select(x => (int)x.TotalSeconds));
            Assert.Equal(delays.Count + 1, server.ConnectionCount);
            var timeout = Assert.IsType<TimeoutException>(error);
            var login = Assert.IsType<Ism7LoginException>(timeout.InnerException);
            Assert.Equal(state, login.State);
            Assert.Contains($"invalid login state '{state}' - reconnecting in 10 seconds", output);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task LoginError_IsNeverRetried()
    {
        // "error" is not known to be transient, so it is treated like a rejected login
        await using var server = new FakeIsm7Server(_ => new List<InfonumberReadResp>(), loginState: LoginState.error);
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath), (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

            using var cts = new CancellationTokenSource();
            Exception? error = null;
            await CaptureConsoleAsync(async () =>
            {
                error = await Record.ExceptionAsync(() => supervisor.RunAsync("test-password", cts.Token).WaitAsync(TestTimeout));
            });

            var login = Assert.IsType<Ism7LoginException>(error);
            Assert.Equal(LoginState.error, login.State);
            Assert.Equal(1, server.ConnectionCount);
            Assert.Empty(delays);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task ReconnectTimeoutZero_ConnectionErrorEndsImmediately()
    {
        // exactly the behavior before the reconnect loop: the first problem ends RunAsync with the error
        await using var server = new FakeIsm7Server(_ => new List<InfonumberReadResp>(), rejectConnections: 100);
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath), (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            })
            {
                ReconnectTimeout = TimeSpan.Zero,
            };

            using var cts = new CancellationTokenSource();
            Exception? error = null;
            var output = await CaptureConsoleAsync(async () =>
            {
                error = await Record.ExceptionAsync(() => supervisor.RunAsync("test-password", cts.Token).WaitAsync(TestTimeout));
            });

            Assert.NotNull(error);
            Assert.IsNotType<TimeoutException>(error);
            Assert.Equal(1, server.ConnectionCount);
            Assert.Empty(delays);
            Assert.DoesNotContain("reconnecting", output);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task ReconnectTimeoutZero_ConnectionLostAfterInitializationEndsWithError()
    {
        await using var server = new FakeIsm7Server(request => Ism7ClientStartupTests.RespondFromTable(request, Ism7ClientStartupTests.HappyPathTelegrams));
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath, () =>
            {
                server.DropConnection();
                return Task.CompletedTask;
            }), (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            })
            {
                ReconnectTimeout = TimeSpan.Zero,
            };

            using var cts = new CancellationTokenSource();
            Exception? error = null;
            await CaptureConsoleAsync(async () =>
            {
                error = await Record.ExceptionAsync(() => supervisor.RunAsync("test-password", cts.Token).WaitAsync(TestTimeout));
            });

            Assert.NotNull(error);
            Assert.Equal(1, server.ConnectionCount);
            Assert.Empty(delays);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task ReconnectTimeoutExceeded_RetriesWithBackoffUpToMaximumThenFailsWithLastError()
    {
        // the ism7 never comes back. Time is simulated: every delay advances the clock, so the whole 5 minutes run instantly.
        await using var server = new FakeIsm7Server(_ => new List<InfonumberReadResp>(), rejectConnections: 100);
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var clock = new ManualTimeProvider();
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath), (delay, _) =>
            {
                delays.Add(delay);
                clock.Advance(delay);
                return Task.CompletedTask;
            }, clock);
            // defaults: backoff 10 s up to 60 s, give up after 5 minutes
            Assert.Equal(TimeSpan.FromSeconds(10), supervisor.MinBackoff);
            Assert.Equal(TimeSpan.FromSeconds(60), supervisor.MaxBackoff);
            Assert.Equal(TimeSpan.FromMinutes(5), supervisor.ReconnectTimeout);

            using var cts = new CancellationTokenSource();
            Exception? error = null;
            var output = await CaptureConsoleAsync(async () =>
            {
                error = await Record.ExceptionAsync(() => supervisor.RunAsync("test-password", cts.Token).WaitAsync(TestTimeout));
            });

            // 10 + 20 + 40 + 60 + 60 + 60 + 50 = 300 s, the last delay is cut so the final attempt is made at the limit
            Assert.Equal(new[] { 10, 20, 40, 60, 60, 60, 50 }, delays.Select(x => (int)x.TotalSeconds));
            Assert.All(delays, x => Assert.True(x <= TimeSpan.FromSeconds(60)));
            Assert.Equal(TimeSpan.FromSeconds(300), delays.Aggregate(TimeSpan.Zero, (sum, x) => sum + x));
            Assert.Equal(delays.Count + 1, server.ConnectionCount);
            var timeout = Assert.IsType<TimeoutException>(error);
            Assert.NotNull(timeout.InnerException);
            Assert.Contains("within 300 seconds", timeout.Message);
            Assert.Contains("ism7 connection failed: ", timeout.Message);
            // the wait is visible in the log
            Assert.Contains("- reconnecting in 10 seconds (attempt 1, giving up after 300 seconds without a connection)", output);
            Assert.Contains("- reconnecting in 50 seconds (attempt 7, ", output);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task FlappingConnection_DoesNotResetTheReconnectTimeout()
    {
        // the ism7 accepts the connection and drops it again shortly after the initialization, again and again.
        // That is a real problem and must end the process instead of reconnecting forever.
        await using var server = new FakeIsm7Server(request => Ism7ClientStartupTests.RespondFromTable(request, Ism7ClientStartupTests.HappyPathTelegrams));
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var clock = new ManualTimeProvider();
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath, () =>
            {
                // up for 100 s - less than StableConnectionTime
                clock.Advance(TimeSpan.FromSeconds(100));
                server.DropConnection();
                return Task.CompletedTask;
            }), (delay, _) =>
            {
                delays.Add(delay);
                clock.Advance(delay);
                return Task.CompletedTask;
            }, clock);

            using var cts = new CancellationTokenSource();
            Exception? error = null;
            await CaptureConsoleAsync(async () =>
            {
                error = await Record.ExceptionAsync(() => supervisor.RunAsync("test-password", cts.Token).WaitAsync(TestTimeout));
            });

            Assert.IsType<TimeoutException>(error);
            // the backoff keeps growing as well
            Assert.Equal(new[] { 10, 20, 40 }, delays.Select(x => (int)x.TotalSeconds));
            Assert.Equal(4, server.ConnectionCount);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task StableConnection_StartsANewOutageForTheReconnectTimeout()
    {
        // 10 minutes up, then the ism7 disappears for a while: the timeout counts from the loss of the stable connection,
        // not from the start of ism7mqtt
        await using var server = new FakeIsm7Server(request => Ism7ClientStartupTests.RespondFromTable(request, Ism7ClientStartupTests.HappyPathTelegrams));
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var clock = new ManualTimeProvider();
            var initializations = 0;
            var secondInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath, () =>
            {
                if (Interlocked.Increment(ref initializations) == 1)
                {
                    clock.Advance(TimeSpan.FromMinutes(10));
                    server.DropConnection();
                }
                else
                {
                    secondInitialization.TrySetResult();
                }
                return Task.CompletedTask;
            }), (delay, _) =>
            {
                delays.Add(delay);
                clock.Advance(delay);
                return Task.CompletedTask;
            }, clock);

            using var cts = new CancellationTokenSource();
            Task runTask = null!;
            await CaptureConsoleAsync(async () =>
            {
                runTask = supervisor.RunAsync("test-password", cts.Token);
                Assert.Same(secondInitialization.Task, await Task.WhenAny(secondInitialization.Task, Task.Delay(TestTimeout)));
                cts.Cancel();
                Assert.Same(runTask, await Task.WhenAny(runTask, Task.Delay(TestTimeout)));
            });

            Assert.True(runTask.IsCompletedSuccessfully);
            Assert.Equal(new[] { supervisor.MinBackoff }, delays);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task EveryConnectionStartsItsBundleAndSequenceIdsAgain()
    {
        // ids are only correlation ids within one session. A reconnect is a new session and has to look like a fresh start of
        // ism7mqtt, so nothing grows when the connection is lost again and again.
        var firstBundleIdPerConnection = new Dictionary<int, string>();
        FakeIsm7Server server = null!;
        server = new FakeIsm7Server(request =>
        {
            lock (firstBundleIdPerConnection)
            {
                firstBundleIdPerConnection.TryAdd(server.ConnectionCount, request.BundleId);
            }
            return Ism7ClientStartupTests.RespondFromTable(request, Ism7ClientStartupTests.HappyPathTelegrams);
        });
        await using var disposeServer = server;
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var initializations = 0;
            var thirdInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath, () =>
            {
                if (Interlocked.Increment(ref initializations) == 3)
                    thirdInitialization.TrySetResult();
                else
                    server.DropConnection();
                return Task.CompletedTask;
            }), (_, _) => Task.CompletedTask);

            using var cts = new CancellationTokenSource();
            Task runTask = null!;
            await CaptureConsoleAsync(async () =>
            {
                runTask = supervisor.RunAsync("test-password", cts.Token);
                Assert.Same(thirdInitialization.Task, await Task.WhenAny(thirdInitialization.Task, Task.Delay(TestTimeout)));
                cts.Cancel();
                Assert.Same(runTask, await Task.WhenAny(runTask, Task.Delay(TestTimeout)));
            });

            Assert.Equal(3, firstBundleIdPerConnection.Count);
            Assert.All(firstBundleIdPerConnection.Values, x => Assert.Equal(firstBundleIdPerConnection.Values.First(), x));
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public void IdGenerator_EveryInstanceStartsAtTheBeginning()
    {
        var first = new ism7mqtt.ISM7.IdGenerator();
        Assert.Equal(1, first.GetNextBundleId());
        Assert.Equal(2, first.GetNextBundleId());
        Assert.Equal(2, first.GetNextSequenceId());

        var second = new ism7mqtt.ISM7.IdGenerator();
        Assert.Equal("1", second.GetNextBundleIdString());
        Assert.Equal(2, second.GetNextSequenceId());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public void Advance(TimeSpan time) => Interlocked.Add(ref _ticks, time.Ticks);
    }

    [Fact]
    public async Task StableConnection_ResetsBackoffToMinimum()
    {
        await using var server = new FakeIsm7Server(request => Ism7ClientStartupTests.RespondFromTable(request, Ism7ClientStartupTests.HappyPathTelegrams));
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            using var cts = new CancellationTokenSource();
            var initializations = 0;
            var thirdInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var delays = new List<TimeSpan>();
            // the device drops every connection right after the initialization
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath, () =>
            {
                if (Interlocked.Increment(ref initializations) == 3)
                {
                    thirdInitialization.TrySetResult();
                }
                else
                {
                    server.DropConnection();
                }
                return Task.CompletedTask;
            }), (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            })
            {
                // every connection counts as stable
                StableConnectionTime = TimeSpan.Zero,
            };

            Task runTask = null!;
            await CaptureConsoleAsync(async () =>
            {
                runTask = supervisor.RunAsync("test-password", cts.Token);
                Assert.Same(thirdInitialization.Task, await Task.WhenAny(thirdInitialization.Task, Task.Delay(TestTimeout)));
                cts.Cancel();
                Assert.Same(runTask, await Task.WhenAny(runTask, Task.Delay(TestTimeout)));
            });

            Assert.True(runTask.IsCompletedSuccessfully);
            Assert.Equal(new[] { supervisor.MinBackoff, supervisor.MinBackoff }, delays);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task KeepAliveTimeout_IsLoggedAndReconnects()
    {
        // the fake server never answers keep-alives, like an ism7 that silently disappeared (half-open connection)
        await using var server = new FakeIsm7Server(request => Ism7ClientStartupTests.RespondFromTable(request, Ism7ClientStartupTests.HappyPathTelegrams));
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            var clientCount = 0;
            var secondInitialization = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() =>
            {
                // only the first connection uses a short keep-alive interval, so the second one stays up
                if (Interlocked.Increment(ref clientCount) == 1)
                    return CreateClient(parameterPath, keepAliveInterval: TimeSpan.FromSeconds(1));
                return CreateClient(parameterPath, () =>
                {
                    secondInitialization.TrySetResult();
                    return Task.CompletedTask;
                });
            }, (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

            using var cts = new CancellationTokenSource();
            Task runTask = null!;
            var output = await CaptureConsoleAsync(async () =>
            {
                runTask = supervisor.RunAsync("test-password", cts.Token);
                Assert.Same(secondInitialization.Task, await Task.WhenAny(secondInitialization.Task, Task.Delay(TestTimeout)));
                cts.Cancel();
                Assert.Same(runTask, await Task.WhenAny(runTask, Task.Delay(TestTimeout)));
            });

            Assert.True(runTask.IsCompletedSuccessfully);
            Assert.Equal(2, server.ConnectionCount);
            Assert.Equal(new[] { supervisor.MinBackoff }, delays);
            Assert.Contains("ism7 connection failed: TimeoutException: No keepalive response within the last 1 seconds - reconnecting in 10 seconds", output);
        }
        finally
        {
            File.Delete(parameterPath);
        }
    }

    [Fact]
    public async Task HandshakeNeverAnswered_TimesOutClosesSocketAndRetries()
    {
        var (output, serverSawClose) = await RunAgainstRawServerAsync(_ => Task.CompletedTask);

        Assert.Contains("ism7 connection failed: TimeoutException: Timeout while connecting to the ism7. - reconnecting in 10 seconds", output);
        Assert.True(serverSawClose);
    }

    [Fact]
    public async Task HandshakeAnsweredWithGarbage_IsRetriedWithoutUnhandledException()
    {
        // not a TLS ServerHello, so the TLS client fails - like the TLS alerts seen while the ism7 still holds the previous session
        var (output, serverSawClose) = await RunAgainstRawServerAsync(stream => stream.WriteAsync("HTTP/1.1 400 Bad Request\r\n\r\n"u8.ToArray()).AsTask());

        Assert.Contains("ism7 connection failed: ", output);
        Assert.Contains("- reconnecting in 10 seconds", output);
        Assert.DoesNotContain("TimeoutException", output);
        Assert.True(serverSawClose);
    }

    /// <summary>
    /// Runs a single connection attempt against a plain TCP listener that accepts the connection but never completes
    /// the TLS handshake. Returns the console output and whether the client closed the socket afterwards.
    /// </summary>
    private static async Task<(string Output, bool ServerSawClose)> RunAgainstRawServerAsync(Func<NetworkStream, Task> serve)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", ((IPEndPoint)listener.LocalEndpoint).Port);
        try
        {
            var acceptTask = listener.AcceptTcpClientAsync();
            using var cts = new CancellationTokenSource();
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath, startupTimeout: 1), (delay, _) =>
            {
                // stop after the first failed attempt
                delays.Add(delay);
                cts.Cancel();
                return Task.CompletedTask;
            });

            Task runTask = null!;
            var output = await CaptureConsoleAsync(async () =>
            {
                runTask = supervisor.RunAsync("test-password", cts.Token);
                var accepted = await acceptTask.WaitAsync(TestTimeout);
                await serve(accepted.GetStream());
                Assert.Same(runTask, await Task.WhenAny(runTask, Task.Delay(TestTimeout)));
            });

            Assert.True(runTask.IsCompletedSuccessfully);
            Assert.Equal(new[] { supervisor.MinBackoff }, delays);
            using var connection = await acceptTask;
            return (output, await ClientClosedAsync(connection));
        }
        finally
        {
            listener.Stop();
            File.Delete(parameterPath);
        }
    }

    private static async Task<bool> ClientClosedAsync(TcpClient connection)
    {
        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TestTimeout);
        try
        {
            // skip whatever the client sent (e.g. the ClientHello) until the end of the stream
            while (await connection.GetStream().ReadAsync(buffer, timeout.Token) > 0)
            {
            }
            return true;
        }
        catch (IOException)
        {
            // connection reset by the client - closed as well
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
