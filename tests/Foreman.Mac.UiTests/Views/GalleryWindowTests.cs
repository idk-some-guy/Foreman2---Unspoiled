using Avalonia;
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
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Services;
using Foreman.Mac.Views;
using System;
using System.Linq;
using System.Reflection;
using Xunit;
using GroupBox = Foreman.Mac.Kit.Controls.GroupBox;
using Slider = Foreman.Mac.Kit.Controls.Slider;
using TabStrip = Foreman.Mac.Kit.Controls.TabStrip;
using ProgressBar = Foreman.Mac.Kit.Controls.ProgressBar;
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

        //The declarative registry lists exactly the tiles the window renders, in the same order.
        [AvaloniaFact]
        public void Registry_ListsATileForEveryPanelTheWindowShows() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());

            Assert.Equal(
                ["QualityPicker", "Toolbar / Titlebar", "View Options popover", "Graph Summary rows", "Preset Comparator rows", "Enabled Objects rows",
                 "Type ramp", "Glyphs", "Type ramp & Glyphs (150%)", "Focus ring", "Focus ring (150%)",
                 "Buttons", "Toolbar items", "Buttons & Toolbar items (150%)",
                 "Checkbox", "Radio group", "Switch — Settings only (A16)", "Checkbox, Radio group & Switch (150%)",
                 "Segmented", "Pop-up buttons", "Menu button", "Segmented, Pop-up buttons & Menu button (150%)",
                 "Text field", "Search field", "Stepper", "Slider", "Text field, Search field, Stepper & Slider (150%)",
                 "List rows", "Sidebar rows", "Group box", "Tab strips", "Disclosure",
                 "List rows, Sidebar rows, Group box, Tab strips & Disclosure (150%)",
                 "Tooltip", "Spinner & progress", "Chips", "Key caps",
                 "Tooltip, Spinner & progress, Chips & Key caps (150%)",
                 "Form group (boxed, 132)", "Form rows (inspector, 108)", "Form row notes & errors",
                 "Form row — search field & chip row", "Form group & Form rows (150%)"],
                window.Registry.Tiles.Select(t => t.Name));
        }

        [AvaloniaFact]
        public void Construction_BuildsATilePerExpectedPanel() {
            DataCache cache = NewFixtureCache();
            var settings = new AppSettings();

            var window = new GalleryWindow(cache, settings);
            window.Show();

            string[] captions = [.. window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? "")];

            Assert.Contains("QualityPicker", captions);
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
            Assert.Contains("Menu button", captions);
            Assert.Contains("Segmented, Pop-up buttons & Menu button (150%)", captions);
            Assert.Contains("Text field", captions);
            Assert.Contains("Search field", captions);
            Assert.Contains("Stepper", captions);
            Assert.Contains("Slider", captions);
            Assert.Contains("Text field, Search field, Stepper & Slider (150%)", captions);
            //A sample from each end of the 30-glyph sheet, proving the full set rendered with name labels.
            Assert.Contains("New", captions);
            Assert.Contains("Settings.Appearance", captions);
        }

        [AvaloniaFact]
        public void ButtonsTile_ShowsStandardPrimaryAndDestructiveAcrossFiveStates() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            Button[] buttons = [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primary") || b.Classes.Contains("destructive") || Equals(b.Content, "Cancel"))];

            Assert.Contains(buttons, b => Equals(b.Content, "Cancel") && !b.Classes.Contains("primary") && !b.Classes.Contains("destructive"));
            Assert.Contains(buttons, b => Equals(b.Content, "Export…") && b.Classes.Contains("primary"));
            Assert.Contains(buttons, b => Equals(b.Content, "Discard") && b.Classes.Contains("destructive"));
            Assert.Contains(buttons, b => !b.IsEnabled);
        }

        [AvaloniaFact]
        public void ToolbarItemsTile_ShowsTheOnState() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            ToolbarItem[] items = [.. window.GetVisualDescendants().OfType<ToolbarItem>()];

            Assert.Contains(items, i => i.IsOn);
            Assert.Contains(items, i => !i.IsOn && i.IsEnabled);
            Assert.Contains(items, i => !i.IsEnabled);
        }

        [AvaloniaFact]
        public void ButtonsAndToolbarItemsScaledTile_HeightAndWidthScale() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Cancel") && b.Height == 33);
            Assert.Contains(window.GetVisualDescendants().OfType<ToolbarItem>(), i => i.Height == 60 && i.MinWidth == 66);
        }

        [AvaloniaFact]
        public void CheckboxTile_ShowsUncheckedAndCheckedAcrossFiveStates() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            CheckBox[] boxes = [.. window.GetVisualDescendants().OfType<CheckBox>()];

            Assert.Contains(boxes, b => b.IsChecked == false);
            Assert.Contains(boxes, b => b.IsChecked == true);
            Assert.Contains(boxes, b => !b.IsEnabled);

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Checkbox");
            Assert.Contains(tile.GetVisualDescendants().OfType<CheckBox>(), b => FocusRing.GetShowAlways(b));
        }

        [AvaloniaFact]
        public void RadioGroupTile_ShowsAGroupedExampleWithOneSelection() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            RadioGroup group = window.GetVisualDescendants().OfType<RadioGroup>().First();
            RadioButton[] radios = [.. group.Items.OfType<RadioButton>()];

            Assert.Equal(["Up", "Down", "Auto"], radios.Select(r => r.Content));
            Assert.Single(radios, r => r.IsChecked == true);

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Radio group");
            Assert.Contains(tile.GetVisualDescendants().OfType<RadioButton>(), r => FocusRing.GetShowAlways(r));
        }

        [AvaloniaFact]
        public void SwitchTile_ShowsOffAndOnAcrossFiveStates() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            ToggleSwitch[] toggles = [.. window.GetVisualDescendants().OfType<ToggleSwitch>()];

            Assert.Contains(toggles, t => t.IsChecked == false);
            Assert.Contains(toggles, t => t.IsChecked == true);
            Assert.Contains(toggles, t => !t.IsEnabled);

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Switch — Settings only (A16)");
            Assert.Contains(tile.GetVisualDescendants().OfType<ToggleSwitch>(), t => FocusRing.GetShowAlways(t));
        }

        [AvaloniaFact]
        public void SegmentedTile_ShowsARegularAndAMiniExample() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            Segmented[] segmented = [.. window.GetVisualDescendants().OfType<Segmented>()];
            Assert.Contains(segmented, s => !s.Classes.Contains("mini"));
            Assert.Contains(segmented, s => s.Classes.Contains("mini"));

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Segmented");
            Assert.Contains(tile.GetVisualDescendants().OfType<Segmented>(), s => FocusRing.GetShowAlways(s));
        }

        [AvaloniaFact]
        public void PopUpButtonsTile_ShowsPopUpPullDownAndAnOpenMenuPanel() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            PopUp[] popups = [.. window.GetVisualDescendants().OfType<PopUp>()];
            Assert.Contains(popups, p => !p.IsPullDown);
            Assert.Contains(popups, p => p.IsPullDown);
            Assert.Contains(window.GetVisualDescendants().OfType<MenuPanel>(), m => m.Items.Count > 0);

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Pop-up buttons");
            Assert.Contains(tile.GetVisualDescendants().OfType<PopUp>(), p => FocusRing.GetShowAlways(p));
        }

        [AvaloniaFact]
        public void MenuButtonTile_ShowsDefaultFilledOpenAndOverflow() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            MenuButton[] buttons = [.. window.GetVisualDescendants().OfType<MenuButton>()];
            Assert.Contains(buttons, b => !b.IsFilled);
            Assert.Contains(buttons, b => b.IsFilled);
            Assert.Contains(buttons, b => b.Classes.Contains("overflow"));
            Assert.Contains(window.GetVisualDescendants().OfType<MenuPanel>(), m => m.Items.Count > 0);

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Menu button");
            Assert.Contains(tile.GetVisualDescendants().OfType<MenuButton>(), b => FocusRing.GetShowAlways(b));
        }

        [AvaloniaFact]
        public void TextFieldTile_ShowsEmptyFilledAndDisabled() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Text field");
            TextBox[] fields = [.. tile.GetVisualDescendants().OfType<TextBox>()];

            Assert.Contains(fields, f => string.IsNullOrEmpty(f.Text));
            Assert.Contains(fields, f => f.Text == "Iron Plate");
            Assert.Contains(fields, f => !f.IsEnabled);
            Assert.Contains(fields, f => FocusRing.GetShowAlways(f));
        }

        [AvaloniaFact]
        public void SearchFieldTile_ShowsNaturalAndMinimumWidthsBothFilled() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Search field");
            SearchField[] fields = [.. tile.GetVisualDescendants().OfType<SearchField>()];

            Assert.Contains(fields, f => f.Width == 220 && !string.IsNullOrEmpty(f.Text));
            Assert.Contains(fields, f => f.Width == 120 && !string.IsNullOrEmpty(f.Text));
            Assert.Contains(fields, f => FocusRing.GetShowAlways(f));
        }

        [AvaloniaFact]
        public void StepperTile_ShowsAPlainStepperAndOneWithAUnit() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Stepper");
            Stepper[] steppers = [.. tile.GetVisualDescendants().OfType<Stepper>()];

            Assert.Contains(steppers, s => string.IsNullOrEmpty(s.Unit));
            Assert.Contains(steppers, s => s.Unit == "items/s");
            Assert.Contains(steppers, s => FocusRing.GetShowAlways(s));
        }

        [AvaloniaFact]
        public void SliderTile_ShowsContinuousDiscreteAndAFormRow() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Slider");
            Slider[] sliders = [.. tile.GetVisualDescendants().OfType<Slider>()];

            Assert.Contains(sliders, s => !s.IsDiscrete);
            Assert.Contains(sliders, s => s.IsDiscrete);
            Assert.Contains(sliders, s => s.ShowValue);
            Assert.Contains(sliders, s => s.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "PART_Thumb" && FocusRing.GetShowAlways(b)));
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

            Button[] saveButtons = [.. window.GetVisualDescendants().OfType<Button>().Where(b => Equals(b.Content, "Save"))];
            Assert.Equal(2, saveButtons.Length);

            Rectangle defaultRing = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(saveButtons[0]));
            Rectangle scaledRing = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(saveButtons[1]));

            Assert.Equal(3, defaultRing.StrokeThickness);
            Assert.Equal(3, scaledRing.StrokeThickness);
            Assert.Equal(new Thickness(-3.5), defaultRing.Margin);
            Assert.Equal(new Thickness(-3.5), scaledRing.Margin);
        }

        //The QualityPicker tile must show the way it actually renders in the real app - floating in its own
        //dark chrome bubble, which has to stay a fixed dark color to read against either window theme.
        [AvaloniaFact]
        public void QualityPickerTile_HasNonTransparentChromeBackground() {
            DataCache cache = NewFixtureCache();
            var settings = new AppSettings();

            var window = new GalleryWindow(cache, settings);
            window.Show();

            QualityPicker picker = window.GetVisualDescendants().OfType<QualityPicker>().Single(p => p.Parent is Border);
            Border chrome = picker.GetVisualAncestors().OfType<Border>().First();
            Assert.Equal(Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(chrome.Background).Color);
        }

        //Bug fix: the window used to fill from a one-shot GalleryCanvasBackdrop token pinned to white in
        //both variants, making every kit tile impossible to judge against its own background. It now binds
        //the same live WindowBackground every other window does, re-resolved by RefreshTheme on every flip.
        [AvaloniaFact]
        public void Background_TracksWindowBackgroundTokenLiveAcrossThemeToggle() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Application app = Application.Current!;

            Color expectedBefore = ThemeResources.ResolveColor("Foreman.Color.WindowBackground", app.ActualThemeVariant, Colors.White);
            Assert.Equal(expectedBefore, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);

            RealClick(window, FindByName(window, "ThemeToggleButton"));

            Color expectedAfter = ThemeResources.ResolveColor("Foreman.Color.WindowBackground", app.ActualThemeVariant, Colors.White);
            Assert.NotEqual(expectedBefore, expectedAfter);
            Assert.Equal(expectedAfter, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color);
        }

        //The gallery goes through WindowScaler.Track like every other window.
        [AvaloniaFact]
        public void Construction_TracksWindowScaler() {
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

        [AvaloniaFact]
        public void ListRowsTile_ShowsNormalSelectedKeyIdleAndFocusedStates() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "List rows");
            ListRow[] rows = [.. tile.GetVisualDescendants().OfType<ListRow>()];

            Assert.Contains(rows, r => !r.IsSelected);
            Assert.Contains(rows, r => r.IsSelected && r.IsKey);
            Assert.Contains(rows, r => r.IsSelected && !r.IsKey);
            Assert.Contains(rows, r => r.GetVisualDescendants().OfType<Border>()
                .Any(b => b.Name == "PART_Frame" && FocusRing.GetShowAlways(b)));

            Application.Current!.TryGetResource("Kit.List", null, out object? listTheme);
            Assert.Contains(tile.GetVisualDescendants().OfType<ListBox>(), lb => ReferenceEquals(lb.Theme, listTheme));

            TextBlock emptyText = tile.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text!.Contains("plutonium"));
            Assert.DoesNotContain(emptyText.GetVisualAncestors(), a => a is ListBox);
            Assert.Contains(emptyText.GetVisualAncestors(), a => a is Border);
        }

        [AvaloniaFact]
        public void SidebarRowsTile_ShowsNormalSelectedKeyAndIdleStates() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Sidebar rows");
            SidebarRow[] rows = [.. tile.GetVisualDescendants().OfType<SidebarRow>()];

            Assert.Contains(rows, r => !r.IsSelected);
            Assert.Contains(rows, r => r.IsSelected && r.IsKey);
            Assert.Contains(rows, r => r.IsSelected && !r.IsKey);
        }

        [AvaloniaFact]
        public void GroupBoxTile_HostsAHeaderedFormGroup() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Group box");
            GroupBox box = tile.GetVisualDescendants().OfType<GroupBox>().Single();

            Assert.Equal("Canvas", box.Header);
        }

        [AvaloniaFact]
        public void TabStripsTile_ShowsToolbarTabsAndUnderlineVariants() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Tab strips");
            TabStrip[] strips = [.. tile.GetVisualDescendants().OfType<TabStrip>()];

            Assert.Contains(strips, s => s.Classes.Contains("toolbar-tabs"));
            Assert.Contains(strips, s => s.Classes.Contains("underline"));
            Assert.All(strips, s => Assert.False(FocusRing.GetShowAlways(s)));
            Assert.Contains(tile.GetVisualDescendants().OfType<Border>(), b => FocusRing.GetShowAlways(b));
            Assert.Contains(strips, s => s.Items.Any(i => !i.IsEnabled));
        }

        [AvaloniaFact]
        public void DisclosureTile_ShowsCollapsedAndExpanded() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Disclosure");
            Disclosure[] disclosures = [.. tile.GetVisualDescendants().OfType<Disclosure>()];

            Assert.Contains(disclosures, d => !d.IsExpanded);
            Assert.Contains(disclosures, d => d.IsExpanded);
        }

        [AvaloniaFact]
        public void TooltipTile_ShowsMaterialAndOpaqueFallback() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Tooltip");
            ToolTip[] tips = [.. tile.GetVisualDescendants().OfType<ToolTip>()];

            Assert.True(tips.Length >= 3);
            Assert.Contains(tips, t => Equals(t.Content, "Find node — ⌘F"));
            Assert.Contains(tips, t => (t.Content as string)?.StartsWith("Overproduction", StringComparison.Ordinal) == true);
        }

        [AvaloniaFact]
        public void SpinnerAndProgressTile_ShowsBothSizesAndBothProgressForms() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Spinner & progress");
            Spinner[] spinners = [.. tile.GetVisualDescendants().OfType<Spinner>()];
            ProgressBar[] bars = [.. tile.GetVisualDescendants().OfType<ProgressBar>()];

            Assert.Contains(spinners, s => !s.Classes.Contains("lg"));
            Assert.Contains(spinners, s => s.Classes.Contains("lg"));
            Assert.Contains(bars, b => !b.IsIndeterminate);
            Assert.Contains(bars, b => b.IsIndeterminate);
        }

        [AvaloniaFact]
        public void ChipsTile_ShowsAFocusedChip() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Chips");
            Chip[] chips = [.. tile.GetVisualDescendants().OfType<Chip>()];

            Assert.Equal(2, chips.Length);
            Assert.Contains(chips, c => FocusRing.GetShowAlways(c));
        }

        [AvaloniaFact]
        public void KeyCapsTile_ShowsMultiKeyCombosAndANamedKey() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Key caps");
            KeyCap[] caps = [.. tile.GetVisualDescendants().OfType<KeyCap>()];

            Assert.Contains(caps, c => c.Text == "⌘" && c.AccessibleName == "Command");
            Assert.Contains(caps, c => c.Text == "Return");
            Assert.All(caps, c => Assert.False(c.Focusable));
        }

        [AvaloniaFact]
        public void FormGroupBoxedTile_ShowsABoxedGroupWithFiveRowsAtTheWindowLabelColumn() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Form group (boxed, 132)");
            FormGroup group = tile.GetVisualDescendants().OfType<FormGroup>().Single();
            Assert.True(group.IsBoxed);
            Assert.Equal("Canvas", group.Title);

            FormRow[] rows = [.. tile.GetVisualDescendants().OfType<FormRow>()];
            Assert.Equal(5, rows.Length);
            Assert.All(rows, r => Assert.False(r.IsInspector));
            Assert.Contains(rows, r => r.Label == "Node width");
            Assert.Contains(rows, r => r.Label == "Link width" && !string.IsNullOrEmpty(r.Note));
        }

        [AvaloniaFact]
        public void FormRowsInspectorTile_ShowsFourRowsAtTheInspectorLabelColumn() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Form rows (inspector, 108)");
            FormRow[] rows = [.. tile.GetVisualDescendants().OfType<FormRow>()];

            Assert.Equal(4, rows.Length);
            Assert.All(rows, r => Assert.True(r.IsInspector));
            Assert.Contains(rows, r => r.Label == "Quality");
            Assert.Contains(rows, r => r.Label == "Rate");
        }

        [AvaloniaFact]
        public void FormRowNotesAndErrorsTile_ShowsANoteRowAndAnErrorRow() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Form row notes & errors");
            FormRow[] rows = [.. tile.GetVisualDescendants().OfType<FormRow>()];

            Assert.Contains(rows, r => r.Label == "Assembler" && r.Note == "Used for every new node until you change it.");
            Assert.Contains(rows, r => r.Label == "Beacon count" && r.Error == "Must be 0 or more.");
            Assert.Contains(rows, r => r.Label == "Belt overlays");
        }

        [AvaloniaFact]
        public void FormRowSearchAndChipTile_ShowsALoadingSearchFieldAndAChipRow() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Form row — search field & chip row");

            SearchField[] fields = [.. tile.GetVisualDescendants().OfType<SearchField>()];
            Assert.Contains(fields, f => f.IsLoading);

            Chip[] chips = [.. tile.GetVisualDescendants().OfType<Chip>()];
            Assert.Equal(2, chips.Length);
            Assert.Contains(chips, c => c.Label == "Iron plate");
            Assert.Contains(chips, c => c.Label == "Copper plate");
        }

        [AvaloniaFact]
        public void FormCompositesScaledTile_LabelColumnsResolveTo198And162() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Form group & Form rows (150%)");
            FormRow[] rows = [.. tile.GetVisualDescendants().OfType<FormRow>()];

            FormRow windowShaped = rows.First(r => !r.IsInspector);
            FormRow inspectorShaped = rows.First(r => r.IsInspector);

            Grid windowGrid = windowShaped.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");
            Grid inspectorGrid = inspectorShaped.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Grid");
            Assert.Equal(198, windowGrid.ColumnDefinitions[0].Width.Value);
            Assert.Equal(162, inspectorGrid.ColumnDefinitions[0].Width.Value);
        }
    }
}
