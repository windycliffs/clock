namespace WindyCliffs.Clock
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Converts a <see cref="TimeSpan"/> wait timeout to the whole-millisecond <see cref="int"/> value
    /// the <see cref="Task"/> wait overloads accept, validating it exactly as <see cref="Task.Wait(TimeSpan)"/>
    /// does. Shared by <see cref="SystemClock"/> and <see cref="MockClock"/> so both clocks reject the
    /// same out-of-range values on every target runtime.
    /// </summary>
    internal static class WaitTimeout
    {
        // The .NET wait APIs accept a timeout only in whole milliseconds and only up to int.MaxValue;
        // -1 (Timeout.Infinite / Timeout.InfiniteTimeSpan) means "wait indefinitely".
        internal static int ToMilliseconds(TimeSpan timeout, string paramName)
        {
            // Truncate to whole milliseconds, matching MockTimer's range check and the BCL's own
            // Task.Wait(TimeSpan) conversion. -1 is valid (an infinite wait); anything more negative,
            // or larger than int.MaxValue, is out of range.
            long milliseconds = (long)timeout.TotalMilliseconds;

            if (milliseconds < Timeout.Infinite || milliseconds > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    paramName,
                    timeout,
                    "The time-out value is negative and is not equal to Infinite, or is greater than int.MaxValue milliseconds.");
            }

            return (int)milliseconds;
        }
    }
}
