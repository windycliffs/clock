namespace WindyCliffs.Clock
{
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Tracks at most one pending cancellation per <see cref="CancellationTokenSource"/> for a single
    /// <see cref="MockClock"/>, so that scheduling a cancellation for a source replaces any earlier one
    /// (mirroring <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>).
    /// </summary>
    internal sealed class ScheduledCancellationCollection
    {
        private readonly MockClock clock;

        private readonly object syncRoot = new object();

        private readonly Dictionary<CancellationTokenSource, ScheduledCancellation> entries =
            new Dictionary<CancellationTokenSource, ScheduledCancellation>();

        public ScheduledCancellationCollection(MockClock clock) => this.clock = clock;

        /// <summary>
        /// Schedules <paramref name="source"/> to be cancelled after <paramref name="timeout"/>, replacing
        /// any cancellation already pending for it. A zero timeout cancels immediately; an infinite timeout
        /// schedules nothing, so it only clears the previous pending cancellation.
        /// </summary>
        public void AddOrReplace(CancellationTokenSource source, TimeSpan timeout)
        {
            // Replace the entry under the lock so the latest deadline wins even under concurrent calls. A
            // positive timeout targets the future, so the new action never fires inside the lock.
            ScheduledCancellation? previous;
            lock (this.syncRoot)
            {
                this.entries.TryGetValue(source, out previous);
                this.entries.Remove(source);

                if (timeout != Timeout.InfiniteTimeSpan && timeout != TimeSpan.Zero)
                {
                    this.entries[source] = new ScheduledCancellation(this, source, this.clock.UtcNow + timeout);
                }
            }

            // Outside the lock: Dispose takes the replaced action's lock, which the fire path holds while it
            // reacquires this lock, so disposing under the lock could deadlock; Cancel runs user callbacks.
            previous?.Dispose();
            if (timeout == TimeSpan.Zero)
            {
                source.Cancel();
            }
        }

        private void Remove(CancellationTokenSource source)
        {
            lock (this.syncRoot)
            {
                this.entries.Remove(source);
            }
        }

        /// <summary>
        /// A <see cref="ScheduledAction"/> that cancels its <see cref="CancellationTokenSource"/> when the
        /// clock reaches the deadline, then removes itself from the owning collection.
        /// </summary>
        private sealed class ScheduledCancellation : ScheduledAction
        {
            public ScheduledCancellation(ScheduledCancellationCollection owner, CancellationTokenSource source, DateTimeOffset onOrAfter)
                : base(owner.clock, onOrAfter, () => Elapse(owner, source))
            {
            }

            private static void Elapse(ScheduledCancellationCollection owner, CancellationTokenSource source)
            {
                try
                {
                    source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Disposed before its deadline; nothing to cancel. The catch is narrow so a throwing
                    // cancellation callback still surfaces to the AdvanceBy/AdvanceTo caller.
                }
                finally
                {
                    owner.Remove(source);
                }
            }
        }
    }
}
