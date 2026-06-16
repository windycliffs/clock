# AGENTS.md

Guidance for AI agents working in this repository.

## What this is

`WindyCliffs.Clock` is a small .NET library that abstracts time-dependent
operations behind an `IClock` interface so consuming code stays testable.
`SystemClock` is the production (OS-clock) implementation; `MockClock` is a
fully code-controlled clock for deterministic tests. The library is published to
NuGet as `WindyCliffs.Clock`.

## Repository layout

```
.
├── README.md            # Overview + quick start
├── CHANGELOG.md         # Keep a Changelog history
├── CONTRIBUTING.md      # Build/test + versioning rules
├── AGENTS.md            # This file
├── docs/
│   ├── ARCHITECTURE.md  # Design of the abstraction and MockClock mechanism
│   └── USAGE.md         # Detailed API usage
├── .github/workflows/
│   └── release.yml      # Builds, tests, packs, publishes to NuGet via OIDC trusted publishing
└── src/                 # Build root — all build artefacts live here
    ├── global.json              # Pins the .NET 10 SDK band
    ├── Directory.Build.props    # Shared props + package version metadata
    ├── Directory.Build.targets  # Shared targets (placeholder)
    ├── Directory.Packages.props # Central Package Management
    ├── .editorconfig            # Code style
    ├── repo.slnx                # Solution
    ├── WindyCliffs.Clock/       # Library (netstandard2.0)
    │   ├── WindyCliffs.Clock.csproj # Package metadata (PackageReadmeFile etc.)
    │   ├── README.md                # NuGet package README (shipped in the package)
    │   ├── IClock.cs
    │   ├── SystemClock.cs
    │   └── MockClock.cs
    └── WindyCliffs.Clock.Tests/ # xunit 3 tests (net48;net8.0;net10.0)
```

## Building and testing

`src/` is the build root. **Run `dotnet` from `src/`** so the pinned SDK in
`global.json` is resolved (it is found by walking up from the working
directory, so invoking `dotnet` at the repo root would silently use a different
SDK):

```
cd src
dotnet build repo.slnx
dotnet test repo.slnx
```

CI does the equivalent from the repo root by setting `src` as the working
directory (`defaults.run.working-directory: src` in `release.yml`), so its
commands reference `repo.slnx` directly rather than a `src/` prefix.

The build must be **warning-free** and all tests must pass on `net48`, `net8.0`,
and `net10.0`.

## Conventions

- The library targets **`netstandard2.0`** deliberately for broad compatibility;
  do not narrow it without explicit maintainer approval.
- `using` directives go inside the namespace; namespaces are block-scoped;
  members are `this.`-qualified. Match `src/.editorconfig`.
- The package version is bumped via `MajorVersion`/`MinorVersion`/`Revision` in
  `src/Directory.Build.props`, following the rules in
  [CONTRIBUTING.md](CONTRIBUTING.md). Record version changes in `CHANGELOG.md`.
- The NuGet package README is `src/WindyCliffs.Clock/README.md` (shipped in the
  package via `PackageReadmeFile` and rendered on nuget.org). It is **distinct
  from the repo-root `README.md`** (which targets GitHub readers). Keep it
  current whenever the public API or usage changes.

## Breaking changes

**Any breaking change to the library must be confirmed with the human user
before it is made.** A breaking change is anything not backward-compatible for
consumers — removing or renaming public members, changing public signatures, or
altering observable behaviour. When a requested task would require one, stop and
get explicit confirmation first; then bump the version per
[CONTRIBUTING.md](CONTRIBUTING.md) — while `MajorVersion` is `0`, do **not** bump
Major unless explicitly told (see its "Pre-1.0 versioning" note).
