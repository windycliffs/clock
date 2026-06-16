namespace WindyCliffs.Clock
{
    using System;
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

        private readonly ScheduledCancellationCollection scheduledCancellations;

        /// <summary>
        /// Initialises a new instance of the <see cref="MockClock"/> class.
        /// </summary>
        public MockClock()
        {
            this.scheduledCancellations = new ScheduledCancellationCollection(this);
        }

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

            // The Token getter throws ObjectDisposedException on a disposed source (a programming error);
            // an already-cancelled token leaves nothing to schedule, mirroring CancellationTokenSource.CancelAfter.
            if (source.Token.IsCancellationRequested)
            {
                return;
            }

            this.scheduledCancellations.AddOrReplace(source, timeout);
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
    }
}
