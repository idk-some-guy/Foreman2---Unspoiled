# Contributing

Foreman2 - Unspoiled is a macOS and Linux port of [DanielKote/Foreman2](https://github.com/DanielKote/Foreman2). Read this before opening an issue or a pull request: the routing rules below decide whether this is even the right repository.

## Windows: never

This project does not support Windows and never will. Windows is already covered by upstream Foreman2, which is a native WinForms application. If you're on Windows, use [upstream](https://github.com/DanielKote/Foreman2) instead.

## Two branches, two purposes

- **`parity`** tracks upstream feature-for-feature. It accepts only ports of changes that landed in upstream Foreman2, nothing else. If upstream adds a feature or fixes a bug, a port of that change belongs here.
- **`main`** ("Unspoiled") is where this project's own work happens: modernization, new features, anything that diverges from upstream. It branches from `parity` and is free to extend in whatever direction makes sense for a native macOS/Linux app.

A pull request against `parity` that adds a feature upstream doesn't have will be redirected to `main` or closed. A pull request against `main` that regresses `parity` behavior on purpose is fine, that's the point of the split.

## Where feature requests go

- **Unspoiled feature requests are welcome here.** Open an issue against `main` for anything you'd like to see in this project's own direction.
- **Windows-related requests go to upstream.** This repository will never add Windows support, so a Windows ask has nothing to attach to here.
- **Requests to get an Unspoiled feature adopted into upstream also go to upstream.** If you want a `main`-only feature to become part of Foreman2 itself, that conversation belongs in upstream's tracker, not here.

Anything else related to Unspoiled (bugs, ideas, questions about `main`) stays here.

## Building and testing

Requires the .NET 10 SDK.

```
dotnet build ForemanMac.slnx
dotnet test ForemanMac.slnx
```

Packaging scripts live under `packaging/`:

- `packaging/build-app.sh <output-dir>` builds and ad-hoc-signs the macOS `.app` bundle.
- `packaging/build-dmg.sh <app-path> <dmg-path>` wraps a built `.app` into a distributable dmg.
- `packaging/build-linux.sh [output-dir]` publishes self-contained for `linux-x64` and packages a `tar.gz`.
- `packaging/build-all.sh [output-dir]` runs the macOS pipeline end to end (publish, bundle, dmg, structural verification).
- `packaging/verify-bundle.sh` and `packaging/verify-linux-package.sh` run structural checks against a built bundle or tarball without needing a signed release or a Linux host.

## Documenting divergence from upstream

Every deliberate difference from upstream Foreman2's behavior, however small, gets a one-line entry in [`docs/upstream-divergences.md`](docs/upstream-divergences.md): what changed, and why. This applies to both branches. A `parity`-branch change that alters behavior for a platform reason (a WinForms API with no macOS/Linux equivalent, say) still needs an entry; it just needs to still behave the same as upstream from the user's point of view.

## Review bar

- Tests are required. New behavior needs a failing test first, then the implementation that makes it pass.
- Changes on `parity` are checked against the actual upstream source, not just against what seems reasonable. If a `parity` PR claims to port an upstream change, the reviewer will read that upstream change and confirm the port matches it.
- Changes on `main` are held to normal code review: tests, and reasoning that holds up, without the upstream cross-check `parity` requires.
