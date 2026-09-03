# Solver evaluation

Plan: `docs/superpowers/plans/2026-09-03-phase9-solver.md`. Decision gate (verbatim from
the plan): identical solve results across the full test-suite graph corpus (29 pre-existing
cases plus Task 1's Infinity-default cases, all within the existing 1e-4 absolute tolerance),
then startup weight and speed. This document is the spike and decision record; Task 2 covers
the spike only, the corpus-parity gate itself runs in Task 4/5.

## HiGHS binding spike

### (a) Binding package and native platform coverage

`Highs.Native` 1.15.1 (NuGet, `authors: ERGO-Code`, the same organization that maintains
HiGHS itself) is the only maintained .NET binding found; no separate official
"HiGHS.NET" package exists on NuGet. It was added to a throwaway console project
(`dotnet add package Highs.Native`) and the resulting cached package at
`~/.nuget/packages/highs.native/1.15.1` was inspected directly on disk, not from the
README's claims:

```
runtimes/
  linux-arm64/native/highs.so
  linux-x64/native/highs.so
  osx-arm64/native/highs.dylib
  osx-x64/native/highs.dylib
  win-x64/native/highs.dll
  win-x86/native/highs.dll
lib/netstandard2.0/Highs.Native.dll
```

Both required platforms, `osx-arm64` and `linux-x64`, ship a prebuilt native binary.
`file` confirms the actual architectures rather than trusting the folder name:

```
runtimes/osx-arm64/native/highs.dylib: Mach-O 64-bit dynamically linked shared library arm64
runtimes/linux-x64/native/highs.so:    ELF 64-bit LSB shared object, x86-64, ... dynamically linked
```

No build step is involved; `dotnet add package` plus `dotnet run` pulled and ran the
`osx-arm64` binary natively on this machine (confirmed native, not Rosetta, since
`dotnet --info` reports `RID: osx-arm64` / `Architecture: arm64` and the loaded dylib is
itself arm64 Mach-O).

### (b) Payload size vs. OrTools

`docs/perf-packaging-reference.md` §1c puts the incumbent OrTools `osx-arm64` tree at
100 separate `.dylib` files totaling roughly 55 MB (`libortools.9.dylib` alone about 23 MB).

Measured directly from the downloaded `Highs.Native` 1.15.1 package:

| Item | Size |
|---|---|
| `runtimes/osx-arm64/native/highs.dylib` | 4,655,952 bytes (4.4 MB) |
| `runtimes/linux-x64/native/highs.so` | 6,375,640 bytes (6.1 MB) |
| `lib/netstandard2.0/Highs.Native.dll` (managed wrapper, same across all RIDs) | 24,064 bytes |
| Full `.nupkg` (all six platforms bundled) | 16,093,210 bytes (15.4 MB) |

A single-RID publish only carries the one matching native file plus the ~24 KB managed
wrapper, so an `osx-arm64` build adds about 4.4 MB total against OrTools' ~55 MB across
100 files, and a `linux-x64` build adds about 6.1 MB. Either is roughly a 90%+ reduction
in native payload and a drop from 100 shipped native files to 1.

### (c) License chain

The package `.nuspec` declares `<license type="expression">MIT</license>`. The solver
binary's own startup banner, printed by the probe run below, confirms the same at
runtime: `Copyright (c) 2026 under MIT licence terms`, and points to
`THIRD_PARTY_NOTICES.md` for bundled components. That file (fetched from the HiGHS
repository) lists, for the default non-HiPO build that `Highs.Native` ships (the banner's
"MIT licence terms" line rules out the separate Apache-licensed HiPO variant mentioned in
the package README): CLI11 (BSD-3, command-line parsing only, not linked into the library
itself), pdqsort (zlib), and zstr (MIT). All three are permissive and compatible with
Blue Oak shipping; nothing in the default build pulls in the Apache-2.0 `metis` or
BSD-3 `amd`/`rcm` components, which only apply to the HiPO-enabled build variant. `Highs.Native`
1.15.1's own license and the HiGHS core license are the same MIT grant.

### (d) Minimal LP probe

Built and run against `osx-arm64` on this machine. Problem, with a known unique optimum:

```
maximize 40x + 30y
subject to:
  x + y  <= 12
  2x + y <= 16
  x, y >= 0
```

Both constraints bind at the optimum: `x = 4, y = 8, objective = 400`.

```csharp
var model = new HighsModel
{
    sense = HighsObjectiveSense.kMaximize,
    colcost = new double[] { 40.0, 30.0 },
    offset = 0.0,
    collower = new double[] { 0.0, 0.0 },
    colupper = new double[] { INF, INF },
    rowlower = new double[] { double.NegativeInfinity, double.NegativeInfinity },
    rowupper = new double[] { 12.0, 16.0 },
    a_format = HighsMatrixFormat.kColwise,
    astart = new int[] { 0, 2, 4 },
    aindex = new int[] { 0, 1, 0, 1 },
    avalue = new double[] { 1.0, 2.0, 1.0, 1.0 },
};

using var solver = new HighsLpSolver();
solver.passLp(model);
solver.run();
var sol = solver.getSolution();
// sol.colvalue[0] == 4, sol.colvalue[1] == 8, solver.getObjectiveValue() == 400
```

Actual output:

```
run status: kOk
model status: kOptimal
x = 4, y = 8
objective = 400
```

Matches the hand-derived optimum exactly.

### (e) Objective-coefficient extremes (critical for Task 4's clamp)

Same base model, `colcost[0]` swapped for each probed value:

| `colcost[0]` | `passLp` | `run` status | model status | Result |
|---|---|---|---|---|
| `double.PositiveInfinity` | `kOk` | `kError` | `kUnknown` | Rejected at solve time: `"Cannot maximize with a cost on variable 0 of inf and upper bound of inf"`. No solution produced. |
| `1e30` | `kOk` | `kError` | `kUnknown` | Same rejection. HiGHS's own presolve log states any `|cost| >= 1e20` is treated as Infinity internally, so `1e30` hits the identical error path as literal `+Infinity`. |
| `1e10` | `kOk` | `kOk` | `kOptimal` | Solves (`x=8, y=0, objective=8e10`), but HiGHS emits `WARNING: Problem has some excessively large costs` and suggests rescaling. Numerically live, not numerically comfortable. |
| `1e6` | `kOk` | `kOk` | `kOptimal` | Solves cleanly (`x=8, y=0, objective=8e6`), zero warnings of any kind. |

So `+Infinity` is not accepted, not silently converted to NaN, and not clamped
internally to a huge-but-finite value that still solves: it is an explicit solve-time
error (`kError` / `kUnknown`), with no solution to read back. Anything at or above
`1e20` hits that same wall. Below that threshold the solver is live; `1e10` still solves
but flags itself as numerically risky, while `1e6` is trivially fine, no warning,
clean optimum.

That last row matters, but only for one of the app's two dials. Task 1's fix caps
`LowPriorityPower`'s exponent at 6 at the `GraphOptimisation.cs` call site, so that
multiplier tops out at `Math.Pow(10, 6) = 1e6`, squarely inside HiGHS's comfortable range.
`PullOutputNodesPower` isn't capped at that same call site: `ProductionGraph`'s own default
of 10 produces `Math.Pow(10, 10) = 1e10`, HiGHS's warning threshold, and only the Settings UI
masks the exponent to `<= 5`, not the solve path itself. `ProductionSolver`'s error-objective
coefficient (`1e2 / Math.Min(1e-3, minRecipeOutRate)`) is uncapped altogether and can clear
`1e7` on an extreme `minRecipeOutRate`. Task 4's clamp is a live guard against those paths,
not just defense in depth against a caller bypassing Task 1's own guard.

### (f) linux-x64 verification scope

Confirmed by `.nupkg` inspection only, per (a) and (b) above: the file exists at
`runtimes/linux-x64/native/highs.so`, is a genuine x86-64 ELF shared object (not a
placeholder or a renamed copy of another RID's binary), and is 6.1 MB. Actually running
the LP probe on Linux is a disclosed deferred item, the same convention phase 8 used for
Linux-specific verification: nothing here claims the Linux binary has been executed, only
that it is present, correctly architected, and matches the same package version and
license as the verified `osx-arm64` binary.

Verdict: SPIKE-POSITIVE — Highs.Native 1.15.1

## Decision gate measurements

All numbers below were captured on this machine (Task 5), independently of the spike. Every
scratch runner lived under a session scratchpad, never staged, and referenced `Foreman.Core.csproj`
directly so the timed code paths are the real `GoogleSolver`/`HighsSolver`/`ProductionSolver`
classes, not a re-implementation.

### (a) Native payload actually bundled, osx-arm64, solver-specific dylibs only

Measured two ways: directly on the NuGet cache copies (`~/.nuget/packages/...`), and by publishing
`Foreman.Mac.csproj` for `osx-arm64` self-contained and inspecting the flat output directory the
publish process produces (both packages are still referenced at this point in Task 5, so the
combined publish output was filtered down to each package's own files by name before totaling).

| Backend | Files | Total size | Source |
|---|---|---|---|
| Google.OrTools (`google.ortools.runtime.osx-arm64` 9.15.6755) | 100 | 55 MB (57,671,680 bytes reported by `du`) | NuGet cache `runtimes/osx-arm64/native/` |
| Google.OrTools, confirmed from a real publish output | 100 (everything except the 13-file .NET-runtime baseline and Highs.Native's own `highs.dylib`) | 55 MB | `dotnet publish -r osx-arm64 --self-contained true` |
| Highs.Native 1.15.1 | 1 (`highs.dylib`) | 4,655,952 bytes (4.4 MB) | NuGet cache `runtimes/osx-arm64/native/` |
| Highs.Native, confirmed standalone (no OrTools referenced at all) | 1 | 4.4 MB | A scratch console app referencing only `Highs.Native`, built+published standalone |

The standalone Highs.Native build is the important check here: `highs.dylib`'s own embedded
install name is `@rpath/libhighs.1.dylib` (a build/packaging quirk — the file is self-identified
under a different name than the one it ships under), which briefly looked like a missing-dependency
risk once OrTools is gone, since OrTools' own runtime package happens to also carry a same-named
`libhighs.1.dylib` (its own bundled MIP solver family, unrelated to our `Highs.Native` reference).
Built and ran the standalone Highs-only console app with no `libhighs.1.dylib` anywhere on disk:
it solves correctly. `otool -L` on `highs.dylib` confirms its actual runtime dependencies are only
`libz`, `libc++`, and `libSystem.B` — all system libraries. No missing-dependency risk.

Net: dropping OrTools for HiGHS cuts the solver-specific native payload from 55 MB across 100 files
to 4.4 MB in a single file, roughly a 92% size reduction and a 99% file-count reduction.

### (b) Cold first-solve wall time, fresh process per run

Each run is a genuinely untouched process: a fresh copy of the solver's native dylib (a new file,
never read by this OS before, so its pages are not in the page cache) plus a freshly built
self-contained `osx-arm64` executable, run exactly once, immediately after building. This is
stricter than "restart the same binary" — restarting a binary whose dylib was already read once
just measures a warm page cache, which is why the first attempt at this measurement (reusing the
same build across repeat runs) undercounted HiGHS by roughly 40x versus the true cold numbers below.
Both runners build the same 2-variable/2-constraint LP from the HiGHS binding spike (§d above)
through the actual `Solver.CreateSolver("GLOP")` / `HighsLpSolver` entry points.

| Backend | Run 1 | Run 2 | Run 3 | Run 4 | Run 5 | Mean (3 official runs) |
|---|---|---|---|---|---|---|
| GoogleSolver (GLOP) | 9656.536 ms | 4390.770 ms | 3881.013 ms | — | — | 5976.1 ms |
| HighsSolver (HiGHS) | 85.103 ms | 84.048 ms | 86.294 ms | 139.922 ms | 102.918 ms | 85.1 ms (3-run); 99.7 ms (5-run) |

GLOP's cold range reproduces phase 7's own documented "94ms-11s cold-cache finding" (§1c of
`docs/perf-packaging-reference.md`) almost exactly — an early, non-official probe on this same
machine hit 11,009.6 ms once, consistent with the high end of that range. HiGHS never approaches
that territory: worst case observed across five independent cold trials is 139.9 ms, best is
84.0 ms, and two isolated non-official trials (a fresh single-file copy tested before the final
5-run series was settled on) landed at 211.8 ms and 213.7 ms — high enough to matter for the
warmup decision below even though they're outliers against the tighter 5-run spread.

### (c) Solve time on the 2984-node scale baseline fixture, warm, 3 runs each

Fixture: `tests/Foreman.Mac.UiTests/Canvas/ScaleGraphPerfTests.cs`'s `BuildLargeGraph` — 1000
recipe nodes plus 1984 supplier/consumer nodes (2984 total), rebuilt node-for-node in a scratch
console runner via `ProductionGraph` directly (no Avalonia/`GraphViewer` needed for a solve-only
measurement). One warm-up solve primes the native dylib and JIT; the three measured runs follow in
the same process, matching "warm" per the phase 7 precedent (§1c's own "current first-`UpdateNodeValues()`
call against a warm-solver baseline" framing).

| Backend | Run 1 | Run 2 | Run 3 | Mean |
|---|---|---|---|---|
| GoogleSolver (production default path, `graph.UpdateNodeValues()`) | 1.715 ms | 1.772 ms | 1.800 ms | 1.762 ms |
| HighsSolver (explicit injection via the same connected-node-group loop `GraphOptimisation.cs` runs) | 1.580 ms | 1.504 ms | 1.506 ms | 1.530 ms |

HiGHS is about 13% faster than GLOP at this scale, warm. One measurement wrinkle worth recording:
`HighsSolver.Solve()` (as Task 3 wrote it) does not set HiGHS's `output_flag` option, so by default
every individual solve prints a full banner and iteration table to stdout. At this fixture's scale
(2984 nodes decompose into ~1000 independent connected components, each solved separately) that's
roughly 1000 verbose banners per pass, which dominates wall time by two to three orders of
magnitude if left on — the raw numbers above were captured with `output_flag` forced off in the
scratch runner. This is a real gap in the shipped `HighsSolver.cs`, not just a measurement artifact:
the same 2984-node graph solved through the unmodified production class would spam stdout on
every node-group solve. Flagged for the swap below.

## Decision

Gate order: corpus identity, then startup weight (payload + cold first-solve), then speed.

**Corpus identity**: already proven 64/64 across both backends (Task 4) — pass.

**Startup weight**: HiGHS wins decisively on both halves. Payload drops from 55 MB across 100 files
to 4.4 MB in one file (a). Cold first-solve drops from a multi-second, `du`-file-count-driven load
(mean 5976 ms, worst case 9657 ms, reproducing phase 7's own 11-second finding) to under 150 ms in
every trial (mean 85-100 ms depending on which run set) — roughly a 60-100x improvement, not a
marginal one.

**Speed**: HiGHS also wins outright at the 2984-node scale fixture, warm — about 13% faster than
GLOP, not merely a tie within the 10% band the gate allows for a payload tradeoff. (The 10% tie
band and the ~100 ms warmup bar below are the controller's dispatch-time decision rules for this
task, recorded here since they aren't in the plan document itself.)

**Ruling: SWAP.** HiGHS beats GLOP on every leg of the gate — it isn't a close call decided by the
10%-tie allowance, it's a clean win on payload, cold-load time, and steady-state solve speed all
at once. The one caveat is that HiGHS's own cold-load, while dramatically better than GLOP's, isn't
reliably under the ~100 ms "no warmup needed" bar (5 fresh-process trials ranged 84-140 ms, with
two earlier single-sample outliers over 200 ms) — so `SolverWarmup` stays, repointed at `HighsSolver`
instead of deleted, rather than being removed outright. The `HighsSolver.Solve()` stdout-spam gap
found while measuring (c) gets fixed as part of the swap: shipping HiGHS as the default without
silencing it would trade a multi-second one-time freeze for a steady stream of solver logging on
every graph edit at scale, which is its own regression.
