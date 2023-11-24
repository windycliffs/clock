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
    }
}
