# WindyCliffs.Clock

A small .NET library for testable time. It abstracts the clock behind a single
`IClock` interface so code that reads the current time, sleeps, or delays can be
driven deterministically in tests instead of depending on the wall clock.

- **`SystemClock`** — the production implementation, bound to the operating-system clock.
- **`MockClock`** — a fully code-controlled clock for deterministic tests; you advance time yourself.

Targets `netstandard2.0`, so it runs on .NET Framework and modern .NET alike.

## Install

```
dotnet add package WindyCliffs.Clock
```

## Quick start

Depend on `IClock` instead of calling `DateTimeOffset.UtcNow`, `Thread.Sleep`,
or `Task.Delay` directly:

```csharp
using WindyCliffs.Clock;

public sealed class Session
{
    private readonly IClock clock;

    public Session(IClock clock) => this.clock = clock;

    public bool HasExpired(DateTimeOffset expiresAt) => this.clock.UtcNow >= expiresAt;
}
```

In production, register the singleton operating-system clock:

```csharp
services.AddSingleton<IClock>(SystemClock.Instance);
```

In tests, use `MockClock` and advance time yourself — no real waiting:

```csharp
var clock = new MockClock();
var session = new Session(clock);

clock.AdvanceBy(TimeSpan.FromMinutes(5));
Assert.True(session.HasExpired(clock.UtcNow));
```

## What `IClock` offers

| Member | Description |
| --- | --- |
| `UtcNow` | The current time (UTC). Replacement for `DateTimeOffset.UtcNow`. |
| `Sleep(timeout)` | Blocks the caller. Replacement for `Thread.Sleep`. |
| `TaskDelay(timeout, cancellationToken)` | Awaitable, cancellable delay. Replacement for `Task.Delay`. |
| `CancelAfter(source, timeout)` | Cancels a `CancellationTokenSource` after the timeout. Replacement for `CancellationTokenSource.CancelAfter`. |

With `MockClock`, `Sleep`, `TaskDelay`, and `CancelAfter` are all driven by
advancing the clock (`AdvanceBy`/`AdvanceTo`) rather than by real elapsed time,
so time-dependent code — including `async` code — stays deterministic:

```csharp
var clock = new MockClock();

Task delay = clock.TaskDelay(TimeSpan.FromSeconds(30));

clock.AdvanceBy(TimeSpan.FromSeconds(30)); // releases the pending delay
await delay;
```

## Documentation

Full guides live in the GitHub repository:

- [Usage](https://github.com/windycliffs/clock/blob/HEAD/docs/USAGE.md) — detailed API guide.
- [Architecture](https://github.com/windycliffs/clock/blob/HEAD/docs/ARCHITECTURE.md) — how the abstraction and `MockClock` time-advancement are designed.

The source lives at [github.com/windycliffs/clock](https://github.com/windycliffs/clock).

## License

MIT — see [LICENSE](https://github.com/windycliffs/clock/blob/HEAD/LICENSE).
