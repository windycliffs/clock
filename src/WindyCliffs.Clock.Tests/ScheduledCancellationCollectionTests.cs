namespace WindyCliffs.Clock.Tests
{
    using System;
    using System.Threading;
    using Xunit;

    public class ScheduledCancellationCollectionTests
    {
        [Fact]
        public void AddOrReplace_PositiveTimeout_CancelsAtDeadline()
        {
            var clock = new MockClock();
            var collection = new ScheduledCancellationCollection(clock);
            using var cts = new CancellationTokenSource();

            collection.AddOrReplace(cts, TimeSpan.FromSeconds(10));

            Assert.False(cts.IsCancellationRequested, "Cancelled before the clock advanced.");

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void AddOrReplace_DoesNotCancelBeforeDeadline()
        {
            var clock = new MockClock();
            var collection = new ScheduledCancellationCollection(clock);
            using var cts = new CancellationTokenSource();

            collection.AddOrReplace(cts, TimeSpan.FromSeconds(10));

            clock.AdvanceBy(TimeSpan.FromSeconds(9));

            Assert.False(cts.IsCancellationRequested);
        }

        [Fact]
        public void AddOrReplace_Zero_CancelsImmediately()
        {
            var clock = new MockClock();
            var collection = new ScheduledCancellationCollection(clock);
            using var cts = new CancellationTokenSource();

            collection.AddOrReplace(cts, TimeSpan.Zero);

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void AddOrReplace_Infinite_SchedulesNothingAndClearsPrevious()
        {
            var clock = new MockClock();
            var collection = new ScheduledCancellationCollection(clock);
            using var cts = new CancellationTokenSource();

            collection.AddOrReplace(cts, TimeSpan.FromSeconds(10));
            collection.AddOrReplace(cts, Timeout.InfiniteTimeSpan);

            clock.AdvanceBy(TimeSpan.FromHours(1));

            Assert.False(cts.IsCancellationRequested, "An infinite reschedule did not clear the pending cancellation.");
        }

        [Fact]
        public void AddOrReplace_RescheduleToLaterDeadline_LatestWins()
        {
            var clock = new MockClock();
            var collection = new ScheduledCancellationCollection(clock);
            using var cts = new CancellationTokenSource();

            collection.AddOrReplace(cts, TimeSpan.FromSeconds(10));
            collection.AddOrReplace(cts, TimeSpan.FromSeconds(20));

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.False(cts.IsCancellationRequested, "The superseded 10s deadline cancelled the source.");

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void AddOrReplace_RescheduleToEarlierDeadline_LatestWins()
        {
            var clock = new MockClock();
            var collection = new ScheduledCancellationCollection(clock);
            using var cts = new CancellationTokenSource();

            collection.AddOrReplace(cts, TimeSpan.FromSeconds(20));
            collection.AddOrReplace(cts, TimeSpan.FromSeconds(10));

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.True(cts.IsCancellationRequested);
        }

        [Fact]
        public void AddOrReplace_DisposedSourceAtFireTime_DoesNotThrow()
        {
            var clock = new MockClock();
            var collection = new ScheduledCancellationCollection(clock);
            var cts = new CancellationTokenSource();

            collection.AddOrReplace(cts, TimeSpan.FromSeconds(10));
            cts.Dispose();

            // The scheduled cancellation fires Cancel() on a disposed source; the ObjectDisposedException
            // must be swallowed rather than escaping AdvanceBy.
            var exception = Record.Exception(() => clock.AdvanceBy(TimeSpan.FromSeconds(10)));

            Assert.Null(exception);
        }

        [Fact]
        public void AddOrReplace_IndependentSources_TrackedSeparately()
        {
            var clock = new MockClock();
            var collection = new ScheduledCancellationCollection(clock);
            using var first = new CancellationTokenSource();
            using var second = new CancellationTokenSource();

            collection.AddOrReplace(first, TimeSpan.FromSeconds(10));
            collection.AddOrReplace(second, TimeSpan.FromSeconds(20));

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.True(first.IsCancellationRequested, "First source was not cancelled at its own deadline.");
            Assert.False(second.IsCancellationRequested, "Second source cancelled at the first source's deadline.");

            clock.AdvanceBy(TimeSpan.FromSeconds(10));

            Assert.True(second.IsCancellationRequested);
        }
    }
}
