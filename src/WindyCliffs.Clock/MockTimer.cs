namespace WindyCliffs.Clock
{
    using System;
    using System.Threading;

    /// <summary>
    /// A timer driven by a <see cref="MockClock"/> rather than wall-clock time. Like
    /// <see cref="ScheduledAction"/> it subscribes to <see cref="MockClock.Changed"/>, but instead of
    /// firing once and disposing itself it re-arms after each invocation so it can mimic a periodic
    /// <see cref="System.Threading.Timer"/>. Disposing it unsubscribes and stops further callbacks.
    /// </summary>
    /// <remarks>
    /// Unlike a real <see cref="System.Threading.Timer"/>, the callback runs synchronously on the
    /// thread advancing the clock (or the constructing thread, for an already-due timer); when the
    /// clock advances past several intervals at once the callback fires once per elapsed interval; and
    /// an exception thrown by the callback propagates to the advancing caller.
    /// </remarks>
    internal sealed class MockTimer : IDisposable
    {
        // The largest timeout System.Threading.Timer accepts, in milliseconds (0xFFFFFFFE). This is a
        // fixed constant across .NET Framework and modern .NET, so MockClock and SystemClock reject the
        // same out-of-range values on every target runtime.
        private const long MaxSupportedTimeoutMilliseconds = 0xFFFFFFFE;

        private readonly MockClock clock;

        private readonly object? state;

        private readonly TimerCallback callback;

        private readonly bool isPeriodic;

        private readonly TimeSpan period;

        private readonly object syncRoot = new object();

        // Read and written only under syncRoot, so the lock already provides the necessary memory
        // barrier — unlike ScheduledAction, there is no pre-lock fast-path read that would need volatile.
        private bool isDisposed = false;

        // The next instant at or after which the callback fires, or null when the timer will never fire
        // again (an infinite due time at construction, or a one-shot timer that has already fired).
        private DateTimeOffset? nextDueTime;

        public MockTimer(MockClock clock, object? state, TimeSpan dueTime, TimeSpan interval, TimerCallback callback)
        {
            // Range-validate exactly as the System.Threading.Timer constructor does: it converts each
            // TimeSpan to whole milliseconds and requires the result in [-1, MaxSupportedTimeout],
            // checking the ranges before the null-callback check. Milliseconds are needed only here and
            // for the infinite/periodic classification just below, because Timer quantises both to whole
            // milliseconds; everything else works in TimeSpan.
            long dueMilliseconds = (long)dueTime.TotalMilliseconds;
            long intervalMilliseconds = (long)interval.TotalMilliseconds;

            if (dueMilliseconds < -1 || dueMilliseconds > MaxSupportedTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(nameof(dueTime));
            }

            if (intervalMilliseconds < -1 || intervalMilliseconds > MaxSupportedTimeoutMilliseconds)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            if (callback is null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            this.clock = clock;
            this.state = state;
            this.callback = callback;

            // A positive interval repeats; both a zero and an infinite interval make the timer one-shot,
            // matching System.Threading.Timer. period is left unused when !isPeriodic.
            this.isPeriodic = intervalMilliseconds > 0;
            this.period = interval;

            // An infinite due time (-1 ms after truncation) never fires: store no target rather than
            // offsetting UtcNow, which would otherwise move the deadline into the past and fire immediately.
            this.nextDueTime = dueMilliseconds < 0
                ? (DateTimeOffset?)null
                : this.clock.UtcNow + dueTime;

            // A timer with no due time can never fire, so there is nothing to subscribe to; it can only be
            // disposed. Otherwise subscribe and check the current time so an already-due timer (e.g. a
            // zero due time) fires synchronously, matching ScheduledAction's construction-time check.
            if (this.nextDueTime.HasValue)
            {
                this.clock.Changed += this.ClockChanged;
                this.ClockChanged(this.clock, this.clock.UtcNow);
            }
        }

        public void Dispose()
        {
            lock (this.syncRoot)
            {
                this.isDisposed = true;
                this.clock.Changed -= this.ClockChanged;
            }
        }

        private void ClockChanged(MockClock clock, DateTimeOffset utcNow)
        {
            while (true)
            {
                DateTimeOffset due;

                lock (this.syncRoot)
                {
                    if (this.isDisposed || !this.nextDueTime.HasValue)
                    {
                        return;
                    }

                    due = this.nextDueTime.Value;

                    if (utcNow < due)
                    {
                        return;
                    }

                    // Claim this tick under the lock by advancing the schedule before releasing it, so a
                    // concurrent advance (or a re-entrant one raised by the callback) cannot re-fire the
                    // same tick. A one-shot timer becomes inert (null) after its single fire.
                    this.nextDueTime = this.isPeriodic ? due + this.period : (DateTimeOffset?)null;

                    // Once the timer can never fire again, stop listening so it does not linger on the
                    // clock's Changed list for the remainder of the clock's life.
                    if (!this.nextDueTime.HasValue)
                    {
                        this.clock.Changed -= this.ClockChanged;
                    }
                }

                // Invoke outside the lock: the callback is arbitrary user code that may advance the
                // clock (re-entering this handler) or dispose the timer, neither of which must contend
                // for syncRoot while it is held.
                this.callback(this.state);
            }
        }
    }
}
