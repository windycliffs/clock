# Contributing

Thanks for contributing to `WindyCliffs.Clock`. This guide covers building,
testing, and how the NuGet package is versioned and released.

## Building and testing

All build artefacts live under `src/`, which is the build root — run every
`dotnet` command from there so `global.json` (the pinned SDK) is honoured:

```
cd src
dotnet build repo.slnx
dotnet test repo.slnx
```

The build must be **warning-free** and all tests must pass on every target
framework (`net48`, `net8.0`, `net10.0`) before a change is merged.

## Code style

C# style is defined in `src/.editorconfig`. Notable conventions: `using`
directives go **inside** the namespace, namespaces are block-scoped, and members
are qualified with `this.`. Keep new code consistent with the surrounding files.

## Versioning

The package version is **not** edited in the `.csproj`. It is assembled in
`src/Directory.Build.props` from three properties:

```xml
<MajorVersion>…</MajorVersion>
<MinorVersion>…</MinorVersion>
<Revision>…</Revision>
```

(The above shows the structure only — the authoritative current values live in
[`src/Directory.Build.props`](src/Directory.Build.props).) These drive `Version`,
`AssemblyVersion`, and `FileVersion`, and the library csproj reuses `Version` as
the NuGet `PackageVersion`. To change the published version, bump the appropriate
property there.

Apply these rules when deciding what to bump:

1. **No breaking changes** — the public API stays backward-compatible. Bump:
   - **Revision** for bug fixes and other small, low-risk changes; or
   - **Minor** for new, additive functionality (and reset `Revision` to `0`).

   Use judgement about the scope of the change to choose between the two.
2. **Breaking changes** — any change that is not backward-compatible for
   consumers (removing or renaming public members, changing signatures or
   observable behaviour). Increment **Major** (and reset `Minor` and `Revision`
   to `0`).

> A breaking change to the library must be confirmed with the human maintainer
> before it is made. See [AGENTS.md](AGENTS.md).

### Pre-1.0 versioning (temporary)

While `MajorVersion` is `0` (pre-release), a breaking change does **not**
increment the Major version. Keep Major at `0` and bump **Minor** instead
(resetting `Revision` to `0`) — **unless the maintainer explicitly instructs a
Major bump**. This overrides rule 2 above for as long as the project is pre-1.0.

**Remove this entire subsection once the version reaches `1.0.0`**, after which
the standard rule 2 (breaking → Major) applies.

Whenever you bump the version, record the change in
[CHANGELOG.md](CHANGELOG.md) under the `[Unreleased]` section.

## Package README

The NuGet package ships a README that is rendered on nuget.org. It is a separate
file from the repository-root `README.md` (which targets GitHub readers):

- **Location:** `src/WindyCliffs.Clock/README.md`.
- **How it is included:** `WindyCliffs.Clock.csproj` sets
  `<PackageReadmeFile>README.md</PackageReadmeFile>` and packs the file via a
  `<None Include="README.md" Pack="true" PackagePath="/" />` item. The file must
  exist at build time — `GeneratePackageOnBuild` packs it during `dotnet build`,
  so a missing or misnamed file fails the build with `NU5039`.
- **When to update:** keep it current whenever the public API or recommended
  usage changes, so consumers browsing nuget.org see accurate guidance. It is
  aimed at external consumers — keep it concise and example-led.

## Releasing

Publishing is automated. Creating a **published GitHub Release** triggers
`.github/workflows/release.yml`, which builds and tests from `src/`, packs
`WindyCliffs.Clock`, and pushes the resulting package to NuGet. Make sure the
version in `src/Directory.Build.props` and the `CHANGELOG.md` entry are updated
before tagging the release.
