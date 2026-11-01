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
    public async Task LoginRefused_IsRetriedWithDoublingBackoffUpToMaximum()
    {
        await using var server = new FakeIsm7Server(_ => new List<InfonumberReadResp>(), loginState: LoginState.invalidCredentials);
        var parameterPath = TestFixtures.WriteParameterFile("happy-path.json", server.Port);
        try
        {
            using var cts = new CancellationTokenSource();
            var delays = new List<TimeSpan>();
            var supervisor = new Ism7Supervisor(() => CreateClient(parameterPath), (delay, _) =>
            {
                delays.Add(delay);
                if (delays.Count == 5)
                {
                    cts.Cancel();
                }
                return Task.CompletedTask;
            })
            {
                MinBackoff = TimeSpan.FromSeconds(10),
                MaxBackoff = TimeSpan.FromSeconds(40),
                // a connection that never finished its initialization is never stable
                StableConnectionTime = TimeSpan.Zero,
            };

            Task runTask = null!;
            var output = await CaptureConsoleAsync(async () =>
            {
                runTask = supervisor.RunAsync("test-password", cts.Token);
                Assert.Same(runTask, await Task.WhenAny(runTask, Task.Delay(TestTimeout)));
            });

            Assert.True(runTask.IsCompletedSuccessfully);
            Assert.Equal(new[] { 10, 20, 40, 40, 40 }, delays.Select(x => (int)x.TotalSeconds));
            Assert.Equal(5, server.ConnectionCount);
            Assert.Contains("ism7 connection failed: InvalidDataException: invalid login state 'invalidCredentials' - reconnecting in 10 seconds", output);
        }
        finally
        {
            File.Delete(parameterPath);
        }
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
