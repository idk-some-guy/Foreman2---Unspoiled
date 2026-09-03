using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.DataCaching.Loading;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    //Exercises the Task 2 chooser base (docs/panels-reference.md §2/§9 step 2): ChooserLayout/ChooserIconGrid
    //cell-layout math lives in ChooserLayoutTests.cs; this file covers IRChooserPanel's filter/group/paging
    //behavior end to end through a minimal test-only subclass that mirrors upstream ItemChooserPanel's filter
    //predicate (the real ItemChooserPanel/RecipeChooserPanel subclasses are Task 3), plus a real vanilla-preset
    //render. TestItemChooser is a nested subclass so it reaches IRChooserPanel's protected chrome directly,
    //the same way Task 3's real subclasses will - no reflection needed except for the one genuinely private
    //field (startingGroup) that upstream itself keeps process-wide static.
    public class ChooserPanelTests {
        private sealed class Fixture {
            public required DataCache Cache;
            public required GroupPrototype Group;
            public required AssemblerPrototype OnAssembler;
            public required AssemblerPrototype OffAssembler;
        }

        private static DataCacheStore Store(DataCache cache) {
            FieldInfo field = typeof(DataCache).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (DataCacheStore)field.GetValue(cache)!;
        }

        private static Fixture NewFixture() {
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            //Named "logistics" (not an arbitrary test name) so Initialize()'s upstream-verbatim
            //first-ever-show default (docs/panels-reference.md §2) auto-selects it without every test
            //having to special-case the group-not-found corner case.
            var group = new GroupPrototype(cache, "logistics", "Logistics", "a");
            store.Groups[group.Name] = group;

            //EntityObjectBasePrototype.Available defaults to a computed "has an available-producing item"
            //check (false with no associated items wired), not the plain settable flag DataObjectBasePrototype
            //gives everything else - force it true so these behave like an ordinary present-in-the-preset
            //assembler; only Enabled is what these tests vary.
            var onAssembler = new AssemblerPrototype(cache, "asm-on", "Assembler On", EntityType.Assembler, EnergySource.Electric) { Available = true };
            var offAssembler = new AssemblerPrototype(cache, "asm-off", "Assembler Off", EntityType.Assembler, EnergySource.Electric) { Available = true, Enabled = false };

            return new Fixture { Cache = cache, Group = group, OnAssembler = onAssembler, OffAssembler = offAssembler };
        }

        private static SubgroupPrototype AddSubgroup(Fixture fx, IGroup group, string name, string order) {
            var subgroup = new SubgroupPrototype(fx.Cache, name, order);
            var groupProto = (GroupPrototype)group;
            subgroup.MyGroupInternal = groupProto;
            groupProto.SubgroupsInternal.Add(subgroup);
            Store(fx.Cache).Subgroups[name] = subgroup;
            return subgroup;
        }

        private static ItemPrototype AddItem(Fixture fx, SubgroupPrototype subgroup, string name, string friendlyName, bool available = true) {
            var item = new ItemPrototype(fx.Cache, name, friendlyName, subgroup, "a") { Available = available };
            Store(fx.Cache).Items[name] = item;
            return item;
        }

        //Wires a production recipe for `item` so it counts as "visible" per upstream's ItemChooserPanel
        //predicate; `assembler` null leaves it recipe-having-but-assembler-less (the NoAssembler color case).
        private static void AddProduction(Fixture fx, SubgroupPrototype subgroup, ItemPrototype item, AssemblerPrototype? assembler, string recipeName) {
            var recipe = new RecipePrototype(fx.Cache, recipeName, recipeName, subgroup, "a");
            recipe.ProductSetInternal[item] = 1;
            recipe.ProductListInternal.Add(item);
            item.ProductionRecipesInternal.Add(recipe);
            if (assembler is not null) {
                recipe.AssemblersInternal.Add(assembler);
                assembler.RecipesInternal.Add(recipe);
            }
            Store(fx.Cache).Recipes[recipeName] = recipe;
        }

        //Mirrors upstream ItemChooserPanel (Task 3's real subclass) closely enough to drive the base class's
        //own behavior under test, with a few public passthroughs onto the inherited protected chrome.
        private sealed class TestItemChooser(DataCache cache, AppSettings settings) : IRChooserPanel(settings) {
            public IDataObjectBase? LastSelected;

            public TextBox FilterBox => FilterTextBox;
            public CheckBox ShowHiddenBox => ShowHiddenCheckBox;
            public CheckBox IgnoreAssemblerBox => IgnoreAssemblerCheckBox;
            public CheckBox RecipeNameOnlyBox => RecipeNameOnlyFilterCheckBox;
            public IReadOnlyList<IReadOnlyList<IconButton>> GridButtons => IconGrid.Buttons;
            public ScrollBar GridScrollBar => IconGrid.ScrollBar;
            public IGroup? CurrentGroup => SelectedGroup;
            public List<IGroup>? Groups => SortedGroups;
            public ChooserIconGrid Grid => IconGrid;
            public IconButton GroupButtonFor(IGroup group) => GroupsPanel.Children.OfType<IconButton>().First(b => Equals(b.DataObject, group));
            public TextBlock FilterLabelControl => FilterLabel;
            public WrapPanel GroupsPanelControl => GroupsPanel;
            public string FilterLabelText => FilterLabel.Text ?? "";
            public string RecipeNameOnlyText => (string)RecipeNameOnlyFilterCheckBox.Content!;
            public string IgnoreAssemblerText => (string)IgnoreAssemblerCheckBox.Content!;
            public string ShowHiddenText => (string)ShowHiddenCheckBox.Content!;
            public string AsIngredientText => (string)AsIngredientCheckBox.Content!;
            public string AsProductText => (string)AsProductCheckBox.Content!;
            public string AsFuelText => (string)AsFuelCheckBox.Content!;
            public string AddSupplyText => (string)AddSupplyButton.Content!;
            public string AddPassthroughText => (string)AddPassthroughButton.Content!;
            public string AddConsumerText => (string)AddConsumerButton.Content!;
            public string AddUnspoilText => (string)AddUnspoilButton.Content!;
            public string AddUnplantText => (string)AddUnplantButton.Content!;
            public string AddSpoilText => (string)AddSpoilButton.Content!;
            public string AddPlantText => (string)AddPlantButton.Content!;

            public void InvokeUpdateIRButtons(int startRow = 0, bool scrollOnly = false) => UpdateIRButtons(startRow, scrollOnly);
            public void SelectGroup(IGroup? group) => SetSelectedGroup(group);
            public void InvokeClosePanel(ChooserPanelCloseReason reason) => ClosePanel(reason);

            protected override List<IGroup> GetSortedGroups() {
                var groups = new List<IGroup>();
                foreach (IGroup group in ShowUnavailable ? cache.Groups.Values : cache.AvailableGroups) {
                    int itemCount = 0;
                    foreach (ISubgroup sgroup in group.Subgroups)
                        itemCount += ShowUnavailable ? sgroup.Items.Count : sgroup.Items.Count(i => i.Available);
                    if (itemCount > 0)
                        groups.Add(group);
                }
                groups.Sort();
                return groups;
            }

            protected override List<List<KeyValuePair<IDataObjectBase, Color>>> GetSubgroupList() {
                string filterString = FilterTextBox.Text?.ToLowerInvariant() ?? "";
                bool ignoreAssemblerStatus = IgnoreAssemblerCheckBox.IsChecked ?? false;
                bool showHidden = ShowHiddenCheckBox.IsChecked ?? false;

                var filteredItems = new Dictionary<IGroup, List<List<KeyValuePair<IDataObjectBase, Color>>>>();
                var filteredItemCount = new Dictionary<IGroup, int>();
                foreach (IGroup group in SortedGroups ?? []) {
                    int itemCounter = 0;
                    var sgList = new List<List<KeyValuePair<IDataObjectBase, Color>>>();
                    foreach (ISubgroup sgroup in group.Subgroups) {
                        var itemList = new List<KeyValuePair<IDataObjectBase, Color>>();
                        foreach (IItem item in sgroup.Items.Where(i =>
                            (ShowUnavailable || i.Available) &&
                            (i.LFriendlyName.Contains(filterString) || i.Name.Contains(filterString, StringComparison.OrdinalIgnoreCase)))) {
                            bool visible = (ShowUnavailable || item.Available) &&
                                (item.ConsumptionRecipes.Any(r => r.Enabled && (ShowUnavailable || r.Available)) ||
                                 item.ProductionRecipes.Any(r => r.Enabled && (ShowUnavailable || r.Available)));
                            bool validAssembler =
                                item.ConsumptionRecipes.Any(r => r.Enabled && (ShowUnavailable || r.Available) && r.Assemblers.Any(a => a.Enabled && (ShowUnavailable || a.Available))) ||
                                item.ProductionRecipes.Any(r => r.Enabled && (ShowUnavailable || r.Available) && r.Assemblers.Any(a => a.Enabled && (ShowUnavailable || a.Available)));

                            Color bgColor = visible && item.Available
                                ? validAssembler ? IRButtonDefaultColor : IRButtonNoAssemblerColor
                                : IRButtonHiddenColor;

                            if ((visible || showHidden) && (validAssembler || ignoreAssemblerStatus)) {
                                itemCounter++;
                                itemList.Add(new KeyValuePair<IDataObjectBase, Color>(item, bgColor));
                            }
                        }
                        sgList.Add(itemList);
                    }
                    filteredItems.Add(group, sgList);
                    filteredItemCount.Add(group, itemCounter);
                    UpdateGroupButton(group, itemCounter != 0);
                }

                IGroup? alternateGroup = null;
                if (SelectedGroup is not null && SortedGroups is not null && filteredItemCount[SelectedGroup] == 0) {
                    int selectedGroupIndex = 0;
                    for (int i = 0; i < SortedGroups.Count; i++)
                        if (SortedGroups[i] == SelectedGroup)
                            selectedGroupIndex = i;
                    for (int i = selectedGroupIndex; i >= 0; i--)
                        if (filteredItemCount[SortedGroups[i]] > 0)
                            alternateGroup = SortedGroups[i];
                    if (alternateGroup is null)
                        for (int i = selectedGroupIndex; i < SortedGroups.Count; i++)
                            if (filteredItemCount[SortedGroups[i]] > 0)
                                alternateGroup = SortedGroups[i];
                    alternateGroup ??= SelectedGroup;
                }
                SetSelectedGroup(alternateGroup ?? SelectedGroup, causeUpdate: false);

                return SelectedGroup is not null ? filteredItems[SelectedGroup] : [];
            }

            protected override void IRButtonMouseUp(IconButton button, PointerReleasedEventArgs e) {
                if (button.DataObject is IItem item && e.InitialPressMouseButton == MouseButton.Left) {
                    LastSelected = item;
                    ClosePanel(ChooserPanelCloseReason.ItemSelected);
                }
            }
        }

        //IRChooserPanel.startingGroup is process-wide static state by design (docs/panels-reference.md §2:
        //"last-selected group persists across panel instances") - reset it so tests don't leak selection
        //across cases; xUnit gives each test method a fresh ChooserPanelTests instance, so this runs before
        //every test.
        public ChooserPanelTests() {
            FieldInfo field = typeof(IRChooserPanel).GetField("startingGroup", BindingFlags.Static | BindingFlags.NonPublic)!;
            field.SetValue(null, null);
        }

        private static TestItemChooser NewPanel(Fixture fx, bool showUnavailable = false) {
            var panel = new TestItemChooser(fx.Cache, new AppSettings { ShowUnavailable = showUnavailable });
            panel.Initialize();
            return panel;
        }

        private static List<IDataObjectBase> PopulatedDataObjects(TestItemChooser panel) =>
            [.. panel.GridButtons.SelectMany(column => column).Select(b => b.DataObject).OfType<IDataObjectBase>()];

        private static IconButton FindCell(TestItemChooser panel, IDataObjectBase dataObject) =>
            panel.GridButtons.SelectMany(column => column).First(b => Equals(b.DataObject, dataObject));

        [AvaloniaFact]
        public void Captions_MatchUpstreamVerbatim() {
            TestItemChooser panel = NewPanel(NewFixture());

            Assert.Equal("Filter:", panel.FilterLabelText);
            Assert.Equal("Recipe Only", panel.RecipeNameOnlyText);
            Assert.Equal("Ignore Assembler", panel.IgnoreAssemblerText);
            Assert.Equal("Show Hidden", panel.ShowHiddenText);
            Assert.Equal("Ingredient", panel.AsIngredientText);
            Assert.Equal("Product", panel.AsProductText);
            Assert.Equal("Fuel", panel.AsFuelText);
            Assert.Equal("Source", panel.AddSupplyText);
            Assert.Equal("Pass-Through", panel.AddPassthroughText);
            Assert.Equal("Output", panel.AddConsumerText);
            Assert.Equal("UnSpoil", panel.AddUnspoilText);
            Assert.Equal("UnPlant", panel.AddUnplantText);
            Assert.Equal("Spoil", panel.AddSpoilText);
            Assert.Equal("Plant", panel.AddPlantText);
        }

        [AvaloniaFact]
        public void IconButton_Tooltip_UsesFriendlyName_FallsBackToDash() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            ItemPrototype named = AddItem(fx, sg, "dev-name", "Friendly Name");
            ItemPrototype blank = AddItem(fx, sg, "dev-blank", "");

            var namedButton = new IconButton();
            namedButton.SetPopulated(named, IconButton.EmptyFillColor);
            var blankButton = new IconButton();
            blankButton.SetPopulated(blank, IconButton.EmptyFillColor);

            Assert.Equal("Friendly Name", ToolTip.GetTip(namedButton));
            Assert.Equal("-", ToolTip.GetTip(blankButton));
        }

        //Pixel proof of NFButton.OnEnabledChanged's grayscale-on-disable (docs/panels-reference.md §2): a
        //pure-red icon, once IsEnabled goes false (the group-button-with-zero-filtered-items path), must
        //paint as a luminance-weighted gray with alpha cut to 0.4 - not just a recolor, an actual desaturated
        //bitmap. Falsification-checked by temporarily removing PaintOnto's ColorFilter assignment: this
        //assertion fails (actual stays pure red) without it, and passes with it restored.
        [AvaloniaFact]
        public void IconButton_Disabled_RendersLuminanceGrayscaleAtQuarterAlpha() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            ItemPrototype item = AddItem(fx, sg, "red-item", "Red Item");
            using var redBitmap = new SKBitmap(4, 4);
            using (var canvas = new SKCanvas(redBitmap))
                canvas.Clear(new SKColor(255, 0, 0, 255));
            item.SetIconAndColor(new IconColorPair(redBitmap, System.Drawing.Color.Red));

            var button = new IconButton();
            button.SetPopulated(item, IconButton.EmptyFillColor);

            using SKSurface enabledSurface = SKSurface.Create(new SKImageInfo(4, 4));
            button.PaintOnto(enabledSurface.Canvas, new SKRect(0, 0, 4, 4));
            SKColor enabledPixel = PixelAt(enabledSurface, 2, 2);
            Assert.Equal(new SKColor(255, 0, 0, 255), enabledPixel);

            button.IsEnabled = false;
            using SKSurface disabledSurface = SKSurface.Create(new SKImageInfo(4, 4));
            button.PaintOnto(disabledSurface.Canvas, new SKRect(0, 0, 4, 4));
            SKColor disabledPixel = PixelAt(disabledSurface, 2, 2);

            //PaintOnto fills FillColor under the icon first (as upstream's BackColor does under BackgroundImage),
            //so the disabled icon's 0.4-alpha gray composites over that fill rather than reading back in
            //isolation: expect 0.4*luminance(255,0,0) + 0.6*105 (EmptyFillColor) ~= 85, fully opaque.
            Assert.Equal(disabledPixel.Red, disabledPixel.Green);
            Assert.Equal(disabledPixel.Green, disabledPixel.Blue);
            Assert.InRange(disabledPixel.Red, 80, 90);
            Assert.Equal((byte)255, disabledPixel.Alpha);
        }

        //Sums each ancestor's own laid-out Bounds.X/Y from `descendant` up to (not including) `root` - the
        //same coordinate accumulation RenderOffscreen's PaintVisual walk does top-down (parentAbsX + bounds.X
        //at each level), just computed bottom-up here so the pixel-centering test below isn't reading its
        //expected position from the very paint-walk code it's checking.
        //Includes root's own Bounds.X/Y in the sum: RenderOffscreen's PaintVisual(canvas, this, 0, 0) feeds
        //parentAbsX/Y=0 in, but then immediately computes x = parentAbsX + bounds.X for that very first call -
        //since `this` (root) is still a live child of FloatingPanelHost's overlay Canvas when RenderOffscreen
        //runs (never reparented), its own Bounds.X/Y reflects wherever Canvas.Left/Top last placed it, not
        //(0,0) - so root's own offset is very much part of the real absolute paint position, not excluded
        //from it.
        private static Point AbsoluteTopLeft(Visual descendant, Visual root) {
            double x = 0, y = 0;
            for (Visual? current = descendant; current is not null; current = current.GetVisualParent()) {
                x += current.Bounds.X;
                y += current.Bounds.Y;
                if (ReferenceEquals(current, root))
                    break;
            }
            return new Point(x, y);
        }

        private static SKColor PixelAt(SKSurface surface, int x, int y) {
            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            return pixmap.GetPixelColor(x, y);
        }

        //Review finding 3: RenderOffscreen's text-drawing walk used to bottom-anchor every label's baseline
        //("rowY + rowHeight - 4"), so a checkbox caption's ink sat visibly low against its glyph in the
        //rendered PNG instead of centered - visible by eye in task-3-recipe-chooser-keyitem-render.png before
        //the fix. Proves the fix by scanning real rendered pixels: find the label ink's own vertical extent
        //(anything light enough to be white text against the dark-gray chrome background) in a column strip
        //past the checkbox glyph, and check its midpoint lands within a few px of the glyph's own vertical
        //center - computed independently via AbsoluteTopLeft's real Avalonia layout Bounds walk, not from the
        //same paint-walk math being tested, so this can't pass by construction.
        [AvaloniaFact]
        public void RenderOffscreen_CheckboxLabelText_VerticallyCentersAgainstGlyph() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            AddItem(fx, sg, "widget", "Widget");
            TestItemChooser panel = NewPanel(fx);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 700);
            var window = new AvaloniaWindow { Content = control, Width = 900, Height = 700 };
            window.Show();
            control.FloatingPanelHost.Show(panel, new DrawingPoint(50, 50));

            using SKSurface surface = SKSurface.Create(new SKImageInfo(900, 700));
            panel.RenderOffscreen(surface.Canvas, 900, 700);

            CheckBox checkBox = panel.ShowHiddenBox;
            Point topLeft = AbsoluteTopLeft(checkBox, panel);
            double glyphCenterY = topLeft.Y + checkBox.Bounds.Height / 2;
            int xScanStart = (int)topLeft.X + 16; //past the 12px glyph plus its 4px gap, into the label text itself

            using SKImage image = surface.Snapshot();
            using SKPixmap pixmap = image.PeekPixels();
            int minInkY = int.MaxValue, maxInkY = int.MinValue;
            for (int py = (int)topLeft.Y - 5; py <= topLeft.Y + checkBox.Bounds.Height + 5; py++) {
                for (int px = xScanStart; px < xScanStart + (int)checkBox.Bounds.Width - 16; px++) {
                    SKColor c = pixmap.GetPixelColor(px, py);
                    if (c.Red > 200 && c.Green > 200 && c.Blue > 200) {
                        minInkY = Math.Min(minInkY, py);
                        maxInkY = Math.Max(maxInkY, py);
                    }
                }
            }

            Assert.True(minInkY <= maxInkY, "Expected to find label ink pixels in the scanned strip.");
            double inkCenterY = (minInkY + maxInkY) / 2.0;
            Assert.InRange(inkCenterY, glyphCenterY - 3, glyphCenterY + 3);
        }

        [AvaloniaFact]
        public void Initialize_SelectsLogisticsGroup_OverOtherSortedGroups() {
            Fixture fx = NewFixture();
            SubgroupPrototype logisticsSub = AddSubgroup(fx, fx.Group, "sg-logistics", "a");
            AddItem(fx, logisticsSub, "widget", "Widget");
            var earlierGroup = new GroupPrototype(fx.Cache, "aaa-earlier", "AAA Earlier", "0"); //sorts before "logistics"
            Store(fx.Cache).Groups[earlierGroup.Name] = earlierGroup;
            SubgroupPrototype earlierSub = AddSubgroup(fx, earlierGroup, "sg-earlier", "a");
            AddItem(fx, earlierSub, "gadget", "Gadget");

            TestItemChooser panel = NewPanel(fx);

            Assert.Equal(fx.Group, panel.CurrentGroup);
        }

        [AvaloniaFact]
        public void GroupButtons_DisabledWhenZeroFilteredItems_EnabledOtherwise() {
            Fixture fx = NewFixture();
            SubgroupPrototype withItems = AddSubgroup(fx, fx.Group, "sg-a", "a");
            ItemPrototype widget = AddItem(fx, withItems, "widget", "Widget");
            AddProduction(fx, withItems, widget, fx.OnAssembler, "recipe-widget");
            //Available (so GetSortedGroups puts it in the group row at all) but recipe-less (so it never
            //passes the "visible" check) - distinct from an unavailable item, which GetSortedGroups itself
            //would exclude the whole group over, never reaching the per-filter group-button-disable path.
            var emptyGroup = new GroupPrototype(fx.Cache, "empty-grp", "Empty Group", "b");
            Store(fx.Cache).Groups[emptyGroup.Name] = emptyGroup;
            SubgroupPrototype emptySub = AddSubgroup(fx, emptyGroup, "sg-empty", "a");
            AddItem(fx, emptySub, "unreachable", "Unreachable");

            TestItemChooser panel = NewPanel(fx);

            Assert.True(panel.GroupButtonFor(fx.Group).IsEnabled);
            Assert.False(panel.GroupButtonFor(emptyGroup).IsEnabled);
        }

        [AvaloniaFact]
        public void Search_MatchesDevName_CaseInsensitive() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            ItemPrototype target = AddItem(fx, sg, "iron-plate-widget", "Metal Sheet");
            AddProduction(fx, sg, target, fx.OnAssembler, "recipe-target");
            AddItem(fx, sg, "copper-cable-thing", "Copper Wire");

            TestItemChooser panel = NewPanel(fx);
            panel.FilterBox.Text = "IRON-PLATE";

            Assert.Equal([target], PopulatedDataObjects(panel));
        }

        [AvaloniaFact]
        public void Search_MatchesTranslatedFriendlyName_CaseInsensitive() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            ItemPrototype target = AddItem(fx, sg, "dev-name-a", "Molten Copper");
            AddProduction(fx, sg, target, fx.OnAssembler, "recipe-target");
            AddItem(fx, sg, "dev-name-b", "Iron Gear");

            TestItemChooser panel = NewPanel(fx);
            panel.FilterBox.Text = "MOLTEN";

            Assert.Equal([target], PopulatedDataObjects(panel));
        }

        [AvaloniaFact]
        public void ShowHidden_Toggle_IncludesOtherwiseExcludedItems() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            ItemPrototype hidden = AddItem(fx, sg, "no-recipe-item", "No Recipe Item"); //no production/consumption recipe -> not "visible"

            TestItemChooser panel = NewPanel(fx);
            Assert.DoesNotContain(hidden, PopulatedDataObjects(panel));

            //Item-panel "visible" and "valid assembler" both re-check recipe.Enabled internally (upstream
            //ItemChooserPanel.GetSubgroupList, ported verbatim), so a with-no-recipe-at-all item only comes
            //back with both flags checked - Show Hidden alone can never satisfy the validAssembler clause.
            panel.ShowHiddenBox.IsChecked = true;
            panel.IgnoreAssemblerBox.IsChecked = true;
            panel.InvokeUpdateIRButtons();

            Assert.Contains(hidden, PopulatedDataObjects(panel));
        }

        [AvaloniaFact]
        public void IgnoreAssembler_Toggle_IncludesItemsWithNoValidAssembler() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            ItemPrototype noAssembler = AddItem(fx, sg, "no-assembler-item", "No Assembler Item");
            AddProduction(fx, sg, noAssembler, fx.OffAssembler, "recipe-off");

            TestItemChooser panel = NewPanel(fx);
            Assert.DoesNotContain(noAssembler, PopulatedDataObjects(panel));

            panel.IgnoreAssemblerBox.IsChecked = true;
            panel.InvokeUpdateIRButtons();

            Assert.Contains(noAssembler, PopulatedDataObjects(panel));
        }

        [AvaloniaFact]
        public void CellColors_DefaultNoAssemblerEmpty_MatchUpstreamRgb() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            ItemPrototype defaultItem = AddItem(fx, sg, "default-item", "Default Item");
            AddProduction(fx, sg, defaultItem, fx.OnAssembler, "recipe-default");
            ItemPrototype noAssemblerItem = AddItem(fx, sg, "noasm-item", "No Assembler Item");
            AddProduction(fx, sg, noAssemblerItem, fx.OffAssembler, "recipe-noasm");

            TestItemChooser panel = NewPanel(fx);
            panel.IgnoreAssemblerBox.IsChecked = true;
            panel.InvokeUpdateIRButtons();

            Assert.Equal(Color.FromRgb(70, 70, 70), FindCell(panel, defaultItem).FillColor);
            Assert.Equal(Color.FromRgb(100, 100, 0), FindCell(panel, noAssemblerItem).FillColor);
            IconButton emptyCell = panel.GridButtons.SelectMany(c => c).First(b => b.DataObject is null);
            Assert.Equal(Color.FromRgb(105, 105, 105), emptyCell.FillColor);
        }

        [AvaloniaFact]
        public void ShowUnavailable_ConstructorFlag_GatesAvailabilityAndGroupInclusion() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            ItemPrototype unavailable = AddItem(fx, sg, "unavailable-item", "Unavailable Item", available: false);
            AddProduction(fx, sg, unavailable, fx.OnAssembler, "recipe-unavailable");

            TestItemChooser hiddenByDefault = NewPanel(fx, showUnavailable: false);
            Assert.Empty(hiddenByDefault.Groups ?? []);

            TestItemChooser shown = NewPanel(fx, showUnavailable: true);
            Assert.Contains(unavailable, PopulatedDataObjects(shown));
        }

        [AvaloniaFact]
        public void Paging_ScrollBarEnablesPastEightRows_AndScrollShiftsVisibleContent() {
            Fixture fx = NewFixture();
            var items = new List<ItemPrototype>();
            for (int i = 0; i < 9; i++) {
                SubgroupPrototype sg = AddSubgroup(fx, fx.Group, $"sg-page-{i}", i.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                ItemPrototype item = AddItem(fx, sg, $"page-item-{i}", $"Page Item {i}");
                AddProduction(fx, sg, item, fx.OnAssembler, $"recipe-page-{i}");
                items.Add(item);
            }

            TestItemChooser panel = NewPanel(fx);

            Assert.True(panel.GridScrollBar.IsEnabled, "9 one-item subgroups make 9 rows, past the 8-row visible window.");
            //Upstream's Maximum (row count - 1) only reads as the true scrollable bound because WinForms'
            //native VScrollBar silently caps user interaction at Maximum-LargeChange+1 - a quirk Avalonia's
            //ScrollBar does not share, so a bare row-count-1 here lets a thumb drag or track click scroll
            //past the last row. The effective bound (row count - visible rows) is what Avalonia needs in
            //Maximum itself for that clamp to hold.
            Assert.Equal(1, panel.GridScrollBar.Maximum);
            Assert.Equal(items[0], panel.GridButtons[0][0].DataObject); //row 0, column 0: subgroup 0's item
            Assert.Equal(items[1], panel.GridButtons[0][1].DataObject); //row 1, column 0: subgroup 1's item (new row per subgroup)
            Assert.Null(panel.GridButtons[1][0].DataObject); //row 0, column 1: subgroup 0 contributes only one item

            panel.InvokeUpdateIRButtons(startRow: 1, scrollOnly: true);

            Assert.Equal(items[1], panel.GridButtons[0][0].DataObject);
        }

        //Direct content proof of Finding A2's "no over-scroll": scrolling to the scrollbar's own Maximum
        //must land the true last row in the grid's last visible slot, never a blank one. 20 one-item
        //subgroups (rows 0-19) against an 8-row viewport put the effective max offset at 20-8=12; the old
        //row-count-1 Maximum (19) would land row 12 at the top with seven blank rows trailing below the
        //real content, since rows past index 19 don't exist.
        [AvaloniaFact]
        public void Paging_ScrollBarMaximum_LandsLastRowAtBottomSlot_NoBlankOverscroll() {
            Fixture fx = NewFixture();
            var items = new List<ItemPrototype>();
            for (int i = 0; i < 20; i++) {
                SubgroupPrototype sg = AddSubgroup(fx, fx.Group, $"sg-of-{i}", i.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                ItemPrototype item = AddItem(fx, sg, $"of-item-{i}", $"Overflow Item {i}");
                AddProduction(fx, sg, item, fx.OnAssembler, $"recipe-of-{i}");
                items.Add(item);
            }

            TestItemChooser panel = NewPanel(fx);
            Assert.Equal(12, panel.GridScrollBar.Maximum);

            panel.InvokeUpdateIRButtons(startRow: (int)panel.GridScrollBar.Maximum, scrollOnly: true);

            IconButton lastVisibleSlot = panel.GridButtons[0][ChooserIconGrid.VisibleRowCount - 1];
            Assert.Equal(items[19], lastVisibleSlot.DataObject);
        }

        //Finding A2's wheel-stepping half: wheeling all the way down should leave nothing further to scroll -
        //the scrollbar's own Value should land exactly on its Maximum, matching what the thumb visually shows.
        //Under the pre-fix Maximum (row count - 1) this fails outright: wheeling stops at the correct row
        //(12, via the upstream-ported "Maximum - LargeChange + 1" arithmetic in the wheel handler) while the
        //raw Maximum property still claims 19 is reachable - Value(12) != Maximum(19).
        [AvaloniaFact]
        public void WheelScrolling_ExhaustsExactlyAtScrollBarMaximum() {
            Fixture fx = NewFixture();
            for (int i = 0; i < 20; i++) {
                SubgroupPrototype sg = AddSubgroup(fx, fx.Group, $"sg-wh-{i}", i.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                ItemPrototype item = AddItem(fx, sg, $"wh-item-{i}", $"Wheel Item {i}");
                AddProduction(fx, sg, item, fx.OnAssembler, $"recipe-wh-{i}");
            }
            TestItemChooser panel = NewPanel(fx);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 700);
            var window = new AvaloniaWindow { Content = control, Width = 900, Height = 700 };
            window.Show();
            control.FloatingPanelHost.Show(panel, new DrawingPoint(50, 50));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Point gridTopLeft = AbsoluteTopLeft(panel.Grid, panel);
            var wheelPoint = new global::Avalonia.Point(gridTopLeft.X + 10, gridTopLeft.Y + 10);
            for (int notch = 0; notch < 25; notch++) //well past the 12-row effective range, to prove it stops
                window.MouseWheel(wheelPoint, new global::Avalonia.Vector(0, -1), RawInputModifiers.None);

            Assert.Equal(panel.GridScrollBar.Maximum, panel.GridScrollBar.Value);
        }

        //Finding A1: upstream insets its chooser content from the panel edge (IRChooserPanel.Designer.cs:88's
        //headerStack.Padding=(4,4,4,2) wrapping filterRow.Padding=(4,2,4,2) at :103, so FilterLabel's real
        //flow position lands at absolute x=8; :293's groupsPanel.Padding=(4,1,4,4) insets the group-icon row
        //by 4px left). Avalonia's StackPanel/WrapPanel carry no Padding of their own, so the port uses Margin
        //on the same containers to the same pixel values.
        [AvaloniaFact]
        public void HeaderAndGroupContent_IsInsetFromPanelEdge_MatchingUpstreamPadding() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            AddItem(fx, sg, "widget", "Widget");
            TestItemChooser panel = NewPanel(fx);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 700);
            var window = new AvaloniaWindow { Content = control, Width = 900, Height = 700 };
            window.Show();
            control.FloatingPanelHost.Show(panel, new DrawingPoint(50, 50));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            //AbsoluteTopLeft sums root's own Bounds too (by design, for RenderOffscreen's whole-viewport walk -
            //see its own comment above); subtract panel.Bounds.X/Y to get the position relative to the panel's
            //own content instead of wherever FloatingPanelHost placed it on screen.
            Point panelTopLeft = AbsoluteTopLeft(panel, panel);
            Point filterLabelTopLeft = AbsoluteTopLeft(panel.FilterLabelControl, panel);
            Assert.Equal(8, filterLabelTopLeft.X - panelTopLeft.X); //headerStack.Margin.Left(4) + filterRow.Margin.Left(4)

            IconButton firstGroupButton = panel.GroupButtonFor(fx.Group);
            Point groupButtonTopLeft = AbsoluteTopLeft(firstGroupButton, panel);
            groupButtonTopLeft = new Point(groupButtonTopLeft.X - panelTopLeft.X, groupButtonTopLeft.Y - panelTopLeft.Y);
            Assert.Equal(4, groupButtonTopLeft.X); //groupsPanel.Margin.Left(4)
        }

        [AvaloniaFact]
        public void GroupSwitch_ChangesGridContents() {
            Fixture fx = NewFixture();
            SubgroupPrototype sgA = AddSubgroup(fx, fx.Group, "sg-a", "a");
            ItemPrototype itemA = AddItem(fx, sgA, "item-a", "Item A");
            AddProduction(fx, sgA, itemA, fx.OnAssembler, "recipe-a");
            var groupB = new GroupPrototype(fx.Cache, "grp-b", "Group B", "b");
            Store(fx.Cache).Groups[groupB.Name] = groupB;
            SubgroupPrototype sgB = AddSubgroup(fx, groupB, "sg-b", "a");
            ItemPrototype itemB = AddItem(fx, sgB, "item-b", "Item B");
            AddProduction(fx, sgB, itemB, fx.OnAssembler, "recipe-b");

            TestItemChooser panel = NewPanel(fx);
            Assert.Contains(itemA, PopulatedDataObjects(panel));

            panel.SelectGroup(groupB);

            Assert.Contains(itemB, PopulatedDataObjects(panel));
            Assert.DoesNotContain(itemA, PopulatedDataObjects(panel));
        }

        [AvaloniaFact]
        public void ClosePanel_IsIdempotent_RaisesPanelClosedOnceAndPersistsSettings() {
            Fixture fx = NewFixture();
            var settings = new AppSettings();
            var panel = new TestItemChooser(fx.Cache, settings);
            panel.Initialize();
            int closedCount = 0;
            IRChooserPanel.ChooserPanelCloseReason? reason = null;
            panel.PanelClosed += (_, e) => { closedCount++; reason = e.Reason; };
            panel.ShowHiddenBox.IsChecked = true;

            panel.InvokeClosePanel(IRChooserPanel.ChooserPanelCloseReason.ItemSelected);
            panel.InvokeClosePanel(IRChooserPanel.ChooserPanelCloseReason.Cancelled);

            Assert.Equal(1, closedCount);
            Assert.Equal(IRChooserPanel.ChooserPanelCloseReason.ItemSelected, reason);
            Assert.True(settings.ShowHidden);
        }

        [AvaloniaFact]
        public void Constructor_RecipeNameOnlyFilterSettingTrue_SeedsCheckBoxChecked() {
            Fixture fx = NewFixture();
            var panel = new TestItemChooser(fx.Cache, new AppSettings { RecipeNameOnlyFilter = true });
            panel.Initialize();

            Assert.True(panel.RecipeNameOnlyBox.IsChecked);
        }

        [AvaloniaFact]
        public void DetachedFromVisualTree_ClosesPanelAsCancelled() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            AddItem(fx, sg, "widget", "Widget");
            TestItemChooser panel = NewPanel(fx);
            IRChooserPanel.ChooserPanelCloseReason? reason = null;
            panel.PanelClosed += (_, e) => reason = e.Reason;

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(400, 300);
            var window = new AvaloniaWindow { Content = control, Width = 400, Height = 300 };
            window.Show();
            control.FloatingPanelHost.Show(panel, new DrawingPoint(0, 0));

            control.FloatingPanelHost.Close();

            Assert.Equal(IRChooserPanel.ChooserPanelCloseReason.Cancelled, reason);
        }

        //IMPORTANT 2 (final fix wave): Initialize() used to call FilterTextBox.Focus() before the panel was
        //ever attached to the visual tree (a silent no-op there), and FloatingPanelHost.Show then focused
        //the panel's own root Border instead - so typing right after opening a chooser landed nowhere.
        [AvaloniaFact]
        public void Show_FocusesFilterTextBox_SoTypingFiltersImmediately() {
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            AddItem(fx, sg, "widget", "Widget");
            TestItemChooser panel = NewPanel(fx);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(400, 300);
            var window = new AvaloniaWindow { Content = control, Width = 400, Height = 300 };
            window.Show();
            control.FloatingPanelHost.Show(panel, new DrawingPoint(0, 0));

            Assert.True(panel.FilterBox.IsFocused);

            window.KeyTextInput("w");

            Assert.Equal("w", panel.FilterBox.Text);
        }

        //CRITICAL 1(a) (final fix wave): a wheel event landing over the panel's own icon grid used to bubble
        //straight up to GraphCanvasControl and zoom the canvas underneath, since only OnPointerPressed
        //guarded against panel-chrome input. Nine one-item subgroups (same paging setup as
        //Paging_ScrollBarEnablesPastEightRows_AndScrollShiftsVisibleContent below) give the grid something
        //to actually scroll.
        [AvaloniaFact]
        public void WheelOverPanel_DoesNotZoomCanvas_ButScrollsGridUnderneath() {
            Fixture fx = NewFixture();
            for (int i = 0; i < 9; i++) {
                SubgroupPrototype sg = AddSubgroup(fx, fx.Group, $"sg-wheel-{i}", i.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                ItemPrototype item = AddItem(fx, sg, $"wheel-item-{i}", $"Wheel Item {i}");
                AddProduction(fx, sg, item, fx.OnAssembler, $"recipe-wheel-{i}");
            }
            TestItemChooser panel = NewPanel(fx);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(900, 700);
            var window = new AvaloniaWindow { Content = control, Width = 900, Height = 700 };
            window.Show();
            control.FloatingPanelHost.Show(panel, new DrawingPoint(50, 50));
            float scaleBefore = control.Viewport.ViewScale;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); //flushes the deferred Arrange pass so panel.Grid.Bounds is real before we compute a screen point from it

            Point gridTopLeft = AbsoluteTopLeft(panel.Grid, panel);
            var wheelPoint = new global::Avalonia.Point(gridTopLeft.X + 10, gridTopLeft.Y + 10);
            window.MouseWheel(wheelPoint, new global::Avalonia.Vector(0, -1), RawInputModifiers.None);

            Assert.Equal(scaleBefore, control.Viewport.ViewScale);
            Assert.Equal(1, panel.GridScrollBar.Value);
        }

        //Populates a real vanilla-preset panel and renders it offscreen to prove the base panel mounts via
        //FloatingPanelHost and paints the whole panel (header chrome, group row, icon grid) end to end - at
        //a normal viewport and at a small one, so the small render also proves FitToViewport's shrink kicked
        //in (task deliverable: PNGs into the SDD workspace).
        [AvaloniaFact]
        public async Task Render_PopulatedGridWithRealPreset_ProducesNonEmptyPng() {
            var cache = new DataCache(filterRecipes: true);
            await cache.LoadAllData(new Preset("Factorio 2.0 Vanilla", true, true), new Progress<KeyValuePair<int, string>>());
            var fx = new Fixture {
                Cache = cache,
                Group = new GroupPrototype(cache, "§§test:unused", "Unused", "z"),
                OnAssembler = new AssemblerPrototype(cache, "§§test:unused-asm", "Unused", EntityType.Assembler, EnergySource.Electric),
                OffAssembler = new AssemblerPrototype(cache, "§§test:unused-asm-off", "Unused Off", EntityType.Assembler, EnergySource.Electric) { Enabled = false },
            };

            string sddDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                ".superpowers", "sdd", "2026-09-02-phase5a-floating-panels");
            Directory.CreateDirectory(sddDir);

            RenderPanelToPng(fx, 900, 700, Path.Combine(sddDir, "task-2-chooser-panel-render.png"));
            RenderPanelToPng(fx, 500, 400, Path.Combine(sddDir, "task-2-chooser-panel-render-small.png"), assertShrunk: true);
        }

        //Mounts a fresh panel through the real Task 1 host at the given viewport size (proving FitToViewport
        //actually ran for that size, not just a fixed design layout) and renders it offscreen via
        //RenderOffscreen's real Measure/Arrange-then-SKCanvas walk.
        private static void RenderPanelToPng(Fixture fx, int viewerWidth, int viewerHeight, string outPath, bool assertShrunk = false) {
            TestItemChooser panel = NewPanel(fx, showUnavailable: true);
            Assert.NotEmpty(PopulatedDataObjects(panel));

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(viewerWidth, viewerHeight);
            var window = new AvaloniaWindow { Content = control, Width = viewerWidth, Height = viewerHeight };
            window.Show();
            control.FloatingPanelHost.Show(panel, new DrawingPoint(50, 50));
            Assert.True(control.FloatingPanelHost.IsOpen);
            if (assertShrunk)
                Assert.True(panel.GridButtons[0][0].Width < ChooserLayout.CellSize,
                    $"Expected the {viewerWidth}x{viewerHeight} viewport to force the grid below the design cell size.");

            using SKSurface surface = SKSurface.Create(new SKImageInfo(viewerWidth, viewerHeight));
            panel.RenderOffscreen(surface.Canvas, viewerWidth, viewerHeight);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(outPath, data.ToArray());

            Assert.True(new FileInfo(outPath).Length > 0);
        }

        //---- shrink-to-fit: ports upstream ForemanTest/ChooserLayoutTests.cs's ItemChooser_HeightFitsShortViewer
        //     and ItemChooser_NoRightDeadSpaceWhenViewerShrinks against this port's panel/host, per review ----

        //Mounts a panel at the given viewer size and forces a real Measure/Arrange pass (the same one
        //RenderOffscreen uses), so panel.Grid.Bounds reflects actual laid-out geometry - not just
        //EditPanelViewportLayout.Apply's outer Width/Height clamp, which shrinks the reported control size
        //regardless of whether the content inside it actually reflowed to fit. That distinction is exactly
        //what these two ported tests need to catch: an unshrunk grid still reports a clamped outer Bounds,
        //but its own arranged Bounds run past the available space.
        private static (TestItemChooser panel, Rect gridBounds, int maxWidth, int maxHeight) MountAndMeasure(
            int viewerWidth, int viewerHeight, DrawingPoint anchor) {
            const int margin = 25; //EditPanelScreenLayout.DefaultMargin
            Fixture fx = NewFixture();
            SubgroupPrototype sg = AddSubgroup(fx, fx.Group, "sg", "a");
            ItemPrototype item = AddItem(fx, sg, "widget", "Widget");
            AddProduction(fx, sg, item, fx.OnAssembler, "recipe-widget");
            TestItemChooser panel = NewPanel(fx);

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(viewerWidth, viewerHeight);
            var window = new AvaloniaWindow { Content = control, Width = viewerWidth, Height = viewerHeight };
            window.Show();
            control.FloatingPanelHost.Show(panel, anchor);

            int maxWidth = viewerWidth - margin * 2;
            int maxHeight = viewerHeight - margin * 2;
            panel.Measure(new Size(maxWidth, maxHeight));
            panel.Arrange(new Rect(0, 0, panel.DesiredSize.Width, panel.DesiredSize.Height));

            return (panel, panel.Grid.Bounds, maxWidth, maxHeight);
        }

        [AvaloniaFact]
        public void Panel_HeightFitsShortViewer_AcrossShrinkingViewerSizes() {
            (int Width, int Height)[] viewerSizes = [(1280, 720), (1024, 600), (900, 550), (800, 500)];

            foreach ((int viewerWidth, int viewerHeight) in viewerSizes) {
                (_, Rect gridBounds, int maxWidth, int maxHeight) = MountAndMeasure(viewerWidth, viewerHeight, new DrawingPoint(20, 20));

                Assert.True(gridBounds.Bottom <= maxHeight,
                    $"At viewer {viewerWidth}x{viewerHeight}, the grid's own bottom edge ({gridBounds.Bottom}) " +
                    $"should stay within the {maxHeight}px available height - an unshrunk grid overflows past it.");
            }
        }

        [AvaloniaFact]
        public void Panel_NoOverflowPastClampedBounds_AcrossShrinkingViewerSizes() {
            (int Width, int Height)[] viewerSizes =
                [(1200, 800), (700, 700), (500, 500), (400, 400), (320, 350), (280, 300), (240, 280)];

            foreach ((int viewerWidth, int viewerHeight) in viewerSizes) {
                (TestItemChooser panel, Rect gridBounds, int maxWidth, int maxHeight) =
                    MountAndMeasure(viewerWidth, viewerHeight, new DrawingPoint(0, 0));

                Assert.True(gridBounds.Right <= maxWidth,
                    $"At viewer {viewerWidth}x{viewerHeight}, the grid's own right edge ({gridBounds.Right}) " +
                    $"should stay within the {maxWidth}px available width.");
                Assert.True(gridBounds.Bottom <= maxHeight,
                    $"At viewer {viewerWidth}x{viewerHeight}, the grid's own bottom edge ({gridBounds.Bottom}) " +
                    $"should stay within the {maxHeight}px available height.");
                //Grid stays centered under HorizontalAlignment.Center regardless of shrink (reference §2 divergence 5).
                Assert.Equal(global::Avalonia.Layout.HorizontalAlignment.Center, panel.Grid.HorizontalAlignment);
            }
        }

        //Live bug (found driving the real app): "Add Item"/"Add Recipe" turned the whole canvas gray instead
        //of opening a small floating chooser. Root cause: EditPanelViewportLayout.MeasureNaturalSize measures
        //with an unbounded (Infinity, Infinity) constraint, so GroupsPanel (a WrapPanel with no fixed size of
        //its own) never wraps there regardless of how many groups exist - it always reports one huge single-
        //row DesiredSize.Width. Apply() then clamps that oversized "natural" width down to the full available
        //viewerWidth-margin, so the panel's outer Border ends up sized to the entire canvas rather than its
        //real (wrapped) content size. FitToViewport's own internal chrome-height measurement pass already
        //bounds GroupsPanel to the real maxWidth correctly (MeasureFixedChromeHeight) - Apply's later re-
        //measure just doesn't carry that same bound over, which is the actual bug. 30 groups is enough to
        //overflow one 64px-icon unwrapped row on any realistic viewer width tested here; every other test in
        //this file uses at most a handful of groups, so this width-driven overflow never came up before -
        //exercised end to end through GraphCanvasControl.AddItemAsync, the exact call site the toolbar uses.
        [AvaloniaFact]
        public void AddItemAsync_ManyGroups_PanelDoesNotStretchToFillTheWholeCanvas() {
            const int viewerWidth = 1600, viewerHeight = 1000, margin = 25; //EditPanelScreenLayout.DefaultMargin
            var cache = new DataCache(filterRecipes: true);
            var quality = new QualityPrototype(cache, "normal", "Normal", "a");
            DataCacheStore store = Store(cache);
            store.Qualities[quality.Name] = quality;
            store.DefaultQuality = quality;

            for (int i = 0; i < 30; i++) {
                var group = new GroupPrototype(cache, $"group-{i}", $"Group {i}", i.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
                store.Groups[group.Name] = group;
                var subgroup = new SubgroupPrototype(cache, $"sg-{i}", "a");
                subgroup.MyGroupInternal = group;
                group.SubgroupsInternal.Add(subgroup);
                store.Subgroups[subgroup.Name] = subgroup;
                var item = new ItemPrototype(cache, $"item-{i}", $"Item {i}", subgroup, "a") { Available = true };
                store.Items[item.Name] = item;
            }

            var control = new GraphCanvasControl();
            control.Viewport.SetSize(viewerWidth, viewerHeight);
            control.Viewer.Context.DCache = cache;
            var window = new AvaloniaWindow { Content = control, Width = viewerWidth, Height = viewerHeight };
            window.Show();

            control.AddItemAsync(new DrawingPoint(0, 0));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            System.Drawing.Rectangle bounds = control.FloatingPanelHost.Bounds;
            int maxWidth = viewerWidth - margin * 2;
            Assert.True(bounds.Width < maxWidth,
                $"Expected the chooser to keep its own wrapped width, not stretch to fill the {maxWidth}px " +
                $"available canvas width - got {bounds.Width}px (exactly the clamp ceiling means it stretched).");
        }
    }
}
