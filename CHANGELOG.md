# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and this project adheres to
[Semantic Versioning](https://semver.org/).

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
