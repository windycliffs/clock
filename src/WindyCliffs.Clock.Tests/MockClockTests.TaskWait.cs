namespace WindyCliffs.Clock.Tests
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Xunit;

    public partial class MockClockTests
    {
        private static readonly TimeSpan TestWaitTimeout = TimeSpan.FromSeconds(10);

        // Runs `wait` (handed the test cancellation token) on a worker thread, confirms it is genuinely
        // blocked, then performs `releaseWhileBlocked` (advance the clock, complete a task, cancel a
        // token, ...) and reports what the wait produced. Mirrors the Sleep_PositiveTimeout pattern.
        private static (bool Finished, T Result, Exception? Error) RunBlockedWait<T>(
            Func<CancellationToken, T> wait,
            Action releaseWhileBlocked,
            CancellationToken testToken)
        {
            using var started = new ManualResetEventSlim(false);
            using var finished = new ManualResetEventSlim(false);

            T result = default!;
            Exception? error = null;

            var thread = new Thread(_ =>
            {
                started.Set();

                try
                {
                    result = wait(testToken);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    finished.Set();
                }
            });

            thread.Start();

            Assert.True(started.Wait(TimeSpan.FromSeconds(5), testToken), "Worker never started.");

            // A generous margin so the worker is firmly inside the blocking wait (and has scheduled its
            // managed-time timeout) before we release it.
            Assert.False(finished.Wait(TimeSpan.FromSeconds(1), testToken), "Wait returned before it was released.");

            releaseWhileBlocked();

            bool finishedInTime = finished.Wait(TimeSpan.FromSeconds(5), testToken);
            thread.Join(TimeSpan.FromSeconds(5));

            return (finishedInTime, result, error);
        }

        private static Task Faulted() => Task.FromException(new InvalidOperationException());

        // ---- TaskWait ----

        [Fact]
        public void TaskWait_NullTask()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentNullException>(
                () => clock.TaskWait(null!, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_NegativeTimeout()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.TaskWait(Task.CompletedTask, TimeSpan.FromSeconds(-2), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_TimeoutTooLarge()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.TaskWait(Task.CompletedTask, TimeSpan.FromDays(30), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_CompletedTask_ReturnsTrue()
        {
            var clock = new MockClock();

            Assert.True(clock.TaskWait(Task.CompletedTask, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_ZeroTimeout_IncompleteTask_ReturnsFalse()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();

            Assert.False(clock.TaskWait(pending.Task, TimeSpan.Zero, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_ZeroTimeout_CompletedTask_ReturnsTrue()
        {
            var clock = new MockClock();

            Assert.True(clock.TaskWait(Task.CompletedTask, TimeSpan.Zero, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWait_FaultedTask_ThrowsAggregateException()
        {
            var clock = new MockClock();

            AggregateException error = Assert.Throws<AggregateException>(
                () => clock.TaskWait(Faulted(), TestWaitTimeout, TestContext.Current.CancellationToken));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }

        [Fact]
        public void TaskWait_AlreadyCancelledTokenButCompletedTask_ReturnsTrue()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // A completed task wins over the cancelled token, matching Task.Wait — no exception.
            Assert.True(clock.TaskWait(Task.CompletedTask, TestWaitTimeout, cts.Token));
        }

        [Fact]
        public void TaskWait_TimesOutWhenClockAdvances_ReturnsFalse()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();

            var outcome = RunBlockedWait(
                token => clock.TaskWait(pending.Task, TestWaitTimeout, token),
                () => clock.AdvanceBy(TestWaitTimeout),
                TestContext.Current.CancellationToken);

            Assert.True(outcome.Finished, "Wait never returned after the clock advanced past the timeout.");
            Assert.Null(outcome.Error);
            Assert.False(outcome.Result);
        }

        [Fact]
        public void TaskWait_TaskCompletesWhileWaiting_ReturnsTrue()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();

            var outcome = RunBlockedWait(
                token => clock.TaskWait(pending.Task, TestWaitTimeout, token),
                () => pending.SetResult(true),
                TestContext.Current.CancellationToken);

            Assert.True(outcome.Finished, "Wait never returned after the task completed.");
            Assert.Null(outcome.Error);
            Assert.True(outcome.Result);
        }

        [Fact]
        public void TaskWait_CancelledWhileWaiting_ThrowsOperationCanceled()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();
            using var cts = new CancellationTokenSource();

            var outcome = RunBlockedWait(
                _ => clock.TaskWait(pending.Task, TestWaitTimeout, cts.Token),
                () => cts.Cancel(),
                TestContext.Current.CancellationToken);

            Assert.True(outcome.Finished, "Wait never returned after cancellation.");
            Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);
        }

        [Fact]
        public void TaskWait_InfiniteTimeout_DoesNotTimeOutWhenClockAdvances()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();
            CancellationToken testToken = TestContext.Current.CancellationToken;
            using var started = new ManualResetEventSlim(false);
            using var finished = new ManualResetEventSlim(false);

            var thread = new Thread(_ =>
            {
                started.Set();
                clock.TaskWait(pending.Task, Timeout.InfiniteTimeSpan, testToken);
                finished.Set();
            });

            thread.Start();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5), testToken), "Worker never started.");
            Assert.False(finished.Wait(TimeSpan.FromSeconds(1), testToken), "Wait returned prematurely.");

            clock.AdvanceBy(TimeSpan.FromHours(1));

            Assert.False(
                finished.Wait(TimeSpan.FromMilliseconds(500), testToken),
                "Infinite wait returned when the clock was advanced.");

            // Release the worker so the test does not leak a blocked thread.
            pending.SetResult(true);
            Assert.True(finished.Wait(TimeSpan.FromSeconds(5), testToken), "Worker never finished.");
            thread.Join(TimeSpan.FromSeconds(5));
        }

        // ---- TaskWaitAny ----

        [Fact]
        public void TaskWaitAny_NullTasks()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentNullException>(
                () => clock.TaskWaitAny(null!, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_NullElement()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentException>(
                () => clock.TaskWaitAny(new Task[] { null! }, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_NegativeTimeout()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.TaskWaitAny(new[] { Task.CompletedTask }, TimeSpan.FromSeconds(-2), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_EmptyArray_ReturnsMinusOne()
        {
            var clock = new MockClock();

            Assert.Equal(-1, clock.TaskWaitAny(Array.Empty<Task>(), TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_CompletedTask_ReturnsIndex()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();

            Assert.Equal(1, clock.TaskWaitAny(new[] { pending.Task, Task.CompletedTask }, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_MultipleCompleted_ReturnsLowestIndex()
        {
            var clock = new MockClock();

            Assert.Equal(0, clock.TaskWaitAny(new[] { Task.CompletedTask, Task.CompletedTask }, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_FaultedTask_ReturnsIndexWithoutThrowing()
        {
            var clock = new MockClock();

            Assert.Equal(0, clock.TaskWaitAny(new[] { Faulted() }, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAny_TimesOutWhenClockAdvances_ReturnsMinusOne()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();

            var outcome = RunBlockedWait(
                token => clock.TaskWaitAny(new[] { pending.Task }, TestWaitTimeout, token),
                () => clock.AdvanceBy(TestWaitTimeout),
                TestContext.Current.CancellationToken);

            Assert.True(outcome.Finished, "Wait never returned after the clock advanced past the timeout.");
            Assert.Null(outcome.Error);
            Assert.Equal(-1, outcome.Result);
        }

        [Fact]
        public void TaskWaitAny_TaskCompletesWhileWaiting_ReturnsIndex()
        {
            var clock = new MockClock();
            var first = new TaskCompletionSource<bool>();
            var second = new TaskCompletionSource<bool>();

            var outcome = RunBlockedWait(
                token => clock.TaskWaitAny(new[] { first.Task, second.Task }, TestWaitTimeout, token),
                () => second.SetResult(true),
                TestContext.Current.CancellationToken);

            Assert.True(outcome.Finished, "Wait never returned after a task completed.");
            Assert.Null(outcome.Error);
            Assert.Equal(1, outcome.Result);
        }

        [Fact]
        public void TaskWaitAny_CancelledWhileWaiting_ThrowsOperationCanceled()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();
            using var cts = new CancellationTokenSource();

            var outcome = RunBlockedWait(
                _ => clock.TaskWaitAny(new[] { pending.Task }, TestWaitTimeout, cts.Token),
                () => cts.Cancel(),
                TestContext.Current.CancellationToken);

            Assert.True(outcome.Finished, "Wait never returned after cancellation.");
            Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);
        }

        [Fact]
        public void TaskWaitAny_AlreadyCancelledToken_Throws()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Task.WaitAny observes an already-cancelled token before any task, so even an incomplete
            // wait throws immediately rather than blocking.
            Assert.Throws<OperationCanceledException>(
                () => clock.TaskWaitAny(new[] { pending.Task }, TestWaitTimeout, cts.Token));
        }

        // ---- TaskWaitAll ----

        [Fact]
        public void TaskWaitAll_NullTasks()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentNullException>(
                () => clock.TaskWaitAll(null!, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_NullElement()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentException>(
                () => clock.TaskWaitAll(new Task[] { null! }, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_NegativeTimeout()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentOutOfRangeException>(
                () => clock.TaskWaitAll(new[] { Task.CompletedTask }, TimeSpan.FromSeconds(-2), TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_EmptyArray_ReturnsTrue()
        {
            var clock = new MockClock();

            Assert.True(clock.TaskWaitAll(Array.Empty<Task>(), TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_AllCompleted_ReturnsTrue()
        {
            var clock = new MockClock();

            Assert.True(clock.TaskWaitAll(new[] { Task.CompletedTask, Task.CompletedTask }, TestWaitTimeout, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void TaskWaitAll_FaultedTask_ThrowsAggregateException()
        {
            var clock = new MockClock();

            AggregateException error = Assert.Throws<AggregateException>(
                () => clock.TaskWaitAll(new[] { Task.CompletedTask, Faulted() }, TestWaitTimeout, TestContext.Current.CancellationToken));
            Assert.IsType<InvalidOperationException>(error.InnerException);
        }

        [Fact]
        public void TaskWaitAll_TimesOutWhenClockAdvances_ReturnsFalse()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();

            var outcome = RunBlockedWait(
                token => clock.TaskWaitAll(new[] { Task.CompletedTask, pending.Task }, TestWaitTimeout, token),
                () => clock.AdvanceBy(TestWaitTimeout),
                TestContext.Current.CancellationToken);

            Assert.True(outcome.Finished, "Wait never returned after the clock advanced past the timeout.");
            Assert.Null(outcome.Error);
            Assert.False(outcome.Result);
        }

        [Fact]
        public void TaskWaitAll_TasksCompleteWhileWaiting_ReturnsTrue()
        {
            var clock = new MockClock();
            var first = new TaskCompletionSource<bool>();
            var second = new TaskCompletionSource<bool>();

            var outcome = RunBlockedWait(
                token => clock.TaskWaitAll(new[] { first.Task, second.Task }, TestWaitTimeout, token),
                () =>
                {
                    first.SetResult(true);
                    second.SetResult(true);
                },
                TestContext.Current.CancellationToken);

            Assert.True(outcome.Finished, "Wait never returned after the tasks completed.");
            Assert.Null(outcome.Error);
            Assert.True(outcome.Result);
        }

        [Fact]
        public void TaskWaitAll_CancelledWhileWaiting_ThrowsOperationCanceled()
        {
            var clock = new MockClock();
            var pending = new TaskCompletionSource<bool>();
            using var cts = new CancellationTokenSource();

            var outcome = RunBlockedWait(
                _ => clock.TaskWaitAll(new[] { Task.CompletedTask, pending.Task }, TestWaitTimeout, cts.Token),
                () => cts.Cancel(),
                TestContext.Current.CancellationToken);

            Assert.True(outcome.Finished, "Wait never returned after cancellation.");
            Assert.IsAssignableFrom<OperationCanceledException>(outcome.Error);
        }

        [Fact]
        public void TaskWaitAll_AlreadyCancelledTokenWithCompletedTasks_Throws()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Unlike TaskWait, Task.WaitAll throws for an already-cancelled token even when every task
            // has completed; the managed implementation must match that on every target framework.
            Assert.Throws<OperationCanceledException>(
                () => clock.TaskWaitAll(new[] { Task.CompletedTask, Task.CompletedTask }, TestWaitTimeout, cts.Token));
        }

        // ---- Parity with SystemClock for already-settled inputs ----

        [Fact]
        public void Parity_TaskWait_CompletedTask()
        {
            var clock = new MockClock();
            CancellationToken token = TestContext.Current.CancellationToken;

            Assert.Equal(
                SystemClock.Instance.TaskWait(Task.CompletedTask, TestWaitTimeout, token),
                clock.TaskWait(Task.CompletedTask, TestWaitTimeout, token));
        }

        [Fact]
        public void Parity_TaskWait_FaultedTask_BothThrowAggregate()
        {
            var clock = new MockClock();
            CancellationToken token = TestContext.Current.CancellationToken;

            Assert.Throws<AggregateException>(() => SystemClock.Instance.TaskWait(Faulted(), TestWaitTimeout, token));
            Assert.Throws<AggregateException>(() => clock.TaskWait(Faulted(), TestWaitTimeout, token));
        }

        [Fact]
        public void Parity_TaskWaitAny_MultipleCompleted_LowestIndex()
        {
            var clock = new MockClock();
            CancellationToken token = TestContext.Current.CancellationToken;
            var system = new[] { Task.CompletedTask, Task.CompletedTask };
            var mock = new[] { Task.CompletedTask, Task.CompletedTask };

            Assert.Equal(
                SystemClock.Instance.TaskWaitAny(system, TestWaitTimeout, token),
                clock.TaskWaitAny(mock, TestWaitTimeout, token));
        }

        [Fact]
        public void Parity_TaskWaitAll_EmptyArray()
        {
            var clock = new MockClock();
            CancellationToken token = TestContext.Current.CancellationToken;

            Assert.Equal(
                SystemClock.Instance.TaskWaitAll(Array.Empty<Task>(), TestWaitTimeout, token),
                clock.TaskWaitAll(Array.Empty<Task>(), TestWaitTimeout, token));
        }
    }
}
