﻿namespace WindyCliffs.Clock
{
    using System;

    /// <summary>
    /// The mock clock completely controlled by code that can be used to implement deterministic tests
    /// dependent on time.
    /// </summary>
    public class MockClock : IClock
    {
        /// <summary>
        /// The default start time set on <see cref="MockClock"/> instances after initialization.
        /// </summary>
        public static readonly DateTimeOffset DefaultStartTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private static readonly TimeSpan DefaultAdvancementStep = TimeSpan.FromSeconds(1);

        private DateTimeOffset utcNow = DefaultStartTime;

        private TimeSpan advancementStep = DefaultAdvancementStep;

        /// <summary>
        /// Gets or sets the current time on the mock clock in UTC timezone.
        /// </summary>
        public DateTimeOffset UtcNow
        {
            get => this.utcNow;
            set
            {
                if (this.utcNow == value)
                {
                    return;
                }

                this.utcNow = value;
                this.Changed?.Invoke(this, value);
            }
        }

        /// <summary>
        /// Gets or sets the advancement step used by <see cref="AdvanceBy(TimeSpan)"/> and
        /// <see cref="AdvanceTo(DateTimeOffset)"/> methods.
        /// </summary>
        public TimeSpan AdvancementStep
        {
            get => this.advancementStep;
            set
            {
                if (value <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                this.advancementStep = value;
            }
        }

        /// <summary>
        /// The event fired when <see cref="UtcNow"/> gets changed.
        /// The current instance of <see cref="MockClock"/> and the new value of
        /// <see cref="UtcNow"/> are passed as parameters to the event handlers.
        /// </summary>
        public event Action<MockClock, DateTimeOffset>? Changed;

        /// <summary>
        /// Advances the time on the clock in steps by the given interval.
        /// </summary>
        /// <param name="interval">The interval to advance the time by.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="interval"/> is negative.
        /// </exception>
        public void AdvanceBy(TimeSpan interval) => this.AdvanceBy(interval, this.advancementStep);

        /// <summary>
        /// Advances the time on the clock in steps by the given interval.
        /// </summary>
        /// <param name="interval">The interval to advance the time by.</param>
        /// <param name="step">The advancement step.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="step"/> is not positive or when <paramref name="interval"/> is
        /// negative.
        /// </exception>
        public void AdvanceBy(TimeSpan interval, TimeSpan step)
        {
            if (interval < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(interval));
            }

            this.AdvanceTo(this.utcNow + interval, step);
        }

        /// <summary>
        /// Advances the time on the clock in steps till it reaches the given value.
        /// </summary>
        /// <param name="newTime">The target time.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="newTime"/> is less than the current value of <see cref="UtcNow"/>.
        /// </exception>
        public void AdvanceTo(DateTimeOffset newTime) => this.AdvanceTo(newTime, this.advancementStep);

        /// <summary>
        /// Advances the time on the clock in steps till it reaches the given value.
        /// </summary>
        /// <param name="newTime">The target time.</param>
        /// <param name="step">The advancement step.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// When <paramref name="step"/> is not positive or when <paramref name="newTime"/> is
        /// less than the current value of <see cref="UtcNow"/>.
        /// </exception>
        public void AdvanceTo(DateTimeOffset newTime, TimeSpan step)
        {
            if (newTime < this.UtcNow)
            {
                throw new ArgumentOutOfRangeException(nameof(newTime));
            }

            if (step <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            while (this.UtcNow + step <= newTime)
            {
                this.UtcNow += step;
            }

            this.UtcNow = newTime;
        }
    }
}
