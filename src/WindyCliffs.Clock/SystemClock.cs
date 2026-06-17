namespace WindyCliffs.Clock
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The implementation of <see cref="IClock"/> bound to the operating system clock.
    /// </summary>
    public class SystemClock : IClock
    {
        /// <summary>
        /// The singleton instance of <see cref="SystemClock"/>.
        /// </summary>
        public static readonly SystemClock Instance = new SystemClock();

        private SystemClock() { }

        /// <inheritdoc />
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        /// <inheritdoc />
        public void Sleep(TimeSpan timeout) => Thread.Sleep(timeout);

        /// <inheritdoc />
        public Task TaskDelay(TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.Delay(timeout, cancellationToken);

        /// <inheritdoc />
        public void CancelAfter(CancellationTokenSource source, TimeSpan timeout)
        {
            // Enforce the IClock contract's ArgumentNullException; a bare delegate on a null receiver
            // would throw NullReferenceException instead.
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            source.CancelAfter(timeout);
        }

        /// <inheritdoc />
        public IDisposable StartTimer(object? state, TimeSpan dueTime, TimeSpan interval, TimerCallback callback)
            // IClock orders the arguments (state, dueTime, interval, callback) for readability; the
            // Timer constructor orders them (callback, state, dueTime, period). The constructor performs
            // the null-callback and range validation the IClock contract specifies, and Timer itself is
            // the returned IDisposable (Dispose stops it — no Change-then-dispose dance is needed).
            => new Timer(callback, state, dueTime, interval);
    }
}
