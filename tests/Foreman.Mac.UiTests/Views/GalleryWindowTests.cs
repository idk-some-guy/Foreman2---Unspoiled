using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Foreman.Mac.UiTests.Views {
    //Task: dev-only gallery smoke test (docs entry: "--gallery" mode). Builds a small fixture cache - a
    //recipe with an electric assembler (modules + beacon) alongside a burner alternative (fuel) - feeding
    //the theme/enabled-objects/comparator showcase tiles below, then proves every expected tile gets built.
    public class GalleryWindowTests {
        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static DataCache NewFixtureCache() {
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

        [AvaloniaFact]
        public void Construction_BuildsATilePerExpectedPanel() {
            DataCache cache = NewFixtureCache();
            var settings = new AppSettings();

            var window = new GalleryWindow(cache, settings);
            window.Show();

            string[] captions = [.. window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

            Assert.Contains("QualityPicker", captions);
        }

        //The QualityPicker tile must show the way it actually renders in the real app - floating in its own
        //dark chrome bubble over the graph canvas's white backdrop (GraphViewer.Paint's canvas.Clear(
        //SKColors.White)), not lost against an equally black window.
        [AvaloniaFact]
        public void QualityPickerTile_HasNonTransparentChromeBackground() {
            DataCache cache = NewFixtureCache();
            var settings = new AppSettings();

            var window = new GalleryWindow(cache, settings);
            window.Show();

            Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);

            QualityPicker picker = window.GetVisualDescendants().OfType<QualityPicker>().Single(p => p.Parent is Border);
            Border chrome = picker.GetVisualAncestors().OfType<Border>().First();
            Assert.Equal(Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(chrome.Background).Color);
        }

        //Task 6: WindowScaler was disclosed-unscaled for the gallery (task-6-brief) - Track wraps
        //window.Content in a LayoutTransformControl, the same seam every other window (MainWindow,
        //SettingsWindow, GraphSummaryWindow, PresetComparatorWindow) already goes through.
        [AvaloniaFact]
        public void Construction_TracksWindowScaler() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            Assert.IsType<LayoutTransformControl>(window.Content);
        }

        private static Button FindByName(Window window, string name) =>
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

        private static void RealClick(Window window, Button button) {
            Dispatcher.UIThread.RunJobs();
            Avalonia.Point center = Avalonia.VisualExtensions.TranslatePoint(button, new Avalonia.Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(center, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(center, MouseButton.Left, RawInputModifiers.None);
        }

        //Task 6 gate tool (task-6-brief): the Design language section shows every retokened surface this
        //plan touched - toolbar/titlebar, the View Options popover, and the three status-colored row kinds.
        [AvaloniaFact]
        public void ThemeShowcase_BuildsATilePerRetokenedSurface() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            string[] captions = [.. window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];
            Assert.Contains("Toolbar / Titlebar", captions);
            Assert.Contains("View Options popover", captions);
            Assert.Contains("Graph Summary rows", captions);
            Assert.Contains("Preset Comparator rows", captions);
            Assert.Contains("Enabled Objects rows", captions);

            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("foreman-toolbar-icon"));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("foreman-toolbar-wide"));
        }

        //The toggle is the gate tool's whole point (task-6-brief: "genuinely useful for eyeballing both
        //themes quickly") - a real click has to flip the live app theme and rebuild the section against it.
        [AvaloniaFact]
        public void ThemeToggleButton_RealClick_FlipsRequestedThemeVariantAndRebuildsShowcase() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Application app = Application.Current!;
            ThemeVariant before = app.ActualThemeVariant;
            Control contentBefore = Assert.IsType<ContentControl>(
                window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "ThemeShowcaseHost")).Content as Control ?? throw new InvalidOperationException();

            RealClick(window, FindByName(window, "ThemeToggleButton"));

            Assert.NotEqual(before, app.ActualThemeVariant);
            ContentControl host = window.GetVisualDescendants().OfType<ContentControl>().Single(c => c.Name == "ThemeShowcaseHost");
            Assert.NotSame(contentBefore, host.Content);
        }

        //M-6 (final fix wave): the header used to hardcode Brushes.Black, staying black through a theme
        //toggle while every retokened surface below it flipped - it now reads Foreman.Color.Text like the
        //rest of the showcase, so it tracks the toggle too.
        [AvaloniaFact]
        public void DesignLanguageHeader_ForegroundTracksThemedTextToken() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Application app = Application.Current!;

            TextBlock Header() => window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Design language (Task 6 gate)");

            Color expectedBefore = ThemeResources.ResolveColor("Foreman.Color.Text", app.ActualThemeVariant, Colors.Black);
            Assert.Equal(expectedBefore, Assert.IsType<ImmutableSolidColorBrush>(Header().Foreground).Color);

            RealClick(window, FindByName(window, "ThemeToggleButton"));

            Color expectedAfter = ThemeResources.ResolveColor("Foreman.Color.Text", app.ActualThemeVariant, Colors.Black);
            Assert.NotEqual(expectedBefore, expectedAfter);
            Assert.Equal(expectedAfter, Assert.IsType<ImmutableSolidColorBrush>(Header().Foreground).Color);
        }
    }
}
