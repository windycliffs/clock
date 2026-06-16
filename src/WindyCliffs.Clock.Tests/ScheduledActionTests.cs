namespace WindyCliffs.Clock.Tests
{
    using System;
    using Xunit;

    public class ScheduledActionTests
    {
        [Fact]
        public void FiresWhenClockReachesTarget()
        {
            var clock = new MockClock();
            var fired = false;

            using var action = new ScheduledAction(clock, clock.UtcNow + TimeSpan.FromSeconds(5), () => fired = true);

            Assert.False(fired, "Fired before the clock advanced.");

            clock.AdvanceBy(TimeSpan.FromSeconds(5));

            Assert.True(fired);
        }

        [Fact]
        public void DoesNotFireBeforeTarget()
        {
            var clock = new MockClock();
            var fired = false;

            using var action = new ScheduledAction(clock, clock.UtcNow + TimeSpan.FromSeconds(5), () => fired = true);

            clock.AdvanceBy(TimeSpan.FromSeconds(4));

            Assert.False(fired);
        }

        [Fact]
        public void FiresImmediatelyWhenTargetAlreadyReached()
        {
            var clock = new MockClock();
            var fired = false;

            // The constructor checks the current time, so a target at (or before) now fires synchronously.
            using var action = new ScheduledAction(clock, clock.UtcNow, () => fired = true);

            Assert.True(fired);
        }

        [Fact]
        public void FiresExactlyOnce()
        {
            var clock = new MockClock();
            var count = 0;

            using var action = new ScheduledAction(clock, clock.UtcNow + TimeSpan.FromSeconds(1), () => count++);

            // Advancing well past the target raises Changed several times; the action must fire only once.
            clock.AdvanceBy(TimeSpan.FromSeconds(5));

            Assert.Equal(1, count);
        }

        [Fact]
        public void DisposeBeforeTargetPreventsFiring()
        {
            var clock = new MockClock();
            var fired = false;

            var action = new ScheduledAction(clock, clock.UtcNow + TimeSpan.FromSeconds(5), () => fired = true);
            action.Dispose();

            clock.AdvanceBy(TimeSpan.FromSeconds(5));

            Assert.False(fired, "Fired after being disposed.");
        }

        [Fact]
        public void DisposeIsIdempotent()
        {
            var clock = new MockClock();

            var action = new ScheduledAction(clock, clock.UtcNow + TimeSpan.FromSeconds(5), () => { });

            action.Dispose();

            var exception = Record.Exception(() => action.Dispose());

            Assert.Null(exception);
        }

        [Fact]
        public void DisposeAfterFiringIsSafe()
        {
            var clock = new MockClock();

            var action = new ScheduledAction(clock, clock.UtcNow + TimeSpan.FromSeconds(1), () => { });

            // The action self-disposes when it fires; an explicit Dispose afterwards must be a no-op.
            clock.AdvanceBy(TimeSpan.FromSeconds(1));

            var exception = Record.Exception(() => action.Dispose());

            Assert.Null(exception);
        }
    }
}
