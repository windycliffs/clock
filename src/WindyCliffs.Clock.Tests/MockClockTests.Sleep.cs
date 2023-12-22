namespace WindyCliffs.Clock.Tests
{
    using System;
    using System.Threading;
    using Xunit;

    public partial class MockClockTests
    {
        [Fact]
        public void Sleep_ZeroTimeout()
        {
            var clock = new MockClock();

            clock.Sleep(TimeSpan.Zero);
        }

        [Fact]
        public void Sleep_InfiniteTimeout()
        {
            using var started = new ManualResetEventSlim(false);
            using var finished = new ManualResetEventSlim(false);

            var isInterrupted = false;
            var clock = new MockClock();

            var thread = new Thread(_ =>
            {
                started.Set();

                try
                {
                    clock.Sleep(Timeout.InfiniteTimeSpan);
                }
                catch (ThreadInterruptedException)
                {
                    isInterrupted = true;
                }
                finally
                {
                    finished.Set();
                }
            });

            thread.Start();

            started.Wait();

            Assert.False(finished.Wait(TimeSpan.FromSeconds(1)), "Thread finished prematurely.");

            thread.Interrupt();

            Assert.True(finished.Wait(TimeSpan.FromSeconds(1)), "Thread never finished.");
            Assert.True(isInterrupted, "Thread wasn't interrupted.");
        }

        [Fact]
        public void Sleep_NegativeTimeout()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Sleep(TimeSpan.FromSeconds(-1)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(10)]
        public void Sleep_PositiveTimeout(int timeoutSeconds)
        {
            using var started = new ManualResetEventSlim(false);
            using var finished = new ManualResetEventSlim(false);

            var clock = new MockClock();

            var thread = new Thread(_ =>
            {
                started.Set();

                clock.Sleep(TimeSpan.FromSeconds(timeoutSeconds));

                finished.Set();
            });

            thread.Start();

            started.Wait();

            Assert.False(finished.Wait(TimeSpan.FromSeconds(1)), "Thread finished prematurely.");

            clock.AdvanceBy(TimeSpan.FromSeconds(timeoutSeconds));

            Assert.True(finished.Wait(TimeSpan.FromSeconds(1)), "Thread never finished.");
        }
    }
}
