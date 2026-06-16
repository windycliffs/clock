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
<MajorVersion>0</MajorVersion>
<MinorVersion>2</MinorVersion>
<Revision>0</Revision>
```

These drive `Version`, `AssemblyVersion`, and `FileVersion`, and the library
csproj reuses `Version` as the NuGet `PackageVersion`. To change the published
version, bump the appropriate property here.

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

Whenever you bump the version, record the change in
[CHANGELOG.md](CHANGELOG.md) under the `[Unreleased]` section.

## Releasing

Publishing is automated. Creating a **published GitHub Release** triggers
`.github/workflows/release.yml`, which builds and tests from `src/`, packs
`WindyCliffs.Clock`, and pushes the resulting package to NuGet. Make sure the
version in `src/Directory.Build.props` and the `CHANGELOG.md` entry are updated
before tagging the release.
