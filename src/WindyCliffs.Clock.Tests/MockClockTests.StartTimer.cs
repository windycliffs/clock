namespace WindyCliffs.Clock.Tests
{
    using System;
    using System.Threading;
    using Xunit;

    public partial class MockClockTests
    {
        [Fact]
        public void StartTimer_NullCallback()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentNullException>(
                () => clock.StartTimer(null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan, null!));
        }

        [Theory]
        [InlineData(-2)]
        [InlineData(4294967295)]
        public void StartTimer_DueTimeOutOfRange(long dueMilliseconds)
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.StartTimer(null, TimeSpan.FromMilliseconds(dueMilliseconds), Timeout.InfiniteTimeSpan, _ => { }));
        }

        [Theory]
        [InlineData(-2)]
        [InlineData(4294967295)]
        public void StartTimer_IntervalOutOfRange(long intervalMilliseconds)
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.StartTimer(null, TimeSpan.Zero, TimeSpan.FromMilliseconds(intervalMilliseconds), _ => { }));
        }

        [Fact]
        public void StartTimer_ReturnsDisposable()
        {
            var clock = new MockClock();

            using IDisposable timer = clock.StartTimer(null, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan, _ => { });

            Assert.NotNull(timer);
        }

        [Fact]
        public void StartTimer_OneShot_FiresOnceAtDueTime()
        {
            var clock = new MockClock();
            var count = 0;

            using IDisposable timer = clock.StartTimer(null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan, _ => count++);

            clock.AdvanceBy(TimeSpan.FromSeconds(4));
            Assert.Equal(0, count);

            // Advancing well past the due time still fires a one-shot timer exactly once.
            clock.AdvanceBy(TimeSpan.FromSeconds(10));
            Assert.Equal(1, count);
        }

        [Fact]
        public void StartTimer_ZeroInterval_IsOneShot()
        {
            var clock = new MockClock();
            var count = 0;

            using IDisposable timer = clock.StartTimer(null, TimeSpan.FromSeconds(1), TimeSpan.Zero, _ => count++);

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.Equal(1, count);
        }

        [Fact]
        public void StartTimer_ZeroDueTime_FiresImmediately()
        {
            var clock = new MockClock();
            var fired = false;

            // A zero due time is already reached, so the callback fires synchronously during the call.
            using IDisposable timer = clock.StartTimer(null, TimeSpan.Zero, Timeout.InfiniteTimeSpan, _ => fired = true);

            Assert.True(fired, "Timer with a zero due time did not fire synchronously during StartTimer.");
        }

        [Fact]
        public void StartTimer_InfiniteDueTime_NeverFires()
        {
            var clock = new MockClock();
            var fired = false;

            using IDisposable timer = clock.StartTimer(null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan, _ => fired = true);

            clock.AdvanceBy(TimeSpan.FromHours(1));

            Assert.False(fired, "Timer fired despite an infinite due time.");
        }

        [Fact]
        public void StartTimer_Periodic_FiresOncePerInterval()
        {
            var clock = new MockClock();
            var count = 0;

            using IDisposable timer = clock.StartTimer(null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), _ => count++);

            clock.AdvanceBy(TimeSpan.FromSeconds(5));

            Assert.Equal(5, count);
        }

        [Fact]
        public void StartTimer_Periodic_DueTimeDiffersFromInterval()
        {
            var clock = new MockClock();
            var count = 0;

            // The first tick lands at dueTime (+2s); subsequent ticks follow every interval (+3s, +4s, +5s),
            // measured from each claimed due instant rather than from when the callback ran.
            using IDisposable timer = clock.StartTimer(null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1), _ => count++);

            clock.AdvanceBy(TimeSpan.FromSeconds(5));

            Assert.Equal(4, count);
        }

        [Fact]
        public void StartTimer_StatePassedToCallback()
        {
            var clock = new MockClock();
            var expectedState = new object();
            object? actualState = null;

            using IDisposable timer = clock.StartTimer(expectedState, TimeSpan.FromSeconds(1), Timeout.InfiniteTimeSpan, state => actualState = state);

            clock.AdvanceBy(TimeSpan.FromSeconds(1));

            Assert.Same(expectedState, actualState);
        }
    }
}
