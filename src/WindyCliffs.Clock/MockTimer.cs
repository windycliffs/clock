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
    /// an exception thrown by the callback propagates to the advancing caller. The owner
    /// (<see cref="MockClock.StartTimer"/>) is responsible for validating the arguments and passing the
    /// already-truncated whole-millisecond <paramref name="dueMilliseconds"/> / <paramref name="periodMilliseconds"/>.
    /// </remarks>
    internal sealed class MockTimer : IDisposable
    {
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

        public MockTimer(MockClock clock, object? state, long dueMilliseconds, long periodMilliseconds, TimerCallback callback)
        {
            this.clock = clock;
            this.state = state;
            this.callback = callback;

            // A positive period repeats; both zero and -1 (infinite) milliseconds make the timer
            // one-shot, matching System.Threading.Timer. period is left unused when !isPeriodic.
            this.isPeriodic = periodMilliseconds > 0;
            this.period = TimeSpan.FromMilliseconds(periodMilliseconds);

            // An infinite due time (-1 ms) never fires: compute no target rather than offsetting UtcNow,
            // which would otherwise move the deadline into the past and fire immediately.
            this.nextDueTime = dueMilliseconds < 0
                ? (DateTimeOffset?)null
                : this.clock.UtcNow + TimeSpan.FromMilliseconds(dueMilliseconds);

            this.clock.Changed += this.ClockChanged;

            // Fire synchronously when the due time is already reached (e.g. a zero due time), matching
            // ScheduledAction's construction-time check.
            this.ClockChanged(this.clock, this.clock.UtcNow);
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
                }

                // Invoke outside the lock: the callback is arbitrary user code that may advance the
                // clock (re-entering this handler) or dispose the timer, neither of which must contend
                // for syncRoot while it is held.
                this.callback(this.state);
            }
        }
    }
}
