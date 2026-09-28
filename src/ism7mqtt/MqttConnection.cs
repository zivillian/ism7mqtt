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
        private static readonly TimeSpan HealthFileInterval = TimeSpan.FromSeconds(30);

        private readonly IMqttClient _client;
        private readonly MqttClientOptions _options;
        private DateTime _lastHealthFileWrite = DateTime.MinValue;
        private bool _healthFileErrorLogged;

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
        /// Optional file that is touched while the broker is reachable, e.g. for a Docker HEALTHCHECK.
        /// </summary>
        public string HealthFile { get; set; }

        public bool EnableDebug { get; set; }

        public async Task ConnectAsync(CancellationToken cancellationToken)
        {
            await _client.ConnectAsync(_options, cancellationToken);
            await RestoreSessionAsync(cancellationToken);
            TouchHealthFile(force: true);
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
                    if (await _client.TryPingAsync(cancellationToken))
                    {
                        TouchHealthFile(force: false);
                        continue;
                    }
                    if (connected)
                    {
                        Console.Error.WriteLine($"mqtt disconnected - reconnecting every {CheckInterval.TotalSeconds:0} seconds");
                        connected = false;
                    }
                    await _client.ConnectAsync(_options, cancellationToken);
                    await RestoreSessionAsync(cancellationToken);
                    Console.WriteLine("mqtt reconnected");
                    connected = true;
                    failureLogged = false;
                    TouchHealthFile(force: true);
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

        private void TouchHealthFile(bool force)
        {
            if (String.IsNullOrEmpty(HealthFile)) return;
            var now = DateTime.UtcNow;
            if (!force && now - _lastHealthFileWrite < HealthFileInterval) return;
            try
            {
                File.WriteAllText(HealthFile, now.ToString("O"));
                _lastHealthFileWrite = now;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                if (_healthFileErrorLogged) return;
                Console.Error.WriteLine($"could not write health file '{HealthFile}': {ex.Message}");
                _healthFileErrorLogged = true;
            }
        }
    }
}
