# Performance & packaging reference (phase 7 prep)

Source of truth: `upstream/Foreman/ProductionGraphView/{ProductionGraphViewer,GridManager}.cs`, `upstream/Foreman/ProductionGraphView/Elements/{GraphElement,BaseNodeElement,AssemblerElement,BeaconElement}.cs`, `upstream/Foreman/Models/Solver/{GoogleSolver,ProductionSolver,GraphOptimisation}.cs`; on the port side, `src/Foreman.Mac/Canvas/**`, `src/Foreman.Core/Models/ProductionGraph.cs`, `src/Foreman.Core/Models/Solver/**`, `src/Foreman.Core/AppPaths.cs`, every `.superpowers/sdd/*/progress.md` ledger, and `docs/upstream-divergences.md`. Every `file:line` against `upstream/Foreman/` is an upstream path; everything else is this repo.

## 1. Performance

### 1a. How upstream stays fast at 1000+ nodes

Three independent mechanisms, all in `ProductionGraphViewer.OnPaint`/`Paint` (`ProductionGraphView/ProductionGraphViewer.cs:663-730`):

**LOD / simple-draw passthrough.** `NodeDrawingStyle { Regular, PrintStyle, Simple, IconsOnly }` (`:23`) and a separate `LOD { Low, Medium, High }` enum (`:27`, unused by the draw-style switch itself — it gates `DetailsDraw`'s own internal content, not the style dispatch) are two different throttles. The one that actually protects FPS at scale is `NodeCountForSimpleView` (`:40`, default `200`, set at `:114`): the main paint loop picks a style per frame —

```csharp
element.Paint(graphics, FullGraph ? NodeDrawingStyle.PrintStyle : IconsOnly ? NodeDrawingStyle.IconsOnly
    : (visibleElements > NodeCountForSimpleView || ViewScale < 0.2) ? NodeDrawingStyle.Simple : NodeDrawingStyle.Regular);
```
(`:725`, `visibleElements` counted at `:723`). `BaseNodeElement.Draw` (`Elements/BaseNodeElement.cs:197-224`) only calls the expensive `DetailsDraw` (recipe icons, module dots/tally, percentage text) `if (style == Regular || style == PrintStyle)` (`:222`); `Simple`/`IconsOnly` draw just the rounded-rect background/border. `AssemblerElement.Draw`/`BeaconElement.Draw` early-return their own detail content on `IconsOnly || Simple` (`AssemblerElement.cs:47`, `BeaconElement.cs:45`). So past 200 visible nodes *or* below 0.2 zoom, every node collapses to background+border only — no icon draws, no text layout, no module-dot loops.

**Visible-rect culling.** `GraphElement.UpdateVisibility(graphZone)` (`Elements/GraphElement.cs:77-79`) sets `Visible = IntersectsWithZone(graphZone)`, an AABB test against the local bounds (`:69-75`), run once per element per frame against `VisibleGraphBounds` (the current viewport rect in graph space, computed at `:1249`). `GraphElement.Paint` (`:85-93`) is gated on `if (Visible)` before calling `Draw` — an off-screen node in a 1000-node graph never reaches its own draw call at all, regardless of style. This runs before the style-dispatch pass (`:678-684`), so culling and simple-draw compose: only on-screen nodes get styled, and most of those get simplified past the 200 threshold.

**Cached brushes/fonts.** Every `Brush`/`Font` `BaseNodeElement` draws with is `private static readonly`, allocated once per process (`errorBgBrush`, `ManualRateBGFilterBrush`, the three flow-border brushes, `selectionOverlayBrush`, `TextBrush`, `BaseFont`/`CounterBaseFont`/`TitleFont`: `Elements/BaseNodeElement.cs:35-47`). GDI+'s `Brush`/`Font` construction is not free, and upstream never pays it inside the per-frame draw path — only `GraphicsStuff.FillRoundRect`/`DrawText` calls reuse these statics.

No bitmap/render-target caching exists beyond this (no cached `Bitmap` per node, no dirty-rect repaint — `Invalidate()` always repaints the whole control, `:660`). The three mechanisms above are the entire FPS story at scale.

### 1b. Our hot paths, audited

| Mechanism | Port status | Citation |
|---|---|---|
| LOD/`NodeCountForSimpleView` style dispatch | Faithful — same threshold (`200`), same `ViewScale < 0.2f` fallback, same `Simple`/`IconsOnly` skip of detail draws | `src/Foreman.Mac/Canvas/GraphViewer.cs:52,420-424`; `Elements/BaseNodeElement.cs` mirrors `:197-224`'s style branch (verified in port-reading, not re-quoted here) |
| Visible-rect culling | Faithful — `GraphElement.UpdateVisibility`/`Paint`'s `if (Visible)` gate ported 1:1, run once per element per frame before the style pass | `src/Foreman.Mac/Canvas/GraphViewer.cs:405-407` (visibility pass), `Elements/GraphElement.cs` (port) |
| `GetPaintingOrder()` walked 4x/frame | Faithful — upstream does the identical thing (visibility pass, `PrePaint` pass, `visibleElements` `Count`, paint loop, each a separate `GetPaintingOrder()` call: `ProductionGraphViewer.cs:678,683,719,723-724`); port matches (`GraphViewer.cs:406,417,420,425`). Each call is a cheap `yield return` iterator over Annotations+Links+Nodes (`GraphViewer.cs:357-365`), not a materialized list, so 4x enumeration ≠ 4x allocation. Not a regression, not free either — a legitimate future micro-opt (materialize once per `Paint` call) that upstream itself never took. |
| Grid paint (`GridManager`) | **Correct** — cached `static readonly SKPaint` fields for every pen/fill (`GridPaint`, `GridMajorPaint`, `GridFillPaint`, `ZeroAxisPaint`, `LockedAxisPaint`), matching upstream's cached-`Pen` pattern; called once per frame, not per element | `src/Foreman.Mac/Canvas/GridManager.cs:22-26` |
| Per-element `SKPaint` construction | **Port-introduced cost.** Every node/link/annotation `Draw` allocates fresh `SKPaint` objects inline via `using var paint = new SKPaint {...}`, disposed at end of scope — correct for leaks (no leak: `using` guarantees disposal) but not for allocation churn. Upstream's static-`Brush` pattern was not carried into these draw paths the way it was into `GridManager`. At 1000+ visible nodes (or thousands of links), this is thousands of `SKPaint` constructions + disposals every single frame, where upstream's equivalent cost is zero (brushes are process-lifetime singletons). Concretely: `BaseNodeElement.cs:215,220,223,229,236` (`Fill(color)` helper at `:240` constructs a new paint every call), `BaseLinkElement.cs:231` (one link, one draw, one paint — scales directly with visible link count), `PassthroughNodeElement.cs:57,63,71`, `TextAnnotationElement.cs:161,173`, `ShapeAnnotationElement.cs:81,88`, `AnnotationElement.cs:251,262,265`. Real fix shape: promote the fixed-color paints (border/background/manual-rate/warning/selection-overlay fills, all keyed by a small closed set of `SKColor`s) to `static readonly SKPaint` fields the way `GridManager` already does; `SKPaint.Color` is mutable, so a single cached instance per fixed color is sufficient — no per-draw-call state needs to vary except stroke width on a few link/arrow paints, which can stay allocated once and mutated rather than reconstructed. |
| `IconButton.Bake` PNG round-trip | Cached per-instance (dirty-flag gated, `IconButton.cs:97-107`), not a per-frame cost — but every rebuild still does `SKSurface.Snapshot()` → `Encode(Png)` → `MemoryStream` → `new Bitmap(stream)` decode (`IconButton.cs:117-122`) instead of a raw pixel copy, because avoiding a nested Skia GPU lease (Metal render-target corruption, see `docs/upstream-divergences.md` and the phase-5a gate-bug ledger) forced this codec round-trip as the workaround. Already catalogued as a phase-7 perf backlog item at the point it was introduced. | `src/Foreman.Mac/Canvas/Panels/IconButton.cs:117-122`; `.superpowers/sdd/2026-09-02-phase5a-floating-panels/progress.md:32` ("PERF BACKLOG rider (phase 7): per-IconButton PNG-encode roundtrip in Bake — consider raw pixel copy instead of PNG codec") |
| `SettingsWindow` recipe-tooltip pre-bake | Boot-adjacent, not per-frame: `LoadUnfilteredLists` unconditionally bakes a full `RecipePainter` PNG tooltip for **every** recipe in the unfiltered list the moment Settings opens (`foreach (EnabledObjectsListItem item in unfilteredRecipeList) item.TooltipContent = new Image { Source = BakeRecipeTooltip(recipe) }`), one `SKSurface`→PNG→`Bitmap` round-trip per recipe, with no memoization and no lazy/on-hover gate. Icon bakes on the same screen *are* memoized (`bakedIconCache`), tooltip bakes are not. At preset scale (hundreds of recipes) this is a real, avoidable chunk of Settings-window-open latency; low priority since it's a one-screen one-time cost, not a render-loop cost. | `src/Foreman.Mac/Views/SettingsWindow.axaml.cs:389-391` (eager loop), `:429-435` (`GetOrBakeIcon`, memoized, contrast case), `:449-455` (`BakeRecipeTooltip`, not memoized) |
| `ImportNodesFromDocument` scale | Bounded one-time cost per paste/import, not per-frame, but worth flagging at scale: snapshots existing node-element keys into a `HashSet` (`GraphViewer.cs:200`), then after insert does a LINQ `.Where` scan over the **entire** `NodeElementDictionary` to find the newly-added elements (`:205`) — O(old+new) — followed by a synchronous `Graph.UpdateNodeValues()` full LP re-solve (`:223`). Pasting/importing a large sub-graph into an already-1000+-node graph runs this scan-then-solve entirely on the UI thread with no chunking or yield point — same synchronous-solve-on-UI-thread class of issue as §1c below, just triggered by paste/import instead of node-add. | `src/Foreman.Mac/Canvas/GraphViewer.cs:199-225` |

Net read: the *macro* scaling story (LOD, culling, threshold) was ported faithfully and is not a phase-7 risk. The *micro* allocation story (per-draw-call `SKPaint` churn) is a real, mechanical, low-risk fix that upstream's own static-brush idiom already models — it just wasn't carried past `GridManager` into the element draw paths.

### 1c. The first-node freeze

Jozef's live report (phase 6 ledger, PERF BACKLOG entry): a substantial UI freeze the first time a node is added to a fresh graph, never again afterward. Evidence chain for the leading suspect:

1. **Every `UpdateNodeValues()` call runs synchronously on the calling thread — including the UI thread.** `ProductionGraph.UpdateNodeValues()` calls `OptimizeGraphNodeValues()` directly, no `Task.Run`, no `ConfigureAwait`, no dispatch (`src/Foreman.Core/Models/ProductionGraph.cs:308-313`). Every call site in the shell is a direct, synchronous invocation from a UI event handler — e.g. the first-node-via-link-drag path, `GraphCanvasControl.CreatePassthroughFromLinkDrag` (`src/Foreman.Mac/Canvas/GraphCanvasControl.cs:230-244`), calls `Viewer.Graph.UpdateNodeValues()` at `:239` with nothing between it and the pointer-release handler that triggered it. A grep of `src/Foreman.Mac/Canvas/GraphCanvasControl.cs` for `Task.Run` returns zero matches — there is no background-thread offload anywhere in the node-creation/edit path.
2. **`OptimizeGraphNodeValues` constructs a brand-new native solver on every call.** `GraphOptimisation.OptimizeGraphNodeValues` does `var solver = new ProductionSolver(...)` (`src/Foreman.Core/Models/Solver/GraphOptimisation.cs:28`), and `ProductionSolver`'s constructor calls `GoogleSolver.Create()` → `Solver_t.CreateSolver("GLOP")` (`src/Foreman.Core/Models/Solver/GoogleSolver.cs:14-19`, verbatim upstream code — this class is not touched by the port at all, so this cost exists identically on Windows). `CreateSolver` is the first P/Invoke boundary into `Google.OrTools.LinearSolver`; the *first* call in the process forces the CLR to resolve and dynamically load the native OrTools dependency chain.
3. **That dependency chain is large.** The published `osx-arm64` runtime folder ships **100 separate native `.dylib` files totaling ~55 MB** under `runtimes/osx-arm64/native/` — `libortools.9.dylib` alone is ~23 MB, plus the managed↔native bridge `google-ortools-native.dylib` (~1.7 MB), plus ~98 more (the full Abseil library set, and — despite this codebase only ever asking for the `"GLOP"` LP solver — the bundled Coin-OR `libCbc`/`libClp`/`libCgl`/`libOsi` MIP/LP solver family OR-Tools ships alongside GLOP). None of this is touched, loaded, or JIT-warmed before the first `UpdateNodeValues()` call.
4. **Nothing else plausible is still cold by then.** Icon decoding is not a suspect — `DataCache.LoadAllData` (`src/Foreman.Core/DataCaching/DataCache.cs:74-95`) decodes the full icon set during the modal `DataLoadWindow` boot sequence, well before the user can add a node, so icon-decode paths are already JIT-warm and every icon bitmap already resident by the time the canvas is interactive. A `google-ortools`/`warmup`/`GoogleSolver` grep across all of `src/Foreman.Mac` returns **zero matches** — there is no boot-time solver warmup call anywhere in the shell, confirming the cold path is untouched until the user acts.

Conclusion: the leading, well-evidenced suspect is the **first `Solver_t.CreateSolver("GLOP")` call synchronously loading and dynamically linking a ~55 MB native dependency tree on the UI thread**, triggered by whichever UI action first calls `Graph.UpdateNodeValues()` — which for a fresh graph is exactly "add the first node." Secondary, smaller contributors on the same first hit: JIT of the `GraphOptimisation`/`ProductionSolver`/`GoogleSolver` call graph (first execution of any code path always pays a JIT cost, but this is normally sub-frame and wouldn't read as "substantial"), and static-constructor cost on first touch of node-element types (e.g. `BaseNodeElement.BoldTypeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold)`, `Elements/BaseNodeElement.cs:52`, a lazy static field that only runs on the very first `BaseNodeElement` ever constructed — again, normally cheap, but stacks on top of the solver load in the same frame).

**Intended fix shape (do not implement in this pass — phase 7 scope):** warm the solver off the UI thread at boot, not on first use. Concretely: during `ShellBootstrapper.BootAsync`'s existing async boot sequence (already running preset load on a background path per the `DataLoadWindow` machinery), fire a `Task.Run(() => GoogleSolver.Create())` — or, cheaper and more targeted, a throwaway `new ProductionSolver(...)` + `.Solve()` on an empty/trivial problem — so the native dylib tree is mapped and the P/Invoke stubs and JIT are warm well before the user's first pointer release. This is a pure warmup call (discard the result); no change to `UpdateNodeValues`'s own synchronous-on-caller-thread contract is implied or required — that contract is upstream-faithful and out of scope here. A verification pass (not part of this doc) should time the current first-`UpdateNodeValues()` call against a warm-solver baseline to confirm the fix actually closes the gap before calling it done.

## 2. Deferred-minors inventory

Every parked/backlog/deferred item found across all seven `.superpowers/sdd/*/progress.md` ledgers and `docs/upstream-divergences.md`, deduplicated, with a phase-7 disposition. "Fix in 7" = small, mechanical, in scope for the perf+deferred-minors sweep. "Punt to 9" = cosmetic/UX polish or needs a design decision phase 7 shouldn't make unilaterally. "Non-issue" = looked real, verified not to need action.

| Item | Source | Disposition |
|---|---|---|
| Toolbar clicks (Autoconnect/Align/Add, **and** Settings/Graph Summary modals) while a floating panel is open never reach `GraphCanvasControl.OnPointerPressed` → panel stays open, visibly stale after the toolbar action fires | `.superpowers/sdd/2026-09-02-phase5a-floating-panels/progress.md:28` (original, toolbar-only), widened by `.superpowers/sdd/2026-09-02-phase5b-dialog-windows/progress.md:35-36` ("RE-PARK toolbar-staleness with WIDENED scope: Settings + Graph Summary toolbar buttons now also open modals over a possibly-open panel") | **Fix in 7.** Candidate fix already scoped: a `TopLevel` focus-changed listener that closes the panel when new focus lands on neither the canvas nor a panel descendant, or (simpler) an explicit `FloatingPanelHost.Close()` call at the top of every toolbar command handler and every modal-open call site. |
| `EditRecipePanel`'s internal `ScrollViewer` wheel-scroll is untested (the chooser grid's wheel path is tested; the recipe panel's own scroll host never got the same coverage) | `.superpowers/sdd/2026-09-02-phase5a-floating-panels/progress.md:28` | **Fix in 7** — test-only gap, cheap to close, and the P5a final review explicitly confirmed wheel-suppression does *not* starve this ScrollViewer (bubble-Handled routing is architecturally sound) — so this is a coverage gap, not a suspected bug, low-risk to add. |
| `GraphCanvasControl.OnPointerReleased`'s panel guard doesn't set `e.Handled = true` for symmetry with `OnPointerPressed` (`GraphCanvasControl.cs:516`) — harmless today, no `ContextRequested` recognizer depends on it | `.superpowers/sdd/2026-09-02-phase5a-floating-panels/progress.md:28` | **Fix in 7** — one-liner, explicitly named "first item when work resumes," zero risk. |
| 16 fire-and-forget `_ = Async()` call sites across `MainWindow`/`SettingsWindow`/`GraphSummaryWindow`/`PresetComparatorWindow`/`GraphCanvasControl` leave exceptions unobserved, **including** the verbatim-ported `cache.Assemblers["rocket-silo"]` indexer in `OpenSettingsAsync`'s non-preset-reload branch, which throws `KeyNotFoundException` on a silo-less preset | `docs/upstream-divergences.md:193` ("Phase 7 backlog note (final fix wave)"); rocket-silo specific throw noted again at `.superpowers/sdd/2026-09-02-phase5b-dialog-windows/progress.md:35` (Minor#6) | **Fix in 7** — explicitly named phase-7 scope by the P5b final review itself. Fix shape: wrap each fire-and-forget with a `try/catch` → `ErrorLogging.LogException` (the pattern already used everywhere else in this port), and guard the rocket-silo indexer with `TryGetValue` so a silo-less preset doesn't throw at all — that one's a real latent bug, not just an unobserved-exception hygiene issue. |
| `IconButton.Bake`'s PNG-encode round-trip (`SKSurface`→`Encode(Png)`→`MemoryStream`→`Bitmap` decode) instead of a raw-pixel copy, forced by the Metal nested-GPU-lease workaround | `.superpowers/sdd/2026-09-02-phase5a-floating-panels/progress.md:32`; re-confirmed in §1b above | **Punt to 9**, reason: this is the *fix* for a hard Metal rendering-corruption bug (nested `ISkiaSharpApiLeaseFeature` GPU leases), not incidental debt — replacing PNG codec with a raw-pixel path requires re-deriving pixel-format/stride handling per Avalonia backend (the PNG round-trip was chosen specifically because every Avalonia backend already has to decode PNG correctly, sidestepping backend-specific `WriteableBitmap` pixel-format guarantees). Real gain is bounded (bake only runs on dirty/resize, not per-frame) and the redesign risk of reintroducing the Metal corruption is non-trivial; better done as a deliberate, isolated task with its own live-screenshot verification gate than folded into a broad perf sweep. |
| `ErrorLogging`'s `errorlog.txt` still writes to `AppPaths.ExecutableDirectory` in production (test isolation seam exists via `AsyncLocal<string?> LogFilePath` override, but the real default is unchanged) | `docs/upstream-divergences.md:74`; independently re-confirmed in §3 below (`ErrorLogging.cs:11`) | **Fix in 7** — folds into the file-location policy that already moved every other write (saved graphs, exports, settings, scratch files, imported presets) off the executable directory; this is the one straggler, and it's a packaging blocker (see §3 — a signed `.app` in `/Applications` can't write next to its own executable), not just a style nit. |
| Baseline-caching optimization (`LoadDocument`'s skip-reload-when-same-preset path) is correct-by-mechanism-trace but has no dedicated test | `.superpowers/sdd/2026-09-02-phase6-file-preset-io/progress.md:36` (Task 9, "Minor informational") | **Non-issue for phase 7** — already adjudicated safe by a controller mechanism trace (`ApplyEnabledList` reset-then-apply is idempotent against prior cache state) during phase 6 review; a regression test would be nice-to-have but nothing here is broken or blocking. Low-priority test-debt, punt to 9 if ever picked up. |
| `PresetImporter`'s `AppPaths.ScratchDirectory` is created on demand (`Directory.CreateDirectory(scratchDirectory)`, `PresetImporter.cs:50`) but the directory itself is never removed — only individual files inside it get cleaned up on a failed import (`CleanupFailedImport`, deletes `temp-save.zip` + mod-folder copy). A successful import leaves the (now-empty, since its debug/error files are conditional) `Scratch/` directory behind permanently. | Verified directly against `src/Foreman.Core/DataCaching/PresetImporter.cs` (no `Directory.Delete` call anywhere in the file) | **Non-issue.** `Scratch/` is a designated, permanent, user-owned working directory under `~/Library/Application Support/Foreman/` (the same convention `settings.json` and `Presets/` already use) — leaving an empty or near-empty directory there is not debris in the upstream sense (upstream's equivalent mess landed loose in Foreman's own install folder). No action needed; not a phase-9 candidate either, just working as designed. |
| `RecipeNameOnlyFilter` was written but never read (`IRChooserPanel.cs:103`) before being seeded from settings | `.superpowers/sdd/2026-09-02-phase6-file-preset-io/progress.md:41` | **Already resolved** — fixed same-session per the phase-5b/6 ledger (`POST-REVIEW POLISH 7b1b848: ... RecipeNameOnlyFilter seeded`). Listed here only for completeness of the sweep; no phase-7 action. |
| `ForemanIconCacheFile.cs`/`IconCache.cs` minor items (nearest-neighbour vs bilinear sampling, null-Decode exception fidelity) | `.superpowers/sdd/2026-09-01-phase1-core-extraction/progress.md:8-13` | **Already resolved** in phase 1's own fix round; listed for sweep completeness only. |
| Underline/strikeout dropped on annotation text rendering (disclosed divergence, not silent debt) | `.superpowers/sdd/2026-09-01-phase3-canvas-readonly/progress.md:18` | **Punt to 9** — cosmetic text-rendering feature gap, already disclosed as a deliberate divergence rather than an oversight; no functional risk, not performance-adjacent, better suited to a UI-polish pass than a perf/packaging one. |
| Version label clips at the right edge (full git SHA in the SemVer string) | `.superpowers/sdd/2026-09-01-phase2-app-shell/progress.md:23` | **Punt to 9** — pure cosmetic, zero functional or performance impact, unrelated to this phase's scope. |

## 3. macOS packaging

No packaging scaffolding exists in the repo today — no `Info.plist`, no `.entitlements`, no bundling script, no `RuntimeIdentifier`/`SelfContained` set in either `.csproj` (`Foreman.Mac.csproj` currently has no `<RuntimeIdentifier>`/`<SelfContained>` properties at all). This section is the from-scratch pipeline, verified against the tools actually installed on this machine.

### 3a. Publish

```
dotnet publish src/Foreman.Mac/Foreman.Mac.csproj \
  -c Release -r osx-arm64 --self-contained true \
  -p:PublishSingleFile=false -o /path/to/publish-output
```
`PublishSingleFile` should stay **off**: this app reads its bundled `Content` (Graphics/Mods/Presets/baseCustom.json) as loose files relative to `AppContext.BaseDirectory` (see §3d) — single-file publish extracts to a temp dir at runtime, which would break every one of those relative reads and is unnecessary complexity for a `.app`-bundled distribution anyway. `--self-contained true` is required regardless (no assumption the target Mac has a .NET runtime installed) and is what pulls in the ~55 MB OR-Tools `osx-arm64` native tree documented in §1c.

`dotnet publish`'s output directory, for a project with no special `AppHostFolder`/output-path override, is a **flat directory**: the managed executable, all managed DLLs, all native dylibs (`runtimes/osx-arm64/native/*.dylib` gets flattened into the publish root by the self-contained publish process, not left nested), and every `Content`-item file (`Graphics/*.png`, `Mods/**`, `Presets/**`, `baseCustom.json`) side-by-side with the executable. This flat layout is exactly what §3d depends on.

### 3b. `.app` bundle structure

A macOS app bundle is a directory named `Foreman.app` with this layout:

```
Foreman.app/
  Contents/
    Info.plist
    MacOS/
      Foreman.Mac          ← the published executable + every file from §3a's publish output, unmodified
      (all managed DLLs, native dylibs, Content files — flat, exactly as dotnet publish produced them)
    Resources/
      Foreman.icns
```

**Do not** try to relocate the `Content` files (Graphics/Mods/Presets/baseCustom.json) into `Contents/Resources/` per traditional macOS convention — see §3d for why the flat `Contents/MacOS/` copy is load-bearing, not just convenient. The entire publish output directory from §3a gets copied verbatim into `Contents/MacOS/`; only `Info.plist` (at `Contents/`) and the `.icns` (at `Contents/Resources/`) are packaging-specific additions.

Minimum `Info.plist` keys:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key>            <string>Foreman</string>
  <key>CFBundleDisplayName</key>     <string>Foreman</string>
  <key>CFBundleIdentifier</key>      <string>com.foreman-mac-port.foreman</string>
  <key>CFBundleVersion</key>         <string>2.0.0</string>
  <key>CFBundleShortVersionString</key> <string>2.0.0</string>
  <key>CFBundlePackageType</key>     <string>APPL</string>
  <key>CFBundleExecutable</key>      <string>Foreman.Mac</string>
  <key>CFBundleIconFile</key>        <string>Foreman.icns</string>
  <key>LSMinimumSystemVersion</key>  <string>11.0</string>
  <key>NSHighResolutionCapable</key> <true/>
</dict>
</plist>
```
`CFBundleExecutable` must match the actual output filename `dotnet publish` produces (`Foreman.Mac`, from the `.csproj`'s implicit assembly name — not renamed anywhere in the current project). `LSMinimumSystemVersion` of `11.0` is a reasonable floor for a modern self-contained .NET 10 + Avalonia 11 app; there's no evidence in this repo of a lower target being tested. `FactorioInstallValidator.TryValidateExecutable`'s own `Contents/Info.plist` reader (`docs/upstream-divergences.md` — `CFBundleShortVersionString` read, cited there) is unrelated: that's reading *Factorio's* Info.plist, not Foreman's own.

### 3c. Icon: `.icns` from upstream's original `.ico`

**Policy for phase 7: ship upstream's own icon, unmodified.** The badged/"unspoiled" icon variant is a main-branch-later concern per prior planning context, out of scope here.

Upstream's icon lives at `upstream/Foreman/Foreman2.ico` (621 KB, referenced by `upstream/Foreman/Foreman.csproj:21` — `<ApplicationIcon>Foreman2.ico</ApplicationIcon>`, and shipped as `<Content Include="Foreman2.ico" />` at `:111`). `file` reports it as a 6-frame Windows icon resource topping out at 512×512 with embedded PNG data. Verified, working extraction pipeline (both tools are stock macOS, `/usr/bin/sips` and `/usr/bin/iconutil` — **no third-party install needed**, matching the standing prebuilt/no-build-tools preference):

```bash
# 1. Extract the largest frame as a real PNG (sips's -z resize is a no-op passthrough on raw
#    .ico input unless you force the output format first — verified: naive `sips -z 512 512
#    Foreman2.ico --out x.png` silently emits the original .ico bytes with a .png extension).
sips -s format png upstream/Foreman/Foreman2.ico --out base.png   # → real 512x512 PNG

# 2. Build the iconset (iconutil requires exactly this naming convention)
mkdir Foreman.iconset
for sz in 16 32 128 256 512; do
  sips -z $sz $sz base.png --out "Foreman.iconset/icon_${sz}x${sz}.png"
  sips -z $((sz*2)) $((sz*2)) base.png --out "Foreman.iconset/icon_${sz}x${sz}@2x.png"
done
# icon_512x512@2x.png (1024x1024) is upscaled from the 512px source — upstream ships nothing
# larger, so this is the ceiling; acceptable, same tradeoff any .ico-sourced icns conversion faces.

# 3. Convert
iconutil -c icns Foreman.iconset -o Foreman.icns
```
Verified end-to-end on this machine: step 1 produces a genuine 512×512 PNG (`sips -g pixelWidth` confirms `512`, `file` confirms `PNG image data` — not the raw `.ico` bytes the naive one-step attempt produces); step 3 produces a valid `Mac OS X icon` file (`file` confirms `"ic12" type`, ~2.5 MB) from the 10-file iconset built in step 2. This is a real, tested pipeline, not a guess.

### 3d. Codesigning: ad-hoc only, no notarization, and why that's fine

```bash
codesign --force --deep -s - Foreman.app
```
`-s -` is the ad-hoc signature (no Developer ID, no keychain identity needed — `/usr/bin/codesign` is stock macOS, verified present). This satisfies the *minimum* bar: an unsigned `.app` on modern macOS gets a much harsher Gatekeeper rejection ("is damaged and can't be opened") than a signed-but-unnotarized one, which instead gets the standard "can't be opened because Apple cannot check it for malicious software" warning — recoverable via right-click → Open (bypasses Gatekeeper's translocation/quarantine check for that one launch, then remembered). Ad-hoc signing alone is sufficient to reach the right-click-Open path; it is **not** sufficient to avoid the warning outright — only notarization (Apple's server-side scan) does that, and it requires a paid Developer ID account plus network round-trips to Apple's notary service.

**Why phase 7 never notarizes:** this is a local development build for the person building it, distributed (if at all) as a direct `.app`/`.dmg` handoff, not through a channel where a stranger double-clicks it cold. Right-click-Open is a one-time, one-click step for someone who already trusts the source. Notarization buys nothing here and costs a paid account + build-pipeline complexity (`xcrun notarytool submit` + wait for Apple's async result + staple) for zero practical benefit at this distribution scale. This is the same reasoning that governs the ad-hoc-signing-is-enough call, not a shortcut — revisit only if/when this ever ships to people who don't already trust the source, which is explicitly out of phase 7's scope.

### 3e. DMG

```bash
hdiutil create -volname "Foreman" -srcfolder Foreman.app -ov -format UDZO Foreman.dmg
```
`UDZO` (zlib-compressed, read-only) is the standard distributable-dmg format; `hdiutil` is stock macOS (`/usr/bin/hdiutil`, verified present). No further options needed for a local single-app dmg — no custom background/layout scripting, which is upstream-parity-irrelevant polish outside phase 7's scope.

### 3f. What breaks running from a bundle — verified against the actual write/read paths

**Writes: only one straggler.** A grep of every `File.WriteAllText`/`AppendAllText`/`WriteAllBytes`/`File.Copy`/`Directory.CreateDirectory` call in `src/Foreman.Core` and `src/Foreman.Mac` (excluding tests) confirms every write target resolves through `AppPaths`' user-writable properties — `SavedGraphsDirectory`, `ExportedGraphsDirectory`, `UserDataDirectory`/`UserPresetsDirectory`, `ScratchDirectory` (all under `~/Documents/Foreman/` or `~/Library/Application Support/Foreman/`, `src/Foreman.Core/AppPaths.cs:10-28`) — **except one**: `ErrorLogging.LogFilePath` still defaults to `Path.Combine(AppPaths.ExecutableDirectory, "errorlog.txt")` (`src/Foreman.Core/DataCaching/ErrorLogging.cs:11`). Inside a bundle installed to `/Applications`, `Contents/MacOS/` is not writable by a non-admin user (and even for an admin user, writing into a codesigned bundle's own contents post-signing is the kind of thing macOS increasingly resists) — so the very first uncaught-exception log write after packaging will silently fail (caught internally: `ErrorLogging.LogLine`'s catch just falls back to `Trace.WriteLine`, `ErrorLogging.cs:36`, so this degrades rather than crashes, but silently loses every log line). This is `docs/upstream-divergences.md:74`'s known straggler, now confirmed as the sole remaining write-path bundle-compat gap; fixing it (move the default to `AppPaths.UserDataDirectory`, keeping the existing `AsyncLocal` test-isolation override) folds directly into the phase-7 deferred-minors sweep (§2).

**Reads: verified to keep working, but only because of the flat-copy layout in §3b.** Every bundled-`Content` read resolves through `AppPaths.ExecutableDirectory` (`= AppContext.BaseDirectory`): `IconCache`'s `Graphics/*.png` resolution (`IconCache.cs:40`), `FactorioBundledModHelper`'s `Mods/<name>/*` copy source (`FactorioBundledModHelper.cs:9`), `PresetProcessor`'s bundled `Presets/` directory (`PresetProcessor.cs:19`), `PresetImporter`'s `baseCustom.json` copy source (`PresetImporter.cs:200`), and `PresetResolver`'s `Presets/` scan (`src/Foreman.Mac/Services/PresetResolver.cs:18`). `AppContext.BaseDirectory` for a self-contained-published, non-single-file executable resolves to the directory the executable itself lives in — which, per §3b's flat-copy convention, is `Contents/MacOS/`, the same directory every `Content` file also landed in during `dotnet publish` (§3a). So `Contents/MacOS/Graphics/`, `Contents/MacOS/Mods/`, `Contents/MacOS/Presets/`, and `Contents/MacOS/baseCustom.json` all resolve correctly with **zero code changes**, precisely because the bundle build must not "properly" relocate resources to `Contents/Resources/` the way a native Cocoa app would — doing so would silently break every one of these five read call sites. This is the one packaging-specific gotcha worth stating explicitly: the correct macOS convention is the wrong move here.

**Content-item publish verification:** all four `Content` groups (`Graphics/*.png` in `Foreman.Core.csproj:27-56`, `Mods/foremanexport_2.0.0/**` + `Mods/foremansavereader_2.0.0/**` in `Foreman.Core.csproj:57-58`, `baseCustom.json` in `Foreman.Core.csproj:59`, `Presets/**` in `Foreman.Mac.csproj`) use standard MSBuild `<Content Include="..." CopyToOutputDirectory="PreserveNewest">` (or the file-level `CopyToOutputDirectory` metadata for the individual `Graphics/*.png` entries) — this is the well-established, `dotnet publish`-respected mechanism; no custom publish targets or manual copy steps are needed for these to land in the publish output alongside the executable.
