namespace WindyCliffs.Clock.Tests
{
    using System;
    using Xunit;
    using Xunit.Abstractions;

    public partial class MockClockTests
    {
        private readonly ITestOutputHelper output;

        public MockClockTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Defaults_AreCorrect()
        {
            var clock = new MockClock();

            Assert.Equal(MockClock.DefaultStartTime, clock.UtcNow);
            Assert.Equal(TimeSpan.FromSeconds(1), clock.AdvancementStep);
        }

        [Fact]
        public void Changed_OnSetUtcNow()
        {
            var clock = new MockClock();
            var actualClock = default(MockClock);
            var actualValue = default(DateTimeOffset);
            var expectedValue = MockClock.DefaultStartTime + TimeSpan.FromSeconds(1);

            clock.Changed += (changedClock, changedValue) =>
            {
                actualClock = changedClock;
                actualValue = changedValue;
            };

            clock.UtcNow = expectedValue;

            Assert.Equal(clock, actualClock);
            Assert.Equal(expectedValue, actualValue);
        }

        [Fact]
        public void AdvanceTo_WithDefaultStep()
        {
            var steps = 0;
            var targetTime = MockClock.DefaultStartTime + TimeSpan.FromSeconds(4);

            var clock = new MockClock();
            clock.Changed += (_, __) => steps++;

            clock.AdvanceTo(targetTime);

            Assert.Equal(4, steps);
            Assert.Equal(targetTime, clock.UtcNow);
        }

        [Fact]
        public void AdvanceTo_WithCustomStepAndRemainder()
        {
            var steps = 0;
            var targetTime = MockClock.DefaultStartTime + TimeSpan.FromSeconds(10);

            var clock = new MockClock { AdvancementStep = TimeSpan.FromSeconds(4) };
            clock.Changed += (_, __) => steps++;

            clock.AdvanceTo(targetTime);

            Assert.Equal(3, steps);
            Assert.Equal(targetTime, clock.UtcNow);
        }

        [Fact]
        public void AdvanceTo_WithCustomStepAsArgument()
        {
            var steps = 0;
            var targetTime = MockClock.DefaultStartTime + TimeSpan.FromSeconds(10);

            var clock = new MockClock();
            clock.Changed += (_, __) => steps++;

            clock.AdvanceTo(targetTime, TimeSpan.FromSeconds(4));

            Assert.Equal(3, steps);
            Assert.Equal(targetTime, clock.UtcNow);
        }
        
        [Theory]
        [InlineData(-1, 1)]
        [InlineData(1, -1)]
        [InlineData(1, 0)]
        public void AdvanceTo_InvalidArguments(int intervalSeconds, int stepSeconds)
        {
            var clock = new MockClock();
            var interval = TimeSpan.FromSeconds(intervalSeconds);
            var step = TimeSpan.FromSeconds(stepSeconds);
            var targetTime = clock.UtcNow + interval;

            Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceTo(targetTime, step));
        }

        [Theory]
        [InlineData(-1, 1)]
        [InlineData(1, -1)]
        [InlineData(1, 0)]
        public void AdvanceBy_InvalidArguments(int intervalSeconds, int stepSeconds)
        {
            var clock = new MockClock();
            var interval = TimeSpan.FromSeconds(intervalSeconds);
            var step = TimeSpan.FromSeconds(stepSeconds);

            Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceBy(interval, step));
        }

        [Fact]
        public void AdvanceBy_WithDefaultStep()
        {
            var steps = 0;
            var advancement = TimeSpan.FromSeconds(4);
            var targetTime = MockClock.DefaultStartTime + advancement;

            var clock = new MockClock();
            clock.Changed += (_, __) => steps++;

            clock.AdvanceBy(advancement);

            Assert.Equal(4, steps);
            Assert.Equal(targetTime, clock.UtcNow);
        }

        [Fact]
        public void AdvanceBy_WithCustomStepAndReminder()
        {
            var steps = 0;
            var advancement = TimeSpan.FromSeconds(10);
            var targetTime = MockClock.DefaultStartTime + advancement;

            var clock = new MockClock { AdvancementStep = TimeSpan.FromSeconds(4) };
            clock.Changed += (_, __) => steps++;

            clock.AdvanceBy(advancement);

            Assert.Equal(3, steps);
            Assert.Equal(targetTime, clock.UtcNow);
        }

        [Fact]
        public void AdvanceBy_WithCustomStepAsArgument()
        {
            var steps = 0;
            var advancement = TimeSpan.FromSeconds(10);
            var targetTime = MockClock.DefaultStartTime + advancement;

            var clock = new MockClock();
            clock.Changed += (_, __) => steps++;

            clock.AdvanceBy(advancement, TimeSpan.FromSeconds(4));

            Assert.Equal(3, steps);
            Assert.Equal(targetTime, clock.UtcNow);
        }
    }
}
