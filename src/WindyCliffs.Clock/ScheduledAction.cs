namespace WindyCliffs.Clock
{
    using System;

    /// <summary>
    /// An action that runs once the associated <see cref="MockClock"/> advances to or past a target time.
    /// It subscribes to <see cref="MockClock.Changed"/>, fires exactly once and then disposes itself;
    /// disposing it beforehand unsubscribes without firing.
    /// </summary>
    internal class ScheduledAction : IDisposable
    {
        private readonly MockClock clock;

        private readonly DateTimeOffset onOrAfter;

        private readonly Action action;

        private readonly object syncRoot = new object();

        private volatile bool isDisposed = false;

        public ScheduledAction(MockClock clock, DateTimeOffset onOrAfter, Action action)
        {
            this.clock = clock;
            this.onOrAfter = onOrAfter;
            this.action = action;

            this.clock.Changed += this.ClockChanged;

            this.ClockChanged(this.clock, this.clock.UtcNow);
        }

        public void Dispose()
        {
            // Serialise with ClockChanged so setting the flag and unsubscribing are atomic with the guarded
            // check. The in-fire-path call (from ClockChanged) re-enters the lock safely.
            lock (this.syncRoot)
            {
                this.isDisposed = true;
                this.clock.Changed -= this.ClockChanged;
            }
        }

        private void ClockChanged(MockClock clock, DateTimeOffset utcNow)
        {
            if (!this.isDisposed && utcNow >= this.onOrAfter)
            {
                lock (this.syncRoot)
                {
                    if (!this.isDisposed)
                    {
                        this.action.Invoke();
                        this.Dispose();
                    }
                }
            }
        }
    }
}
