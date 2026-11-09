using System;
using System.Threading;
using System.Threading.Tasks;

namespace ism7mqtt
{
    /// <summary>
    /// Keeps the connection to the ISM7 alive: runs a fresh <see cref="Ism7Client"/> per connection and
    /// reconnects with an increasing delay whenever the connection is closed or cannot be established,
    /// until cancellation is requested or <see cref="ReconnectTimeout"/> is exceeded.
    /// </summary>
    public class Ism7Supervisor
    {
        private readonly Func<Ism7Client> _clientFactory;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly TimeProvider _timeProvider;
        private volatile Ism7Client _current;

        public Ism7Supervisor(Func<Ism7Client> clientFactory, Func<TimeSpan, CancellationToken, Task> delay = null, TimeProvider timeProvider = null)
        {
            _clientFactory = clientFactory;
            _delay = delay ?? Task.Delay;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>
        /// Delay before the first reconnect attempt. Doubles after every failed attempt up to <see cref="MaxBackoff"/>.
        /// </summary>
        public TimeSpan MinBackoff { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Longest delay between two reconnect attempts, so a restarted ISM7 module is picked up within this time.
        /// </summary>
        public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(60);

        /// <summary>
        /// How long to keep reconnecting after the connection was lost (or could not be established at startup).
        /// Once it is exceeded, the error that ended the last connection is raised, like a client that is not
        /// supervised. <see cref="TimeSpan.Zero"/> disables reconnecting: the first error (or close) ends
        /// <see cref="RunAsync"/>. The limit is checked between attempts, so an attempt that is already running
        /// (see <see cref="Ism7Client.StartupTimeout"/>) is not interrupted by it.
        /// </summary>
        public TimeSpan ReconnectTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// A connection that stayed up at least this long after finishing its initialization ends the outage:
        /// the delay starts at <see cref="MinBackoff"/> again and <see cref="ReconnectTimeout"/> is measured
        /// from the next loss. A connection that drops sooner continues the outage, so a flapping connection
        /// does not reconnect forever.
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
            long? outageStart = null;
            var attempts = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                // the client keeps per-connection state (pipe, parsed config, ids, subscriptions) and cannot be reused.
                // A failure here (e.g. an invalid parameter.json) is not transient, so it is not retried.
                var client = _clientFactory();
                // a connection only counts as stable once it is usable, not while connecting or loading the initial values
                long initializedAt = 0;
                var onInitializationFinished = client.OnInitializationFinishedAsync;
                client.OnInitializationFinishedAsync = (config, token) =>
                {
                    initializedAt = _timeProvider.GetTimestamp();
                    return onInitializationFinished?.Invoke(config, token) ?? Task.CompletedTask;
                };
                Exception failure = null;
                _current = client;
                try
                {
                    await client.RunAsync(password, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Ism7LoginException)
                {
                    // a refused login (e.g. a wrong password) is not transient - and repeated attempts might lock the
                    // client out, so fail right away regardless of the reconnect timeout
                    throw;
                }
                catch (Exception ex) when (ReconnectTimeout > TimeSpan.Zero)
                {
                    // e.g. "Socket not connected" or a TLS alert while the ism7 still holds the previous session or a
                    // startup/keep-alive timeout - errors during the session were also logged by the client
                    failure = ex;
                }
                finally
                {
                    _current = null;
                }
                if (cancellationToken.IsCancellationRequested)
                    return;
                // without a reconnect timeout an error is thrown above and a closed connection ends the process
                if (ReconnectTimeout <= TimeSpan.Zero)
                    return;

                var reason = failure is null
                    ? "ism7 connection closed"
                    : $"ism7 connection failed: {(EnableDebug ? failure.ToString() : $"{failure.GetType().Name}: {failure.Message}")}";
                var now = _timeProvider.GetTimestamp();
                if (initializedAt != 0 && _timeProvider.GetElapsedTime(initializedAt, now) >= StableConnectionTime)
                {
                    // the connection was fine for a while, this is a new outage
                    backoff = MinBackoff;
                    outageStart = null;
                    attempts = 0;
                }
                outageStart ??= now;
                var outage = _timeProvider.GetElapsedTime(outageStart.Value, now);
                if (outage >= ReconnectTimeout)
                {
                    throw new TimeoutException($"The connection to the ism7 could not be re-established within {(int)ReconnectTimeout.TotalSeconds} seconds ({attempts} reconnect attempts). Last problem: {reason}", failure);
                }

                // the last attempt is made when the timeout is reached
                var wait = TimeSpan.FromTicks(Math.Min(backoff.Ticks, (ReconnectTimeout - outage).Ticks));
                attempts++;
                Console.Error.WriteLine($"{reason} - reconnecting in {(int)Math.Ceiling(wait.TotalSeconds)} seconds (attempt {attempts}, giving up after {(int)ReconnectTimeout.TotalSeconds} seconds without a connection)");
                try
                {
                    await _delay(wait, cancellationToken);
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
