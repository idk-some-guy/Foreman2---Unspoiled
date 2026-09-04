using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Services;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.Views {
    //Dev-only visual check window for every canvas floating panel, launched via `--gallery` (see
    //App.BootGalleryAsync/Program.Main). Loads the current preset through the same path the real app uses
    //(ShellBootstrapper.LoadPresetAsync), then hosts each panel live against a small scratch graph instead of
    //a static render - a hidden GraphCanvasControl isn't needed since EditFlowPanel/EditRecipePanel only ever
    //touch GraphViewer/Session, not the canvas itself, so a bare Viewport+GridManager+GraphViewer backs them.
    public sealed class GalleryWindow : Window {
        private const double TileColumnWidth = 1520;

        public GalleryWindow(DataCache cache, AppSettings settings) {
            Title = "Foreman Gallery";
            Width = 1600;
            Height = 1000;
            //Matches GraphViewer.Paint's canvas.Clear(SKColors.White): every floating panel here is a Border
            //that already paints its own Brushes.Black chrome (EditFlowPanel/EditRecipePanel/IRChooserPanel),
            //so the backdrop has to be the graph canvas's real white, not another black, or that chrome
            //disappears into the window instead of standing out the way it does over the actual canvas.
            Background = Brushes.White;

            IQuality quality = cache.DefaultQuality ?? cache.AvailableQualities.First();
            var viewer = new GraphViewer(new Viewport(1200, 900), new GridManager()) { Graph = { DefaultAssemblerQuality = quality } };
            viewer.Context.DCache = cache;

            RecipeShowcase? showcase = FindShowcaseRecipe(cache);
            ItemQualityPair keyItem = FindKeyItem(cache, showcase, quality);

            var tiles = new WrapPanel { Orientation = Orientation.Horizontal, Width = TileColumnWidth };
            tiles.Children.Add(Tile("ItemChooserPanel", BuildItemChooserPanel(cache, settings)));
            tiles.Children.Add(Tile("RecipeChooserPanel", BuildRecipeChooserPanel(cache, settings, keyItem)));
            tiles.Children.Add(Tile("EditFlowPanel", BuildEditFlowPanel(viewer, keyItem)));
            tiles.Children.Add(Tile("EditRecipePanel", BuildEditRecipePanel(viewer, cache, quality, showcase)));
            tiles.Children.Add(Tile("QualityPicker", ChromeWrap(BuildQualityPicker(cache))));

            //Task 6 (Plan C1 gate tool): the retokened toolbar/popover/row surfaces, live-toggleable between
            //Light and Dark so Jozef's gate pass can eyeball both variants of every one without leaving the
            //gallery. ThemeToggleButton flips the app's RequestedThemeVariant (there's only ever one window
            //open in gallery mode, so this is safe) and rebuilds the section against the freshly active theme.
            var themeShowcaseHost = new ContentControl { Name = "ThemeShowcaseHost", Content = BuildThemeShowcaseSection(cache) };
            //M-6 (final fix wave): this header used to hardcode Brushes.Black, so it stayed black through a
            //theme toggle while every retokened surface below it flipped - resolved once at construction
            //isn't enough on its own (nothing here re-renders on a resource change the way a real
            //DynamicResource binding would), so the toggle handler below re-resolves it explicitly too.
            var themeHeader = new TextBlock {
                Text = "Design language (Task 6 gate)",
                Foreground = ThemeResources.ResolveBrush("Foreman.Color.Text", Colors.Black),
                FontWeight = FontWeight.Bold,
            };
            var themeToggleButton = new Button { Name = "ThemeToggleButton", Content = "Toggle Theme (Light/Dark)" };
            themeToggleButton.Click += (_, _) => {
                Application app = Application.Current!;
                app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
                themeHeader.Foreground = ThemeResources.ResolveBrush("Foreman.Color.Text", Colors.Black);
                themeShowcaseHost.Content = BuildThemeShowcaseSection(cache);
            };
            var themeSection = new StackPanel {
                Spacing = 6,
                Margin = new Thickness(16),
                Children = { themeHeader, themeToggleButton, themeShowcaseHost },
            };

            Content = new ScrollViewer {
                Content = new StackPanel { Children = { themeSection, tiles } },
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };

            WindowScaler.Track(this);
        }

        //Rebuilt from scratch on every theme toggle (see ThemeToggleButton.Click above) rather than patched
        //in place - a dev tool, so simplicity wins over incremental-update plumbing.
        private static WrapPanel BuildThemeShowcaseSection(DataCache cache) {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Width = TileColumnWidth };
            wrap.Children.Add(Tile("Toolbar / Titlebar", BuildToolbarShowcaseTile()));
            wrap.Children.Add(Tile("View Options popover", BuildViewOptionsShowcaseTile()));
            wrap.Children.Add(Tile("Graph Summary rows", BuildSummaryRowsShowcaseTile()));
            wrap.Children.Add(Tile("Preset Comparator rows", BuildComparatorRowsShowcaseTile()));
            wrap.Children.Add(Tile("Enabled Objects rows", BuildEnabledObjectsRowsShowcaseTile(cache)));
            return wrap;
        }

        //Reuses the real foreman-toolbar-* style classes (contract #toolbar) instead of hand-picking colors -
        //everything but the bar's own background comes straight from Foreman.Styles.axaml's DynamicResource
        //setters, so this tile updates for free on every theme toggle.
        private static Border BuildToolbarShowcaseTile() {
            var newGlyph = new TextBlock { Text = "✚" };
            newGlyph.Classes.Add("foreman-toolbar-glyph");
            var newButton = new Button { Content = newGlyph };
            newButton.Classes.Add("foreman-toolbar-icon");

            var dirtyGlyph = new TextBlock { Text = "⇩" };
            dirtyGlyph.Classes.Add("foreman-toolbar-glyph");
            var dirtySaveButton = new Button { Content = dirtyGlyph };
            dirtySaveButton.Classes.Add("foreman-toolbar-icon");
            dirtySaveButton.Classes.Add("on");

            var addItemGlyph = new TextBlock { Text = "▢" };
            addItemGlyph.Classes.Add("foreman-toolbar-glyph");
            var addItemLabel = new TextBlock { Text = "Item" };
            addItemLabel.Classes.Add("foreman-toolbar-label");
            var addItemButton = new Button {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { addItemGlyph, addItemLabel } },
            };
            addItemButton.Classes.Add("foreman-toolbar-wide");

            var bar = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(8),
                Children = { newButton, dirtySaveButton, addItemButton },
            };

            return new Border {
                Background = ThemeResources.ResolveBrush("Foreman.Color.TitlebarBackground", Color.FromRgb(0xE7, 0xE7, 0xEA)),
                Child = bar,
            };
        }

        //Mirrors MainWindow.axaml's ViewOptionsCard/GridLinesGroupBox structure (contract #decisions §1) -
        //same Radius/Thickness resources this task added, same border/background tokens.
        private static Border BuildViewOptionsShowcaseTile() {
            IBrush border = ThemeResources.ResolveBrush("Foreman.Color.Border", Color.FromArgb(0x17, 0, 0, 0));

            var gridlinesGroup = new Border {
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = Resource<CornerRadius>("Foreman.Radius.Control"),
                Padding = Resource<Thickness>("Foreman.Thickness.Space2"),
                Child = new StackPanel {
                    Spacing = 4,
                    Children = {
                        new TextBlock { Text = "Gridlines (2n scaling)", FontWeight = FontWeight.Bold },
                        new CheckBox { Content = "Show Gridlines" },
                    },
                },
            };

            return new Border {
                Background = ThemeResources.ResolveBrush("Foreman.Color.ControlBackground", Colors.White),
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = Resource<CornerRadius>("Foreman.Radius.Card"),
                Padding = Resource<Thickness>("Foreman.Thickness.Space3"),
                Child = gridlinesGroup,
            };
        }

        //Two GraphSummaryWindow.BuildingRow/ItemRow instances (available + unavailable) - the same row DTOs
        //and the same static RowForegroundBrush seed GraphSummaryWindow's own constructor uses, just fed
        //synthetic display text instead of a live graph (a gate tool, not a simulation).
        private static StackPanel BuildSummaryRowsShowcaseTile() {
            GraphSummaryWindow.RowForegroundBrush = ThemeResources.ResolveBrush("Foreman.Color.StatusForeground", Colors.Black);
            IBrush available = ThemeResources.ResolveBrush("Foreman.Color.ControlBackground", Colors.White);
            IBrush unavailable = ThemeResources.ResolveBrush("Foreman.Color.StatusWarningBackground", Color.FromRgb(255, 192, 203));

            return new StackPanel {
                Spacing = 2,
                Children = {
                    BuildingRowVisual(new BuildingRow("furnace", "furnace", null, available, "3", "Electric Furnace", "-", 0, "-", 0)),
                    ItemRowVisual(new ItemRow("plate", "plate", null, available, "Iron Plate", "12/s", 12, "-", 0, "12/s", 12, "-", 0, "-", 0, "12/s", 12, "-", 0)),
                    ItemRowVisual(new ItemRow("legacy-item", "legacy-item", null, unavailable, "Legacy Item", "-", 0, "-", 0, "-", 0, "-", 0, "-", 0, "-", 0, "-", 0)),
                },
            };
        }

        private static Border BuildingRowVisual(BuildingRow row) =>
            new() {
                Background = row.Background,
                Padding = new Thickness(2),
                Child = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = {
                        new TextBlock { Text = row.CountText, Foreground = row.Foreground, Width = 60 },
                        new TextBlock { Text = row.Name, Foreground = row.Foreground, Width = 160 },
                    },
                },
            };

        private static Border ItemRowVisual(ItemRow row) =>
            new() {
                Background = row.Background,
                Padding = new Thickness(2),
                Child = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = {
                        new TextBlock { Text = row.Name, Foreground = row.Foreground, Width = 160 },
                        new TextBlock { Text = row.OutText, Foreground = row.Foreground, Width = 60 },
                    },
                },
            };

        //Three ComparatorRow instances (Equal/CloseEnough/Different) - the same status-color trio
        //PresetComparatorViewModel.RebuildTab computes for a matched Left/Right pair.
        private static StackPanel BuildComparatorRowsShowcaseTile() {
            IBrush equalBackground = ThemeResources.ResolveBrush("Foreman.Color.ControlBackground", Colors.White);
            IBrush closeEnoughBackground = ThemeResources.ResolveBrush("Foreman.Color.StatusCautionBackground", Color.FromRgb(240, 230, 140));
            IBrush differentBackground = ThemeResources.ResolveBrush("Foreman.Color.StatusWarningBackground", Color.FromRgb(255, 192, 203));
            IBrush availableForeground = ThemeResources.ResolveBrush("Foreman.Color.StatusForeground", Colors.Black);
            IBrush unavailableForeground = ThemeResources.ResolveBrush("Foreman.Color.StatusWarningForeground", Color.FromRgb(139, 0, 0));

            return new StackPanel {
                Spacing = 2,
                Children = {
                    ComparatorRowVisual(new ComparatorRow("equal", "Iron Plate", "iron-plate", null, availableForeground, FontStyle.Normal) { Background = equalBackground }),
                    ComparatorRowVisual(new ComparatorRow("close-enough", "Iron Plate (renamed)", "iron-plate", null, availableForeground, FontStyle.Normal) { Background = closeEnoughBackground }),
                    ComparatorRowVisual(new ComparatorRow("different", "Removed Recipe", "removed-recipe", null, unavailableForeground, FontStyle.Italic) { Background = differentBackground }),
                },
            };
        }

        private static Border ComparatorRowVisual(ComparatorRow row) =>
            new() {
                Background = row.Background,
                Padding = new Thickness(4, 1),
                Child = new TextBlock { Text = row.Name, Foreground = row.Foreground, FontStyle = row.FontStyle },
            };

        //Two EnabledObjectsListItem rows (available + unavailable) built off real cache items, through the
        //same RefreshTheme() seam SettingsWindow calls before building its own rows.
        private static StackPanel BuildEnabledObjectsRowsShowcaseTile(DataCache cache) {
            EnabledObjectsListItem.RefreshTheme();

            var rows = new StackPanel { Spacing = 2 };
            IItem available = cache.AvailableItems.First();
            rows.Children.Add(EnabledObjectsRowVisual(new EnabledObjectsListItem(available, null) { IsChecked = true }));

            IItem? unavailable = cache.Items.Values.FirstOrDefault(i => !i.Available);
            if (unavailable is not null)
                rows.Children.Add(EnabledObjectsRowVisual(new EnabledObjectsListItem(unavailable, null) { IsChecked = false }));

            return rows;
        }

        private static Border EnabledObjectsRowVisual(EnabledObjectsListItem item) =>
            new() {
                Background = item.RowBackground,
                Padding = new Thickness(2),
                Child = new StackPanel {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = {
                        new CheckBox { IsChecked = item.IsChecked },
                        new TextBlock { Text = item.Name, Foreground = item.Foreground },
                    },
                },
            };

        //Same TryGetResource(key, variant, fallback) shape as ThemeResources.ResolveColor, typed for the
        //Thickness/CornerRadius resources this task added - both dictionaries define every key this calls,
        //so a miss here means a real bug, not a themeable fallback.
        private static T Resource<T>(string key) {
            Application app = Application.Current!;
            return app.TryGetResource(key, app.ActualThemeVariant, out object? value) && value is T typed
                ? typed
                : throw new InvalidOperationException($"Gallery: resource '{key}' not found.");
        }

        private static StackPanel Tile(string caption, Control content) {
            var stack = new StackPanel { Spacing = 6, Margin = new Thickness(16) };
            stack.Children.Add(new TextBlock { Text = caption, Foreground = Brushes.Black, FontWeight = FontWeight.Bold });
            stack.Children.Add(content);
            return stack;
        }

        //QualityPicker never floats on its own in the real app - it's always embedded inside a panel that
        //already paints Border.Background = Brushes.Black on itself (EditFlowPanel/EditRecipePanel/
        //IRChooserPanel). Reusing that same brush here shows it the way it actually renders once hosted.
        private static Border ChromeWrap(Control content) => new() { Background = Brushes.Black, Padding = new Thickness(8), Child = content };

        private static ItemChooserPanel BuildItemChooserPanel(DataCache cache, AppSettings settings) {
            var panel = new ItemChooserPanel(cache, settings);
            panel.Initialize();
            return panel;
        }

        private static RecipeChooserPanel BuildRecipeChooserPanel(DataCache cache, AppSettings settings, ItemQualityPair keyItem) {
            var panel = new RecipeChooserPanel(cache, settings, keyItem, new FRange(0, 0, true), NewNodeType.Disconnected);
            panel.Initialize();
            return panel;
        }

        private static EditFlowPanel BuildEditFlowPanel(GraphViewer viewer, ItemQualityPair keyItem) {
            NodeId nodeId = viewer.Session.Editor.CreatePassthroughNode(keyItem, new DrawingPoint(0, 0));
            if (!viewer.Session.View.TryGetNode(nodeId, out INodeViewModel? node) || node is null)
                throw new InvalidOperationException("Gallery: failed to create the example passthrough node.");
            return new EditFlowPanel(node, viewer);
        }

        //Shows the paired composition the real app actually floats (GraphCanvasControl.EditNode, reference
        //§9): EditRecipePanel next to its own standalone RecipePanel, not the recipe card embedded inside
        //the edit panel (review finding B1).
        private static StackPanel BuildEditRecipePanel(GraphViewer viewer, DataCache cache, IQuality quality, RecipeShowcase? showcase) {
            IRecipe recipe = showcase?.Recipe ?? cache.Recipes.Values.First();
            NodeId nodeId = viewer.Session.Editor.CreateRecipeNode(new RecipeQualityPair(recipe, quality), new DrawingPoint(400, 0));
            if (!viewer.Session.View.TryGetNode(nodeId, out INodeViewModel? node) || node is not IRecipeNodeViewModel recipeNode)
                throw new InvalidOperationException("Gallery: failed to create the example recipe node.");

            if (showcase is RecipeShowcase demo && viewer.Session.Editor.RequestNodeController(nodeId) is RecipeNodeController controller) {
                controller.SetAssembler(new AssemblerQualityPair(demo.Assembler, quality));
                foreach (IModule module in demo.Modules)
                    controller.AddAssemblerModule(new ModuleQualityPair(module, quality));
                if (demo.Beacon is IBeacon beacon) {
                    controller.SetBeacon(new BeaconQualityPair(beacon, quality));
                    controller.SetBeaconCount(2);
                    controller.SetBeaconsPerAssembler(1);
                }
                if (demo.Fuel is IItem fuel)
                    controller.SetFuel(fuel);
                viewer.Graph.UpdateNodeValues();
            }

            var editPanel = new EditRecipePanel(recipeNode, viewer);
            var recipePanel = new RecipePanel([recipe], viewer.Context.AbbreviateSciPacks);
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { editPanel, recipePanel } };
        }

        private static QualityPicker BuildQualityPicker(DataCache cache) {
            var picker = new QualityPicker();
            picker.SetQualities(cache.AvailableQualities.Where(q => q.Enabled));
            return picker;
        }

        //The richest assembler this cache's data can actually back: prefers one with modules and a beacon
        //over one that merely has fuel, since no vanilla entity carries both at once (a burner assembler
        //never has module slots) - fuel only wins a tie-break when nothing offers modules or a beacon at all.
        private readonly record struct RecipeShowcase(IRecipe Recipe, IAssembler Assembler, IReadOnlyList<IModule> Modules, IBeacon? Beacon, IItem? Fuel);

        private static RecipeShowcase? FindShowcaseRecipe(DataCache cache) {
            RecipeShowcase? best = null;
            int bestScore = -1;

            foreach (IRecipe recipe in cache.Recipes.Values.Where(r => r.Available && r.Enabled)) {
                foreach (IAssembler assembler in recipe.Assemblers.Where(a => a.Enabled && a.Available)) {
                    List<IModule> modules = assembler.AllowModules && assembler.ModuleSlots > 0
                        ? [.. recipe.AssemblerModules.Intersect(assembler.Modules).Where(m => m.Enabled && m.Available).Take(2)]
                        : [];
                    IBeacon? beacon = assembler.AllowBeacons
                        ? cache.Beacons.Values.FirstOrDefault(b => b.Enabled && b.Available && recipe.BeaconModules.Any(m => b.Modules.Contains(m)))
                        : null;
                    IItem? fuel = assembler.IsBurner ? assembler.Fuels.FirstOrDefault(f => f.Available) : null;

                    int score = (modules.Count > 0 ? 2 : 0) + (beacon is not null ? 2 : 0) + (fuel is not null ? 1 : 0);
                    if (score <= bestScore)
                        continue;
                    bestScore = score;
                    best = new RecipeShowcase(recipe, assembler, modules, beacon, fuel);
                }
            }
            return best;
        }

        private static ItemQualityPair FindKeyItem(DataCache cache, RecipeShowcase? showcase, IQuality quality) {
            IItem item = FirstOrNull(showcase?.Recipe.IngredientList)
                ?? FirstOrNull(showcase?.Recipe.ProductList)
                ?? cache.AvailableItems.First();
            return new ItemQualityPair(item, quality);
        }

        private static IItem? FirstOrNull(IReadOnlyList<IItem>? items) => items is { Count: > 0 } ? items[0] : null;
    }
}
