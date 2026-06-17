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
    IDisposable StartTimer(object? state, TimeSpan dueTime, TimeSpan interval, TimerCallback callback);
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
- `StartTimer(state, dueTime, interval, callback)` constructs a
  `System.Threading.Timer(callback, state, dueTime, interval)` and returns it (the
  timer is itself the `IDisposable`). All argument validation is the `Timer`
  constructor's own.

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
the clock's `Changed` event through an internal `ScheduledAction` (a small helper
class, shared by `Sleep`, `TaskDelay`, and `CancelAfter`, that runs an action once
the clock reaches a target time and then unsubscribes):

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

`MockClock.CancelAfter` validates its arguments, then delegates the scheduling to
an internal `ScheduledCancellationCollection`:

1. `CancelAfter(source, timeout)` validates its arguments (null `source` throws
   `ArgumentNullException`; a negative non-infinite `timeout` throws
   `ArgumentOutOfRangeException`) and then reads `source.Token.IsCancellationRequested`.
   The `Token` getter throws `ObjectDisposedException` if the source is **already**
   disposed — surfacing that programming error synchronously, exactly as the real
   `CancellationTokenSource.CancelAfter` does — and an already-cancelled token
   short-circuits (nothing left to schedule).
2. It then calls `ScheduledCancellationCollection.AddOrReplace(source, timeout)`.

`ScheduledCancellationCollection` is owned by the `MockClock` and keeps at most one
pending cancellation per `source`, in a `Dictionary` keyed by reference
(`CancellationTokenSource` uses reference equality) guarded by a dedicated lock.
`AddOrReplace`, **atomically under that lock**, removes any pending cancellation for
the source and (for a positive timeout) schedules a new `ScheduledCancellation` —
so a later call **reschedules** an earlier deadline (matching
`CancellationTokenSource.CancelAfter`) with no lost update even under concurrent
calls. `Timeout.InfiniteTimeSpan` schedules nothing (clearing the previous entry);
`TimeSpan.Zero` cancels immediately.

A `ScheduledCancellation` is a `ScheduledAction` subclass whose action cancels the
source and then removes its own entry (by key) from the collection. The
`source.Cancel()` call is wrapped in a narrow `catch (ObjectDisposedException)`, so
a source disposed **after** the call, while the cancellation is still pending, is
handled gracefully — advancing the clock does not propagate the exception — while
errors thrown by user-registered cancellation callbacks still surface. Because
cancellation runs from a `Changed` handler, those callbacks run synchronously on
the thread driving `AdvanceBy`/`AdvanceTo`, consistent with how `MockClock` raises
`Changed` in general.

The replaced action is disposed *after* the lock is released: the fire path holds
the action's own lock while it reacquires the collection lock (to remove its
entry), so disposing under the collection lock would invert that order and risk a
deadlock.

One intentional limitation, appropriate for a test double:

- A `CancelAfter` whose deadline is never reached leaves one `ScheduledAction`
  subscribed to `Changed` (and one dictionary entry) for the clock's lifetime,
  holding the source closure; it is released only when the deadline is reached,
  the cancellation is rescheduled, or the `MockClock` is collected. `MockClock`
  does not implement `IDisposable`, so there is no deterministic cleanup hook —
  this mirrors a real pending `CancelAfter` timer and is acceptable for a clock
  whose lifetime is a single test.

## How `MockClock.StartTimer` works

`MockClock.StartTimer` simply constructs an internal `MockTimer`, which both
validates the arguments and schedules itself. Validation matches the
`System.Threading.Timer` constructor **exactly**: each of `dueTime`/`interval` is
converted to `long` milliseconds (so a sub-millisecond value collapses to zero and
fires immediately, like a real `Timer`) and must fall in `[-1, 0xFFFFFFFE]`, with
the ranges checked before the null-callback check, matching the constructor's
order. The `0xFFFFFFFE` upper bound is `Timer`'s fixed `MaxSupportedTimeout`,
identical on every target runtime, so `MockClock` and `SystemClock` reject the same
values.

`MockTimer` resembles `ScheduledAction` — when it has a due time it subscribes to
`Changed` and checks the current time immediately so a zero/elapsed due time fires
synchronously — but it is a **separate class**, because `ScheduledAction`'s
fire-once-then-self-dispose invariant is incompatible with a periodic timer.
Instead `MockTimer` holds a nullable `nextDueTime` and, on each `Changed`:

1. Under its lock it checks whether `nextDueTime` has been reached; if so it
   **claims the tick** by advancing `nextDueTime` (by `interval` if periodic, or to
   `null` for a one-shot) *before* releasing the lock. Claiming under the lock means
   a concurrent — or re-entrant — advance cannot fire the same tick twice.
2. It then invokes the callback **outside** the lock (the callback is arbitrary
   user code that may advance the clock or dispose the timer; neither must contend
   for the lock while it is held — the same discipline `ScheduledCancellationCollection`
   follows).
3. It loops, so a single advance that crosses several intervals fires the callback
   once per elapsed interval (**catch-up**), making the result independent of the
   advancement step. A one-shot timer's `nextDueTime` becomes `null` after the
   single fire, so the loop terminates. The moment `nextDueTime` becomes `null` —
   meaning the timer can never fire again — `MockTimer` unsubscribes from `Changed`
   so it does not linger on the clock's invocation list for the rest of the test.

An infinite `dueTime` (`-1 ms`) sets `nextDueTime` to `null` at construction (no
arithmetic on `UtcNow`, which would otherwise move the deadline into the past), so
such a timer never even subscribes to `Changed`: it can only be disposed. `Dispose`
sets a disposed flag and unsubscribes from `Changed` under the lock; it is
idempotent and safe from within the callback. A callback already in flight when
`Dispose` runs may still complete, mirroring `System.Threading.Timer.Dispose()`.

Three deliberate differences from a real `System.Threading.Timer`, all consistent
with how `MockClock` already runs scheduled work: the callback runs **synchronously
on the advancing thread** (not a thread-pool thread); missed intervals are **caught
up** one-per-interval rather than coalesced; and an exception thrown by the callback
**propagates** to the `AdvanceBy`/`AdvanceTo` caller (a real timer's thread-pool
callback exception would crash the process), just as user cancellation callbacks
surface through `CancelAfter`.

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
