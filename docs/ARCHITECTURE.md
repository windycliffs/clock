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
    void CancelAfter(CancellationTokenSource source, TimeSpan timeout);
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
- `CancelAfter(source, timeout)` delegates to `source.CancelAfter(timeout)` (after a
  null check on `source`, so a null argument yields `ArgumentNullException` rather
  than `NullReferenceException`).

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

## How `MockClock.CancelAfter` works

`MockClock.CancelAfter` reuses the same `ScheduledAction` mechanism as `Sleep`
and `TaskDelay`, but the scheduled action calls `source.Cancel()`:

1. `CancelAfter(source, timeout)` validates its arguments (null `source` throws
   `ArgumentNullException`; a negative non-infinite `timeout` throws
   `ArgumentOutOfRangeException`) and then probes `source.Token`, which throws
   `ObjectDisposedException` if the source is **already** disposed — surfacing
   that programming error synchronously, exactly as the real
   `CancellationTokenSource.CancelAfter` does. An already-cancelled source then
   short-circuits (nothing left to schedule), again matching the real method. For
   `Timeout.InfiniteTimeSpan` it returns without scheduling anything.
2. Otherwise it creates a fire-and-forget `ScheduledAction` targeted at
   `UtcNow + timeout`. A `TimeSpan.Zero` timeout fires synchronously inside the
   `ScheduledAction` constructor (which checks the current time); a positive
   timeout fires when the clock is advanced past the target. The action cancels
   `source` and the `ScheduledAction` self-disposes.
3. The `source.Cancel()` call is wrapped in a `try`/`catch (ObjectDisposedException)`
   so that a source disposed **after** the call, while the cancellation is still
   pending, is handled gracefully — advancing the clock does not propagate the
   exception. The catch is deliberately narrow, so errors thrown by
   user-registered cancellation callbacks still surface.

`MockClock` keeps at most one pending cancellation per `source`, in a
`ConcurrentDictionary` keyed by reference (`CancellationTokenSource` uses
reference equality). Each call first removes and disposes any cancellation still
pending for that source, then schedules the new one — so a later call
**reschedules** an earlier deadline, matching `CancellationTokenSource.CancelAfter`.
The scheduled action removes its own entry when it fires, using the dictionary's
atomic remove-only-if-the-value-still-matches so a concurrent reschedule is never
clobbered. The previous action is disposed only after it has been removed from the
dictionary, and the dictionary's own operations never call back into user code,
so there is no lock-ordering hazard against the `ScheduledAction` lock taken on
the fire path.

Because `source.Cancel()` runs from a `Changed` handler, registered cancellation
callbacks run synchronously on the thread driving `AdvanceBy`/`AdvanceTo`,
consistent with how `MockClock` raises `Changed` in general.

One intentional limitation, appropriate for a test double:

- A `CancelAfter` whose deadline is never reached leaves one `ScheduledAction`
  subscribed to `Changed` (and one dictionary entry) for the clock's lifetime,
  holding the source closure; it is released only when the deadline is reached,
  the cancellation is rescheduled, or the `MockClock` is collected. `MockClock`
  does not implement `IDisposable`, so there is no deterministic cleanup hook —
  this mirrors a real pending `CancelAfter` timer and is acceptable for a clock
  whose lifetime is a single test.

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
