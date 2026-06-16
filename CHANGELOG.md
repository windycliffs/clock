# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/) and this project adheres to
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- `IClock.TaskDelay(TimeSpan, CancellationToken)` with `SystemClock` (delegates
  to `Task.Delay`) and `MockClock` (driven by the existing `AdvanceBy`/`AdvanceTo`
  time-advancement machinery, and cancellable) implementations.
- `global.json` pinning the .NET 10 SDK band, `Directory.Build.targets`, and
  `Directory.Packages.props` enabling Central Package Management.
- `CHANGELOG.md`, `CONTRIBUTING.md`, and `AGENTS.md` at the repository root.
- `docs/ARCHITECTURE.md` and `docs/USAGE.md`; usage section added to `README.md`.

### Changed
- Test project migrated from xunit 2 to **xunit 3** (`xunit.v3`).
- Test target frameworks changed from `net480;net6.0;net8.0` to
  `net48;net8.0;net10.0` (dropped end-of-life .NET 6, added .NET 10).
- Solution converted from `WindyCliffs.Clock.sln` to the `repo.slnx` format.
- CI (`release.yml`) now runs all `dotnet` commands from `src/` and provisions
  the SDK from `global.json`.

## [0.2.0]

### Added
- `IClock.Sleep(TimeSpan)` with `SystemClock` and `MockClock` implementations,
  including `MockClock` time-advancement (`AdvanceBy`/`AdvanceTo`) support.
