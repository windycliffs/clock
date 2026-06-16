namespace WindyCliffs.Clock.Tests
{
    using System;
    using System.Threading;
    using Xunit;

    public partial class MockClockTests
    {
        [Fact]
        public void CancelAfter_NullSource()
        {
            var clock = new MockClock();

            Assert.Throws<ArgumentNullException>(() => clock.CancelAfter(null!, TimeSpan.FromSeconds(1)));
        }

        [Fact]
        public void CancelAfter_NegativeTimeout()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            Assert.Throws<ArgumentOutOfRangeException>(() => clock.CancelAfter(cts, TimeSpan.FromSeconds(-1)));
        }

        [Fact]
        public void CancelAfter_ZeroTimeout_CancelsImmediately()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            clock.CancelAfter(cts, TimeSpan.Zero);

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void CancelAfter_AdvancePastDeadline_Cancels()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();
            var timeout = TimeSpan.FromSeconds(10);

            clock.CancelAfter(cts, timeout);

            Assert.False(cts.IsCancellationRequested, "Source cancelled before the clock advanced.");

            clock.AdvanceBy(timeout);

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void CancelAfter_AdvanceUndershootThenReach()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();
            var timeout = TimeSpan.FromSeconds(10);

            clock.CancelAfter(cts, timeout);

            clock.AdvanceBy(timeout - TimeSpan.FromSeconds(1));

            Assert.False(cts.IsCancellationRequested, "Source cancelled before reaching the deadline.");

            clock.AdvanceBy(TimeSpan.FromSeconds(1));

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void CancelAfter_InfiniteTimeout_NeverCancels()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            clock.CancelAfter(cts, Timeout.InfiniteTimeSpan);

            clock.AdvanceBy(TimeSpan.FromHours(1));

            Assert.False(cts.IsCancellationRequested, "Infinite timeout cancelled the source by advancing the clock.");
        }

        [Fact]
        public void CancelAfter_AlreadyCancelledSource_IsNoOp()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // CancelAfter on an already-cancelled source finishes early and schedules nothing, so advancing
            // past the would-be deadline does nothing and must not throw.
            clock.CancelAfter(cts, TimeSpan.FromSeconds(10));

            var exception = Record.Exception(() => clock.AdvanceBy(TimeSpan.FromSeconds(10)));

            Assert.Null(exception);
            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void CancelAfter_DisposedSourceAtFireTime_DoesNotThrow()
        {
            var clock = new MockClock();
            var cts = new CancellationTokenSource();

            clock.CancelAfter(cts, TimeSpan.FromSeconds(10));

            cts.Dispose();

            // Advancing past the deadline fires the scheduled Cancel() on a disposed source; the
            // ObjectDisposedException must be swallowed rather than escaping AdvanceBy.
            var exception = Record.Exception(() => clock.AdvanceBy(TimeSpan.FromSeconds(10)));

            Assert.Null(exception);
        }

        [Fact]
        public void CancelAfter_DisposedSourceAtCallTime_Throws()
        {
            var clock = new MockClock();
            var cts = new CancellationTokenSource();
            cts.Dispose();

            // An already-disposed source at call time is a programming error: it must surface
            // synchronously rather than be swallowed (unlike disposal while a cancellation is pending).
            Assert.Throws<ObjectDisposedException>(() => clock.CancelAfter(cts, TimeSpan.FromSeconds(10)));
        }

        [Fact]
        public void CancelAfter_DisposedSourceAtCallTime_ZeroTimeout_Throws()
        {
            var clock = new MockClock();
            var cts = new CancellationTokenSource();
            cts.Dispose();

            // The synchronous disposed check runs before any scheduling, so a zero timeout throws too
            // rather than reaching the immediate-fire path.
            Assert.Throws<ObjectDisposedException>(() => clock.CancelAfter(cts, TimeSpan.Zero));
        }

        [Fact]
        public void CancelAfter_RescheduleToLaterDeadline_LatestCallWins()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            // The issue #9 scenario: a later call must replace the earlier deadline (cancel at 20s, not 10s).
            clock.CancelAfter(cts, TimeSpan.FromSeconds(10));
            clock.CancelAfter(cts, TimeSpan.FromSeconds(20));

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.False(cts.IsCancellationRequested, "The superseded 10s deadline cancelled the source.");

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void CancelAfter_RescheduleToEarlierDeadline_LatestCallWins()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            clock.CancelAfter(cts, TimeSpan.FromSeconds(20));
            clock.CancelAfter(cts, TimeSpan.FromSeconds(10));

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void CancelAfter_RescheduleToInfinite_DisablesCancellation()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            clock.CancelAfter(cts, TimeSpan.FromSeconds(10));
            clock.CancelAfter(cts, Timeout.InfiniteTimeSpan);

            clock.AdvanceBy(TimeSpan.FromHours(1));

            Assert.False(cts.IsCancellationRequested, "Rescheduling to infinite did not cancel the pending 10s deadline.");
        }

        [Fact]
        public void CancelAfter_RescheduleToZero_CancelsImmediately()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            clock.CancelAfter(cts, TimeSpan.FromSeconds(10));
            clock.CancelAfter(cts, TimeSpan.Zero);

            Assert.True(cts.IsCancellationRequested);

            // The superseded 10s action must be inert: advancing past its old deadline must not throw.
            var exception = Record.Exception(() => clock.AdvanceBy(TimeSpan.FromSeconds(10)));

            Assert.Null(exception);
        }

        [Fact]
        public void CancelAfter_ZeroThenPositive_StaysCancelled()
        {
            var clock = new MockClock();
            using var cts = new CancellationTokenSource();

            // Zero cancels immediately; the later positive call sees an already-cancelled source and finishes
            // early without scheduling anything. The source cannot be un-cancelled, and advancing the clock
            // afterwards does nothing and must not throw.
            clock.CancelAfter(cts, TimeSpan.Zero);
            clock.CancelAfter(cts, TimeSpan.FromSeconds(10));

            Assert.True(cts.IsCancellationRequested);

            var exception = Record.Exception(() => clock.AdvanceBy(TimeSpan.FromSeconds(10)));

            Assert.Null(exception);
            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void CancelAfter_DisposedSourceAtRescheduleTime_AdvancingStaysSafe()
        {
            var clock = new MockClock();
            var cts = new CancellationTokenSource();

            clock.CancelAfter(cts, TimeSpan.FromSeconds(10));
            cts.Dispose();

            // Rescheduling on an already-disposed source is a programming error and throws, without mutating
            // state (the probe runs before the map is touched, giving a strong exception guarantee).
            Assert.Throws<ObjectDisposedException>(() => clock.CancelAfter(cts, TimeSpan.FromSeconds(5)));

            // The original pending action lingers only until the clock advances past its deadline: it then
            // fires, swallows the ObjectDisposedException from Cancel(), and removes itself — so advancing
            // never throws.
            var exception = Record.Exception(() => clock.AdvanceBy(TimeSpan.FromSeconds(10)));

            Assert.Null(exception);
        }
    }
}
