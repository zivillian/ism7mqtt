using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ism7mqtt
{
    /// <summary>
    /// Keeps the connection to the ISM7 alive: runs a fresh <see cref="Ism7Client"/> per connection and
    /// reconnects with an increasing delay whenever the connection is closed or cannot be established,
    /// until cancellation is requested.
    /// </summary>
    public class Ism7Supervisor
    {
        private readonly Func<Ism7Client> _clientFactory;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private volatile Ism7Client _current;

        public Ism7Supervisor(Func<Ism7Client> clientFactory, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            _clientFactory = clientFactory;
            _delay = delay ?? Task.Delay;
        }

        /// <summary>
        /// Delay before the first reconnect attempt. Doubles after every failed attempt up to <see cref="MaxBackoff"/>.
        /// </summary>
        public TimeSpan MinBackoff { get; set; } = TimeSpan.FromSeconds(10);

        public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// A connection that stayed up at least this long after finishing its initialization resets the delay to <see cref="MinBackoff"/>.
        /// </summary>
        public TimeSpan StableConnectionTime { get; set; } = TimeSpan.FromMinutes(5);

        public bool EnableDebug { get; set; }

        /// <summary>
        /// The client of the current connection attempt, or null while waiting to reconnect.
        /// </summary>
        public Ism7Client Current => _current;

        public async Task RunAsync(string password, CancellationToken cancellationToken)
        {
            var backoff = MinBackoff;
            while (!cancellationToken.IsCancellationRequested)
            {
                // the client keeps per-connection state (pipe, parsed config, subscriptions) and cannot be reused.
                // A failure here (e.g. an invalid parameter.json) is not transient, so it is not retried.
                var client = _clientFactory();
                // a connection only counts as stable once it is usable, not while connecting or loading the initial values
                long initializedAt = 0;
                var onInitializationFinished = client.OnInitializationFinishedAsync;
                client.OnInitializationFinishedAsync = (config, token) =>
                {
                    initializedAt = Stopwatch.GetTimestamp();
                    return onInitializationFinished?.Invoke(config, token) ?? Task.CompletedTask;
                };
                string reason;
                _current = client;
                try
                {
                    await client.RunAsync(password, cancellationToken);
                    // the ism7 closed the connection without an error
                    reason = "ism7 connection closed";
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // e.g. "Socket not connected" or a TLS alert while the ism7 still holds the previous session, a refused
                    // login or a startup/keep-alive timeout - errors during the session were also logged by the client
                    reason = $"ism7 connection failed: {(EnableDebug ? ex.ToString() : $"{ex.GetType().Name}: {ex.Message}")}";
                }
                finally
                {
                    _current = null;
                }
                if (cancellationToken.IsCancellationRequested)
                    return;

                if (initializedAt != 0 && Stopwatch.GetElapsedTime(initializedAt) >= StableConnectionTime)
                    backoff = MinBackoff;
                Console.Error.WriteLine($"{reason} - reconnecting in {backoff.TotalSeconds} seconds");
                try
                {
                    await _delay(backoff, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }
}
