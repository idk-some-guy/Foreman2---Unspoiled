using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views;
using System;
using System.Collections.Generic;
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
                 "Form row — search field & chip row", "Form group & Form rows (150%)",
                 "Inspector group — expanded, collapsed, hovered, focused", "Split divider — rest, hovered, dragging, focused",
                 "Inspector group & Split divider (150%)",
                 "Pick cell — filled and empty, five states each", "Pick grid — module slots & beacon row",
                 "Pick cell & Pick grid (150%)",
                 "Chooser cell — five states", "Chooser grid — search results, two headings",
                 "Chooser cell & Chooser grid (150%)",
                 "Scrim & Sheet frame", "Button row — five shapes", "Alert frame — two-button & three-button",
                 "Scrim, Sheet frame, Button row & Alert frame (150%)",
                 "Popover frame — anchored below, with a form group inside", "Popover frame — tail clamped near a corner",
                 "Radial ring — six slices, one armed",
                 "Popover frame & Radial ring (150%)",
                 "State panel — empty (inspector & new graph)", "State panel — no results",
                 "State panel — loading (determinate & compact)", "State panel — error", "State panel (150%)",
                 "Chooser grid — no results (State panel)", "Sheet frame — error (State panel)",
                 "Form group — load failure (State panel)", "Inspector group — body delegation (State panel)",
                 "Zoom stepper — at rest, hovered, solving, solve failed", "Zoom stepper — solve-error popover",
                 "Zoom stepper (150%)", "Toolbar row — full, labels dropped, overflowed", "Toolbar row (150%)",
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
        public void SliderTile_FormRowExampleUsesARealFormRowWithTheLabelBaselineOnTheSlidersValueLabel() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Slider");
            FormRow formRow = tile.GetVisualDescendants().OfType<FormRow>().Single(r => r.Label == "Link opacity");
            Slider slider = formRow.GetVisualDescendants().OfType<Slider>().Single();

            TextBlock label = formRow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
            TextBlock valueLabel = slider.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ValueLabel");

            double labelBaselineY = label.TranslatePoint(new Point(0, label.TextLayout!.TextLines[0].Baseline), window)!.Value.Y;
            double valueBaselineY = valueLabel.TranslatePoint(new Point(0, valueLabel.TextLayout!.TextLines[0].Baseline), window)!.Value.Y;

            Assert.Equal(valueBaselineY, labelBaselineY, 2);
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

            //Scoped to the two Focus ring tiles - later composites (K3) legitimately reuse "Save" as a
            //Kit.ButtonRow default-button label elsewhere in the gallery.
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
        public void FormGroupBoxedTile_LinkOpacityRow_IsARealSliderWithShowValue_BaselineAlignedToItsOwnValueLabel() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Form group (boxed, 132)");
            FormRow opacityRow = tile.GetVisualDescendants().OfType<FormRow>().Single(r => r.Label == "Link opacity");

            Slider slider = Assert.IsType<Slider>(opacityRow.Content);
            Assert.True(slider.ShowValue);
            Assert.Equal("%", slider.Unit);

            TextBlock label = opacityRow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
            TextBlock valueLabel = slider.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_ValueLabel");

            double labelBaselineY = label.TranslatePoint(new Point(0, label.TextLayout!.TextLines[0].Baseline), window)!.Value.Y;
            double valueBaselineY = valueLabel.TranslatePoint(new Point(0, valueLabel.TextLayout!.TextLines[0].Baseline), window)!.Value.Y;

            Assert.Equal(valueBaselineY, labelBaselineY, 2);
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

        [AvaloniaFact]
        public void InspectorGroupTile_ShowsFourGroupsExpandedHoveredFocusedCollapsed() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Inspector group — expanded, collapsed, hovered, focused");
            InspectorGroup[] groups = [.. tile.GetVisualDescendants().OfType<InspectorGroup>()];

            Assert.Equal(4, groups.Length);
            Assert.Contains(groups, g => (string?)g.Header == "Recipe" && g.IsExpanded);
            Assert.Contains(groups, g => (string?)g.Header == "Module slots" && g.Classes.Contains("gallery-hover"));
            Assert.Contains(groups, g => (string?)g.Header == "Beacons" && g.IsExpanded);
            Assert.Contains(groups, g => (string?)g.Header == "View options" && !g.IsExpanded);
        }

        [AvaloniaFact]
        public void SplitDividerTile_ShowsFourDividersRestHoveredDraggingFocused() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Split divider — rest, hovered, dragging, focused");
            SplitDivider[] dividers = [.. tile.GetVisualDescendants().OfType<SplitDivider>()];

            Assert.Equal(4, dividers.Length);
            Assert.Contains(dividers, d => d.Classes.Contains("gallery-hover"));
            Assert.Contains(dividers, d => d.Classes.Contains("gallery-dragging"));
            Assert.All(dividers, d => Assert.Equal(300, d.Value));
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

        [AvaloniaFact]
        public void PickCellStatesTile_ShowsFilledAndEmptyRowsAtFiveStatesEach() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Pick cell — filled and empty, five states each");
            PickCell[] cells = [.. tile.GetVisualDescendants().OfType<PickCell>()];

            Assert.Equal(10, cells.Length);
            Assert.Equal(5, cells.Count(c => c.Icon is not null));
            Assert.Equal(5, cells.Count(c => c.IsEmpty));
            Assert.Contains(cells, c => c.IsSelected);
            Assert.Contains(cells, c => c.Classes.Contains("gallery-hover"));
            Assert.Contains(cells, c => !c.IsEnabled);
        }

        [AvaloniaFact]
        public void PickGridTile_ShowsAModuleRowAndABeaconRowInsideFormRows() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Pick grid — module slots & beacon row");
            FormRow[] rows = [.. tile.GetVisualDescendants().OfType<FormRow>()];
            PickGrid[] grids = [.. tile.GetVisualDescendants().OfType<PickGrid>()];

            Assert.Equal(2, rows.Length);
            Assert.All(rows, r => Assert.True(r.IsInspector));
            Assert.Equal(2, grids.Length);
            Assert.Contains(grids, g => g.Slots.Count == 4);
            Assert.Contains(grids, g => g.Slots.Count == 2);
        }

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
        public void ChooserCellStatesTile_ShowsFiveStates() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Chooser cell — five states");
            ChooserCell[] cells = [.. tile.GetVisualDescendants().OfType<ChooserCell>()];

            Assert.Equal(5, cells.Length);
            Assert.All(cells, c => Assert.NotNull(c.Icon));
            Assert.Contains(cells, c => c.IsSelected);
            Assert.Contains(cells, c => c.Classes.Contains("gallery-hover"));
            Assert.Contains(cells, c => !c.IsEnabled);
        }

        [AvaloniaFact]
        public void ChooserGridTile_ShowsTwoSearchResultHeadings() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Chooser grid — search results, two headings");
            ChooserGrid grid = tile.GetVisualDescendants().OfType<ChooserGrid>().Single();

            Assert.Equal(2, grid.Sections.Count);
            Assert.Equal(5, grid.GetVisualDescendants().OfType<ChooserCell>().Count());
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
        public void SheetFrameTile_ShowsAnOpenSheetWithATitleAndButtonRow() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Scrim & Sheet frame");
            SheetFrame sheet = tile.GetVisualDescendants().OfType<SheetFrame>().Single();

            Assert.True(sheet.IsOpen);
            Assert.Equal("Export an image", sheet.Title);
            Assert.NotNull(sheet.Buttons);
        }

        [AvaloniaFact]
        public void ButtonRowShapesTile_ShowsAllFiveRows_DestructiveNeverDefault() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Button row — five shapes");
            ButtonRow[] rows = [.. tile.GetVisualDescendants().OfType<ButtonRow>()];

            Assert.Equal(5, rows.Length);
            ButtonRow destructiveRow = rows.Single(r => r.Items.OfType<Button>().Any(b => ButtonRow.GetRole(b) == ButtonRowRole.Destructive));
            Button destructive = destructiveRow.Items.OfType<Button>().Single(b => ButtonRow.GetRole(b) == ButtonRowRole.Destructive);
            Assert.DoesNotContain("primary", destructive.Classes);
        }

        [AvaloniaFact]
        public void AlertFramesTile_ShowsTwoAndThreeButtonAlerts() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Alert frame — two-button & three-button");
            AlertFrame[] alerts = [.. tile.GetVisualDescendants().OfType<AlertFrame>()];

            Assert.Equal(2, alerts.Length);
            Assert.Contains(alerts, a => a.Buttons!.Items.OfType<Button>().Count() == 2);
            Assert.Contains(alerts, a => a.Buttons!.Items.OfType<Button>().Count() == 3);
            //The app icon asset (the same one AboutWindow renders), not an empty 32pt column.
            Assert.All(alerts, a => Assert.IsAssignableFrom<Bitmap>(a.Icon));
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
        public void PopoverFrameTile_OpensBelowItsAnchor_HostingAFormGroup() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Popover frame — anchored below, with a form group inside");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();

            Assert.True(popover.IsOpen);
            Assert.Equal(PopoverEdge.Bottom, popover.ResolvedPlacement);
            FormGroup group = Assert.IsType<FormGroup>(popover.Content);
            Assert.Equal("Show on nodes", group.Title);
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
        public void PopoverFrameCornerTile_ClampsTheTailNearTheAnchorsCorner() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Popover frame — tail clamped near a corner");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            Border tail = popover.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Tail");

            Assert.True(popover.IsOpen);
            Assert.Equal(PopoverEdge.Bottom, popover.ResolvedPlacement);
            Assert.Equal(26, tail.Margin.Left);
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
        public void RadialRingTile_ShowsSixSlicesWithOneArmed() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Radial ring — six slices, one armed");
            RadialRing ring = tile.GetVisualDescendants().OfType<RadialRing>().Single();

            Assert.True(ring.IsOpen);
            Assert.Equal(6, ring.Items.Count);
            Assert.Equal(2, ring.ArmedIndex);
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
        public void StatePanelEmptyTile_ShowsInspectorAndNewGraphShapes_WithFittingButtonCounts() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "State panel — empty (inspector & new graph)");
            StatePanel[] panels = [.. tile.GetVisualDescendants().OfType<StatePanel>()];

            Assert.Equal(2, panels.Length);
            Assert.All(panels, p => Assert.Equal(StatePanelShape.Empty, p.Shape));
            Assert.Single(panels[0].Buttons!.Items);
            Assert.Equal(2, panels[1].Buttons!.Items.Count);
        }

        [AvaloniaFact]
        public void StatePanelNoResultsTile_QuotesTheQuery_AndOffersClearFilters() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "State panel — no results");
            StatePanel panel = tile.GetVisualDescendants().OfType<StatePanel>().Single();

            Assert.Equal(StatePanelShape.NoResults, panel.Shape);
            Assert.Contains("beacn", panel.Headline);
            Assert.Equal(2, panel.Buttons!.Items.Count);
        }

        [AvaloniaFact]
        public void StatePanelLoadingTile_FullStaysFull_NarrowRegionForcesCompact() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "State panel — loading (determinate & compact)");
            StatePanel[] panels = [.. tile.GetVisualDescendants().OfType<StatePanel>()];

            Assert.Equal(2, panels.Length);
            Assert.All(panels, p => Assert.Equal(StatePanelShape.Loading, p.Shape));
            Assert.False(panels[0].IsCompact);
            Assert.True(panels[1].IsCompact);
        }

        [AvaloniaFact]
        public void StatePanelErrorTile_NamesTheCause_WithThreeButtons() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "State panel — error");
            StatePanel panel = tile.GetVisualDescendants().OfType<StatePanel>().Single();

            Assert.Equal(StatePanelShape.Error, panel.Shape);
            Assert.Equal(3, panel.Buttons!.Items.Count);
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
        public void InspectorGroupBodyStateTile_HostsAStatePanel_InTheGroupsFormGroupBody() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Inspector group — body delegation (State panel)");
            InspectorGroup group = tile.GetVisualDescendants().OfType<InspectorGroup>().Single();
            FormGroup body = Assert.IsType<FormGroup>(group.Content);
            StatePanel panel = tile.GetVisualDescendants().OfType<StatePanel>().Single();

            Assert.Equal(StatePanelShape.Error, panel.Shape);
            ItemsPresenter itemsPresenter = body.GetVisualDescendants().OfType<ItemsPresenter>().Single(p => p.TemplatedParent == body);
            Assert.False(itemsPresenter.IsVisible);
        }

        [AvaloniaFact]
        public void ChooserGridNoResultsTile_HostsAStatePanel_AndHidesTheGridScroll() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Chooser grid — no results (State panel)");
            ChooserGrid grid = tile.GetVisualDescendants().OfType<ChooserGrid>().Single();
            StatePanel panel = tile.GetVisualDescendants().OfType<StatePanel>().Single();

            Assert.Equal(StatePanelShape.NoResults, panel.Shape);
            ScrollViewer scroll = grid.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "PART_Scroll");
            Assert.False(scroll.IsVisible);
        }

        [AvaloniaFact]
        public void SheetFrameErrorTile_HostsAStatePanel_InItsStateSlot() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Sheet frame — error (State panel)");
            StatePanel panel = tile.GetVisualDescendants().OfType<StatePanel>().Single();
            Assert.Equal(StatePanelShape.Error, panel.Shape);
        }

        [AvaloniaFact]
        public void FormGroupLoadFailureTile_HostsAStatePanel_AndHidesTheEmptyRowList() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Form group — load failure (State panel)");
            FormGroup group = tile.GetVisualDescendants().OfType<FormGroup>().Single();
            StatePanel panel = tile.GetVisualDescendants().OfType<StatePanel>().Single();

            Assert.Equal(StatePanelShape.Error, panel.Shape);
            //The panel's own ButtonRow is an ItemsControl too (its own PART_ItemsPresenter) - scope to the one templated by the group itself.
            ItemsPresenter itemsPresenter = group.GetVisualDescendants().OfType<ItemsPresenter>().Single(p => p.TemplatedParent == group);
            Assert.False(itemsPresenter.IsVisible);
        }

        [AvaloniaFact]
        public void ZoomStepperTile_ShowsFourInstances_AtRestHoveredSolvingAndError() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Zoom stepper — at rest, hovered, solving, solve failed");
            ZoomStepper[] steppers = [.. tile.GetVisualDescendants().OfType<ZoomStepper>()];

            Assert.Equal(4, steppers.Length);
            Assert.False(steppers[0].IsSolving);
            Assert.False(steppers[0].HasSolveError);
            Assert.True(steppers[2].IsSolving);
            Assert.True(steppers[3].HasSolveError);

            Button hoveredReadout = steppers[1].GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Readout");
            Assert.Contains("gallery-hover", hoveredReadout.Classes);
        }

        [AvaloniaFact]
        public void ZoomStepperTile_SolvingExample_ShowsItsSpinnerAfterTheGraceSettles() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Zoom stepper — at rest, hovered, solving, solve failed");
            ZoomStepper solving = tile.GetVisualDescendants().OfType<ZoomStepper>().ElementAt(2);
            Assert.True(solving.IsSolving);

            Spinner spinner = solving.GetVisualDescendants().OfType<Spinner>().Single(s => s.Name == "PART_Spinner");
            AnimationClock.Settle(650);

            Assert.True(spinner.IsVisible);
            Assert.Equal(1, spinner.Opacity);
            Assert.Contains("animating", spinner.Classes);
            Assert.True(spinner.Bounds.Width > 0);
        }

        [AvaloniaFact]
        public void ZoomStepperPopoverTile_OpensItsOwnErrorPopover_CarryingTheHostSuppliedStatePanel() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Zoom stepper — solve-error popover");
            ZoomStepper stepper = tile.GetVisualDescendants().OfType<ZoomStepper>().Single();
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();

            Assert.True(stepper.HasSolveError);
            Assert.True(stepper.IsErrorPopoverOpen);
            Assert.True(popover.IsOpen);
            StatePanel panel = Assert.IsType<StatePanel>(popover.Content);
            Assert.Equal(StatePanelShape.Error, panel.Shape);
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
        public void ToolbarRowTile_ShowsFullLabelsDroppedAndOverflowedStates() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Toolbar row — full, labels dropped, overflowed");
            ToolbarRow[] rows = [.. tile.GetVisualDescendants().OfType<ToolbarRow>()];

            Assert.Equal(3, rows.Length);
            Assert.All(rows, r => Assert.Equal(52, r.Bounds.Height));
            //The title and the zoom stepper never collapse (addendum §08), at every one of the three widths.
            Assert.All(rows, r => Assert.True(r.ZoomStepper.IsVisible));

            ToolbarRow full = rows[0];
            Assert.All(full.Groups.SelectMany(g => g.Items), i => Assert.DoesNotContain("icon-only", i.Classes));
            Assert.Equal(220, full.SearchField.Width);

            ToolbarRow overflowed = rows[2];
            Assert.True(overflowed.OverflowButton.IsVisible);
            Assert.Equal(["Export", "View"], overflowed.OverflowButton.ActionItems.Select(a => a.Text));
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
                    foreach (Visual ancestor in AncestorsUpTo(row, tile))
                        if (ancestor is Border border)
                            Assert.Equal(default, border.BorderThickness);

                    var frame = Assert.IsType<Border>(row.GetVisualParent());
                    Assert.Equal(frame.Bounds.Width, row.Bounds.Width);
                }
        }

        private static IEnumerable<Visual> AncestorsUpTo(Visual start, Visual stop) {
            Visual? current = start.GetVisualParent();
            while (current is not null && !ReferenceEquals(current, stop)) {
                yield return current;
                current = current.GetVisualParent();
            }
        }

        [AvaloniaFact]
        public void PalettePanelTile_ShowsTopRowSelected_WithResultsAndFooterHints() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Palette panel — quick-add results");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            PalettePanel panel = Assert.IsType<PalettePanel>(popover.Content);

            Assert.True(popover.IsOpen);
            Assert.Equal(272, panel.Bounds.Width);
            Assert.Equal(4, panel.Results.Count);
            Assert.Equal(panel.Results[0], panel.SelectedItem);
            Assert.Equal(3, panel.FooterHints.Count);
            Assert.Equal("iron pl", panel.Search.Text);
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
        public void PalettePanelNoResultsTile_ShowsACompactStatePanel_WithTheSearchHeaderStillVisible() {
            DataCache cache = NewFixtureCache();
            var window = new GalleryWindow(cache, new AppSettings());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            StackPanel tile = window.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "Palette panel — no results");
            PopoverFrame popover = tile.GetVisualDescendants().OfType<PopoverFrame>().Single();
            PalettePanel panel = Assert.IsType<PalettePanel>(popover.Content);
            StatePanel state = tile.GetVisualDescendants().OfType<StatePanel>().Single();

            Assert.True(popover.IsOpen);
            Assert.Empty(panel.Results);
            Assert.Equal(StatePanelShape.NoResults, state.Shape);
            Assert.True(state.IsCompact);
            Assert.True(panel.Search.IsVisible);
            Assert.Single(panel.FooterHints);

            AnimationClock.Settle(450);
            Spinner spinner = panel.Search.GetVisualDescendants().OfType<Spinner>().Single(s => s.Name == "PART_LoadingSpinner");
            Assert.True(spinner.IsVisible);
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
