namespace WindyCliffs.Clock
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The mock clock completely controlled by code that can be used to implement deterministic tests
    /// dependent on time.
    /// </summary>
    public class MockClock : IClock
    {
        /// <summary>
        /// The default start time set on <see cref="MockClock"/> instances after initialization.
        /// </summary>
        public static readonly DateTimeOffset DefaultStartTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static readonly TimeSpan DefaultAdvancementStep = TimeSpan.FromSeconds(1);

        private long utcNow = DefaultStartTime.ToFileTime();

        private TimeSpan advancementStep = DefaultAdvancementStep;

        private readonly object cancelAfterSyncRoot = new object();

        private readonly Dictionary<CancellationTokenSource, ScheduledAction> pendingCancellations =
            new Dictionary<CancellationTokenSource, ScheduledAction>();

        /// <summary>
        /// Gets or sets the current time on the mock clock in UTC timezone.
        /// </summary>
        public DateTimeOffset UtcNow
        {
            get => DateTimeOffset.FromFileTime(Interlocked.Read(ref this.utcNow));
            set
            {
                long fileTime = value.ToFileTime();

                if (this.utcNow == fileTime)
                {
                    return;
                }

                Interlocked.Exchange(ref this.utcNow, fileTime);
                this.Changed?.Invoke(this, value);
            }
        }

        /// <inheritdoc />
        public void Sleep(TimeSpan timeout)
        {
            // This blocks the thread indefinitely.
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                Thread.Sleep(Timeout.InfiniteTimeSpan);
                return;
            }

            if (timeout == TimeSpan.Zero)
            {
                return;
            }

            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "The time-out value is negative and is not equal to Infinite.");
            }

            using var waiter = new ManualResetEventSlim(false);

            using var scheduledAction = new ScheduledAction(this, this.UtcNow + timeout, () => waiter.Set());

            waiter.Wait();
        }

        /// <inheritdoc />
        public Task TaskDelay(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            // Guard order mirrors Task.Delay (not Sleep): validate the timeout first, then honour an
            // already-cancelled token, then the zero short-circuit. So TaskDelay(negative, cancelledToken)
            // throws synchronously, exactly as Task.Delay does.
            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "The time-out value is negative and is not equal to Infinite.");
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }

            if (timeout == TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }

            // The non-generic TaskCompletionSource is .NET 5+; on netstandard2.0 only the generic form
            // exists. The resulting Task<bool> is returned as a plain Task (covariant, harmless).
            // RunContinuationsAsynchronously keeps user continuations off the thread driving AdvanceBy/
            // AdvanceTo, which fires the ScheduledAction synchronously inside the Changed handler.
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // An infinite delay schedules nothing; only cancellation can complete it.
            ScheduledAction? scheduledAction = timeout == Timeout.InfiniteTimeSpan
                ? null
                : new ScheduledAction(this, this.UtcNow + timeout, () => completion.TrySetResult(true));

            // A token from a CancellationTokenSource can be cancelled; CancellationToken.None — the
            // default when no token is supplied — never can, so there is nothing to register against.
            if (cancellationToken.CanBeCanceled)
            {
                CancellationTokenRegistration registration = cancellationToken.Register(() =>
                {
                    scheduledAction?.Dispose();
                    completion.TrySetCanceled(cancellationToken);
                });

                // Release the registration once the delay settles, so a long-lived token does not retain
                // the closure. (For an infinite delay that is never cancelled the registration lives as
                // long as the token, matching Task.Delay's own behaviour.) The completion source uses
                // RunContinuationsAsynchronously, so this cleanup never runs on the advancing thread.
                completion.Task.ContinueWith(
                    (_, state) => ((CancellationTokenRegistration)state!).Dispose(),
                    registration,
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }

            return completion.Task;
        }

        /// <inheritdoc />
        public void CancelAfter(CancellationTokenSource source, TimeSpan timeout)
        {
            // Guard order mirrors TaskDelay: reference-type precondition first, then the timeout range.
            if (source is null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "The time-out value is negative and is not equal to Infinite.");
            }

            // Surface an already-disposed source synchronously, mirroring CancellationTokenSource.CancelAfter:
            // disposing then calling is a programming error worth raising. Only a source disposed *after* this
            // call, while the cancellation is still pending, is tolerated (handled in the action below). The
            // Token getter throws ObjectDisposedException on a disposed source. Probe before mutating state.
            _ = source.Token;

            // Reschedule: drop any cancellation still pending for this source so the most recent call's
            // deadline replaces the earlier one, matching CancellationTokenSource.CancelAfter. Dispose the
            // previous action *outside* cancelAfterSyncRoot: the fire path takes the action's syncRoot and
            // then cancelAfterSyncRoot (in the action's finally below), so taking them in the opposite order
            // here would risk a deadlock. CancelAfter therefore never holds both locks at once.
            ScheduledAction? previous;
            lock (this.cancelAfterSyncRoot)
            {
                this.pendingCancellations.TryGetValue(source, out previous);
                this.pendingCancellations.Remove(source);
            }

            previous?.Dispose();

            // An infinite timeout schedules nothing, mirroring CancellationTokenSource.CancelAfter, which
            // treats Timeout.InfiniteTimeSpan as "no scheduled cancellation" — so the reschedule above simply
            // cancels the previous pending cancellation.
            if (timeout == Timeout.InfiniteTimeSpan)
            {
                return;
            }

            // A zero timeout cancels immediately and stores nothing: the ScheduledAction would fire inside
            // its own constructor (it checks the current time), so storing it would leave an already-fired,
            // disposed entry in the map. The source is not disposed (probed above), so Cancel cannot throw
            // ObjectDisposedException here.
            if (timeout == TimeSpan.Zero)
            {
                source.Cancel();
                return;
            }

            // Single-element holder so the action can reference its own ScheduledAction (to remove its map
            // entry on fire) without a null-capture warning. The ScheduledAction constructor checks the time
            // synchronously, so it fires inside the constructor only when the target is already due; for a
            // positive (future) timeout it does not fire here, so holder[0] is fully assigned before the
            // action can run or be observed via pendingCancellations.
            var holder = new ScheduledAction[1];
            holder[0] = new ScheduledAction(this, this.UtcNow + timeout, () =>
            {
                try
                {
                    source.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The source was disposed before the deadline was reached; there is nothing to cancel.
                    // The catch is intentionally narrow: any other exception (e.g. one thrown by a
                    // user-registered cancellation callback) propagates to the AdvanceBy/AdvanceTo caller.
                }
                finally
                {
                    // Remove this action's own entry once it has fired, but only if a concurrent reschedule
                    // has not already replaced it. Pure reference operations on the (possibly disposed)
                    // source key — they never touch CTS members, so they cannot throw here.
                    lock (this.cancelAfterSyncRoot)
                    {
                        if (this.pendingCancellations.TryGetValue(source, out ScheduledAction current) &&
                            ReferenceEquals(current, holder[0]))
                        {
                            this.pendingCancellations.Remove(source);
                        }
                    }
                }
            });

            lock (this.cancelAfterSyncRoot)
            {
                this.pendingCancellations[source] = holder[0];
            }
        }

        /// <summary>
        /// Gets or sets the advancement step used by <see cref="AdvanceBy(TimeSpan)"/> and
        /// <see cref="AdvanceTo(DateTimeOffset)"/> methods.
        /// </summary>
        public TimeSpan AdvancementStep
        {
            get => this.advancementStep;
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                this.advancementStep = value;
            }
        }

        /// <summary>
        /// The event fired when <see cref="UtcNow"/> gets changed.
        /// The current instance of <see cref="MockClock"/> and the new value of
        /// <see cref="UtcNow"/> are passed as parameters to the event handlers.
        /// </summary>
        public event Action<MockClock, DateTimeOffset>? Changed;

        /// <summary>
        /// Advances the time on the clock in steps by the given interval.
        /// </summary>
        /// <param name="interval">The interval to advance the time by.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="interval"/> is negative.
        /// </exception>
        public void AdvanceBy(TimeSpan interval) => this.AdvanceBy(interval, this.advancementStep);

        /// <summary>
        /// Advances the time on the clock in steps by the given interval.
        /// </summary>
        /// <param name="interval">The interval to advance the time by.</param>
        /// <param name="step">The advancement step.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="step"/> is not positive or when <paramref name="interval"/> is
        /// negative.
        /// </exception>
        public void AdvanceBy(TimeSpan interval, TimeSpan step)
        {
            if (interval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            this.AdvanceTo(this.UtcNow + interval, step);
        }

        /// <summary>
        /// Advances the time on the clock in steps till it reaches the given value.
        /// </summary>
        /// <param name="newTime">The target time.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="newTime"/> is less than the current value of <see cref="UtcNow"/>.
        /// </exception>
        public void AdvanceTo(DateTimeOffset newTime) => this.AdvanceTo(newTime, this.advancementStep);

        /// <summary>
        /// Advances the time on the clock in steps till it reaches the given value.
        /// </summary>
        /// <param name="newTime">The target time.</param>
        /// <param name="step">The advancement step.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="step"/> is not positive or when <paramref name="newTime"/> is
        /// less than the current value of <see cref="UtcNow"/>.
        /// </exception>
        public void AdvanceTo(DateTimeOffset newTime, TimeSpan step)
        {
            if (newTime < this.UtcNow)
            {
                throw new ArgumentOutOfRangeException(nameof(newTime));
            }

            if (step <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            while (this.UtcNow + step <= newTime)
            {
                this.UtcNow += step;
            }

            this.UtcNow = newTime;
        }

        private class ScheduledAction : IDisposable
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
                // Serialise with ClockChanged so setting the flag and unsubscribing are atomic with the
                // guarded check. TaskDelay may call this from a cancellation callback concurrently with a
                // clock advancement; the in-fire-path call (from ClockChanged) re-enters the lock safely.
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
}
