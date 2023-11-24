namespace WindyCliffs.Clock.Tests
{
    using System;
    using Xunit;

    public class SystemClockTests
    {
        [Fact]
        public void Instance_IsNotNull()
        {
            Assert.NotNull(SystemClock.Instance);
        }

        [Fact]
        public void UtcNow_ReturnsCurrentTime()
        {
            DateTimeOffset low = DateTimeOffset.UtcNow;
            DateTimeOffset high = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(1.0);
            
            DateTimeOffset actual = SystemClock.Instance.UtcNow;

            Assert.InRange(actual, low, high);
            Assert.Equal(TimeSpan.Zero, actual.Offset);
        }
    }
}