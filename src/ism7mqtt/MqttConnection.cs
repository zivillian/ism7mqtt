using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet.Client;

namespace ism7mqtt
{
    /// <summary>
    /// Keeps the connection to the MQTT broker alive and restores its state after every (re)connect.
    /// </summary>
    /// <remarks>
    /// The client connects with a clean session, so the broker forgets all subscriptions whenever
    /// the connection drops. <see cref="OnConnectedAsync"/> therefore runs after EVERY successful
    /// connect, not only after the first one. Without that, write commands (<c>.../set</c>) and the
    /// Home Assistant status topic silently stop working after the first reconnect (#77, #164).
    ///
    /// Reconnecting is done by a loop that checks the connection every few seconds, as recommended
    /// by MQTTnet, instead of a single attempt from the <c>DisconnectedAsync</c> event.
    /// </remarks>
    public class MqttConnection
    {
        private static readonly TimeSpan MqttHealthFileInterval = TimeSpan.FromSeconds(30);

        private readonly IMqttClient _client;
        private readonly MqttClientOptions _options;
        private DateTime _lastMqttHealthFileWrite = DateTime.MinValue;
        private bool _mqttHealthFileErrorLogged;

        public MqttConnection(IMqttClient client, MqttClientOptions options)
        {
            _client = client;
            _options = options;
        }

        /// <summary>
        /// Runs after every successful connect: subscribe to topics, re-publish discovery info, ...
        /// </summary>
        public Func<CancellationToken, Task> OnConnectedAsync { get; set; }

        /// <summary>
        /// How often the connection is checked (and, if lost, how often a reconnect is attempted).
        /// </summary>
        public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Upper time limit for a single ping; a ping that takes longer counts as failed.
        /// </summary>
        public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Optional file that is touched while the connection to the broker is up, e.g. for a Docker
        /// HEALTHCHECK. It reflects only the MQTT side, not the connection to the ISM7.
        /// </summary>
        public string MqttHealthFile { get; set; }

        public bool EnableDebug { get; set; }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            await _client.ConnectAsync(_options, cancellationToken);
            await RestoreSessionAsync(cancellationToken);
            TouchMqttHealthFile(force: true);
        }

        public async Task KeepConnectedAsync(CancellationToken cancellationToken)
        {
            var connected = true;
            var failureLogged = false;
            while (true)
            {
                await Task.Delay(CheckInterval, cancellationToken);
                try
                {
                    if (await PingAsync(cancellationToken))
                    {
                        TouchMqttHealthFile(force: false);
                        continue;
                    }
                    if (connected)
                    {
                        Console.Error.WriteLine($"mqtt disconnected - reconnecting every {CheckInterval.TotalSeconds:0} seconds");
                        connected = false;
                    }
                    // A ping can fail while the client still believes it is connected (half-open TCP
                    // connection). ConnectAsync would then be rejected, so drop the old connection first.
                    await DisconnectQuietlyAsync();
                    await _client.ConnectAsync(_options, cancellationToken);
                    await RestoreSessionAsync(cancellationToken);
                    Console.WriteLine("mqtt reconnected");
                    connected = true;
                    failureLogged = false;
                    TouchMqttHealthFile(force: true);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (!failureLogged || EnableDebug)
                    {
                        Console.Error.WriteLine($"mqtt reconnect failed, still retrying: {ex.Message}");
                        failureLogged = true;
                    }
                }
            }
        }

        /// <summary>
        /// Ping with an upper time limit, so a broker that accepts the TCP connection but no longer
        /// answers does not block the check loop. A timed out ping counts as a failed ping.
        /// </summary>
        private async Task<bool> PingAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(PingTimeout);
            try
            {
                return await _client.TryPingAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        private async Task DisconnectQuietlyAsync()
        {
            if (!_client.IsConnected) return;
            try
            {
                await _client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None);
            }
            catch
            {
                // already gone
            }
        }

        private async Task RestoreSessionAsync(CancellationToken cancellationToken)
        {
            if (OnConnectedAsync is null) return;
            try
            {
                await OnConnectedAsync(cancellationToken);
            }
            catch
            {
                // A connection without its subscriptions looks healthy but is deaf. Drop it, so the
                // next round connects again and retries the subscriptions.
                try
                {
                    await _client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None);
                }
                catch
                {
                    // already gone
                }
                throw;
            }
        }

        private void TouchMqttHealthFile(bool force)
        {
            if (String.IsNullOrEmpty(MqttHealthFile)) return;
            var now = DateTime.UtcNow;
            if (!force && now - _lastMqttHealthFileWrite < MqttHealthFileInterval) return;
            try
            {
                File.WriteAllText(MqttHealthFile, now.ToString("O"));
                _lastMqttHealthFileWrite = now;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (_mqttHealthFileErrorLogged) return;
                Console.Error.WriteLine($"could not write mqtt health file '{MqttHealthFile}': {ex.Message}");
                _mqttHealthFileErrorLogged = true;
            }
        }
    }
}
