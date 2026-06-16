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
        /// A <paramref name="source"/> that is disposed <em>after</em> this call returns, while the
        /// cancellation is still pending, is tolerated: the pending cancellation is silently dropped. A
        /// <paramref name="source"/> that is <em>already</em> disposed when this method is called is a
        /// programming error and throws <see cref="ObjectDisposedException"/>.
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
    }
}
