# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and this project adheres to
[Semantic Versioning](https://semver.org/).

## [0.6.0]

### Added
- `IClock.TaskWait(Task, TimeSpan, CancellationToken)`,
  `IClock.TaskWaitAny(Task[], TimeSpan, CancellationToken)`, and
  `IClock.TaskWaitAll(Task[], TimeSpan, CancellationToken)` with `SystemClock`
  (delegate to `Task.Wait` / `Task.WaitAny` / `Task.WaitAll`) and `MockClock`
  implementations. With `MockClock` the timeout is measured on the managed time
  scale (it elapses only when the clock is advanced via `AdvanceBy`/`AdvanceTo`),
  while the wait itself still blocks the calling thread. Return values, cancellation,
  and faulted-task (`AggregateException`) behaviour match the underlying BCL methods.

### Changed
- **Breaking:** `IClock` gains three members, so existing third-party implementations
  must add `TaskWait`, `TaskWaitAny`, and `TaskWaitAll` (default interface members are
  not an option on the `netstandard2.0` / C# 9 target). Per the pre-1.0 versioning rule
  this is a minor bump.

## [0.5.0]

### Added
- `IClock.StartTimer(object?, TimeSpan, TimeSpan, TimerCallback)` with `SystemClock`
  (creates a `System.Threading.Timer`) and `MockClock` (drives an internal
  `MockTimer` from the existing `AdvanceBy`/`AdvanceTo` time-advancement machinery)
  implementations. Argument handling matches the `System.Threading.Timer`
  constructor; with `MockClock` the callback fires synchronously on the advancing
  thread, once per elapsed interval when the clock jumps past several at once.

### Changed
- **Breaking:** `IClock` gains a member, so existing third-party implementations
  must add `StartTimer`. Per the pre-1.0 versioning rule this is a minor bump.

## [0.4.1]

### Fixed
- `MockClock.CancelAfter` now reschedules when called more than once on the same
  `CancellationTokenSource`: the most recent call's timeout replaces any
  still-pending one, matching `SystemClock` / `CancellationTokenSource.CancelAfter`.
  Previously each call scheduled an independent cancellation, so the earliest
  deadline incorrectly won.

## [0.4.0]

### Added
- `IClock.CancelAfter(CancellationTokenSource, TimeSpan)` with `SystemClock`
  (delegates to `CancellationTokenSource.CancelAfter`) and `MockClock` (cancels
  the source when the clock is advanced past the timeout, driven by the existing
  `AdvanceBy`/`AdvanceTo` time-advancement machinery) implementations. A source
  disposed after the call, while the cancellation is still pending, is ignored;
  an already-disposed source throws `ObjectDisposedException`, mirroring the
  underlying `CancellationTokenSource.CancelAfter`.

## [0.3.1]

### Added
- MIT license declared on the NuGet package (`PackageLicenseExpression`).

## [0.3.0]

### Added
- `IClock.TaskDelay(TimeSpan, CancellationToken)` with `SystemClock` (delegates
  to `Task.Delay`) and `MockClock` (driven by the existing `AdvanceBy`/`AdvanceTo`
  time-advancement machinery, and cancellable) implementations.

## [0.2.0]

### Added
- `IClock.Sleep(TimeSpan)` with `SystemClock` and `MockClock` implementations,
  including `MockClock` time-advancement (`AdvanceBy`/`AdvanceTo`) support.
