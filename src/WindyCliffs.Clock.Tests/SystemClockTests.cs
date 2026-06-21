namespace WindyCliffs.Clock.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
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

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        public async Task TaskDelay_NonNegativeTimeout(int timeoutMilliseconds)
        {
            await SystemClock.Instance.TaskDelay(
                TimeSpan.FromMilliseconds(timeoutMilliseconds),
                TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task TaskDelay_NegativeTimeout()
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => SystemClock.Instance.TaskDelay(TimeSpan.FromSeconds(-1), TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task TaskDelay_AlreadyCancelledToken()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Task delay = SystemClock.Instance.TaskDelay(TimeSpan.FromSeconds(1), cts.Token);

            Assert.True(delay.IsCanceled);
            await Assert.ThrowsAsync<TaskCanceledException>(() => delay);
        }

        [Fact]
        public async Task TaskDelay_CancelledWhileWaiting()
        {
            using var cts = new CancellationTokenSource();

            Task delay = SystemClock.Instance.TaskDelay(TimeSpan.FromMinutes(5), cts.Token);

            Assert.False(delay.IsCompleted, "Task completed before cancellation.");

            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => delay);
            Assert.True(delay.IsCanceled);
        }

        [Fact]
        public async Task TaskDelay_InfiniteTimeout_CancelCompletes()
        {
            using var cts = new CancellationTokenSource();

            Task delay = SystemClock.Instance.TaskDelay(Timeout.InfiniteTimeSpan, cts.Token);

            Assert.False(delay.IsCompleted, "Infinite delay completed before cancellation.");

            cts.Cancel();

            await Assert.ThrowsAsync<TaskCanceledException>(() => delay);
            Assert.True(delay.IsCanceled);
        }

        [Fact]
        public void CancelAfter_NullSource()
        {
            Assert.Throws<ArgumentNullException>(
                () => SystemClock.Instance.CancelAfter(null!, TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void CancelAfter_NegativeTimeout()
        {
            using var cts = new CancellationTokenSource();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => SystemClock.Instance.CancelAfter(cts, TimeSpan.FromSeconds(-1)));
        }

        [Fact]
        public void CancelAfter_CancelsAfterDelay()
        {
            using var cts = new CancellationTokenSource();

            SystemClock.Instance.CancelAfter(cts, TimeSpan.FromMilliseconds(50));

            int signaled = WaitHandle.WaitAny(
                new[] { cts.Token.WaitHandle, TestContext.Current.CancellationToken.WaitHandle },
                TimeSpan.FromSeconds(5));

            Assert.Equal(0, signaled);
            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void StartTimer_NullCallback()
        {
            Assert.Throws<ArgumentNullException>(
                () => SystemClock.Instance.StartTimer(null, TimeSpan.FromMilliseconds(1), Timeout.InfiniteTimeSpan, null!));
        }

        [Theory]
        [InlineData(-2)]
        [InlineData(4294967295)]
        public void StartTimer_DueTimeOutOfRange(long dueMilliseconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SystemClock.Instance.StartTimer(null, TimeSpan.FromMilliseconds(dueMilliseconds), Timeout.InfiniteTimeSpan, _ => { }));
        }

        [Theory]
        [InlineData(-2)]
        [InlineData(4294967295)]
        public void StartTimer_IntervalOutOfRange(long intervalMilliseconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SystemClock.Instance.StartTimer(null, TimeSpan.Zero, TimeSpan.FromMilliseconds(intervalMilliseconds), _ => { }));
        }

        [Fact]
        public void StartTimer_ReturnsDisposable()
        {
            using IDisposable timer = SystemClock.Instance.StartTimer(null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan, _ => { });

            Assert.NotNull(timer);
        }

        [Fact]
        public void StartTimer_FiresAfterDueTime()
        {
            using var fired = new ManualResetEventSlim(false);
            var expectedState = new object();
            object? actualState = null;

            using IDisposable timer = SystemClock.Instance.StartTimer(
                expectedState,
                TimeSpan.FromMilliseconds(50),
                Timeout.InfiniteTimeSpan,
                state =>
                {
                    actualState = state;
                    fired.Set();
                });

            Assert.True(fired.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "Timer never fired.");
            Assert.Same(expectedState, actualState);
        }

        [Fact]
        public void StartTimer_Periodic_FiresRepeatedly()
        {
            using var firedTwice = new CountdownEvent(2);

            using IDisposable timer = SystemClock.Instance.StartTimer(
                null,
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(20),
                _ =>
                {
                    if (!firedTwice.IsSet)
                    {
                        firedTwice.Signal();
                    }
                });

            Assert.True(firedTwice.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "Timer did not fire repeatedly.");
        }

        [Fact]
        public void StartTimer_Dispose_StopsCallbacks()
        {
            var count = 0;

            IDisposable timer = SystemClock.Instance.StartTimer(
                null,
                TimeSpan.FromMilliseconds(20),
                TimeSpan.FromMilliseconds(20),
                _ => Interlocked.Increment(ref count));

            timer.Dispose();

            int afterDispose = Volatile.Read(ref count);

            // Wait well past several would-be periods; no further callbacks must run after Dispose.
            // 500 ms is well beyond several 20 ms periods, giving a loaded CI machine slack so the
            // negative assertion does not produce a false positive on slow scheduling.
            Assert.False(
                SpinWait.SpinUntil(() => Volatile.Read(ref count) > afterDispose, TimeSpan.FromMilliseconds(500)),
                "Callback ran after the timer was disposed.");
        }

        [Fact]
        public void StartTimer_InfiniteDueTime_NeverFires()
        {
            using var fired = new ManualResetEventSlim(false);

            using IDisposable timer = SystemClock.Instance.StartTimer(
                null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan,
                _ => fired.Set());

            Assert.False(fired.Wait(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken), "Timer fired despite an infinite due time.");
        }

        // A short, fixed wall-clock budget for the genuine-timeout tests below, large enough to be
        // reliable on a loaded CI machine without slowing the suite (cf. CancelAfter_CancelsAfterDelay).
        private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(50);

        [Fact]
        public void TaskWait_NullTask()
        {
            Assert.Throws<ArgumentNullException>(
                () => SystemClock.Instance.TaskWait(null!, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_NegativeTimeout()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SystemClock.Instance.TaskWait(Task.CompletedTask, TimeSpan.FromSeconds(-2), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_TimeoutTooLarge()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => SystemClock.Instance.TaskWait(Task.CompletedTask, TimeSpan.FromDays(30), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_CompletedTask_ReturnsTrue()
        {
            Assert.True(SystemClock.Instance.TaskWait(Task.CompletedTask, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_TimesOut_ReturnsFalse()
        {
            var pending = new TaskCompletionSource<bool>();

            Assert.False(SystemClock.Instance.TaskWait(pending.Task, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_AlreadyCancelledToken()
        {
            var pending = new TaskCompletionSource<bool>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => SystemClock.Instance.TaskWait(pending.Task, Timeout.InfiniteTimeSpan, cts.Token));
        }

        [Fact]
        public void TaskWait_FaultedTask_ThrowsAggregateException()
        {
            Task faulted = Task.FromException(new InvalidOperationException());

            AggregateException error = Assert.Throws<AggregateException>(
                () => SystemClock.Instance.TaskWait(faulted, ShortTimeout, TestContext.Current.CancellationToken));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }

        [Fact]
        public void TaskWaitAny_NullTasks()
        {
            Assert.Throws<ArgumentNullException>(
                () => SystemClock.Instance.TaskWaitAny(null!, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_NullElement()
        {
            Assert.Throws<ArgumentException>(
                () => SystemClock.Instance.TaskWaitAny(new Task[] { null! }, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_EmptyArray_ReturnsMinusOne()
        {
            Assert.Equal(-1, SystemClock.Instance.TaskWaitAny(Array.Empty<Task>(), ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_CompletedTask_ReturnsIndex()
        {
            var pending = new TaskCompletionSource<bool>();
            var tasks = new[] { pending.Task, Task.CompletedTask };

            Assert.Equal(1, SystemClock.Instance.TaskWaitAny(tasks, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_TimesOut_ReturnsMinusOne()
        {
            var pending = new TaskCompletionSource<bool>();

            Assert.Equal(-1, SystemClock.Instance.TaskWaitAny(new[] { pending.Task }, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_FaultedTask_ReturnsIndexWithoutThrowing()
        {
            Task faulted = Task.FromException(new InvalidOperationException());

            Assert.Equal(0, SystemClock.Instance.TaskWaitAny(new[] { faulted }, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_NullTasks()
        {
            Assert.Throws<ArgumentNullException>(
                () => SystemClock.Instance.TaskWaitAll(null!, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_NullElement()
        {
            Assert.Throws<ArgumentException>(
                () => SystemClock.Instance.TaskWaitAll(new Task[] { null! }, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_EmptyArray_ReturnsTrue()
        {
            Assert.True(SystemClock.Instance.TaskWaitAll(Array.Empty<Task>(), ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_AllCompleted_ReturnsTrue()
        {
            var tasks = new[] { Task.CompletedTask, Task.CompletedTask };

            Assert.True(SystemClock.Instance.TaskWaitAll(tasks, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_TimesOut_ReturnsFalse()
        {
            var pending = new TaskCompletionSource<bool>();
            var tasks = new[] { Task.CompletedTask, pending.Task };

            Assert.False(SystemClock.Instance.TaskWaitAll(tasks, ShortTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_FaultedTask_ThrowsAggregateException()
        {
            Task faulted = Task.FromException(new InvalidOperationException());
            var tasks = new[] { Task.CompletedTask, faulted };

            AggregateException error = Assert.Throws<AggregateException>(
                () => SystemClock.Instance.TaskWaitAll(tasks, ShortTimeout, TestContext.Current.CancellationToken));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }
    }
}
