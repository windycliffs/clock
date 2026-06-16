# Architecture

`WindyCliffs.Clock` is a small library that abstracts time-dependent operations
behind a single interface so that code which reads the clock or sleeps can be
tested deterministically.

## The `IClock` abstraction

All consumers depend on `IClock` rather than on `DateTimeOffset.UtcNow` or
`Thread.Sleep` directly:

```csharp
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    void Sleep(TimeSpan timeout);
    Task TaskDelay(TimeSpan timeout, CancellationToken cancellationToken = default);
}
```

Injecting `IClock` (for example via a DI container) lets production code use the
real operating-system clock while tests substitute a fully controllable clock.

## Implementations

### `SystemClock`

The production implementation, bound to the operating-system clock. It is a
stateless singleton exposed through `SystemClock.Instance`:

- `UtcNow` returns `DateTimeOffset.UtcNow`.
- `Sleep(timeout)` delegates to `Thread.Sleep(timeout)`.
- `TaskDelay(timeout, cancellationToken)` delegates to `Task.Delay(timeout, cancellationToken)`.

### `MockClock`

The test implementation. Time never advances on its own — the test code drives
it explicitly, which makes time-dependent behaviour deterministic.

- `UtcNow` is a settable property backed by an `Interlocked`-guarded file-time
  field, so reads and writes are thread-safe.
- Setting `UtcNow` to a **different** value raises the `Changed` event; setting
  it to the value it already holds is a no-op and raises nothing.
- `AdvanceBy(interval)` / `AdvanceTo(target)` move time forward in discrete
  steps (`AdvancementStep`, default one second), raising `Changed` once per
  step. Overloads accept an explicit step.

## How `MockClock.Sleep` works

`MockClock.Sleep` does not block on wall-clock time. Instead it cooperates with
the clock's `Changed` event through a private `ScheduledAction`:

1. `Sleep(timeout)` validates the timeout (zero returns immediately; a negative
   non-infinite value throws `ArgumentOutOfRangeException`).
2. For a finite positive timeout it creates a `ManualResetEventSlim` and a
   `ScheduledAction` targeted at `UtcNow + timeout`, then waits on the event.
3. `ScheduledAction` subscribes to `Changed`. Each time the clock advances it
   checks whether the target time has been reached; when it has, it invokes its
   action (signalling the waiter) and unsubscribes. The check is guarded by a
   lock and a disposed flag so the action fires exactly once.
4. Advancing the clock past the target (via `AdvanceBy`/`AdvanceTo`, typically
   from another thread) is therefore what releases a sleeping caller.

`Sleep(Timeout.InfiniteTimeSpan)` is the one exception: it calls
`Thread.Sleep(Timeout.InfiniteTimeSpan)` and blocks the real OS thread forever.
It is **not** released by clock advancement — only a thread interrupt ends it.

## How `MockClock.TaskDelay` works

`MockClock.TaskDelay` is the asynchronous sibling of `Sleep` and uses the same
`ScheduledAction` mechanism, but instead of signalling a `ManualResetEventSlim`
it completes a `TaskCompletionSource`:

1. `TaskDelay(timeout, cancellationToken)` validates the timeout (negative
   non-infinite throws `ArgumentOutOfRangeException`), returns an already-cancelled
   task if the token is already cancelled, and returns a completed task for
   `TimeSpan.Zero`.
2. For a finite positive timeout it creates a `TaskCompletionSource` and a
   `ScheduledAction` targeted at `UtcNow + timeout`; when the clock advances past
   the target the action completes the task. The source is created with
   `TaskCreationOptions.RunContinuationsAsynchronously` so that user continuations
   do **not** run on the thread driving `AdvanceBy`/`AdvanceTo` (the `Changed`
   handler fires synchronously during advancement).
3. If the token can be cancelled, a registration disposes the `ScheduledAction`
   and cancels the task when the token fires; the registration is released once
   the task settles. The first of completion/cancellation wins (both use
   `TrySet*`).

Unlike infinite `Sleep` — which blocks a real OS thread that only a thread
interrupt can release — `TaskDelay(Timeout.InfiniteTimeSpan)` schedules nothing
and is released only by cancelling the token, never by advancing the clock.

## Target frameworks and build layout

- The production library targets **`netstandard2.0`** deliberately, to remain
  consumable by the widest range of .NET runtimes (including .NET Framework and
  modern .NET). This is a compatibility choice and should not be narrowed
  casually.
- The test project multi-targets **`net48;net8.0;net10.0`** so the behaviour is
  exercised on both the .NET Framework and modern CoreCLR runtimes.
- All build artefacts (`global.json`, `Directory.Build.*`,
  `Directory.Packages.props`, `.editorconfig`, `repo.slnx`) live under `src/`,
  which is the build root. See [AGENTS.md](../AGENTS.md) for the full layout and
  why `dotnet` commands run from `src/`.
