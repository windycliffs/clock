# WindyCliffs.Clock

Library for consuming time-based operations and keeping your code testable.

It abstracts the clock behind a single `IClock` interface, so code that reads
the current time or sleeps can be driven deterministically in tests.

## Install

```
dotnet add package WindyCliffs.Clock
```

## Quick start

Depend on `IClock` instead of calling `DateTimeOffset.UtcNow` or `Thread.Sleep`
directly:

```csharp
using WindyCliffs.Clock;

public sealed class Session
{
    private readonly IClock clock;

    public Session(IClock clock) => this.clock = clock;

    public bool HasExpired(DateTimeOffset expiresAt) => this.clock.UtcNow >= expiresAt;
}
```

In production, register the operating-system clock:

```csharp
services.AddSingleton<IClock>(SystemClock.Instance);
```

In tests, use `MockClock` and advance time yourself:

```csharp
var clock = new MockClock();
var session = new Session(clock);

clock.AdvanceBy(TimeSpan.FromMinutes(5));
Assert.True(session.HasExpired(clock.UtcNow));
```

## Documentation

- [Usage](docs/USAGE.md) — detailed API guide (`SystemClock`, `MockClock`,
  time advancement, `Sleep` and `TaskDelay` semantics, the `Changed` event,
  testing patterns).
- [Architecture](docs/ARCHITECTURE.md) — how the abstraction and the
  `MockClock` time-advancement mechanism are designed.
- [Contributing](CONTRIBUTING.md) — building, testing, and versioning.

## Build & test

```
cd src
dotnet build repo.slnx
dotnet test repo.slnx
```
