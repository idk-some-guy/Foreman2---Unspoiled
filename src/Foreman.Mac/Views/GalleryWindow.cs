using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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
using Foreman.Mac.Kit.Controls;
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

            //K2's dev-only kit primitives section: type ramp, glyph sheet, a 150% row, focus ring.
            var kitTiles = new WrapPanel { Orientation = Orientation.Horizontal, Width = TileColumnWidth };
            foreach (GalleryTile tile in registry.Tiles.Where(t => t.Family == "Kit"))
                kitTiles.Children.Add(Tile(tile.Name, tile.Build()));

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
                Content = new StackPanel { Children = { themeSection, tiles, kitTiles } },
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
            registry.Register(new GalleryTile("Kit", "Type ramp", "Default", BuildTypeRampTile));
            registry.Register(new GalleryTile("Kit", "Glyphs", "Default", BuildGlyphSheetTile));
            registry.Register(new GalleryTile("Kit", "Type ramp & Glyphs (150%)", "150%", BuildKitScaledTile));
            registry.Register(new GalleryTile("Kit", "Focus ring", "Default", BuildFocusRingTile));
            registry.Register(new GalleryTile("Kit", "Focus ring (150%)", "150%", BuildFocusRingScaledTile));
            registry.Register(new GalleryTile("Kit", "Buttons", "Default", BuildButtonsTile));
            registry.Register(new GalleryTile("Kit", "Toolbar items", "Default", BuildToolbarItemsTile));
            registry.Register(new GalleryTile("Kit", "Buttons & Toolbar items (150%)", "150%", BuildButtonsAndToolbarItemsScaledTile));
            registry.Register(new GalleryTile("Kit", "Checkbox", "Default", BuildCheckboxTile));
            registry.Register(new GalleryTile("Kit", "Radio group", "Default", BuildRadioGroupTile));
            registry.Register(new GalleryTile("Kit", "Switch — Settings only (A16)", "Default", BuildSwitchTile));
            registry.Register(new GalleryTile("Kit", "Checkbox, Radio group & Switch (150%)", "150%", BuildSelectionControlsScaledTile));
            registry.Register(new GalleryTile("Kit", "Segmented", "Default", BuildSegmentedTile));
            registry.Register(new GalleryTile("Kit", "Pop-up buttons", "Default", BuildPopUpButtonsTile));
            registry.Register(new GalleryTile("Kit", "Menu button", "Default", BuildMenuButtonTile));
            registry.Register(new GalleryTile("Kit", "Segmented, Pop-up buttons & Menu button (150%)", "150%", BuildMenuPrimitivesScaledTile));
            return registry;
        }

        //Sample text lifted verbatim from the kit mock's type-row examples (contract #type).
        private static readonly (string Label, string Class, string Sample)[] TypeRampSteps = [
            ("Title — 20/700", "title", "Graph Summary"),
            ("Headline — 13/600", "headline", "Iron Plate Line"),
            ("Body — 13/400", "body", "Electric Furnace · ×4"),
            ("Callout — 12/400", "callout", "Foreman 2 · v 1.1.0u based on 2.4.0"),
            ("Caption — 11/400", "caption", "AppSettings.DefaultNodeDirection"),
        ];

        private static StackPanel BuildTypeRampTile() {
            var stack = new StackPanel { Spacing = 4 };
            foreach ((string label, string cls, string sample) in TypeRampSteps) {
                var labelText = KitText(label, "caption", "tertiary");
                labelText.Width = 190;
                var sampleText = KitText(sample, cls);
                stack.Children.Add(new StackPanel {
                    Orientation = Orientation.Horizontal, Spacing = 16,
                    Children = { labelText, sampleText },
                });
            }
            return stack;
        }

        //All 30 shared masters (Themes/Glyphs.axaml) at the 20 pt drawn size, named.
        private static readonly string[] GlyphSheetNames = [
            "New", "Open", "Save", "Item", "Recipe", "Connect", "Align", "Export", "Summary",
            "View", "View.Fill", "Passthrough", "Beacon", "Beacon.Fill",
            "Chevron", "Search",
            "RadialAdd", "RadialSource", "RadialOutput", "RadialSpoil", "RadialUnspoil",
            "RadialPlant", "RadialUnplant", "RadialCancel",
            "Settings.Presets", "Settings.EnabledObjects", "Settings.General",
            "Settings.Advanced", "Settings.Annotations", "Settings.Appearance",
        ];

        private static WrapPanel BuildGlyphSheetTile() {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, Width = 900 };
            foreach (string name in GlyphSheetNames) {
                var cell = new StackPanel {
                    Spacing = 2, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { KitGlyph(name, 20, "size20"), KitText(name, "caption", "secondary") },
                };
                wrap.Children.Add(cell);
            }
            return wrap;
        }

        //Multiplied values live in this container's own Resources so the Default tiles beside it don't also rescale.
        private static readonly string[] KitScaledSizeKeys = [
            "Foreman.Type.LargeTitle.FontSize", "Foreman.Type.EmphasizedBody.FontSize", "Foreman.Type.Body.FontSize",
            "Foreman.Type.Callout.FontSize", "Foreman.Type.Caption.FontSize",
            "Foreman.Size.Glyph16", "Foreman.Size.Glyph17", "Foreman.Size.Glyph18", "Foreman.Size.Glyph20",
        ];

        private static StackPanel BuildKitScaledTile() {
            var container = new StackPanel { Spacing = 8 };
            foreach (string key in KitScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildTypeRampTile());
            container.Children.Add(BuildGlyphSheetTile());
            return container;
        }

        private static StackPanel BuildFocusRingTile() {
            var button = new Button { Content = "Save" };
            FocusRing.SetIsEnabled(button, true);
            FocusRing.SetShowAlways(button, true);

            var field = new TextBox { PlaceholderText = "Node name", Width = 160 };
            FocusRing.SetIsEnabled(field, true);
            FocusRing.SetShowAlways(field, true);

            var checkBox = new CheckBox { Content = "Show Gridlines" };
            FocusRing.SetIsEnabled(checkBox, true);
            FocusRing.SetShowAlways(checkBox, true);

            return new StackPanel { Spacing = 12, Children = { button, field, checkBox } };
        }

        private static StackPanel BuildFocusRingScaledTile() {
            var container = new StackPanel { Spacing = 8 };
            foreach (string key in KitScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildFocusRingTile());
            return container;
        }

        private enum GalleryButtonState { Normal, Hover, Pressed, Focused, Disabled }

        private static readonly (string Sample, string[] Classes)[] ButtonVariants = [
            ("Cancel", []),
            ("Export…", ["primary"]),
            ("Discard", ["destructive"]),
        ];

        private static readonly (string Label, GalleryButtonState State)[] ButtonStates = [
            ("Normal", GalleryButtonState.Normal),
            ("Hover", GalleryButtonState.Hover),
            ("Pressed", GalleryButtonState.Pressed),
            ("Focused", GalleryButtonState.Focused),
            ("Disabled", GalleryButtonState.Disabled),
        ];

        private static Button KitButton(string text, params string[] classes) {
            var button = new Button { Theme = KitTheme("Kit.Button"), Content = text };
            foreach (string cls in classes)
                button.Classes.Add(cls);
            return button;
        }

        private static void ApplyGalleryState(Control control, GalleryButtonState state) {
            switch (state) {
                case GalleryButtonState.Hover:
                    control.Classes.Add("gallery-hover");
                    break;
                case GalleryButtonState.Pressed:
                    control.Classes.Add("gallery-pressed");
                    break;
                case GalleryButtonState.Focused:
                    FocusRing.SetShowAlways(control, true);
                    break;
                case GalleryButtonState.Disabled:
                    control.IsEnabled = false;
                    break;
            }
        }

        private static Grid BuildButtonsTile() {
            var grid = new Grid { RowSpacing = 8, ColumnSpacing = 16 };
            for (int c = 0; c < ButtonStates.Length; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            for (int r = 0; r <= ButtonVariants.Length; r++)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int c = 0; c < ButtonStates.Length; c++) {
                TextBlock header = KitText(ButtonStates[c].Label, "caption", "tertiary");
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, c);
                grid.Children.Add(header);
            }

            for (int r = 0; r < ButtonVariants.Length; r++) {
                (string sample, string[] classes) = ButtonVariants[r];
                for (int c = 0; c < ButtonStates.Length; c++) {
                    Button button = KitButton(sample, classes);
                    ApplyGalleryState(button, ButtonStates[c].State);
                    Grid.SetRow(button, r + 1);
                    Grid.SetColumn(button, c);
                    grid.Children.Add(button);
                }
            }
            return grid;
        }

        private static readonly (string Label, GalleryButtonState State, bool On)[] ToolbarItemStates = [
            ("Normal", GalleryButtonState.Normal, false),
            ("Hover", GalleryButtonState.Hover, false),
            ("Pressed", GalleryButtonState.Pressed, false),
            ("Focused", GalleryButtonState.Focused, false),
            ("Disabled", GalleryButtonState.Disabled, false),
            ("On", GalleryButtonState.Normal, true),
        ];

        private static ToolbarItem KitToolbarItem(string glyphName, string label) {
            Application.Current!.TryGetResource($"Kit.Glyph.{glyphName}.17", null, out object? geometry);
            return new ToolbarItem { Theme = KitTheme("Kit.ToolbarItem"), Glyph = geometry as Geometry, Label = label };
        }

        private static Grid BuildToolbarItemsTile() {
            var grid = new Grid { ColumnSpacing = 16 };
            for (int c = 0; c < ToolbarItemStates.Length; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int c = 0; c < ToolbarItemStates.Length; c++) {
                TextBlock header = KitText(ToolbarItemStates[c].Label, "caption", "tertiary");
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, c);
                grid.Children.Add(header);

                (string _, GalleryButtonState state, bool on) = ToolbarItemStates[c];
                ToolbarItem item = KitToolbarItem("Recipe", "Recipe");
                ApplyGalleryState(item, state);
                item.IsOn = on;
                Grid.SetRow(item, 1);
                Grid.SetColumn(item, c);
                grid.Children.Add(item);
            }
            return grid;
        }

        private static readonly string[] ButtonAndToolbarItemScaledSizeKeys = [
            "Foreman.Size.ControlHeightRegular", "Foreman.Type.EmphasizedBody.FontSize",
            "Foreman.Size.ToolbarItemWidth", "Foreman.Size.ToolbarItemHeight",
            "Foreman.Size.Glyph17", "Foreman.Type.Legacy.ToolbarItem.Label.FontSize",
        ];

        private static StackPanel BuildButtonsAndToolbarItemsScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in ButtonAndToolbarItemScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildButtonsTile());
            container.Children.Add(BuildToolbarItemsTile());
            return container;
        }

        //Shared by Checkbox/Radio/Switch: the five state-grid combinations the kit mock shows for each.
        private static readonly (string Label, GalleryButtonState State, bool On)[] SelectionStates = [
            ("Normal", GalleryButtonState.Normal, false),
            ("Hover", GalleryButtonState.Hover, false),
            ("Pressed", GalleryButtonState.Pressed, true),
            ("Focused", GalleryButtonState.Focused, true),
            ("Disabled", GalleryButtonState.Disabled, true),
        ];

        private static Grid BuildCheckboxTile() {
            var grid = new Grid { ColumnSpacing = 16 };
            for (int c = 0; c < SelectionStates.Length; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int c = 0; c < SelectionStates.Length; c++) {
                TextBlock header = KitText(SelectionStates[c].Label, "caption", "tertiary");
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, c);
                grid.Children.Add(header);

                (string _, GalleryButtonState state, bool on) = SelectionStates[c];
                var box = new CheckBox { Theme = KitTheme("Kit.CheckBox"), IsChecked = on };
                ApplyGalleryState(box, state);
                Grid.SetRow(box, 1);
                Grid.SetColumn(box, c);
                grid.Children.Add(box);
            }
            return grid;
        }

        private static RadioButton KitRadioButton(string? content = null) =>
            new() { Theme = KitTheme("Kit.RadioButton"), Content = content };

        private static Grid BuildRadioGroupTile() {
            var grid = new Grid { ColumnSpacing = 16 };
            for (int c = 0; c < SelectionStates.Length; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int c = 0; c < SelectionStates.Length; c++) {
                TextBlock header = KitText(SelectionStates[c].Label, "caption", "tertiary");
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, c);
                grid.Children.Add(header);

                (string _, GalleryButtonState state, bool on) = SelectionStates[c];
                RadioButton radio = KitRadioButton();
                radio.IsChecked = on;
                ApplyGalleryState(radio, state);
                Grid.SetRow(radio, 1);
                Grid.SetColumn(radio, c);
                grid.Children.Add(radio);
            }

            var group = new RadioGroup { Theme = KitTheme("Kit.RadioGroup") };
            RadioButton down = KitRadioButton("Down");
            down.IsChecked = true;
            group.Items.Add(KitRadioButton("Up"));
            group.Items.Add(down);
            group.Items.Add(KitRadioButton("Auto"));
            Grid.SetRow(group, 2);
            Grid.SetColumn(group, 0);
            Grid.SetColumnSpan(group, SelectionStates.Length);
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Children.Add(group);

            return grid;
        }

        private static Grid BuildSwitchTile() {
            var grid = new Grid { ColumnSpacing = 16 };
            for (int c = 0; c < SelectionStates.Length; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int c = 0; c < SelectionStates.Length; c++) {
                TextBlock header = KitText(SelectionStates[c].Label, "caption", "tertiary");
                Grid.SetRow(header, 0);
                Grid.SetColumn(header, c);
                grid.Children.Add(header);

                (string _, GalleryButtonState state, bool on) = SelectionStates[c];
                var toggle = new Switch { Theme = KitTheme("Kit.Switch"), IsChecked = on };
                ApplyGalleryState(toggle, state);
                Grid.SetRow(toggle, 1);
                Grid.SetColumn(toggle, c);
                grid.Children.Add(toggle);
            }
            return grid;
        }

        private static readonly string[] SelectionControlsScaledSizeKeys = [
            "Foreman.Size.CheckBox", "Foreman.Size.RadioDot", "Foreman.Size.CheckGlyph",
            "Foreman.Size.CheckDashWidth", "Foreman.Size.CheckDashHeight",
            "Foreman.Size.SelectionLabelGap", "Foreman.Size.RadioGroupRowGap",
            "Foreman.Size.SwitchWidth", "Foreman.Size.SwitchHeight", "Foreman.Size.SwitchKnob",
        ];

        private static StackPanel BuildSelectionControlsScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in SelectionControlsScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildCheckboxTile());
            container.Children.Add(BuildRadioGroupTile());
            container.Children.Add(BuildSwitchTile());
            return container;
        }

        private static Segmented KitSegmented(params string[] items) {
            var segmented = new Segmented { Theme = KitTheme("Kit.Segmented") };
            foreach (string item in items)
                segmented.Items.Add(item);
            return segmented;
        }

        private static StackPanel BuildSegmentedTile() {
            Segmented regular = KitSegmented("Up", "Down");
            FocusRing.SetShowAlways(regular, true);

            Segmented mini = KitSegmented("Up", "Down", "Auto");
            mini.Classes.Add("mini");
            mini.SelectedIndex = 1;

            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Regular", "caption", "tertiary"), regular, KitText("Mini", "caption", "tertiary"), mini },
            };
        }

        private static PopUp KitPopUp(bool pullDown, params string[] items) {
            var popup = new PopUp { Theme = KitTheme("Kit.PopUp"), IsPullDown = pullDown };
            if (pullDown)
                popup.Label = "Add as";
            foreach (string item in items)
                popup.Items.Add(item);
            if (!pullDown && items.Length > 0)
                popup.SelectedItem = items[0];
            return popup;
        }

        private static StackPanel BuildPopUpButtonsTile() {
            PopUp popup = KitPopUp(false, "Items / s", "Belts / s", "Power / s");
            FocusRing.SetShowAlways(popup, true);
            PopUp pulldown = KitPopUp(true, "Add as blueprint", "Add as ghost");
            PopUp openExample = KitPopUp(false, "Items / s", "Belts / s", "Power / s");
            var openRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { openExample, openExample.Menu } };

            return new StackPanel {
                Spacing = 8,
                Children = {
                    KitText("Pop-up", "caption", "tertiary"), popup,
                    KitText("Pull-down", "caption", "tertiary"), pulldown,
                    KitText("Open (menu panel)", "caption", "tertiary"), openRow,
                },
            };
        }

        private static MenuButton KitMenuButton(string label, Geometry glyph, params (string Text, bool IsDefault, bool IsChecked)[] items) {
            var button = new MenuButton { Theme = KitTheme("Kit.MenuButton"), Label = label, Glyph = glyph };
            foreach ((string text, bool isDefault, bool isChecked) in items)
                button.Items.Add(new MenuButtonItem { Text = text, IsDefault = isDefault, IsChecked = isChecked });
            return button;
        }

        private static StackPanel BuildMenuButtonTile() {
            var filterGlyph = (Geometry)Application.Current!.FindResource("Kit.Glyph.Filter.14")!;

            MenuButton normal = KitMenuButton("Filter", filterGlyph,
                ("Show unavailable objects", false, false),
                ("Show hidden objects", false, false),
                ("Show only compatible recipes", true, true));
            FocusRing.SetShowAlways(normal, true);

            MenuButton filled = KitMenuButton("Filter", filterGlyph,
                ("Show unavailable objects", false, true),
                ("Show hidden objects", false, true),
                ("Show only compatible recipes", true, true));

            MenuButton openExample = KitMenuButton("Filter", filterGlyph,
                ("Show unavailable objects", false, true),
                ("Show hidden objects", false, false),
                ("Show only compatible recipes", true, true));
            var openRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { openExample, openExample.Menu } };

            var overflow = new MenuButton { Theme = KitTheme("Kit.MenuButton") };
            overflow.Classes.Add("overflow");

            return new StackPanel {
                Spacing = 8,
                Children = {
                    KitText("Default", "caption", "tertiary"), normal,
                    KitText("Filled", "caption", "tertiary"), filled,
                    KitText("Open (menu panel)", "caption", "tertiary"), openRow,
                    KitText("Overflow", "caption", "tertiary"), overflow,
                },
            };
        }

        private static readonly string[] MenuPrimitivesScaledSizeKeys = [
            "Foreman.Size.ControlHeightRegular", "Foreman.Size.ControlGlyphGap", "Foreman.Size.PopUpIndicator",
            "Foreman.Size.GlyphMicro", "Foreman.Size.GlyphSmall", "Foreman.Size.MenuItemHeight",
            "Foreman.Size.MenuPanelMinWidth", "Foreman.Size.MenuCheckColumn",
        ];

        private static StackPanel BuildMenuPrimitivesScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in MenuPrimitivesScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildSegmentedTile());
            container.Children.Add(BuildPopUpButtonsTile());
            container.Children.Add(BuildMenuButtonTile());
            return container;
        }

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"Gallery: kit theme '{key}' not found.");
        }

        private static TextBlock KitText(string text, params string[] classes) {
            var block = new TextBlock { Theme = KitTheme("Kit.Text"), Text = text };
            foreach (string cls in classes)
                block.Classes.Add(cls);
            return block;
        }

        private static Path KitGlyph(string name, int size, params string[] classes) {
            Application.Current!.TryGetResource($"Kit.Glyph.{name}.{size}", null, out object? geometry);
            var path = new Path { Theme = KitTheme("Kit.Glyph"), Data = geometry as Geometry };
            foreach (string cls in classes)
                path.Classes.Add(cls);
            return path;
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
            var stack = new StackPanel { Name = caption, Spacing = 6, Margin = new Thickness(16) };
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
