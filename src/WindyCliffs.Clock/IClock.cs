namespace WindyCliffs.Clock
{
    using System;

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
    }
}
