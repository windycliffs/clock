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

        /// <inheritdoc />
        public IDisposable StartTimer(object? state, TimeSpan dueTime, TimeSpan interval, TimerCallback callback)
            // MockTimer validates its arguments (mirroring the System.Threading.Timer constructor) and
            // schedules itself against this clock's Changed event.
            => new MockTimer(this, state, dueTime, interval, callback);

        /// <inheritdoc />
        public bool TaskWait(Task task, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (task is null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            // Validate the timeout exactly as Task.Wait does, before delegating to TaskDelay (which
            // accepts a wider range). The converted value is unused here; MockClock passes the
            // TimeSpan straight to TaskDelay, so this call is only for its validation side effect.
            _ = WaitTimeout.ToMilliseconds(timeout, nameof(timeout));

            // Fast path mirroring Task.Wait: an already-completed task settles without blocking, and the
            // cancellation token only takes precedence over a task that did not run to completion — and
            // then only over a canceled one (a faulted task surfaces its own AggregateException even
            // under a cancelled token). task.Wait() re-throws faults/cancellation as Task.Wait does.
            if (task.IsCompleted)
            {
                if (task.IsCanceled)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                task.Wait();
                return true;
            }

            // Encode the managed-time timeout as a task: TaskDelay completes only when the clock is
            // advanced past the deadline, never completes for an infinite timeout, and is already
            // completed for a zero timeout.
            using var timeoutCancellation = new CancellationTokenSource();
            Task timeoutTask = this.TaskDelay(timeout, timeoutCancellation.Token);
            try
            {
                // Block in real time on whichever task settles first. Timeout.Infinite is passed as the
                // millisecond timeout (the timing already lives in timeoutTask); the
                // (Task[], CancellationToken) overload is unavailable on netstandard2.0. WaitAny's
                // first pass returns the lowest already-completed index, so a completed task wins over
                // an already-elapsed (e.g. zero) timeout, matching Task.Wait.
                if (Task.WaitAny(new[] { task, timeoutTask }, Timeout.Infinite, cancellationToken) == 0)
                {
                    // The task is complete; this returns immediately and re-throws a faulted or
                    // canceled task as an AggregateException, exactly as Task.Wait does.
                    task.Wait();
                    return true;
                }

                return false;
            }
            finally
            {
                // Drop the pending ScheduledAction if the timeout did not fire. The abandoned, now
                // canceled timeoutTask is never observed and raises no UnobservedTaskException.
                timeoutCancellation.Cancel();
            }
        }

        /// <inheritdoc />
        public int TaskWaitAny(Task[] tasks, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            _ = WaitTimeout.ToMilliseconds(timeout, nameof(timeout));

            // Task.WaitAny returns -1 for an empty array without waiting (but still observes an
            // already-cancelled token first); mirror that before scheduling.
            if (tasks.Length == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return -1;
            }

            using var timeoutCancellation = new CancellationTokenSource();

            // Append the managed-time timeout as a sentinel task; its index means "timed out".
            var candidates = new Task[tasks.Length + 1];
            Array.Copy(tasks, candidates, tasks.Length);
            candidates[tasks.Length] = this.TaskDelay(timeout, timeoutCancellation.Token);
            try
            {
                // Task.WaitAny validates the null elements among the user tasks. Unlike TaskWait, a
                // faulted or canceled task is not re-thrown here: its index is simply returned.
                int index = Task.WaitAny(candidates, Timeout.Infinite, cancellationToken);
                return index == tasks.Length ? -1 : index;
            }
            finally
            {
                timeoutCancellation.Cancel();
            }
        }

        /// <inheritdoc />
        public bool TaskWaitAll(Task[] tasks, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }

            _ = WaitTimeout.ToMilliseconds(timeout, nameof(timeout));

            // Mirror Task.WaitAll, which throws for an already-cancelled token regardless of task
            // state. This must be explicit: TaskWaitAll blocks via Task.WaitAny (Task.WhenAll has no
            // timeout overload), and Task.WaitAny's pre-completed-task first pass can otherwise win
            // over a cancelled token on .NET Framework, diverging from SystemClock.TaskWaitAll.
            cancellationToken.ThrowIfCancellationRequested();

            using var timeoutCancellation = new CancellationTokenSource();

            // Task.WhenAll validates the null elements, completes only once every task has, and
            // aggregates all faults; an empty array yields an already-completed task.
            Task completion = Task.WhenAll(tasks);
            Task timeoutTask = this.TaskDelay(timeout, timeoutCancellation.Token);
            try
            {
                if (Task.WaitAny(new[] { completion, timeoutTask }, Timeout.Infinite, cancellationToken) == 0)
                {
                    // Re-throw the aggregated faults as an AggregateException, like Task.WaitAll.
                    completion.Wait();
                    return true;
                }

                return false;
            }
            finally
            {
                timeoutCancellation.Cancel();
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
    }
}
