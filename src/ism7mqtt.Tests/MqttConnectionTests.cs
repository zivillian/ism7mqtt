using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Server;
using Xunit;

namespace ism7mqtt.Tests;

public class MqttConnectionTests
{
    private const string SetTopic = "Wolf/127.0.0.1/test/set";

    [Fact]
    public async Task Resubscribes_after_the_broker_was_restarted()
    {
        // what happens when the broker container gets updated: it disappears, a few reconnects
        // fail, then a NEW broker comes up that knows nothing about earlier subscriptions
        var port = GetFreePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var factory = new MqttFactory();

        var broker = await StartBrokerAsync(factory, port);
        using var mqttClient = factory.CreateMqttClient();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        mqttClient.ApplicationMessageReceivedAsync += e =>
        {
            received.TrySetResult(e.ApplicationMessage.ConvertPayloadToString());
            return Task.CompletedTask;
        };
        var connects = 0;
        var connection = new MqttConnection(mqttClient, ClientOptions(port))
        {
            CheckInterval = TimeSpan.FromMilliseconds(200),
            OnConnectedAsync = async token =>
            {
                Interlocked.Increment(ref connects);
                await mqttClient.SubscribeAsync(SetTopic, cancellationToken: token);
            }
        };
        await connection.ConnectAsync(cts.Token);
        var keepConnected = connection.KeepConnectedAsync(cts.Token);

        await broker.StopAsync();
        broker.Dispose();
        await WaitForAsync(() => !mqttClient.IsConnected, cts.Token);
        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token); // several failed reconnects

        broker = await StartBrokerAsync(factory, port);
        await WaitForAsync(() => Volatile.Read(ref connects) == 2 && mqttClient.IsConnected, cts.Token);

        await PublishAsync(factory, port, SetTopic, "42", cts.Token);
        var payload = await received.Task.WaitAsync(TimeSpan.FromSeconds(10), cts.Token);
        Assert.Equal("42", payload);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => keepConnected);
        await broker.StopAsync();
        broker.Dispose();
    }

    [Fact]
    public async Task Failing_restore_drops_the_connection_and_retries()
    {
        // a connection without its subscriptions looks healthy but is deaf - it must not be kept
        var port = GetFreePort();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var factory = new MqttFactory();
        var broker = await StartBrokerAsync(factory, port);
        using var mqttClient = factory.CreateMqttClient();
        var attempts = 0;
        var connection = new MqttConnection(mqttClient, ClientOptions(port))
        {
            CheckInterval = TimeSpan.FromMilliseconds(200),
            OnConnectedAsync = token =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    throw new InvalidOperationException("subscribe failed");
                return Task.CompletedTask;
            }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.ConnectAsync(cts.Token));
        Assert.False(mqttClient.IsConnected);

        var keepConnected = connection.KeepConnectedAsync(cts.Token);
        await WaitForAsync(() => Volatile.Read(ref attempts) == 2 && mqttClient.IsConnected, cts.Token);

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => keepConnected);
        await broker.StopAsync();
        broker.Dispose();
    }

    [Fact]
    public async Task Writes_the_health_file_while_connected()
    {
        var port = GetFreePort();
        var healthFile = Path.Combine(Path.GetTempPath(), $"ism7mqtt-health-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var factory = new MqttFactory();
        var broker = await StartBrokerAsync(factory, port);
        using var mqttClient = factory.CreateMqttClient();
        var connection = new MqttConnection(mqttClient, ClientOptions(port)) { HealthFile = healthFile };
        try
        {
            Assert.False(File.Exists(healthFile));
            await connection.ConnectAsync(cts.Token);
            Assert.True(File.Exists(healthFile));
            Assert.True(DateTime.UtcNow - File.GetLastWriteTimeUtc(healthFile) < TimeSpan.FromSeconds(10));
        }
        finally
        {
            File.Delete(healthFile);
            await broker.StopAsync();
            broker.Dispose();
        }
    }

    private static MqttClientOptions ClientOptions(int port) => new MqttClientOptionsBuilder()
        .WithTcpServer("127.0.0.1", port)
        .WithClientId("Wolf_test")
        .WithTimeout(TimeSpan.FromSeconds(2))
        .Build();

    private static async Task<MqttServer> StartBrokerAsync(MqttFactory factory, int port)
    {
        var options = factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint()
            .WithDefaultEndpointBoundIPAddress(IPAddress.Loopback)
            .WithDefaultEndpointPort(port)
            .Build();
        options.DefaultEndpointOptions.ReuseAddress = true;
        var server = factory.CreateMqttServer(options);
        await server.StartAsync();
        return server;
    }

    private static async Task PublishAsync(MqttFactory factory, int port, string topic, string payload, CancellationToken cancellationToken)
    {
        using var sender = factory.CreateMqttClient();
        await sender.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", port).Build(), cancellationToken);
        await sender.PublishStringAsync(topic, payload, cancellationToken: cancellationToken);
        await sender.DisconnectAsync(cancellationToken: cancellationToken);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(50, cancellationToken);
        }
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
