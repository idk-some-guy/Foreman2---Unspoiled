# Foreman2 - Unspoiled

A native macOS and Linux port of [DanielKote/Foreman2](https://github.com/DanielKote/Foreman2), a production-planning tool for Factorio. This is an unofficial port, not affiliated with the upstream project or with Wube Software.

Foreman2 itself is derived from [Nick Powell's original Foreman](https://github.com/Rybadour/Foreman). This project carries that lineage forward on platforms upstream doesn't target.

## Platform support

**macOS (Apple Silicon) and Linux. No Windows, ever** — Windows is already covered by upstream Foreman2, a native WinForms application. See [CONTRIBUTING.md](CONTRIBUTING.md) for where Windows-related requests belong.

macOS support targets Apple Silicon (`osx-arm64`); there is currently no Intel Mac build.

Linux support is implemented and cross-compiled from this macOS development machine, but has not been runtime-verified on an actual Linux install. Specifically, still unverified against a real Linux host:

- End-to-end runtime behavior: app launch, windowing, and Avalonia/SkiaSharp native rendering.
- The Factorio user-data path resolution (`~/.factorio`), sourced from a Factorio forums user report and the wiki's stated default location, not from a real engine install.
- The Steam-on-Linux library path variants (`~/.local/share/Steam`, `~/.steam/steam`, `~/.steam/root`), implemented from common knowledge of Steam's Linux layout, not checked against a real Steam install.
- Desktop integration: the Linux release ships as a plain `tar.gz` with a launcher script, no `.desktop` file or icon registration yet.

If you run this on Linux and hit one of these, an issue report with what actually happened is useful.

## Two versions

This project maintains two long-lived branches with different purposes.

### Ported (`parity` branch)

The `parity` branch is a straight port of upstream Foreman2: it aims to stay functionally identical to upstream, receiving only ports of upstream's own changes. It currently tracks upstream release `2.4.0`.

The in-app version string reads `v 1.0.0 based on 2.4.0`: `1.0.0` is this port's own version, `2.4.0` is the upstream Foreman2 release it currently tracks.

**How to get it:** build from source (see [CONTRIBUTING.md](CONTRIBUTING.md) for the build/packaging commands) or use the packaged macOS dmg / Linux tar.gz once a release is published. Releases are cut from tagged commits on `parity` and attach both artifacts.

### Unspoiled (`main` branch)

The `main` branch, "Unspoiled," is where this project extends freely beyond upstream: modernization, new features, anything that departs from upstream Foreman2's own direction. This section is kept current with every user-facing change as it lands on `main`.

**Current state:** `main` has not yet diverged from `parity` beyond this restructuring (project rebranding, the badged app icon, and the `io.idksome.foreman2.unspoilt` bundle identifier). Feature work on `main` starts after this restructure.

**How to get it:** same as Ported, built from the `main` branch instead. Unspoiled versions follow their own semver as `main` diverges.

## Installing

The macOS build is not notarized or signed with a paid Apple Developer certificate, only ad-hoc signed. macOS Gatekeeper will refuse to open it with a normal double-click. To run it:

1. Move `Foreman2.app` to `/Applications` (or wherever you keep apps).
2. Right-click (or Control-click) the app and choose **Open**.
3. Confirm **Open** in the dialog that appears. You only need to do this once; afterward it opens normally.

On Linux, extract the tar.gz anywhere and run the `foreman2` launcher script inside it.

## Game assets

The bundled icons under `Graphics/` are Factorio artwork, © Wube Software. They're included under the same fan-content posture upstream Foreman2 uses, on the condition that this project stays free and non-commercial.

## License

[Blue Oak Model License 1.0.0](LICENSE.md). ©2021 Daniel Kotes; ©2014 Nick Powell; ©2026 Jozef Tokarcik (macOS/Linux port).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for the branch model, build instructions, and where different kinds of requests belong.
