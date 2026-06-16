# Usage

`WindyCliffs.Clock` lets you depend on time through the `IClock` interface so
your code stays testable. In production you wire up `SystemClock`; in tests you
use `MockClock`.

## Installing

```
dotnet add package WindyCliffs.Clock
```

## Depending on `IClock`

Take `IClock` as a dependency instead of calling `DateTimeOffset.UtcNow` or
`Thread.Sleep` directly:

```csharp
using WindyCliffs.Clock;

public sealed class TokenCache
{
    private readonly IClock clock;

    public TokenCache(IClock clock) => this.clock = clock;

    public bool IsExpired(DateTimeOffset expiresAt) => this.clock.UtcNow >= expiresAt;
}
```

### Registering with dependency injection

The library has no DI dependencies of its own, so register the singleton
`SystemClock` against `IClock` with whatever container you use. For
`Microsoft.Extensions.DependencyInjection`:

```csharp
services.AddSingleton<IClock>(SystemClock.Instance);
```

`SystemClock` is stateless and exposed only through `SystemClock.Instance`
(its constructor is private).

## `SystemClock`

The operating-system clock:

- `SystemClock.Instance.UtcNow` returns `DateTimeOffset.UtcNow`.
- `SystemClock.Instance.Sleep(timeout)` delegates to `Thread.Sleep(timeout)`.
- `SystemClock.Instance.TaskDelay(timeout, cancellationToken)` delegates to
  `Task.Delay(timeout, cancellationToken)`.
- `SystemClock.Instance.CancelAfter(source, timeout)` delegates to
  `source.CancelAfter(timeout)`.

Use it in production; use `MockClock` in tests.

## `MockClock`

A clock you control entirely from code. It starts at
`MockClock.DefaultStartTime` (2000-01-01T00:00:00Z) and never advances on its
own.

```csharp
var clock = new MockClock();

clock.UtcNow;                 // 2000-01-01T00:00:00+00:00
clock.UtcNow = someInstant;   // jump straight to a specific time
```

### Advancing time

`AdvanceBy` and `AdvanceTo` move time forward in discrete steps, raising
`Changed` once per step. The step defaults to `AdvancementStep` (one second) and
can be overridden per call:

```csharp
clock.AdvanceBy(TimeSpan.FromSeconds(5));               // five 1-second steps
clock.AdvanceBy(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10)); // six 10s steps
clock.AdvanceTo(MockClock.DefaultStartTime.AddHours(1)); // step up to a target

clock.AdvancementStep = TimeSpan.FromMilliseconds(100);  // change the default step
```

Notes:

- `AdvancementStep` must be **positive**; setting it to zero or a negative value
  throws `ArgumentOutOfRangeException`.
- `AdvanceBy` rejects a **negative** interval, and `AdvanceTo` rejects a target
  **earlier** than the current `UtcNow`, with `ArgumentOutOfRangeException`.
- A non-positive explicit `step` also throws `ArgumentOutOfRangeException`.
- The final assignment always lands exactly on the target, even when the
  interval is not a whole multiple of the step (the remainder is applied as a
  last move).

### The `Changed` event

`Changed` is `Action<MockClock, DateTimeOffset>` and fires whenever `UtcNow`
changes — once per step during `AdvanceBy`/`AdvanceTo`, or once on a direct
assignment:

```csharp
clock.Changed += (changedClock, newValue) =>
    Console.WriteLine($"clock moved to {newValue:O}");
```

Setting `UtcNow` to the value it **already holds** is a no-op and does **not**
raise `Changed`.

### `Sleep` semantics

`MockClock.Sleep` is driven by time advancement rather than wall-clock time:

| `timeout`                        | Behaviour                                                                 |
| -------------------------------- | ------------------------------------------------------------------------- |
| `TimeSpan.Zero`                  | Returns immediately.                                                       |
| Positive, finite                 | Blocks until the clock is advanced to `UtcNow + timeout` (typically from another thread). |
| `Timeout.InfiniteTimeSpan`       | Blocks the **real OS thread** indefinitely; **not** released by advancing the clock — only a thread interrupt ends it. |
| Negative (and not infinite)      | Throws `ArgumentOutOfRangeException`.                                      |

### Testing pattern

A sleeping caller is released by advancing the clock from another thread:

```csharp
var clock = new MockClock();

var worker = new Thread(() => clock.Sleep(TimeSpan.FromSeconds(30)));
worker.Start();

// The worker is blocked until we move time forward:
clock.AdvanceBy(TimeSpan.FromSeconds(30));
worker.Join();
```

Because nothing depends on real elapsed time, the test is deterministic and
runs instantly.

### `TaskDelay` semantics

`MockClock.TaskDelay` is the asynchronous counterpart of `Sleep`. It returns a
task that is also driven by time advancement rather than wall-clock time:

| `timeout`                   | Behaviour                                                                                                            |
| --------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `TimeSpan.Zero`             | Returns an already-completed task.                                                                                   |
| Positive, finite            | The task completes once the clock is advanced to `UtcNow + timeout`.                                                 |
| `Timeout.InfiniteTimeSpan`  | The task completes **only** if `cancellationToken` is cancelled; advancing the clock never releases it (unlike `Sleep`, which only a thread interrupt ends). |
| Negative (and not infinite) | Throws `ArgumentOutOfRangeException` synchronously.                                                                  |

Cancellation never throws synchronously from `TaskDelay`: a cancelled token
(whether already cancelled when called, or cancelled while the task is pending)
transitions the returned task to the canceled state, so awaiting it throws
`TaskCanceledException`.

A pending delay is released by advancing the clock — and because `TaskDelay`
returns immediately, the test can advance the clock on the same thread:

```csharp
var clock = new MockClock();

Task delay = clock.TaskDelay(TimeSpan.FromSeconds(30));

// The task is pending until we move time forward:
clock.AdvanceBy(TimeSpan.FromSeconds(30));

await delay; // completes immediately
```

### `CancelAfter` semantics

`MockClock.CancelAfter` schedules the cancellation of a
`CancellationTokenSource` and, like `Sleep` and `TaskDelay`, is driven by time
advancement rather than wall-clock time:

| `timeout`                   | Behaviour                                                                                          |
| --------------------------- | -------------------------------------------------------------------------------------------------- |
| `TimeSpan.Zero`             | Cancels `source` immediately (synchronously, during the call).                                     |
| Positive, finite            | Cancels `source` once the clock is advanced to `UtcNow + timeout`.                                 |
| `Timeout.InfiniteTimeSpan`  | Schedules nothing; `source` is never cancelled by this call, no matter how far the clock advances. |
| Negative (and not infinite) | Throws `ArgumentOutOfRangeException`.                                                               |

A `null` `source` throws `ArgumentNullException`.

Disposal is handled the same way as the real `CancellationTokenSource.CancelAfter`:

- If `source` is disposed **after** the call, while the cancellation is still
  pending, the pending cancellation is silently dropped — advancing the clock
  past the deadline does **not** throw `ObjectDisposedException`.
- If `source` is **already** disposed when `CancelAfter` is called, that is a
  programming error and throws `ObjectDisposedException` synchronously.

A pending cancellation is triggered by advancing the clock, on the same thread:

```csharp
var clock = new MockClock();
using var cts = new CancellationTokenSource();

clock.CancelAfter(cts, TimeSpan.FromSeconds(30));

// The source is not cancelled until we move time forward:
clock.AdvanceBy(TimeSpan.FromSeconds(30));

Assert.True(cts.IsCancellationRequested);
```

Each call schedules an independent cancellation rather than rescheduling a
previous one, so the earliest deadline reached cancels the source; later
scheduled cancellations then become harmless no-ops.
