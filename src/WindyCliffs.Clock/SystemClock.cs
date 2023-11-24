namespace WindyCliffs.Clock
{
    using System;

    /// <summary>
    /// The implementation of <see cref="IClock"/> bound to the operating system clock.
    /// </summary>
    public class SystemClock : IClock
    {
        /// <summary>
        /// The singleton instance of <see cref="SystemClock"/>.
        /// </summary>
        public static readonly SystemClock Instance = new SystemClock();

        private SystemClock() { }

        /// <inhertdoc />
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
