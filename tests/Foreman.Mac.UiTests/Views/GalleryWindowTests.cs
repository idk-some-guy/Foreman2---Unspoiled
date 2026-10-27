using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas.KitSkia;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using Xunit;
using Slider = Foreman.Mac.Kit.Controls.Slider;
using Spinner = Foreman.Mac.Kit.Controls.Spinner;

namespace Foreman.Mac.UiTests.Views {
    //Task: dev-only gallery smoke test (docs entry: "--gallery" mode). Builds a small fixture cache - a
    //recipe with an electric assembler (modules + beacon) alongside a burner alternative (fuel) - feeding
    //the theme/enabled-objects/comparator showcase tiles below, then proves every expected tile gets built.
    public class GalleryWindowTests {
        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        //Reused by Theming/AppearanceHubWiringTests - GalleryWindow builds its Canvas/Kit tiles at
        //construction, which needs enough of a cache to not throw.
        internal static DataCache NewFixtureCache() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            var group = new GroupPrototype(cache, "production", "Production", "a");
            store.Groups[group.Name] = group;
            var subgroup = new SubgroupPrototype(cache, "production-sub", "a");
            subgroup.MyGroupInternal = group;
            group.SubgroupsInternal.Add(subgroup);
            store.Subgroups[subgroup.Name] = subgroup;

            var ore = new ItemPrototype(cache, "ore", "Ore", subgroup, "a") { Available = true };
            var plate = new ItemPrototype(cache, "plate", "Plate", subgroup, "a") { Available = true };
            var coal = new ItemPrototype(cache, "coal", "Coal", subgroup, "a") { Available = true };
            store.Items[ore.Name] = ore;
            store.Items[plate.Name] = plate;
            store.Items[coal.Name] = coal;

            var recipe = new RecipePrototype(cache, "smelt", "Smelt", subgroup, "a") { Available = true };
            recipe.InternalOneWayAddIngredient(ore, 1);
            ore.ConsumptionRecipesInternal.Add(recipe);
            recipe.InternalOneWayAddProduct(plate, 1, 0);
            plate.ProductionRecipesInternal.Add(recipe);
            store.Recipes[recipe.Name] = recipe;

            var mineCoal = new RecipePrototype(cache, "mine-coal", "Mine Coal", subgroup, "a") { Available = true };
            mineCoal.InternalOneWayAddProduct(coal, 1, 0);
            coal.ProductionRecipesInternal.Add(mineCoal);
            store.Recipes[mineCoal.Name] = mineCoal;

            var electric = new AssemblerPrototype(cache, "electric-assembler", "Electric Assembler", EntityType.Assembler, EnergySource.Electric) {
                Available = true, Enabled = true, ModuleSlots = 2, AllowModules = true, AllowBeacons = true,
            };
            var burner = new AssemblerPrototype(cache, "burner-assembler", "Burner Assembler", EntityType.Assembler, EnergySource.Burner) {
                Available = true, Enabled = true,
            };
            burner.FuelsInternal.Add(coal);

            recipe.AssemblersInternal.Add(electric);
            electric.RecipesInternal.Add(recipe);
            recipe.AssemblersInternal.Add(burner);
            burner.RecipesInternal.Add(recipe);
            mineCoal.AssemblersInternal.Add(electric);
            electric.RecipesInternal.Add(mineCoal);

            //ModulePrototype.Available derives from an Item of the same name existing in the cache.
            var moduleItem = new ItemPrototype(cache, "speed-module", "Speed Module", subgroup, "a") { Available = true };
            store.Items[moduleItem.Name] = moduleItem;
            var module = new ModulePrototype(cache, "speed-module", "Speed Module") { Category = "production" };
            store.Modules[module.Name] = module;
            electric.ModulesInternal.Add(module);
            recipe.AssemblerModulesInternal.Add(module);
            recipe.BeaconModulesInternal.Add(module);

            var beacon = new BeaconPrototype(cache, "beacon-1", "Beacon", EnergySource.Electric) { Available = true, ModuleSlots = 2 };
            store.Beacons[beacon.Name] = beacon;
            beacon.ModulesInternal.Add(module);

            return cache;
        }

        //The declarative registry lists exactly the tiles the window renders, in the same order.
        [AvaloniaFact]
        public void Registry_ListsATileForEveryPanelTheWindowShows() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());

            Assert.Equal(
                ["Type ramp", "Glyphs", "Type ramp & Glyphs (150%)", "Focus ring", "Focus ring (150%)",
                 "Buttons", "Toolbar items", "Buttons & Toolbar items (150%)",
                 "Checkbox", "Radio group", "Switch — Settings only (A16)", "Checkbox, Radio group & Switch (150%)",
                 "Segmented", "Pop-up buttons", "Menu button", "Segmented, Pop-up buttons & Menu button (150%)",
                 "Text field", "Search field", "Stepper", "Slider", "Text field, Search field, Stepper & Slider (150%)",
                 "List rows", "Sidebar rows", "Group box", "Tab strips", "Disclosure",
                 "List rows, Sidebar rows, Group box, Tab strips & Disclosure (150%)",
                 "Tooltip", "Spinner & progress", "Chips", "Key caps",
                 "Tooltip, Spinner & progress, Chips & Key caps (150%)",
                 "Form group (boxed, 132)", "Form rows (inspector, 108)", "Form row notes & errors",
                 "Form row — search field & chip row", "Form group & Form rows (150%)",
                 "Inspector group — expanded, collapsed, hovered, focused", "Split divider — rest, hovered, dragging, focused",
                 "Inspector group & Split divider (150%)",
                 "Pick cell — filled and empty, five states each", "Pick grid — module slots & beacon row",
                 "Pick grid — choice mode, building & beacon",
                 "Pick cell & Pick grid (150%)",
                 "Chooser cell — five states", "Chooser grid — search results, two headings",
                 "Chooser cell & Chooser grid (150%)",
                 "Scrim & Sheet frame", "Button row — five shapes", "Alert frame — two-button & three-button",
                 "Scrim, Sheet frame, Button row & Alert frame (150%)",
                 "Popover frame — anchored below, with a form group inside", "Popover frame — tail clamped near a corner",
                 "Radial ring — six slices, one armed",
                 "Popover frame & Radial ring (150%)",
                 "Color well — rest, focused, open, 40 % alpha, none, disabled",
                 "Color well & Color popover — Fill, None available", "Color well & Color popover (150%)",
                 "State panel — empty (inspector & new graph)", "State panel — no results",
                 "State panel — loading (determinate & compact)", "State panel — error", "State panel (150%)",
                 "Chooser grid — no results (State panel)", "Sheet frame — error (State panel)",
                 "Form group — load failure (State panel)", "Inspector group — body delegation (State panel)",
                 "Zoom stepper — at rest, hovered, solving, solve failed", "Zoom stepper — solve-error popover",
                 "Zoom stepper (150%)", "Solve status — idle, solving, failed, popover", "Solve status (150%)",
                 "Toolbar row — full, labels dropped, overflowed", "Toolbar row (150%)",
                 "Palette panel — quick-add results", "Palette panel — no results", "Palette panel (150%)",
                 "Recent row — selected, hovered, inactive, unavailable & focused", "Recent row (150%)"],
                window.Registry.Tiles.Select(t => t.Name));
        }

        [AvaloniaFact]
        public void Construction_BuildsATilePerExpectedPanel() {
            DataCache cache = NewFixtureCache();
            var settings = new AppSettings();

            var window = new GalleryWindow(cache, settings);
            window.Show();

            string[] captions = [.. window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

            Assert.Contains("Type ramp", captions);
            Assert.Contains("Glyphs", captions);
            Assert.Contains("Type ramp & Glyphs (150%)", captions);
            Assert.Contains("Focus ring", captions);
            Assert.Contains("Focus ring (150%)", captions);
            Assert.Contains("Buttons", captions);
            Assert.Contains("Toolbar items", captions);
            Assert.Contains("Buttons & Toolbar items (150%)", captions);
            Assert.Contains("Checkbox", captions);
            Assert.Contains("Radio group", captions);
            Assert.Contains("Switch — Settings only (A16)", captions);
            Assert.Contains("Checkbox, Radio group & Switch (150%)", captions);
            Assert.Contains("Segmented", captions);
            Assert.Contains("Pop-up buttons", captions);
            Assert.Contains("Pop-up — leading icon", captions);
            Assert.Contains("Pick grid — choice mode, building & beacon", captions);
            Assert.Contains("Menu button", captions);
            Assert.Contains("Segmented, Pop-up buttons & Menu button (150%)", captions);
            Assert.Contains("Text field", captions);
            Assert.Contains("Search field", captions);
            Assert.Contains("Stepper", captions);
            Assert.Contains("Slider", captions);
            Assert.Contains("Text field, Search field, Stepper & Slider (150%)", captions);
            //A sample from each end of the shared glyph set, proving the full set rendered with name labels.
            Assert.Contains("New", captions);
            Assert.Contains("Category.Modules", captions);
        }

        [AvaloniaFact]
        public void ButtonsAndToolbarItemsScaledTile_HeightAndWidthScale() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Cancel") && b.Height == 33);
            Assert.Contains(window.GetVisualDescendants().OfType<ToolbarItem>(), i => i.Height == 60 && i.MinWidth == 66);
        }

        //Addendum §12 "New glyphs": the path column, on the 20 grid.
        private static readonly (string Name, string Path)[] CanvasMasters = [
            ("Node.Over", "M10,16 V4.5 M5.5,9 L10,4.5 L14.5,9"),
            ("Node.Short", "M10,4 V15.5 M5.5,11 L10,15.5 L14.5,11"),
            ("Node.Error", "M10,4 V11.5 M10,15.6 V15.7"),
            ("Node.Key", "M2.8,10 A3.2,3.2 0 1,0 9.2,10 A3.2,3.2 0 1,0 2.8,10 Z M9.2,10 H17 M14,10 V13 M16.5,10 V12.5"),
            ("Canvas.Paused", "M7.5,5 V15 M12.5,5 V15"),
            ("Link.Orthogonal", "M3,15 H9 V5 H17 M7.2,10 A1.8,1.8 0 1,0 10.8,10 A1.8,1.8 0 1,0 7.2,10 Z"),
            ("Link.Rounded", "M3,15 C9,15 11,5 17,5"),
        ];

        //The tile draws each canvas master from the kit geometry; GlyphPaths hands the painters the same shapes.
        [AvaloniaFact]
        public void GlyphsTile_DrawsTheSevenCanvasMasters_ThatGlyphPathsMirrors() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Glyphs");
            foreach ((string name, string addendumPath) in CanvasMasters) {
                Application.Current!.TryGetResource($"Kit.Glyph.{name}.20", null, out object? resource);
                StackPanel cell = tile.GetVisualDescendants().OfType<StackPanel>()
                    .Single(c => c.Children.OfType<TextBlock>().Any(t => t.Text == name));
                Path path = cell.GetVisualDescendants().OfType<Path>().Single();
                Assert.Same(Assert.IsAssignableFrom<Geometry>(resource), path.Data);
                Assert.True(path.Bounds.Width > 0 && path.Bounds.Height > 0, name);

                using SkiaSharp.SKPath expected = SkiaSharp.SKPath.ParseSvgPathData(addendumPath);
                string key = $"Kit.Glyph.{name}";
                Assert.Equal(expected.ToSvgPathData(), GlyphPaths.Get(key).ToSvgPathData());
                Assert.Equal(GlyphPaths.Get(key).ToSvgPathData(), SvgOfKitText($"{key}.20"));
                if (name.StartsWith("Node.", StringComparison.Ordinal)) {
                    expected.Transform(SkiaSharp.SKMatrix.CreateScale(0.5f, 0.5f));
                    Assert.Equal(expected.ToSvgPathData(), GlyphPaths.GetBadge(key).ToSvgPathData());
                    Assert.Equal(GlyphPaths.GetBadge(key).ToSvgPathData(), SvgOfKitText($"{key}.10"));
                }
            }
            Assert.Same(GlyphPaths.Get("Kit.Glyph.Node.Key"), GlyphPaths.Get("Kit.Glyph.Node.Key"));
        }

        private static string SvgOfKitText(string key) {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
            XDocument glyphs = XDocument.Load(System.IO.Path.Combine(root, "src", "Foreman.Mac.Kit", "Themes", "Glyphs.axaml"));
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            string text = glyphs.Descendants().Single(e => (string?)e.Attribute(x + "Key") == key).Value;
            using SkiaSharp.SKPath parsed = SkiaSharp.SKPath.ParseSvgPathData(text);
            return parsed.ToSvgPathData();
        }

        [AvaloniaFact]
        public void KitScaledTile_GlyphSheetViewboxIsOneAndAHalfTimesTheDefaultTilesWhileGeometryStaysAtMasterSize() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel defaultTile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Glyphs");
            StackPanel scaledTile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Type ramp & Glyphs (150%)");

            Viewbox defaultBox = defaultTile.GetVisualDescendants().OfType<Viewbox>().First(v => v.Child is Path);
            Viewbox scaledBox = scaledTile.GetVisualDescendants().OfType<Viewbox>().First(v => v.Child is Path);
            Assert.Equal(defaultBox.Bounds.Width * 1.5, scaledBox.Bounds.Width, 2);

            Path defaultPath = (Path)defaultBox.Child!;
            Path scaledPath = (Path)scaledBox.Child!;
            Assert.Equal(defaultPath.RenderedGeometry!.Bounds, scaledPath.RenderedGeometry!.Bounds);
        }

        [AvaloniaFact]
        public void SelectionControlsScaledTile_SwitchKnobXTracksScaledInset() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Checkbox, Radio group & Switch (150%)");
            Application.Current!.TryGetResource("Foreman.Size.SwitchKnobInset", Application.Current.ActualThemeVariant, out object? insetObj);
            double expectedX = (double)insetObj! * 1.5;

            Border[] knobs = [.. tile.GetVisualDescendants().OfType<Border>().Where(b => b.Name == "PART_Knob")];
            Assert.Contains(knobs, k => k.RenderTransform is TransformOperations ops && Math.Abs(ops.Value.M31 - expectedX) < 0.01);
        }

        [AvaloniaFact]
        public void MenuPrimitivesScaledTile_SegmentedLabelFontSizeTracksScaledCallout() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Segmented, Pop-up buttons & Menu button (150%)");
            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? calloutObj);
            double expected = (double)calloutObj! * 1.5;

            TextBlock[] labels = [.. tile.GetVisualDescendants().OfType<Segmented>()
                .SelectMany(s => s.GetVisualDescendants().OfType<TextBlock>())
                .Where(t => t.Classes.Contains("segLabel"))];
            Assert.Contains(labels, l => Math.Abs(l.FontSize - expected) < 0.01);
        }

        [AvaloniaFact]
        public void MenuPrimitivesScaledTile_ChevronViewboxScalesToFifteenSquare() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            //The chevron Path keeps Stretch=None at its master size; a wrapping Viewbox scales the drawn box.
            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Segmented, Pop-up buttons & Menu button (150%)");
            Viewbox[] chevronBoxes = [.. tile.GetVisualDescendants().OfType<Viewbox>()
                .Where(v => v.Child is Path p && (p.Name == "PART_Chevron" || p.Name == "PART_PulldownChevron"))];
            Assert.Contains(chevronBoxes, v => v.Bounds.Width == 15 && v.Bounds.Height == 15);
        }

        [AvaloniaFact]
        public void MenuPrimitivesScaledTile_ChevronRenderedGeometryMatchesItsUnscaledMaster() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            //Stretch="None" means the Path's own RenderedGeometry never inflates; only the ancestor Viewbox grows.
            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Segmented, Pop-up buttons & Menu button (150%)");
            Path[] chevrons = [.. tile.GetVisualDescendants().OfType<Path>().Where(p => p.Name == "PART_Chevron" || p.Name == "PART_PulldownChevron")];
            var master = (Geometry)Application.Current!.FindResource("Kit.Glyph.Chevron.10")!;
            Assert.Contains(chevrons, c =>
                Math.Abs(c.RenderedGeometry!.Bounds.Width - master.Bounds.Width) < 0.01 &&
                Math.Abs(c.RenderedGeometry.Bounds.Height - master.Bounds.Height) < 0.01);
        }

        [AvaloniaFact]
        public void FieldControlsScaledTile_StepperValueBoxFontSizeTracksScaledCallout() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Text field, Search field, Stepper & Slider (150%)");
            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? calloutObj);
            double expected = (double)calloutObj! * 1.5;

            TextBox[] boxes = [.. tile.GetVisualDescendants().OfType<Stepper>()
                .SelectMany(s => s.GetVisualDescendants().OfType<TextBox>())
                .Where(b => b.Name == "PART_ValueBox")];
            Assert.Contains(boxes, b => Math.Abs(b.FontSize - expected) < 0.01);
        }

        [AvaloniaFact]
        public void FieldControlsScaledTile_SliderValueLabelMinWidthTracksScaledMetric() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Text field, Search field, Stepper & Slider (150%)");

            TextBlock[] labels = [.. tile.GetVisualDescendants().OfType<Slider>()
                .SelectMany(s => s.GetVisualDescendants().OfType<TextBlock>())
                .Where(t => t.Name == "PART_ValueLabel")];
            Assert.Contains(labels, l => l.MinWidth == 54);
        }

        [AvaloniaFact]
        public void RowsAndChromeScaledTile_ListRowLabelFontSizeTracksScaledCallout() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "List rows, Sidebar rows, Group box, Tab strips & Disclosure (150%)");
            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? calloutObj);
            double expected = (double)calloutObj! * 1.5;

            TextBlock[] labels = [.. tile.GetVisualDescendants().OfType<ListRow>()
                .SelectMany(r => r.GetVisualDescendants().OfType<TextBlock>())
                .Where(t => t.Name == "PART_Label")];
            Assert.Contains(labels, l => Math.Abs(l.FontSize - expected) < 0.01);
        }

        [AvaloniaFact]
        public void FeedbackScaledTile_ChipCloseGlyphViewboxWidthTracksScaledGlyphMicro() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            //PART_CloseGlyph keeps Stretch=None at its 12pt master size; a wrapping Viewbox scales the drawn box.
            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Tooltip, Spinner & progress, Chips & Key caps (150%)");
            Application.Current!.TryGetResource("Foreman.Size.GlyphMicro", Application.Current.ActualThemeVariant, out object? glyphMicroObj);
            double expected = (double)glyphMicroObj! * 1.5;

            Viewbox[] boxes = [.. tile.GetVisualDescendants().OfType<Chip>()
                .SelectMany(c => c.GetVisualDescendants().OfType<Viewbox>())
                .Where(v => v.Child is Path p && p.Name == "PART_CloseGlyph")];
            Assert.Contains(boxes, v => Math.Abs(v.Bounds.Width - expected) < 0.01);
        }

        [AvaloniaFact]
        public void FocusRingScaledTile_KeepsTheRingWidthAndOffsetFixed() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            //Scoped to the two Focus ring tiles: composites legitimately reuse "Save" as a Kit.ButtonRow
            //default-button label elsewhere in the gallery.
            StackPanel[] focusRingTiles = [.. window.GetVisualDescendants().OfType<StackPanel>()
                .Where(s => s.Name is "Focus ring" or "Focus ring (150%)")];
            Button[] saveButtons = [.. focusRingTiles.SelectMany(t => t.GetVisualDescendants().OfType<Button>())
                .Where(b => Equals(b.Content, "Save"))];
            Assert.Equal(2, saveButtons.Length);

            Rectangle defaultRing = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(saveButtons[0]));
            Rectangle scaledRing = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(saveButtons[1]));

            Assert.Equal(3, defaultRing.StrokeThickness);
            Assert.Equal(3, scaledRing.StrokeThickness);
            Assert.Equal(new Thickness(-3.5), defaultRing.Margin);
            Assert.Equal(new Thickness(-3.5), scaledRing.Margin);
        }

        //Bug fix: the window used to fill from a one-shot GalleryCanvasBackdrop token pinned to white in
        //both variants, making every kit tile impossible to judge against its own background. It now binds
        //the same live WindowBackground every other window does, re-resolved by RefreshTheme on every flip.
        [AvaloniaFact]
        public void Backgrounds_TrackTheirTokensLiveAcrossThemeToggle() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Application app = Application.Current!;

            Color expectedBefore = ThemeResources.ResolveColor("Foreman.Color.WindowBackground", app.ActualThemeVariant, Colors.White);
            Assert.Equal(expectedBefore, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
            Color paneBefore = ThemeResources.ResolveColor("Foreman.Color.InspectorBackground", app.ActualThemeVariant, Colors.White);
            Assert.Equal(paneBefore, Assert.IsAssignableFrom<ISolidColorBrush>(SplitDividerPane(window).Background).Color);

            RealClick(window, FindByName(window, "ThemeToggleButton"));
            Dispatcher.UIThread.RunJobs();

            Color expectedAfter = ThemeResources.ResolveColor("Foreman.Color.WindowBackground", app.ActualThemeVariant, Colors.White);
            Assert.NotEqual(expectedBefore, expectedAfter);
            Assert.Equal(expectedAfter, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
            Color paneAfter = ThemeResources.ResolveColor("Foreman.Color.InspectorBackground", app.ActualThemeVariant, Colors.White);
            Assert.NotEqual(paneBefore, paneAfter);
            Assert.Equal(paneAfter, Assert.IsAssignableFrom<ISolidColorBrush>(SplitDividerPane(window).Background).Color);
        }

        private static Border SplitDividerPane(GalleryWindow window) {
            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Split divider — rest, hovered, dragging, focused");
            SplitDivider divider = tile.GetVisualDescendants().OfType<SplitDivider>().First();
            return Assert.IsType<Border>(((StackPanel)divider.GetVisualParent()!).Children[0]);
        }

        //The gallery joins AppearanceHub like every other window; scale never wraps Content in a LayoutTransformControl.
        [AvaloniaFact]
        public void Construction_DoesNotWrapContentInALayoutTransform() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            Assert.False(window.Content is LayoutTransformControl);
        }

        private static Button FindByName(Window window, string name) =>
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

        private static void RealClick(Window window, Button button) {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Point center = Avalonia.VisualExtensions.TranslatePoint(button, new Avalonia.Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        }

        //The gallery's own ScrollViewer stretches its content well past the fixed 1000pt window, and
        //BringIntoViewOnFocusChange is off, so a real pointer click on a control past the fold needs the
        //viewport scrolled to it directly (the same idiom QuickAddPalette.BringHighlightIntoView uses).
        private static void ScrollIntoView(Control control) {
            if (control.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not ScrollViewer scrollViewer)
                return;
            Point topLeft = control.TranslatePoint(new Point(0, 0), scrollViewer) ?? default;
            double bottom = topLeft.Y + control.Bounds.Height;
            double viewportHeight = scrollViewer.Viewport.Height;
            if (topLeft.Y < 0)
                scrollViewer.Offset += new Vector(0, topLeft.Y);
            else if (bottom > viewportHeight)
                scrollViewer.Offset += new Vector(0, bottom - viewportHeight);
            Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void ThemeToggleButton_RealClick_FlipsRequestedThemeVariant_AndNoDesignLanguageSectionExists() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Application app = Application.Current!;
            ThemeVariant before = app.ActualThemeVariant;

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Design language");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ContentControl>(), c => c.Name == "ThemeShowcaseHost");
            Assert.DoesNotContain(window.Registry.Tiles, t => t.Family == "Theme");

            RealClick(window, FindByName(window, "ThemeToggleButton"));

            Assert.NotEqual(before, app.ActualThemeVariant);
        }

        [AvaloniaFact]
        public void FormCompositesScaledTile_LabelColumnsResolveTo198And162And129() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Form group & Form rows (150%)");
            FormRow[] rows = [.. tile.GetVisualDescendants().OfType<FormRow>()];

            FormRow windowShaped = rows.First(r => r.LabelColumn == FormRowLabelColumn.Window);
            FormRow inspectorShaped = rows.First(r => r.LabelColumn == FormRowLabelColumn.Inspector);
            FormRow popoverShaped = rows.First(r => r.LabelColumn == FormRowLabelColumn.Popover);

            Grid windowGrid = windowShaped.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");
            Grid inspectorGrid = inspectorShaped.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");
            Grid popoverGrid = popoverShaped.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");
            Assert.Equal(198, windowGrid.ColumnDefinitions[0].Width.Value);
            Assert.Equal(162, inspectorGrid.ColumnDefinitions[0].Width.Value);
            Assert.Equal(129, popoverGrid.ColumnDefinitions[0].Width.Value);
        }

        [AvaloniaFact]
        public void InspectorAndDividerScaledTile_HeaderHeightScalesButDividerHitWidthStaysFive() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Inspector group & Split divider (150%)");

            Border header = tile.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_HeaderRow");
            Assert.Equal(33, header.Height);

            SplitDivider divider = tile.GetVisualDescendants().OfType<SplitDivider>().First();
            Assert.Equal(5, divider.Bounds.Width);
        }

        //Addendum §03's grid figure draws the first module slot selected; the tile says so itself rather
        //than leaning on a default SelectedIndex the grid no longer has.
        [AvaloniaFact]
        public void PickGridTile_DrawsTheFiguresSelectedCell() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Pick grid — module slots & beacon row");
            PickGrid modules = tile.GetVisualDescendants().OfType<PickGrid>().First();
            PickCell[] cells = [.. modules.GetVisualDescendants().OfType<PickCell>()];

            Assert.Equal(0, modules.SelectedIndex);
            Assert.True(cells[0].IsSelected);
            Assert.Equal(1, cells.Count(c => c.IsSelected));
        }

        //Quality's own shape: a leading badge riding beside the word, one colour per tier in the menu too.
        [AvaloniaFact]
        public void PopUpButtonsTile_TheLeadingIconExampleCarriesAnIconAndPerItemMenuIcons() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Pop-up buttons");
            PopUp withIcon = tile.GetVisualDescendants().OfType<PopUp>().Single(p => p.Icon is not null);

            Assert.Equal("Rare", withIcon.SelectedItem);
            Assert.Equal(3, withIcon.ItemIcons.Count);
            Assert.All(withIcon.ItemIcons, Assert.NotNull);
        }

        //The building-picker mock's own §02 figure: four candidates, the fourth dimmed but still a real
        //cell, the third selected; the beacon row's first cell is the None member.
        [AvaloniaFact]
        public void PickGridChoiceTile_DrawsTheFiguresSelectedCellAndUnavailableCandidate() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Pick grid — choice mode, building & beacon");
            PickGrid[] grids = [.. tile.GetVisualDescendants().OfType<PickGrid>()];
            Assert.Equal(2, grids.Length);
            Assert.All(grids, g => Assert.True(g.IsChoiceMode));

            PickGrid building = grids[0];
            PickCell[] buildingCells = [.. building.GetVisualDescendants().OfType<PickCell>()];
            Assert.Equal(4, buildingCells.Length);
            Assert.Equal(2, building.SelectedIndex);
            Assert.True(buildingCells[2].IsSelected);
            Assert.True(buildingCells[3].IsUnavailable);

            PickGrid beacon = grids[1];
            PickCell[] beaconCells = [.. beacon.GetVisualDescendants().OfType<PickCell>()];
            Assert.True(beaconCells[0].IsNone);
        }

        //The choice grid's own 150% scaling (cell size, gap non-multiplication, wrap recompute) is
        //KitPickGridChoiceScaleTests now - this tile keeps only the registration/tile-presence assertion.
        [AvaloniaFact]
        public void PickCompositesScaledTile_CellScalesButGapStaysAtTwoPointFive() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Pick cell & Pick grid (150%)");
            PickGrid grid = tile.GetVisualDescendants().OfType<PickGrid>().First();
            PickCell gridCell = grid.GetVisualDescendants().OfType<PickCell>().First();

            Assert.Equal(39, gridCell.Bounds.Width);
            Assert.Equal(new Thickness(2.5), gridCell.Margin);
        }

        [AvaloniaFact]
        public void ColorWellStatesTile_DrawsTheSixStates() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Color well — rest, focused, open, 40 % alpha, none, disabled");
            ColorWell[] wells = [.. tile.GetVisualDescendants().OfType<ColorWell>()];
            Assert.Equal(6, wells.Length);
            Assert.True(FocusRing.GetShowAlways(wells[1]));
            Assert.Contains("gallery-open", wells[2].Classes);
            Assert.True(wells[3].GetVisualDescendants().OfType<SwatchChecker>().Single().IsVisible);
            Assert.True(wells[4].GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_NoneBox").IsVisible);
            Assert.False(wells[5].IsEnabled);

            //The wells sit in the tile's own popover host, so a click or Space opens a real popover instead of throwing.
            ScrollIntoView(wells[0]);
            AnimationClock.UntilReflowsSettled(window);
            Point restCentre = wells[0].TranslatePoint(new Point(wells[0].Bounds.Width / 2, wells[0].Bounds.Height / 2), window)!.Value;
            window.MouseDown(restCentre, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(restCentre, MouseButton.Left, RawInputModifiers.None);
            AnimationClock.Settle(300);
            Assert.True(wells[0].IsOpen);
            Assert.True(tile.GetVisualDescendants().OfType<PopoverFrame>().Single(f => f.Anchor == wells[0]).IsOpen);
            CanvasKeys.KeyDownOn(tile.GetVisualDescendants().OfType<PopoverFrame>().Single(f => f.Anchor == wells[0]), Key.Escape);
            Assert.False(wells[0].IsOpen);

            wells[1].Focus(NavigationMethod.Tab);
            CanvasKeys.KeyDownOn(wells[1], Key.Space);
            AnimationClock.Settle(300);
            Assert.True(wells[1].IsOpen);
            Assert.True(tile.GetVisualDescendants().OfType<PopoverFrame>().Single(f => f.Anchor == wells[1]).IsOpen);
        }

        [AvaloniaFact]
        public void ColorPopoverTile_OpensTheFillPopover_WithTheNoneCellFirst() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            AnimationClock.Settle(300);

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Color well & Color popover — Fill, None available");
            PopoverFrame frame = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.True(frame.IsOpen);
            PickCell[] cells = [.. frame.GetVisualDescendants().OfType<PickCell>()];
            Assert.Equal(32, cells.Length);
            Assert.True(cells[0].IsNone);
            Assert.Equal("Fill colors", Avalonia.Automation.AutomationProperties.GetName(frame));
        }

        [AvaloniaFact]
        public void ColorScaledTile_WellAndCheckerScaleByOneAndAHalf() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            AnimationClock.Settle(300);

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Color well & Color popover (150%)");
            ColorWell well = tile.GetVisualDescendants().OfType<ColorWell>().Single();
            Assert.Equal(new Size(66, 33), well.Bounds.Size);
            Assert.Equal(6, well.GetVisualDescendants().OfType<SwatchChecker>().Single().SquareSize);
            PopoverFrame frame = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.Equal(39, frame.GetVisualDescendants().OfType<PickCell>().First().Bounds.Width);
        }

        [AvaloniaFact]
        public void ChooserCompositesScaledTile_CellScalesToOneHundredThirtyTwo() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Chooser cell & Chooser grid (150%)");
            ChooserCell cell = tile.GetVisualDescendants().OfType<ChooserCell>().First();

            Assert.Equal(132, cell.Bounds.Width);
        }

        //Caption.LineHeight joined the scale ramp but the tile's own scoped keys never picked it up - the
        //cell's label font grew while its line box stayed at the unscaled 13.2.
        [AvaloniaFact]
        public void ChooserCompositesScaledTile_CellLabelLineHeightScalesWithItsFontSize() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Chooser cell & Chooser grid (150%)");
            TextBlock label = tile.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "PART_Label");

            Assert.Equal(19.8, label.LineHeight, 3);
        }

        [AvaloniaFact]
        public void SheetCompositesScaledTile_SheetWidthStandardScalesTo540() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Scrim, Sheet frame, Button row & Alert frame (150%)");
            //Three SheetFrames live in this tile (the plain sheet plus one nested in each AlertFrame) -
            //the plain one is the only Standard-width sheet.
            SheetFrame sheet = tile.GetVisualDescendants().OfType<SheetFrame>().Single(s => s.WidthClass == SheetWidthClass.Standard);
            Border panel = sheet.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");

            Assert.Equal(540, panel.Width);

            Button rowButton = tile.GetVisualDescendants().OfType<ButtonRow>().First().Items.OfType<Button>().First();
            Assert.Equal(96, rowButton.MinWidth);
        }

        //AlertIcon and Callout.LineHeight joined the scale ramp but the tile's own scoped keys never picked
        //them up - the alert's icon, its column and its informative line box stayed at 100% while the sheet
        //width scaled around them.
        [AvaloniaFact]
        public void SheetCompositesScaledTile_AlertIconAndItsColumnResolveTheScaledAlertIconToken() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Scrim, Sheet frame, Button row & Alert frame (150%)");
            Image icon = tile.GetVisualDescendants().OfType<Image>().First(i => i.Name == "PART_Icon");
            //SheetFrame's own PART_Body (the content host, zero columns) shares the name with AlertFrame's
            //three-column body - the alert's grid is the one that actually carries the icon column.
            Grid body = tile.GetVisualDescendants().OfType<Grid>().First(g => g.Name == "PART_Body" && g.ColumnDefinitions.Count == 3);

            Assert.Equal(48, icon.Bounds.Width);
            Assert.Equal(48, body.ColumnDefinitions[0].Width.Value);
        }

        [AvaloniaFact]
        public void PopoverFrameTile_AnchorClick_ReopensAfterALightDismiss_AcrossRepeatedCycles() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Popover frame — anchored below, with a form group inside");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Button anchor = Assert.IsType<Button>(popover.Anchor);
            ScrollIntoView(anchor);
            int layouts = 0;
            popover.LayoutUpdated += (_, _) => layouts++;

            Assert.True(popover.IsOpen);
            for (int cycle = 0; cycle < 3; cycle++) {
                RealClick(window, anchor);
                AnimationClock.Settle();
                Assert.False(popover.IsOpen);

                RealClick(window, anchor);
                AnimationClock.Settle();
                Assert.True(popover.IsOpen);
            }

            Assert.True(layouts <= 400, $"Popover frame laid out {layouts} times across three reopen cycles.");
        }

        [AvaloniaFact]
        public void PopoverFrameCornerTile_AnchorClick_ReopensAfterALightDismiss_AcrossRepeatedCycles() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Popover frame — tail clamped near a corner");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Button anchor = Assert.IsType<Button>(popover.Anchor);
            ScrollIntoView(anchor);
            int layouts = 0;
            popover.LayoutUpdated += (_, _) => layouts++;

            Assert.True(popover.IsOpen);
            for (int cycle = 0; cycle < 3; cycle++) {
                RealClick(window, anchor);
                AnimationClock.Settle();
                Assert.False(popover.IsOpen);

                RealClick(window, anchor);
                AnimationClock.Settle();
                Assert.True(popover.IsOpen);
            }

            Assert.True(layouts <= 400, $"Popover frame laid out {layouts} times across three reopen cycles.");
        }

        [AvaloniaFact]
        public void PopoverAndRadialScaledTile_RingStays220_PopoverScalesWithItsGroupTitle() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Popover frame & Radial ring (150%)");
            RadialRing ring = tile.GetVisualDescendants().OfType<RadialRing>().Single();

            Assert.Equal(220, ring.Bounds.Width);
            Assert.Equal(220, ring.Bounds.Height);

            TextBlock title = tile.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontSize", Application.Current.ActualThemeVariant, out object? fontSizeObj);
            Assert.Equal((double)fontSizeObj! * 1.5, title.FontSize);
        }

        [AvaloniaFact]
        public void StatePanelScaledTile_GlyphViewboxResolvesFortyEight_PathRendersAtTheScaledSize() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "State panel (150%)");
            Viewbox box = tile.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_GlyphViewbox");
            Assert.Equal(48, box.Bounds.Width);
            Assert.Equal(48, box.Bounds.Height);

            Path glyph = tile.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Glyph");
            Assert.Equal(32, glyph.Width);
            Point topLeft = glyph.TranslatePoint(new Point(0, 0), window) ?? default;
            Point bottomRight = glyph.TranslatePoint(new Point(glyph.Bounds.Width, glyph.Bounds.Height), window) ?? default;
            Assert.Equal(48, bottomRight.X - topLeft.X, 0.01);
            Assert.Equal(48, bottomRight.Y - topLeft.Y, 0.01);
        }

        //Callout.LineHeight joined the scale ramp but the tile's own scoped keys never picked it up - the
        //sentence's type grew while its line box stayed at the unscaled 16.8.
        [AvaloniaFact]
        public void StatePanelScaledTile_SentenceLineHeightScalesWithItsFontSize() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "State panel (150%)");
            TextBlock sentence = tile.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Sentence");

            Assert.Equal(25.2, sentence.LineHeight, 3);
        }

        [AvaloniaFact]
        public void ZoomStepperScaledTile_ResolvesReadoutAndMicroButtonSizes_AtOneAndAHalfScale() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Zoom stepper (150%)");
            Button readout = tile.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_Readout");
            Button minus = tile.GetVisualDescendants().OfType<Button>().First(b => b.Name == "PART_Minus");

            Assert.Equal(63, readout.Bounds.Width);
            Assert.Equal(24, readout.Bounds.Height);
            Assert.Equal(30, minus.Bounds.Width);
            Assert.Equal(24, minus.Bounds.Height);
        }

        [AvaloniaFact]
        public void ZoomStepperTile_SolvingAndFailedColumns_DriveTheHostedSolveStatus() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Zoom stepper — at rest, hovered, solving, solve failed");

            Assert.Equal(
                [SolveStatusState.Idle, SolveStatusState.Idle, SolveStatusState.Solving, SolveStatusState.Failed],
                tile.GetVisualDescendants().OfType<ZoomStepper>().Select(z => z.SolveStatus.State));
        }

        [AvaloniaFact]
        public void ZoomStepperPopoverTile_OpensTheHostedStatusPopover_WithTheRuledPanel() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Zoom stepper — solve-error popover");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();

            Assert.True(popover.IsOpen);
            Assert.Equal("Couldn\u2019t solve this graph", Assert.IsType<StatePanel>(popover.Content).Headline);
        }

        [AvaloniaFact]
        public void SolveStatusTile_ShowsIdleSolvingFailedAndAnOpenPopover() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Solve status — idle, solving, failed, popover");
            SolveStatus[] statuses = [.. tile.GetVisualDescendants().OfType<SolveStatus>()];

            Assert.Equal([SolveStatusState.Idle, SolveStatusState.Solving, SolveStatusState.Failed, SolveStatusState.Failed], statuses.Select(s => s.State));
            Assert.Equal(0, statuses[0].Bounds.Width);
            Assert.Equal(new Size(16, 16), statuses[1].Bounds.Size);
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Assert.True(popover.IsOpen);
            Assert.Same(statuses[3], popover.Anchor);
            Assert.Equal(["Show Node", "Try Again"], Assert.IsType<StatePanel>(popover.Content).Buttons!.Items.OfType<Button>().Select(b => (string)b.Content!));
        }

        [AvaloniaFact]
        public void SolveStatusScaledTile_ResolvesSlotAndSpinner_AtOneAndAHalfScale() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Solve status (150%)");
            SolveStatus solving = tile.GetVisualDescendants().OfType<SolveStatus>().First(s => s.State == SolveStatusState.Solving);
            Panel slot = solving.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_Slot");
            Foreman.Mac.Kit.Controls.Spinner spinner = solving.GetVisualDescendants().OfType<Foreman.Mac.Kit.Controls.Spinner>().Single();

            Assert.Equal(new Size(24, 24), slot.Bounds.Size);
            Assert.Equal(21, spinner.Diameter, 3);
            Assert.Equal(2.25, spinner.StrokeWidth, 3);
        }

        [AvaloniaFact]
        public void ToolbarRowScaledTile_RowHeightResolvesSeventyEight_AtOneAndAHalfScale() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Toolbar row (150%)");
            ToolbarRow row = tile.GetVisualDescendants().OfType<ToolbarRow>().Single();

            Assert.Equal(78, row.Bounds.Height);
        }

        [AvaloniaFact]
        public void ToolbarRowScaledTile_BetweenGroupGapStaysSixteen_AtOneAndAHalfScale() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Toolbar row (150%)");
            ToolbarRow row = tile.GetVisualDescendants().OfType<ToolbarRow>().Single();
            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();

            ToolbarItem[] items = [.. row.Groups.SelectMany(g => g.Items)];
            ToolbarItem save = items.Single(i => i.Label == "Save");
            ToolbarItem recipe = items.Single(i => i.Label == "Recipe");
            double saveRight = (Avalonia.VisualExtensions.TranslatePoint(save, new Point(0, 0), panel)?.X ?? double.NaN) + save.Bounds.Width;
            double recipeLeft = Avalonia.VisualExtensions.TranslatePoint(recipe, new Point(0, 0), panel)?.X ?? double.NaN;
            Assert.Equal(16, recipeLeft - saveRight, 3);
        }

        [AvaloniaFact]
        public void ToolbarRowTile_SitsOnTitlebarMaterial_AndIsAnAXToolBar() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Application app = Application.Current!;

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Toolbar row — full, labels dropped, overflowed");
            ToolbarRow row = tile.GetVisualDescendants().OfType<ToolbarRow>().First();

            Color expected = ThemeResources.ResolveColor("Foreman.Color.TitlebarMaterial", app.ActualThemeVariant, Colors.White);
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(row);
            Assert.Equal(AutomationControlType.ToolBar, peer.GetAutomationControlType());
        }

        [AvaloniaFact]
        public void ToolbarRowTiles_DrawNoBorderAroundTheRow_AndFillTheirContainerWidth() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel fullTile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Toolbar row — full, labels dropped, overflowed");
            StackPanel scaledTile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Toolbar row (150%)");

            foreach (StackPanel tile in new[] { fullTile, scaledTile })
                foreach (ToolbarRow row in tile.GetVisualDescendants().OfType<ToolbarRow>()) {
                    foreach (Visual ancestor in row.GetVisualAncestors().TakeWhile(a => !ReferenceEquals(a, tile)))
                        if (ancestor is Border border)
                            Assert.Equal(default, border.BorderThickness);

                    var frame = Assert.IsType<Border>(row.GetVisualParent());
                    Assert.Equal(frame.Bounds.Width, row.Bounds.Width);
                }
        }

        [AvaloniaFact]
        public void PalettePanelTile_AnchorClick_ReopensAfterALightDismiss_AcrossRepeatedCycles() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Palette panel — quick-add results");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Button anchor = Assert.IsType<Button>(popover.Anchor);
            ScrollIntoView(anchor);
            int layouts = 0;
            popover.LayoutUpdated += (_, _) => layouts++;

            Assert.True(popover.IsOpen);
            for (int cycle = 0; cycle < 3; cycle++) {
                RealClick(window, anchor);
                AnimationClock.Settle();
                Assert.False(popover.IsOpen);

                RealClick(window, anchor);
                AnimationClock.Settle();
                Assert.True(popover.IsOpen);
            }

            Assert.True(layouts <= 400, $"Popover frame laid out {layouts} times across three reopen cycles.");
        }

        [AvaloniaFact]
        public void PalettePanelScaledTile_WidthResolvesFourHundredEight_AtOneAndAHalfScale() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Palette panel (150%)");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            PalettePanel panel = Assert.IsType<PalettePanel>(popover.Content);

            Assert.True(popover.IsOpen);
            Assert.Equal(408, panel.Bounds.Width);

            PaletteRow row = panel.GetVisualDescendants().OfType<PaletteRow>().First();
            Assert.Equal(39, row.Bounds.Height);
            Border icon = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Icon");
            Assert.Equal(33, icon.Bounds.Width);
        }

        //The panel's search field is a hosted primitive, not a key the composite reads directly - its own
        //glyph box (Foreman.Size.SearchFieldGlyph) must resolve the tile's scaled token, not the ambient 100%.
        [AvaloniaFact]
        public void PalettePanelScaledTile_SearchFieldGlyphWidthResolvesTheScaledToken() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Palette panel (150%)");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            PalettePanel panel = Assert.IsType<PalettePanel>(popover.Content);

            Viewbox glyphBox = panel.Search.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_LeadingGlyphBox");
            Assert.Equal(20, glyphBox.Bounds.Width);
        }

        [AvaloniaFact]
        public void RecentRowScaledTile_RowHeightAndIconResolveToSixtyNineAndFortyFive_AtOneAndAHalfScale() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Recent row (150%)");
            RecentRow row = tile.GetVisualDescendants().OfType<RecentRow>().Single();

            Assert.Equal(69, row.Bounds.Height);
            Border icon = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Icon");
            Assert.Equal(45, icon.Bounds.Width);
        }

        [AvaloniaFact]
        public void Close_DoesNotThrow_WithEveryTileRegistered() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Exception? caught = null;
            try {
                window.Close();
                Dispatcher.UIThread.RunJobs();
            } catch (Exception ex) {
                caught = ex;
            }

            Assert.True(caught is null, caught?.ToString());
        }
    }
}
