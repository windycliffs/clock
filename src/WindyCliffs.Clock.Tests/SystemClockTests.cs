namespace WindyCliffs.Clock.Tests
{
    using System;
    using System.Threading;
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

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        public void Sleep_NonNegativeTimeout(int timeoutMilliseconds)
        {
            SystemClock.Instance.Sleep(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        }

        [Fact]
        public void Sleep_InfiniteTimeout()
        {
            using var started = new ManualResetEventSlim(false);
            using var finished = new ManualResetEventSlim(false);

            var isInterrupted = false;
            var clock = SystemClock.Instance;

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

            Assert.True(started.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "Thread never started.");

            Assert.False(finished.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken), "Thread finished prematurely.");

            thread.Interrupt();

            Assert.True(finished.Wait(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken), "Thread never finished.");
            Assert.True(isInterrupted, "Thread wasn't interrupted.");
        }

        [Fact]
        public void Sleep_NegativeTimeout()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SystemClock.Instance.Sleep(TimeSpan.FromSeconds(-1)));
        }
    }
}