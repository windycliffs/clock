namespace WindyCliffs.Clock.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    public partial class MockClockTests
    {
        [Fact]
        public void TaskDelay_ZeroTimeout()
        {
            var clock = new MockClock();

            Task delay = clock.TaskDelay(TimeSpan.Zero, TestContext.Current.CancellationToken);

            Assert.Equal(TaskStatus.RanToCompletion, delay.Status);
        }

        [Fact]
        public async Task TaskDelay_NegativeTimeout()
        {
            var clock = new MockClock();

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => clock.TaskDelay(TimeSpan.FromSeconds(-1), TestContext.Current.CancellationToken));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(10)]
        public async Task TaskDelay_PositiveTimeout(int timeoutSeconds)
        {
            var clock = new MockClock();
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            Task delay = clock.TaskDelay(timeout, TestContext.Current.CancellationToken);

            Assert.False(delay.IsCompleted, "Task completed before the clock advanced.");

            clock.AdvanceBy(timeout);

            await delay;
            Assert.Equal(TaskStatus.RanToCompletion, delay.Status);
        }

        [Fact]
        public async Task TaskDelay_AdvanceUndershootThenReach()
        {
            var clock = new MockClock();
            var timeout = TimeSpan.FromSeconds(10);

            Task delay = clock.TaskDelay(timeout, TestContext.Current.CancellationToken);

            clock.AdvanceBy(timeout - TimeSpan.FromSeconds(1));

            Assert.False(delay.IsCompleted, "Task completed before reaching the deadline.");

            clock.AdvanceBy(TimeSpan.FromSeconds(1));

            await delay;
            Assert.Equal(TaskStatus.RanToCompletion, delay.Status);
        }

        [Fact]
        public void TaskDelay_InfiniteTimeout_DoesNotComplete()
        {
            var clock = new MockClock();

            Task delay = clock.TaskDelay(Timeout.InfiniteTimeSpan, TestContext.Current.CancellationToken);

            clock.AdvanceBy(TimeSpan.FromHours(1));

            Assert.False(delay.IsCompleted, "Infinite delay completed by advancing the clock.");
        }

        [Fact]
        public async Task TaskDelay_AlreadyCancelledToken()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Task delay = clock.TaskDelay(TimeSpan.FromSeconds(1), cts.Token);

            Assert.True(delay.IsCanceled);
            await Assert.ThrowsAsync<TaskCanceledException>(() => delay);
        }

        [Fact]
        public async Task TaskDelay_CancelledWhileWaiting()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            Task delay = clock.TaskDelay(TimeSpan.FromSeconds(10), cts.Token);

            Assert.False(delay.IsCompleted, "Task completed before cancellation.");

            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => delay);
            Assert.True(delay.IsCanceled);

            // Advancing past the original deadline must not flip a cancelled delay to completed.
            clock.AdvanceBy(TimeSpan.FromSeconds(20));

            Assert.True(delay.IsCanceled);
        }

        [Fact]
        public async Task TaskDelay_CancelRacesWithCompletion_SettlesCleanly()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();
            var timeout = TimeSpan.FromSeconds(1);

            Task delay = clock.TaskDelay(timeout, cts.Token);

            // Cancel and advance back to back; whichever wins, the other is a silent no-op.
            cts.Cancel();
            clock.AdvanceBy(timeout);

            Assert.True(delay.IsCompleted);
            Assert.NotEqual(TaskStatus.Faulted, delay.Status);

            if (!delay.IsCanceled)
            {
                await delay;
            }
        }

        [Fact]
        public async Task TaskDelay_InfiniteTimeout_CancelCompletes()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            Task delay = clock.TaskDelay(Timeout.InfiniteTimeSpan, cts.Token);

            Assert.False(delay.IsCompleted, "Infinite delay completed before cancellation.");

            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => delay);
            Assert.True(delay.IsCanceled);
        }
    }
}
