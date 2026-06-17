namespace WindyCliffs.Clock
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The interface abstracting the time-related functionality.
    /// </summary>
    public interface IClock
    {
        /// <summary>
        /// Gets the current datetime offset in UTC timezone.
        /// Replacement for <see cref="DateTimeOffset.UtcNow"/>.
        /// </summary>
        DateTimeOffset UtcNow { get; }

        /// <summary>
        /// Blocks the thread execution for the given amount of time.
        /// Replacement for <see cref="System.Threading.Thread.Sleep"/>.
        /// </summary>
        /// <param name="timeout">
        /// The amount of time for which the thread is suspended. If the value of the timeout argument is
        /// <see cref="TimeSpan.Zero"/>, the thread relinquishes the remainder of its time slice to any thread
        /// of equal priority that is ready to run. If there are no other threads of equal priority that are
        /// ready to run, execution of the current thread is not suspended.
        /// </param>
        void Sleep(TimeSpan timeout);

        /// <summary>
        /// Creates a task that completes after the given amount of time.
        /// Replacement for <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
        /// </summary>
        /// <param name="timeout">
        /// The amount of time to wait before completing the returned task. If the value of the timeout
        /// argument is <see cref="TimeSpan.Zero"/> and <paramref name="cancellationToken"/> is not already
        /// cancelled, the returned task is already completed. If the value is
        /// <see cref="Timeout.InfiniteTimeSpan"/>, the returned task never completes unless
        /// <paramref name="cancellationToken"/> is cancelled.
        /// </param>
        /// <param name="cancellationToken">
        /// A token that, when cancelled, transitions the returned task to the canceled state.
        /// Cancellation does not throw synchronously from this method; instead, awaiting the returned
        /// task throws an <see cref="OperationCanceledException"/> (specifically a
        /// <see cref="TaskCanceledException"/>). If the token is already cancelled when the method is
        /// called, an already-canceled task is returned.
        /// </param>
        /// <returns>
        /// A task that completes after the delay elapses, or that transitions to the canceled state if
        /// <paramref name="cancellationToken"/> is cancelled first.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="timeout"/> is negative and is not equal to
        /// <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        Task TaskDelay(TimeSpan timeout, CancellationToken cancellationToken = default);

        /// <summary>
        /// Schedules the cancellation of the given <see cref="CancellationTokenSource"/> after the
        /// specified amount of time elapses.
        /// Replacement for <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>.
        /// </summary>
        /// <param name="source">The cancellation token source to cancel once the timeout elapses.</param>
        /// <param name="timeout">
        /// The amount of time to wait before cancelling <paramref name="source"/>. A value of
        /// <see cref="TimeSpan.Zero"/> requests cancellation immediately. A value of
        /// <see cref="Timeout.InfiniteTimeSpan"/> disables the scheduled cancellation, so
        /// <paramref name="source"/> is never cancelled by this call.
        /// </param>
        /// <remarks>
        /// <para>
        /// Calling this method again with the same <paramref name="source"/> reschedules the cancellation:
        /// the most recent call's <paramref name="timeout"/> replaces any still-pending one, consistent with
        /// <see cref="CancellationTokenSource.CancelAfter(TimeSpan)"/>.
        /// </para>
        /// <para>
        /// A <paramref name="source"/> that is disposed <em>after</em> this call returns, while the
        /// cancellation is still pending, is tolerated: the pending cancellation is silently dropped. A
        /// <paramref name="source"/> that is <em>already</em> disposed when this method is called is a
        /// programming error and throws <see cref="ObjectDisposedException"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// When <paramref name="source"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="timeout"/> is negative and is not equal to
        /// <see cref="Timeout.InfiniteTimeSpan"/>.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// When <paramref name="source"/> has already been disposed.
        /// </exception>
        void CancelAfter(CancellationTokenSource source, TimeSpan timeout);

        /// <summary>
        /// Starts a timer that invokes <paramref name="callback"/> after <paramref name="dueTime"/>
        /// elapses and, for a positive <paramref name="interval"/>, repeatedly every
        /// <paramref name="interval"/> thereafter.
        /// Replacement for the <see cref="System.Threading.Timer"/> constructor.
        /// </summary>
        /// <param name="state">
        /// An object passed to <paramref name="callback"/> on every invocation, or
        /// <see langword="null"/>.
        /// </param>
        /// <param name="dueTime">
        /// The amount of time to wait before <paramref name="callback"/> is invoked for the first
        /// time. <see cref="TimeSpan.Zero"/> invokes <paramref name="callback"/> immediately;
        /// <see cref="Timeout.InfiniteTimeSpan"/> prevents the first invocation, so the timer never
        /// fires until it is restarted (which this abstraction does not support) — it can only be
        /// disposed.
        /// </param>
        /// <param name="interval">
        /// The amount of time between invocations of <paramref name="callback"/>. A value of
        /// <see cref="TimeSpan.Zero"/> or <see cref="Timeout.InfiniteTimeSpan"/> disables periodic
        /// signalling, so <paramref name="callback"/> is invoked only once (when
        /// <paramref name="dueTime"/> elapses). Any positive value makes the timer periodic.
        /// </param>
        /// <param name="callback">The delegate invoked when the timer fires.</param>
        /// <returns>
        /// An <see cref="IDisposable"/> that stops the timer when disposed. Disposing it prevents
        /// any <em>future</em> invocations and is idempotent and safe to call after the timer has
        /// fired (including from within <paramref name="callback"/> itself); a callback already in
        /// progress when <see cref="IDisposable.Dispose"/> is called may still run to completion,
        /// mirroring <see cref="System.Threading.Timer.Dispose()"/>. The return type is intentionally
        /// <see cref="IDisposable"/> only (not <c>IAsyncDisposable</c>), matching the library's
        /// <c>netstandard2.0</c> target.
        /// </returns>
        /// <remarks>
        /// <para>
        /// <paramref name="dueTime"/> and <paramref name="interval"/> are validated exactly as the
        /// <see cref="System.Threading.Timer"/> constructor validates them: each is converted to
        /// whole milliseconds (truncating any sub-millisecond remainder) and must be between
        /// <c>-1</c> (<see cref="Timeout.InfiniteTimeSpan"/>) and <c>4294967294</c> inclusive.
        /// </para>
        /// <para>
        /// <see cref="MockClock"/> drives the timer from its <see cref="MockClock.AdvanceBy(TimeSpan)"/> /
        /// <see cref="MockClock.AdvanceTo(DateTimeOffset)"/> time-advancement rather than wall-clock
        /// time, with three deliberate differences from a real <see cref="System.Threading.Timer"/>:
        /// the callback runs <em>synchronously</em> on the thread that advances the clock (or the
        /// constructing thread, for an already-due timer) rather than on a thread-pool thread; when
        /// the clock advances past several intervals at once the callback fires once per elapsed
        /// interval (catch-up), so behaviour is independent of the advancement step; and an exception
        /// thrown by the callback propagates to the advancing caller (a real timer's thread-pool
        /// callback exception would crash the process).
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// When <paramref name="callback"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When the whole-millisecond value of <paramref name="dueTime"/> or
        /// <paramref name="interval"/> is less than <c>-1</c> or greater than <c>4294967294</c>.
        /// </exception>
        IDisposable StartTimer(object? state, TimeSpan dueTime, TimeSpan interval, TimerCallback callback);
    }
}
