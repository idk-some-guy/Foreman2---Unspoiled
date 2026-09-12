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
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Gallery;
using Foreman.Mac.Services;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Views {
    //Dev-only visual check window for every canvas floating panel, launched via `--gallery` (see
    //App.BootGalleryAsync/Program.Main). Loads the current preset through the same path the real app uses
    //(ShellBootstrapper.LoadPresetAsync), then hosts each panel live instead of a static render.
    public sealed class GalleryWindow : Window, IThemeAware {
        private const double TileColumnWidth = 1520;

        private readonly DataCache cache;
        private readonly GalleryRegistry registry;
        private readonly ContentControl themeShowcaseHost;
        private readonly TextBlock themeHeader;

        public GalleryRegistry Registry => registry;

        public GalleryWindow(DataCache cache, AppSettings settings) {
            this.cache = cache;
            registry = BuildRegistry(cache);
            Title = "Foreman Gallery";
            Width = 1600;
            Height = 1000;
            //Matches GraphViewer.Paint's canvas.Clear(SKColors.White), so ChromeWrap's black backdrop below
            //stands out the way it does over the actual canvas instead of disappearing into the window.
            Background = ThemeResources.ResolveBrush("Foreman.Color.GalleryCanvasBackdrop", Colors.White);

            var tiles = new WrapPanel { Orientation = Orientation.Horizontal, Width = TileColumnWidth };
            foreach (GalleryTile tile in registry.Tiles.Where(t => t.Family == "Canvas"))
                tiles.Children.Add(Tile(tile.Name, tile.Build()));

            //Task 6 (Plan C1 gate tool): the retokened toolbar/popover/row surfaces, live-toggleable between
            //Light and Dark so Jozef's gate pass can eyeball both variants of every one without leaving the
            //gallery. ThemeToggleButton flips the app's RequestedThemeVariant (there's only ever one window
            //open in gallery mode, so this is safe) and rebuilds the section against the freshly active theme.
            themeShowcaseHost = new ContentControl { Name = "ThemeShowcaseHost", Content = BuildThemeShowcaseSection() };
            //M-6 (final fix wave): this header used to hardcode Brushes.Black, so it stayed black through a
            //theme toggle while every retokened surface below it flipped - resolved once at construction
            //isn't enough on its own (nothing here re-renders on a resource change the way a real
            //DynamicResource binding would), so RefreshTheme below re-resolves it explicitly too.
            themeHeader = new TextBlock {
                Text = "Design language (Task 6 gate)",
                Foreground = ThemeResources.ResolveBrush("Foreman.Color.Text", Colors.Black),
                FontWeight = FontWeight.Bold,
            };
            var themeToggleButton = new Button { Name = "ThemeToggleButton", Content = "Toggle Theme (Light/Dark)" };
            //Just flips the variant now - the AppearanceHub subscription below (ActualThemeVariantChanged
            //reaches the hub already) rebuilds this section through the same RefreshTheme every other
            //appearance/accent/contrast/scale change already drives.
            themeToggleButton.Click += (_, _) => {
                Application app = Application.Current!;
                app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
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

            AppearanceHub.Current?.Subscribe(this);
            Closed += (_, _) => AppearanceHub.Current?.Unsubscribe(this);
        }

        public void RefreshTheme() {
            themeHeader.Foreground = ThemeResources.ResolveBrush("Foreman.Color.Text", Colors.Black);
            themeShowcaseHost.Content = BuildThemeShowcaseSection();
        }

        //Rebuilt from scratch on every theme toggle (see ThemeToggleButton.Click above) rather than patched
        //in place - a dev tool, so simplicity wins over incremental-update plumbing.
        private WrapPanel BuildThemeShowcaseSection() {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Width = TileColumnWidth };
            foreach (GalleryTile tile in registry.Tiles.Where(t => t.Family == "Theme"))
                wrap.Children.Add(Tile(tile.Name, tile.Build()));
            return wrap;
        }

        //Today's dev-only tile set, one registration per panel the window shows - the declarative seam K6's
        //future showcase additions register into instead of GalleryWindow growing more inline Build* calls.
        private static GalleryRegistry BuildRegistry(DataCache cache) {
            var registry = new GalleryRegistry();
            registry.Register(new GalleryTile("Canvas", "QualityPicker", "Default", () => ChromeWrap(BuildQualityPicker(cache))));
            registry.Register(new GalleryTile("Theme", "Toolbar / Titlebar", "Default", BuildToolbarShowcaseTile));
            registry.Register(new GalleryTile("Theme", "View Options popover", "Default", BuildViewOptionsShowcaseTile));
            registry.Register(new GalleryTile("Theme", "Graph Summary rows", "Default", BuildSummaryRowsShowcaseTile));
            registry.Register(new GalleryTile("Theme", "Preset Comparator rows", "Default", BuildComparatorRowsShowcaseTile));
            registry.Register(new GalleryTile("Theme", "Enabled Objects rows", "Default", () => BuildEnabledObjectsRowsShowcaseTile(cache)));
            return registry;
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

        //Two GraphSummarySheet.BuildingRow/ItemRow instances (available + unavailable) - the same row DTOs
        //and the same static RowForegroundBrush seed GraphSummarySheet's own constructor uses, just fed
        //synthetic display text instead of a live graph (a gate tool, not a simulation).
        private static StackPanel BuildSummaryRowsShowcaseTile() {
            GraphSummarySheet.RowForegroundBrush = ThemeResources.ResolveBrush("Foreman.Color.StatusForeground", Colors.Black);
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
            stack.Children.Add(new TextBlock { Text = caption, Foreground = ThemeResources.ResolveBrush("Foreman.Color.GalleryChrome", Colors.Black), FontWeight = FontWeight.Bold });
            stack.Children.Add(content);
            return stack;
        }

        //QualityPicker never floats on its own in the real app - approximates its former black-chrome host.
        private static Border ChromeWrap(Control content) =>
            new() { Background = ThemeResources.ResolveBrush("Foreman.Color.GalleryChrome", Colors.Black), Padding = new Thickness(8), Child = content };

        private static QualityPicker BuildQualityPicker(DataCache cache) {
            var picker = new QualityPicker();
            picker.SetQualities(cache.AvailableQualities.Where(q => q.Enabled));
            return picker;
        }
    }
}
