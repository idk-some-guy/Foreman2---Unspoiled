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

That last row matters directly: Task 1's fix caps the app's exponent at 6, so the
`LowPriorityPower` and `PullOutputNodesPower` multipliers the app can actually produce
top out at `Math.Pow(10, 6) = 1e6`. That is squarely inside HiGHS's comfortable range,
nowhere near either the `1e20` rejection wall or the `1e10` warning threshold. Task 4's
clamp only has to guard against a caller bypassing Task 1's own guard (defense in depth,
not a live numeric-stability concern at today's actual coefficient values).

### (f) linux-x64 verification scope

Confirmed by `.nupkg` inspection only, per (a) and (b) above: the file exists at
`runtimes/linux-x64/native/highs.so`, is a genuine x86-64 ELF shared object (not a
placeholder or a renamed copy of another RID's binary), and is 6.1 MB. Actually running
the LP probe on Linux is a disclosed deferred item, the same convention phase 8 used for
Linux-specific verification: nothing here claims the Linux binary has been executed, only
that it is present, correctly architected, and matches the same package version and
license as the verified `osx-arm64` binary.

Verdict: SPIKE-POSITIVE — Highs.Native 1.15.1
