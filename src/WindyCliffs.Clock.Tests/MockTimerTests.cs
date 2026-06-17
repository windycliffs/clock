namespace WindyCliffs.Clock.Tests
{
    using System;
    using System.Threading;
    using Xunit;

    public class MockTimerTests
    {
        // An infinite (one-shot / never) timeout.
        private static readonly TimeSpan Infinite = Timeout.InfiniteTimeSpan;

        private static TimeSpan Sec(double seconds) => TimeSpan.FromSeconds(seconds);

        [Fact]
        public void FiresImmediatelyWhenDueTimeIsZero()
        {
            var clock = new MockClock();
            var fired = false;

            // A zero due time is already reached, so the constructor's check fires the callback synchronously.
            using var timer = new MockTimer(clock, null, TimeSpan.Zero, Infinite, _ => fired = true);

            Assert.True(fired);
        }

        [Fact]
        public void DoesNotFireBeforeDueTime()
        {
            var clock = new MockClock();
            var fired = false;

            using var timer = new MockTimer(clock, null, Sec(5), Infinite, _ => fired = true);

            clock.AdvanceBy(TimeSpan.FromSeconds(4));

            Assert.False(fired);
        }

        [Fact]
        public void CatchUpFiringIsIndependentOfAdvancementStep()
        {
            var clock = new MockClock();
            var count = 0;

            using var timer = new MockTimer(clock, null, Sec(1), Sec(1), _ => count++);

            // A single advance whose step is larger than the period must still fire once per elapsed
            // period (catch-up), so behaviour does not depend on the advancement granularity.
            clock.AdvanceBy(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

            Assert.Equal(5, count);
        }

        [Fact]
        public void DisposeBeforeDueTimePreventsFiring()
        {
            var clock = new MockClock();
            var fired = false;

            var timer = new MockTimer(clock, null, Sec(5), Infinite, _ => fired = true);
            timer.Dispose();

            clock.AdvanceBy(TimeSpan.FromSeconds(5));

            Assert.False(fired, "Timer fired after being disposed.");
        }

        [Fact]
        public void DisposeMidRunStopsFurtherCallbacks()
        {
            var clock = new MockClock();
            var count = 0;

            using var timer = new MockTimer(clock, null, Sec(1), Sec(1), _ => count++);

            clock.AdvanceBy(TimeSpan.FromSeconds(3));
            Assert.Equal(3, count);

            timer.Dispose();

            clock.AdvanceBy(TimeSpan.FromSeconds(3));
            Assert.Equal(3, count);
        }

        [Fact]
        public void DisposeIsIdempotent()
        {
            var clock = new MockClock();

            var timer = new MockTimer(clock, null, Sec(5), Infinite, _ => { });

            timer.Dispose();

            var exception = Record.Exception(() => timer.Dispose());

            Assert.Null(exception);
        }

        [Fact]
        public void DisposeAfterFiringIsSafe()
        {
            var clock = new MockClock();

            var timer = new MockTimer(clock, null, Sec(1), Infinite, _ => { });

            clock.AdvanceBy(TimeSpan.FromSeconds(1));

            var exception = Record.Exception(() => timer.Dispose());

            Assert.Null(exception);
        }

        [Fact]
        public void DisposeFromWithinCallbackIsSafe()
        {
            var clock = new MockClock();
            var count = 0;
            MockTimer? timer = null;

            // The callback runs outside the lock, so disposing the timer from within it must not
            // deadlock and must stop any further periodic callbacks.
            timer = new MockTimer(clock, null, Sec(1), Sec(1), _ =>
            {
                count++;
                timer!.Dispose();
            });

            clock.AdvanceBy(TimeSpan.FromSeconds(5));

            Assert.Equal(1, count);
        }

        [Fact]
        public void CallbackAdvancingClockDoesNotDoubleFire()
        {
            var clock = new MockClock();
            var count = 0;

            using var timer = new MockTimer(clock, null, Sec(1), Sec(1), _ =>
            {
                count++;

                // Re-entrant advancement from the callback: each tick must still be claimed exactly once
                // and the recursion must stay bounded. Advance only while there is more ground to cover.
                if (clock.UtcNow < MockClock.DefaultStartTime + TimeSpan.FromSeconds(3))
                {
                    clock.AdvanceBy(TimeSpan.FromSeconds(1));
                }
            });

            clock.AdvanceBy(TimeSpan.FromSeconds(1));

            // Ticks at +1s, +2s and +3s each fire once: no tick is fired twice despite the re-entrancy.
            Assert.Equal(3, count);
        }

        [Fact]
        public void TimersAreIsolatedAcrossClocks()
        {
            var clockA = new MockClock();
            var clockB = new MockClock();
            var fired = false;

            using var timer = new MockTimer(clockA, null, Sec(1), Infinite, _ => fired = true);

            clockB.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.False(fired, "A timer fired when an unrelated clock advanced.");
        }

        [Fact]
        public void MultipleTimersOnOneClockFireIndependently()
        {
            var clock = new MockClock();
            var firstCount = 0;
            var secondCount = 0;

            using var first = new MockTimer(clock, null, Sec(1), Sec(1), _ => firstCount++);
            using var second = new MockTimer(clock, null, Sec(2), Sec(2), _ => secondCount++);

            clock.AdvanceBy(TimeSpan.FromSeconds(4));

            Assert.Equal(4, firstCount);  // +1, +2, +3, +4
            Assert.Equal(2, secondCount); // +2, +4
        }
    }
}
