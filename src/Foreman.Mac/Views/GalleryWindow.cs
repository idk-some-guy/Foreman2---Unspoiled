using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Gallery;
using Foreman.Mac.Services;
using Foreman.Mac.Views.Sheets;
using Foreman.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using GroupBox = Foreman.Mac.Kit.Controls.GroupBox;
using Slider = Foreman.Mac.Kit.Controls.Slider;
using TabStrip = Foreman.Mac.Kit.Controls.TabStrip;
using TabStripItem = Foreman.Mac.Kit.Controls.TabStripItem;
using ProgressBar = Foreman.Mac.Kit.Controls.ProgressBar;
using Spinner = Foreman.Mac.Kit.Controls.Spinner;

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
            //Same live WindowBackground every other window binds (SettingsWindow.axaml) - RefreshTheme
            //below re-resolves it on every theme flip, same as themeHeader.Foreground.
            Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White);

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
                //A tile can legitimately focus a control on construction (Kit.SheetFrame opens with focus on its first field); without this the page jumps to whichever tile last stole focus.
                BringIntoViewOnFocusChange = false,
            };

            WindowScaler.Track(this);

            AppearanceHub.Current?.Subscribe(this);
            Closed += (_, _) => AppearanceHub.Current?.Unsubscribe(this);
        }

        public void RefreshTheme() {
            Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White);
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
            registry.Register(new GalleryTile("Kit", "Text field", "Default", BuildTextFieldTile));
            registry.Register(new GalleryTile("Kit", "Search field", "Default", BuildSearchFieldTile));
            registry.Register(new GalleryTile("Kit", "Stepper", "Default", BuildStepperTile));
            registry.Register(new GalleryTile("Kit", "Slider", "Default", BuildSliderTile));
            registry.Register(new GalleryTile("Kit", "Text field, Search field, Stepper & Slider (150%)", "150%", BuildFieldControlsScaledTile));
            registry.Register(new GalleryTile("Kit", "List rows", "Default", BuildListRowsTile));
            registry.Register(new GalleryTile("Kit", "Sidebar rows", "Default", BuildSidebarRowsTile));
            registry.Register(new GalleryTile("Kit", "Group box", "Default", BuildGroupBoxTile));
            registry.Register(new GalleryTile("Kit", "Tab strips", "Default", BuildTabStripsTile));
            registry.Register(new GalleryTile("Kit", "Disclosure", "Default", BuildDisclosureTile));
            registry.Register(new GalleryTile("Kit", "List rows, Sidebar rows, Group box, Tab strips & Disclosure (150%)", "150%", BuildRowsAndChromeScaledTile));
            registry.Register(new GalleryTile("Kit", "Tooltip", "Default", BuildTooltipTile));
            registry.Register(new GalleryTile("Kit", "Spinner & progress", "Default", BuildSpinnerAndProgressTile));
            registry.Register(new GalleryTile("Kit", "Chips", "Default", BuildChipsTile));
            registry.Register(new GalleryTile("Kit", "Key caps", "Default", BuildKeyCapsTile));
            registry.Register(new GalleryTile("Kit", "Tooltip, Spinner & progress, Chips & Key caps (150%)", "150%", BuildFeedbackScaledTile));
            registry.Register(new GalleryTile("Kit", "Form group (boxed, 132)", "Default", BuildFormGroupBoxedTile));
            registry.Register(new GalleryTile("Kit", "Form rows (inspector, 108)", "Default", BuildFormRowsInspectorTile));
            registry.Register(new GalleryTile("Kit", "Form row notes & errors", "Default", BuildFormRowNotesAndErrorsTile));
            registry.Register(new GalleryTile("Kit", "Form row — search field & chip row", "Default", BuildFormRowSearchAndChipTile));
            registry.Register(new GalleryTile("Kit", "Form group & Form rows (150%)", "150%", BuildFormCompositesScaledTile));
            registry.Register(new GalleryTile("Kit", "Inspector group — expanded, collapsed, hovered, focused", "Default", BuildInspectorGroupTile));
            registry.Register(new GalleryTile("Kit", "Split divider — rest, hovered, dragging, focused", "Default", BuildSplitDividerTile));
            registry.Register(new GalleryTile("Kit", "Inspector group & Split divider (150%)", "150%", BuildInspectorAndDividerScaledTile));
            registry.Register(new GalleryTile("Kit", "Pick cell — filled and empty, five states each", "Default", BuildPickCellStatesTile));
            registry.Register(new GalleryTile("Kit", "Pick grid — module slots & beacon row", "Default", BuildPickGridTile));
            registry.Register(new GalleryTile("Kit", "Pick cell & Pick grid (150%)", "150%", BuildPickCompositesScaledTile));
            registry.Register(new GalleryTile("Kit", "Chooser cell — five states", "Default", BuildChooserCellStatesTile));
            registry.Register(new GalleryTile("Kit", "Chooser grid — search results, two headings", "Default", BuildChooserGridTile));
            registry.Register(new GalleryTile("Kit", "Chooser cell & Chooser grid (150%)", "150%", BuildChooserCompositesScaledTile));
            registry.Register(new GalleryTile("Kit", "Scrim & Sheet frame", "Default", BuildSheetFrameTile));
            registry.Register(new GalleryTile("Kit", "Button row — five shapes", "Default", BuildButtonRowShapesTile));
            registry.Register(new GalleryTile("Kit", "Alert frame — two-button & three-button", "Default", BuildAlertFramesTile));
            registry.Register(new GalleryTile("Kit", "Scrim, Sheet frame, Button row & Alert frame (150%)", "150%", BuildSheetCompositesScaledTile));
            registry.Register(new GalleryTile("Kit", "Popover frame — anchored below, with a form group inside", "Default", BuildPopoverFrameTile));
            registry.Register(new GalleryTile("Kit", "Popover frame — tail clamped near a corner", "Default", BuildPopoverFrameCornerTile));
            registry.Register(new GalleryTile("Kit", "Radial ring — six slices, one armed", "Default", BuildRadialRingTile));
            registry.Register(new GalleryTile("Kit", "Popover frame & Radial ring (150%)", "150%", BuildPopoverAndRadialScaledTile));
            registry.Register(new GalleryTile("Kit", "State panel — empty (inspector & new graph)", "Default", BuildStatePanelEmptyTile));
            registry.Register(new GalleryTile("Kit", "State panel — no results", "Default", BuildStatePanelNoResultsTile));
            registry.Register(new GalleryTile("Kit", "State panel — loading (determinate & compact)", "Default", BuildStatePanelLoadingTile));
            registry.Register(new GalleryTile("Kit", "State panel — error", "Default", BuildStatePanelErrorTile));
            registry.Register(new GalleryTile("Kit", "State panel (150%)", "150%", BuildStatePanelScaledTile));
            registry.Register(new GalleryTile("Kit", "Chooser grid — no results (State panel)", "Default", BuildChooserGridNoResultsTile));
            registry.Register(new GalleryTile("Kit", "Sheet frame — error (State panel)", "Default", BuildSheetFrameErrorTile));
            registry.Register(new GalleryTile("Kit", "Form group — load failure (State panel)", "Default", BuildFormGroupLoadFailureTile));
            registry.Register(new GalleryTile("Kit", "Inspector group — body delegation (State panel)", "Default", BuildInspectorGroupBodyStateTile));
            registry.Register(new GalleryTile("Kit", "Zoom stepper — at rest, hovered, solving, solve failed", "Default", BuildZoomStepperTile));
            registry.Register(new GalleryTile("Kit", "Zoom stepper — solve-error popover", "Default", BuildZoomStepperPopoverTile));
            registry.Register(new GalleryTile("Kit", "Zoom stepper (150%)", "150%", BuildZoomStepperScaledTile));
            registry.Register(new GalleryTile("Kit", "Toolbar row — full, labels dropped, overflowed", "Default", BuildToolbarRowTile));
            registry.Register(new GalleryTile("Kit", "Toolbar row (150%)", "150%", BuildToolbarRowScaledTile));
            registry.Register(new GalleryTile("Kit", "Palette panel — quick-add results", "Default", BuildPalettePanelTile));
            registry.Register(new GalleryTile("Kit", "Palette panel — no results", "Default", BuildPalettePanelNoResultsTile));
            registry.Register(new GalleryTile("Kit", "Palette panel (150%)", "150%", BuildPalettePanelScaledTile));
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
            "Foreman.Size.SwitchKnobInset", "Foreman.Type.Body.FontSize",
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
            //A MenuPanel can only have one visual parent, so the standalone panel comes from a second instance that is never opened.
            PopUp menuSource = KitPopUp(false, "Items / s", "Belts / s", "Power / s");
            var openRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { openExample, menuSource.Menu } };

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
            //A MenuPanel can only have one visual parent, so the standalone panel comes from a second instance that is never opened.
            MenuButton menuSource = KitMenuButton("Filter", filterGlyph,
                ("Show unavailable objects", false, true),
                ("Show hidden objects", false, false),
                ("Show only compatible recipes", true, true));
            var openRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { openExample, menuSource.Menu } };

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
            "Foreman.Type.Callout.FontSize", "Foreman.Size.Glyph16", "Foreman.Size.Glyph10",
            "Foreman.Type.SegmentMini.FontSize",
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

        private static TextBox KitTextField(string placeholder) => new() { Theme = KitTheme("Kit.TextField"), PlaceholderText = placeholder };

        private static StackPanel BuildTextFieldTile() {
            TextBox empty = KitTextField("Item name");
            FocusRing.SetShowAlways(empty, true);

            TextBox filled = KitTextField("Item name");
            filled.Text = "Iron Plate";

            TextBox disabled = KitTextField("Item name");
            disabled.IsEnabled = false;

            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Empty", "caption", "tertiary"), empty, KitText("Filled", "caption", "tertiary"), filled, KitText("Disabled", "caption", "tertiary"), disabled },
            };
        }

        private static SearchField KitSearchField() => new() { Theme = KitTheme("Kit.SearchField") };

        private static StackPanel BuildSearchFieldTile() {
            SearchField natural = KitSearchField();
            natural.Text = "electric furnace";
            FocusRing.SetShowAlways(natural, true);

            SearchField min = KitSearchField();
            min.MinWidth = 0;
            min.Width = 120;
            min.Text = "iron";

            return new StackPanel {
                Spacing = 8,
                Children = { KitText("220 natural, filled", "caption", "tertiary"), natural, KitText("120 minimum, filled", "caption", "tertiary"), min },
            };
        }

        private static Stepper KitStepper(double value, string? unit = null) => new() {
            Theme = KitTheme("Kit.Stepper"), Value = value, Minimum = 0, Maximum = 100, Unit = unit,
        };

        private static StackPanel BuildStepperTile() {
            Stepper plain = KitStepper(4);
            FocusRing.SetShowAlways(plain, true);

            Stepper withUnit = KitStepper(12, "items/s");

            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Plain", "caption", "tertiary"), plain, KitText("With unit", "caption", "tertiary"), withUnit },
            };
        }

        private static Slider KitSlider() => new() { Theme = KitTheme("Kit.Slider") };

        private static StackPanel BuildSliderTile() {
            Slider continuous = KitSlider();
            continuous.Minimum = 0;
            continuous.Maximum = 100;
            continuous.Value = 58;
            continuous.TemplateApplied += (_, e) => {
                if (e.NameScope.Find<Border>("PART_Thumb") is { } thumb)
                    FocusRing.SetShowAlways(thumb, true);
            };

            Slider discrete = KitSlider();
            discrete.Minimum = 0;
            discrete.Maximum = 100;
            discrete.Step = 25;
            discrete.Value = 50;

            Slider formSlider = KitSlider();
            formSlider.Minimum = 0;
            formSlider.Maximum = 100;
            formSlider.Value = 72;
            formSlider.Unit = "%";
            formSlider.ShowValue = true;
            FormRow formRow = KitFormRow("Link opacity", formSlider);

            return new StackPanel {
                Spacing = 8,
                Children = {
                    KitText("Continuous", "caption", "tertiary"), continuous,
                    KitText("Discrete, ticks", "caption", "tertiary"), discrete,
                    KitText("Form row", "caption", "tertiary"), formRow,
                },
            };
        }

        private static readonly string[] FieldControlsScaledSizeKeys = [
            "Foreman.Size.ControlHeightRegular", "Foreman.Metrics.TextFieldMinWidth",
            "Foreman.Metrics.SearchFieldMinWidth", "Foreman.Metrics.SearchFieldNaturalWidth", "Foreman.Size.SearchFieldGlyph",
            "Foreman.Size.StepperFieldWidth", "Foreman.Size.StepperArrowColumn",
            "Foreman.Size.SliderTrack", "Foreman.Size.SliderThumbNormal", "Foreman.Size.SliderThumbHover", "Foreman.Size.SliderThumbPressed",
            "Foreman.Size.SliderTickHeight", "Foreman.Size.SliderTickGap", "Foreman.Size.SliderHeight",
            "Foreman.Metrics.SliderMinWidth", "Foreman.Metrics.SliderNaturalWidth", "Foreman.Metrics.SliderValueLabelWidth",
            "Foreman.Type.Callout.FontSize", "Foreman.Size.Glyph16",
        ];

        private static StackPanel BuildFieldControlsScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in FieldControlsScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildTextFieldTile());
            container.Children.Add(BuildSearchFieldTile());
            container.Children.Add(BuildStepperTile());
            container.Children.Add(BuildSliderTile());
            return container;
        }

        private static ListRow KitListRow(string label, string trailing, IBrush icon) => new() {
            Theme = KitTheme("Kit.ListRow"), Label = label, Trailing = trailing, Icon = icon,
        };

        private static StackPanel BuildListRowsTile() {
            IBrush swatch = ThemeResources.ResolveBrush("Foreman.Color.Accent", Colors.SteelBlue);

            ListRow normal = KitListRow("Electric furnace", "×4", swatch);
            ListRow selectedKey = KitListRow("Steel furnace", "×6", swatch);
            selectedKey.IsSelected = true;
            ListRow selectedIdle = KitListRow("Stone furnace", "×12", swatch);
            selectedIdle.IsSelected = true;
            selectedIdle.IsKey = false;
            ListRow selectedFocused = KitListRow("Foundry", "×1", swatch);
            selectedFocused.IsSelected = true;
            selectedFocused.TemplateApplied += (_, e) => {
                if (e.NameScope.Find<Border>("PART_Frame") is Border frame)
                    FocusRing.SetShowAlways(frame, true);
            };

            var list = new ListBox { Theme = KitTheme("Kit.List") };
            list.Items.Add(normal);
            list.Items.Add(selectedKey);
            list.Items.Add(selectedIdle);
            list.Items.Add(selectedFocused);

            var empty = new TextBlock {
                Text = "No results for “plutonium”",
                Theme = KitTheme("Kit.Text"),
            };
            empty.Classes.Add("callout");
            empty.Classes.Add("tertiary");

            Application.Current!.TryGetResource("Foreman.Radius.Card", null, out object? listRadius);
            Application.Current!.TryGetResource("Foreman.Thickness.ListFramePadding", null, out object? listPadding);
            var emptyFrame = new Border {
                Background = ThemeResources.ResolveBrush("Foreman.Color.WellBackground", Colors.White),
                BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.Border", Color.FromArgb(0x17, 0, 0, 0)),
                BorderThickness = new Thickness(1),
                CornerRadius = listRadius as CornerRadius? ?? default,
                Padding = listPadding as Thickness? ?? default,
                Child = empty,
            };

            return new StackPanel {
                Spacing = 8,
                Children = {
                    KitText("States", "caption", "tertiary"), list,
                    KitText("Empty state", "caption", "tertiary"), emptyFrame,
                },
            };
        }

        private static SidebarRow KitSidebarRow(string label, Geometry glyph) => new() {
            Theme = KitTheme("Kit.SidebarRow"), Label = label, Glyph = glyph,
        };

        private static StackPanel BuildSidebarRowsTile() {
            Geometry assemblers = (Geometry)Application.Current!.FindResource("Kit.Glyph.Item.17")!;
            Geometry smelting = (Geometry)Application.Current!.FindResource("Kit.Glyph.Recipe.17")!;
            Geometry intermediates = (Geometry)Application.Current!.FindResource("Kit.Glyph.View.17")!;
            Geometry fluids = (Geometry)Application.Current!.FindResource("Kit.Glyph.Connect.17")!;

            SidebarRow selectedKey = KitSidebarRow("Assemblers", assemblers);
            selectedKey.IsSelected = true;
            SidebarRow hover = KitSidebarRow("Smelting", smelting);
            SidebarRow selectedIdle = KitSidebarRow("Intermediates", intermediates);
            selectedIdle.IsSelected = true;
            selectedIdle.IsKey = false;
            SidebarRow plain = KitSidebarRow("Fluids", fluids);

            var sidebar = new StackPanel {
                Spacing = 2,
                Background = ThemeResources.ResolveBrush("Foreman.Color.SidebarBackground", Color.FromRgb(0xEF, 0xEF, 0xF1)),
                Children = { selectedKey, hover, selectedIdle, plain },
            };

            return new StackPanel { Spacing = 8, Children = { KitText("Sidebar", "caption", "tertiary"), sidebar } };
        }

        private static StackPanel BuildGroupBoxTile() {
            var direction = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = {
                    KitText("Default node direction", "body"),
                    KitSegmented("Up", "Down"),
                },
            };
            var smartDirection = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { KitText("Smart direction", "body") },
            };

            var box = new GroupBox {
                Theme = KitTheme("Kit.GroupBox"),
                Header = "Canvas",
                Content = new StackPanel { Spacing = 4, Children = { direction, smartDirection } },
            };

            return new StackPanel { Spacing = 8, Children = { KitText("Group box", "caption", "tertiary"), box } };
        }

        private static TabStrip KitToolbarTabStrip(int selected, params (string Label, string GlyphKey)[] items) {
            var strip = new TabStrip { Theme = KitTheme("Kit.TabStrip"), SelectedIndex = selected };
            strip.Classes.Add("toolbar-tabs");
            foreach ((string label, string glyphKey) in items)
                strip.Items.Add(new TabStripItem { Text = label, Glyph = (Geometry)Application.Current!.FindResource(glyphKey)! });
            return strip;
        }

        private static TabStrip KitUnderlineTabStrip(int selected, params string[] labels) {
            var strip = new TabStrip { Theme = KitTheme("Kit.TabStrip"), SelectedIndex = selected };
            strip.Classes.Add("underline");
            foreach (string label in labels)
                strip.Items.Add(new TabStripItem { Text = label });
            return strip;
        }

        private static StackPanel BuildTabStripsTile() {
            TabStrip toolbar = KitToolbarTabStrip(0,
                ("General", "Kit.Glyph.Settings.General.18"),
                ("Objects", "Kit.Glyph.Settings.EnabledObjects.18"),
                ("Presets", "Kit.Glyph.Settings.Presets.18"),
                ("Advanced", "Kit.Glyph.Settings.Advanced.18"));
            toolbar.Items.Add(new TabStripItem {
                Text = "Locked",
                Glyph = (Geometry)Application.Current!.FindResource("Kit.Glyph.Settings.Advanced.18")!,
                IsEnabled = false,
            });
            toolbar.TemplateApplied += (_, e) => {
                if (e.NameScope.Find<StackPanel>("PART_Track") is not { } track)
                    return;
                if (track.Children.ElementAtOrDefault(1) is Border hoverItem)
                    hoverItem.Classes.Add("gallery-hover");
                if (track.Children.ElementAtOrDefault(2) is Border pressedItem)
                    pressedItem.Classes.Add("gallery-pressed");
                //Round-trips SelectedIndex so ApplyStateColors picks up the gallery classes added above.
                toolbar.SelectedIndex = toolbar.Items.Count - 1;
                toolbar.SelectedIndex = 0;
                if (track.Children.ElementAtOrDefault(toolbar.SelectedIndex) is Border selectedItem)
                    FocusRing.SetShowAlways(selectedItem, true);
            };

            TabStrip underline = KitUnderlineTabStrip(0, "Buildings", "Items & fluids", "Key nodes");

            return new StackPanel {
                Spacing = 8,
                Children = {
                    KitText(".toolbar-tabs", "caption", "tertiary"), toolbar,
                    KitText(".underline", "caption", "tertiary"), underline,
                },
            };
        }

        private static Disclosure KitDisclosure(string header, bool expanded, object content) => new() {
            Theme = KitTheme("Kit.Disclosure"), Header = header, IsExpanded = expanded, Content = content,
        };

        private static StackPanel BuildDisclosureTile() {
            Disclosure collapsed = KitDisclosure("Module slots", false, KitText("(hidden while collapsed)", "caption", "tertiary"));
            Disclosure expanded = KitDisclosure("Beacons", true, KitText("Speed module ×2, Effectivity module ×1", "caption", "tertiary"));

            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Collapsed", "caption", "tertiary"), collapsed, KitText("Expanded", "caption", "tertiary"), expanded },
            };
        }

        private static readonly string[] RowsAndChromeScaledSizeKeys = [
            "Foreman.Size.ListRowHeight", "Foreman.Size.SidebarRowHeight",
            "Foreman.Size.ListRowIconGap", "Foreman.Size.SidebarRowIconGap", "Foreman.Size.Glyph17", "Foreman.Size.Glyph18",
            "Foreman.Metrics.GroupBoxMaxWidth", "Foreman.Size.ControlHeightRegular",
            "Foreman.Type.TabLabel.FontSize", "Foreman.Metrics.TabItemMinWidth", "Foreman.Size.TabItemHeight",
            "Foreman.Size.TabItemGlyphGap", "Foreman.Size.TabItemGap", "Foreman.Size.UnderlineTabGap",
            "Foreman.Size.GlyphMicro", "Foreman.Type.Callout.FontSize", "Foreman.Type.SectionHeader.FontSize",
            "Foreman.Size.ControlGlyphGap",
        ];

        private static StackPanel BuildRowsAndChromeScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in RowsAndChromeScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildListRowsTile());
            container.Children.Add(BuildSidebarRowsTile());
            container.Children.Add(BuildGroupBoxTile());
            container.Children.Add(BuildTabStripsTile());
            container.Children.Add(BuildDisclosureTile());
            return container;
        }

        //Rendered directly rather than through a real hover, matching every other gallery tile's static-swatch convention.
        private static StackPanel BuildTooltipTile() {
            var shortTip = new ToolTip { Theme = KitTheme("Kit.Tooltip"), Content = "Find node — ⌘F" };
            var longTip = new ToolTip {
                Theme = KitTheme("Kit.Tooltip"),
                Content = "Overproduction: this node makes 42 items / s more than its consumers take.",
            };

            var opaqueSwatch = new ToolTip { Theme = KitTheme("Kit.Tooltip"), Content = "Find node — ⌘F" };
            Application.Current!.TryGetResource("Foreman.Color.TooltipMaterialOpaque", Application.Current.ActualThemeVariant, out object? opaqueMaterial);
            var opaqueHost = new ContentControl { Content = opaqueSwatch };
            opaqueHost.Resources["Foreman.Color.TooltipMaterial"] = opaqueMaterial!;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { shortTip, longTip } };

            var host = new Border { Padding = new Thickness(8) };
            ToolTip.SetTip(host, new ToolTip { Theme = KitTheme("Kit.Tooltip"), Content = "Find node — ⌘F" });
            TooltipTiming.SetIsEnabled(host, true);
            host.Child = KitText("Hover wires TooltipTiming (800 ms delay, 500 ms warm window)", "callout", "secondary");

            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Material + wrapping", "caption", "tertiary"), row, KitText("Opaque fallback (Reduce Transparency)", "caption", "tertiary"), opaqueHost, host },
            };
        }

        private static Spinner KitSpinner(params string[] classes) {
            var spinner = new Spinner { Theme = KitTheme("Kit.Spinner") };
            foreach (string cls in classes)
                spinner.Classes.Add(cls);
            return spinner;
        }

        private static ProgressBar KitProgressBar(double value, bool indeterminate = false) {
            var bar = new ProgressBar { Theme = KitTheme("Kit.ProgressBar") };
            if (indeterminate)
                bar.IsIndeterminate = true;
            else
                bar.Value = value;
            return bar;
        }

        private static StackPanel BuildSpinnerAndProgressTile() {
            var spinnerRow = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { KitSpinner(), KitText("18 pt — Loading recent graphs…", "callout", "secondary"), KitSpinner("lg") },
            };

            var determinate = new StackPanel {
                Spacing = 6,
                Children = { KitProgressBar(62), KitText("Solving graph — 62 %", "callout", "secondary") },
            };
            var indeterminate = new StackPanel {
                Spacing = 6,
                Children = { KitProgressBar(0, indeterminate: true), KitText("Reading the data dump…", "callout", "secondary") },
            };

            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Spinner — 18 / 24 pt", "caption", "tertiary"), spinnerRow, KitText("Progress — determinate / indeterminate", "caption", "tertiary"), determinate, indeterminate },
            };
        }

        private static Chip KitChip(string label) => new() { Theme = KitTheme("Kit.Chip"), Label = label };

        private static StackPanel BuildChipsTile() {
            Chip normal = KitChip("Assembling machine");
            Chip focused = KitChip("Electric furnace");
            focused.TemplateApplied += (_, _) => FocusRing.SetShowAlways(focused, true);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { normal, focused } };
            return new StackPanel { Spacing = 8, Children = { KitText("Chips", "caption", "tertiary"), row } };
        }

        private static KeyCap KitKeyCap(string text, string accessibleName) =>
            new() { Theme = KitTheme("Kit.KeyCap"), Text = text, AccessibleName = accessibleName };

        private static StackPanel BuildKeyCapsTile() {
            var commandK = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Children = { KitKeyCap("⌘", "Command"), KitKeyCap("K", "K") },
            };
            var shiftCommandP = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Children = { KitKeyCap("⇧", "Shift"), KitKeyCap("⌘", "Command"), KitKeyCap("P", "P") },
            };
            var returnKey = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 3,
                Children = { KitKeyCap("Return", "Return") },
            };

            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Key caps", "caption", "tertiary"), commandK, shiftCommandP, returnKey },
            };
        }

        private static readonly string[] FeedbackScaledSizeKeys = [
            "Foreman.Metrics.TooltipMaxWidth", "Foreman.Type.Caption.FontSize",
            "Foreman.Size.Spinner", "Foreman.Size.SpinnerLarge", "Foreman.Size.SpinnerStroke", "Foreman.Size.SpinnerStrokeLarge",
            "Foreman.Size.ProgressBarWidth", "Foreman.Size.ProgressBarHeight", "Foreman.Size.ProgressBarSegment",
            "Foreman.Size.ChipHeight", "Foreman.Size.ChipCloseBox", "Foreman.Size.ChipGap",
            "Foreman.Size.KeyCapMinSquare", "Foreman.Size.GlyphMicro",
        ];

        private static StackPanel BuildFeedbackScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in FeedbackScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildTooltipTile());
            container.Children.Add(BuildSpinnerAndProgressTile());
            container.Children.Add(BuildChipsTile());
            container.Children.Add(BuildKeyCapsTile());
            return container;
        }

        private static FormRow KitFormRow(string label, Control content, bool inspector = false, string? note = null, string? error = null) {
            var row = new FormRow { Theme = KitTheme("Kit.FormRow"), Label = label, Content = content, IsInspector = inspector };
            if (note is not null)
                row.Note = note;
            if (error is not null)
                row.Error = error;
            return row;
        }

        private static FormGroup KitFormGroup(string title, bool boxed, params FormRow[] rows) {
            var group = new FormGroup { Theme = KitTheme("Kit.FormGroup"), Title = title, IsBoxed = boxed };
            foreach (FormRow row in rows)
                group.Items.Add(row);
            return group;
        }

        //Settings shape (contract §01): a boxed group at the 132 pt window label column.
        private static FormGroup BuildFormGroupBoxedTile() {
            FormRow direction = KitFormRow("Default node direction", KitSegmented("Up", "Down"));
            FormRow width = KitFormRow("Node width", KitStepper(140, "pt"));

            Slider opacitySlider = KitSlider();
            opacitySlider.Minimum = 0;
            opacitySlider.Maximum = 100;
            opacitySlider.Value = 72;
            opacitySlider.Unit = "%";
            opacitySlider.ShowValue = true;
            FormRow opacity = KitFormRow("Link opacity", opacitySlider);

            FormRow smart = KitFormRow("Smart direction", new Switch { Theme = KitTheme("Kit.Switch"), IsChecked = true });

            var scaleCheckbox = new CheckBox { Theme = KitTheme("Kit.CheckBox"), Content = "Scale link width by throughput" };
            FormRow linkWidth = KitFormRow("Link width", scaleCheckbox, note: "Off by default. Wide graphs read faster without it.");

            return KitFormGroup("Canvas", true, direction, width, opacity, smart, linkWidth);
        }

        //Inspector shape (contract §01): plain rows (no well) at the 108 pt sidebar label column.
        private static StackPanel BuildFormRowsInspectorTile() {
            FormRow quality = KitFormRow("Quality", KitPopUp(false, "Normal", "High", "Ultra"), inspector: true);
            FormRow rate = KitFormRow("Rate", KitStepper(12.5, "/s"), inspector: true);
            FormRow keyNode = KitFormRow("Key node", new CheckBox { Theme = KitTheme("Kit.CheckBox") }, inspector: true);
            FormRow lowPriority = KitFormRow("Low priority", new CheckBox { Theme = KitTheme("Kit.CheckBox") }, inspector: true);

            var stack = new StackPanel { Width = 276, Spacing = 8, Children = { quality, rate, keyNode, lowPriority } };
            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Inspector shape, 108 pt label column", "caption", "tertiary"), stack },
            };
        }

        private static StackPanel BuildFormRowNotesAndErrorsTile() {
            FormRow assembler = KitFormRow("Assembler", KitTextField("Assembling machine 3"),
                note: "Used for every new node until you change it.");

            FormRow beaconCount = KitFormRow("Beacon count", KitTextField("−2"), error: "Must be 0 or more.");

            var relatedChecks = new StackPanel {
                Spacing = 6,
                Children = {
                    new CheckBox { Theme = KitTheme("Kit.CheckBox"), Content = "Show belt direction arrows" },
                    new CheckBox { Theme = KitTheme("Kit.CheckBox"), Content = "Show throughput badges", IsChecked = true },
                },
            };
            FormRow grouped = KitFormRow("Belt overlays", relatedChecks);

            return new StackPanel {
                Spacing = 8,
                Children = {
                    KitText("Note", "caption", "tertiary"), assembler,
                    KitText("Error", "caption", "tertiary"), beaconCount,
                    KitText("Checkbox-row grouping", "caption", "tertiary"), grouped,
                },
            };
        }

        //Closes the two K2 deferrals this task owns: SearchField's loading spinner slot and Chip's owner-handoff contract.
        private static StackPanel BuildFormRowSearchAndChipTile() {
            SearchField loading = KitSearchField();
            loading.IsLoading = true;
            FormRow searchRow = KitFormRow("Search", loading);

            Chip iron = KitChip("Iron plate");
            Chip copper = KitChip("Copper plate");
            var chipOwner = KitText("Filters (owner)", "caption", "tertiary");
            ChipHost.SetFocusFallback(iron, chipOwner);
            ChipHost.SetFocusFallback(copper, chipOwner);
            var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { iron, copper } };
            FormRow filters = KitFormRow("Filters", chipRow);

            return new StackPanel {
                Spacing = 8,
                Children = {
                    KitText("Search field — loading state", "caption", "tertiary"), searchRow,
                    chipOwner, filters,
                },
            };
        }

        private static readonly string[] FormCompositesScaledSizeKeys = [
            "Foreman.Size.FormLabelColumn", "Foreman.Size.FormLabelColumnInspector", "Foreman.Size.ControlHeightRegular",
            "Foreman.Type.Callout.FontSize", "Foreman.Type.Caption.FontSize", "Foreman.Type.SectionHeader.FontSize",
            "Foreman.Size.StepperFieldWidth", "Foreman.Size.StepperArrowColumn",
            "Foreman.Size.SliderTrack", "Foreman.Size.SliderThumbNormal", "Foreman.Size.SliderHeight",
            "Foreman.Size.SwitchWidth", "Foreman.Size.SwitchHeight", "Foreman.Size.SwitchKnob",
            "Foreman.Size.CheckBox", "Foreman.Size.CheckGlyph", "Foreman.Size.PopUpIndicator",
            "Foreman.Metrics.TextFieldMinWidth", "Foreman.Metrics.SearchFieldMinWidth", "Foreman.Metrics.SearchFieldNaturalWidth",
            "Foreman.Size.SearchFieldGlyph", "Foreman.Size.Spinner", "Foreman.Size.SpinnerStroke",
            "Foreman.Size.ChipHeight", "Foreman.Size.ChipCloseBox", "Foreman.Size.ChipGap",
            "Foreman.Metrics.GroupBoxMaxWidth",
        ];

        private static StackPanel BuildFormCompositesScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in FormCompositesScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildFormGroupBoxedTile());
            container.Children.Add(BuildFormRowsInspectorTile());
            container.Children.Add(BuildFormRowNotesAndErrorsTile());
            container.Children.Add(BuildFormRowSearchAndChipTile());
            return container;
        }

        private static InspectorGroup KitInspectorGroup(string header, bool expanded, FormRow[] rows) {
            var body = new FormGroup { Theme = KitTheme("Kit.FormGroup") };
            foreach (FormRow row in rows)
                body.Items.Add(row);
            return new InspectorGroup { Theme = KitTheme("Kit.InspectorGroup"), Header = header, IsExpanded = expanded, Content = body };
        }

        private static FormRow[] RecipeGroupRows() => [
            KitFormRow("Quality", KitPopUp(false, "Normal", "High", "Ultra"), inspector: true),
            KitFormRow("Building", KitPopUp(false, "Assembler 3", "Assembler 2"), inspector: true),
        ];

        //Contract §02: group 1 expanded, group 2 hovered, group 3 focused (ring on the whole header row),
        //group 4 collapsed - a collapsed group never measures its body expanded on first layout.
        private static StackPanel BuildInspectorGroupTile() {
            InspectorGroup expanded = KitInspectorGroup("Recipe", true, RecipeGroupRows());

            InspectorGroup hovered = KitInspectorGroup("Module slots", true, RecipeGroupRows());
            hovered.Classes.Add("gallery-hover");

            InspectorGroup focused = KitInspectorGroup("Beacons", true, RecipeGroupRows());
            focused.TemplateApplied += (_, _) => FocusRing.SetShowAlways(
                focused.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HeaderRow"), true);

            InspectorGroup collapsed = KitInspectorGroup("View options", false, RecipeGroupRows());

            var stack = new StackPanel { Width = 276, Spacing = 0, Children = { expanded, hovered, focused, collapsed } };
            return new StackPanel { Spacing = 8, Children = { KitText("Inspector group", "caption", "tertiary"), stack } };
        }

        private static Border SplitPane() => new() { Width = 90, Height = 120, Background = new SolidColorBrush(Color.Parse("#11000000")) };

        private static StackPanel BuildSplitDividerTile() {
            SplitDivider rest = new() { Theme = KitTheme("Kit.SplitDivider"), Height = 120 };

            SplitDivider hovered = new() { Theme = KitTheme("Kit.SplitDivider"), Height = 120 };
            hovered.Classes.Add("gallery-hover");

            SplitDivider dragging = new() { Theme = KitTheme("Kit.SplitDivider"), Height = 120 };
            dragging.Classes.Add("gallery-dragging");

            SplitDivider focused = new() { Theme = KitTheme("Kit.SplitDivider"), Height = 120 };
            focused.TemplateApplied += (_, _) => FocusRing.SetShowAlways(focused, true);

            var row = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 16,
                Children = {
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { SplitPane(), rest, SplitPane() } },
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { SplitPane(), hovered, SplitPane() } },
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { SplitPane(), dragging, SplitPane() } },
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { SplitPane(), focused, SplitPane() } },
                },
            };
            return new StackPanel { Spacing = 8, Children = { KitText("Split divider", "caption", "tertiary"), row } };
        }

        private static readonly string[] InspectorAndDividerScaledSizeKeys = [
            "Foreman.Size.ControlHeightRegular", "Foreman.Size.FormLabelColumnInspector",
            "Foreman.Type.SectionHeader.FontSize", "Foreman.Type.Callout.FontSize",
            "Foreman.Size.ControlGlyphGap", "Foreman.Size.GlyphMicro", "Foreman.Size.PopUpIndicator",
        ];

        //SplitDividerHit is deliberately left out of the scaled-resources override - the hit area stays 5 here too.
        private static StackPanel BuildInspectorAndDividerScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in InspectorAndDividerScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildInspectorGroupTile());
            container.Children.Add(BuildSplitDividerTile());
            return container;
        }

        private static DrawingImage PickCellIconArt(IBrush color) =>
            new DrawingImage(new GeometryDrawing { Geometry = new EllipseGeometry(new Rect(0, 0, 18, 18)), Brush = color });

        private static PickCell KitPickCell(bool selected = false, bool empty = false, bool missing = false, IImage? icon = null, string? name = null) => new() {
            Theme = KitTheme("Kit.PickCell"), IsSelected = selected, IsEmpty = empty, IsMissing = missing, Icon = icon, AccessibleName = name,
        };

        //Addendum §03 state grid: Normal / Hover / Selected / Selected+focused / Disabled, filled then empty.
        private static StackPanel BuildPickCellStatesTile() {
            IImage art = PickCellIconArt(ThemeResources.ResolveBrush("Foreman.Color.Accent", Colors.SteelBlue));

            PickCell FilledCell(bool selected) => KitPickCell(selected: selected, icon: art, name: "Productivity Module 3");
            PickCell filledNormal = FilledCell(false);
            PickCell filledHover = FilledCell(false);
            filledHover.Classes.Add("gallery-hover");
            PickCell filledSelected = FilledCell(true);
            PickCell filledSelectedFocused = FilledCell(true);
            filledSelectedFocused.TemplateApplied += (_, _) => FocusRing.SetShowAlways(filledSelectedFocused, true);
            PickCell filledDisabled = FilledCell(false);
            filledDisabled.IsEnabled = false;

            var filledRow = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { filledNormal, filledHover, filledSelected, filledSelectedFocused, filledDisabled },
            };

            PickCell EmptyCell(bool selected) => KitPickCell(selected: selected, empty: true);
            PickCell emptyNormal = EmptyCell(false);
            PickCell emptyHover = EmptyCell(false);
            emptyHover.Classes.Add("gallery-hover");
            PickCell emptySelected = EmptyCell(true);
            PickCell emptySelectedFocused = EmptyCell(true);
            emptySelectedFocused.TemplateApplied += (_, _) => FocusRing.SetShowAlways(emptySelectedFocused, true);
            PickCell emptyDisabled = EmptyCell(false);
            emptyDisabled.IsEnabled = false;

            var emptyRow = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { emptyNormal, emptyHover, emptySelected, emptySelectedFocused, emptyDisabled },
            };

            return new StackPanel {
                Spacing = 8,
                Children = {
                    KitText("Normal · Hover · Selected · Selected+focused · Disabled", "caption", "tertiary"),
                    filledRow, emptyRow,
                },
            };
        }

        //Modules (two filled, two empty) and Beacon modules (two filled), each a PickGrid inside a
        //Kit.FormRow control column, matching the addendum's own figure.
        private static StackPanel BuildPickGridTile() {
            IImage productivity = PickCellIconArt(ThemeResources.ResolveBrush("Foreman.Color.Accent", Colors.SteelBlue));
            IImage speed = PickCellIconArt(ThemeResources.ResolveBrush("Foreman.Color.Warning", Colors.Orange));

            var modules = new PickGrid { Theme = KitTheme("Kit.PickGrid"), GroupLabel = "Module slots" };
            modules.Slots.Add(new PickSlot { Icon = productivity, Name = "Productivity Module 3" });
            modules.Slots.Add(new PickSlot { Icon = speed, Name = "Speed Module 2" });
            modules.Slots.Add(new PickSlot { IsEmpty = true });
            modules.Slots.Add(new PickSlot { IsEmpty = true });

            var beacons = new PickGrid { Theme = KitTheme("Kit.PickGrid"), GroupLabel = "Beacon modules" };
            beacons.Slots.Add(new PickSlot { Icon = speed, Name = "Speed Module 2" });
            beacons.Slots.Add(new PickSlot { Icon = speed, Name = "Speed Module 2" });

            var stack = new StackPanel {
                Width = 276,
                Spacing = 8,
                Children = {
                    KitFormRow("Modules", modules, inspector: true),
                    KitFormRow("Beacon modules", beacons, inspector: true),
                },
            };
            return new StackPanel { Spacing = 8, Children = { KitText("Pick grid", "caption", "tertiary"), stack } };
        }

        private static readonly string[] PickCompositesScaledSizeKeys = [
            "Foreman.Size.PickCell", "Foreman.Size.PickCellIcon", "Foreman.Size.GlyphMicro",
            "Foreman.Size.FormLabelColumnInspector", "Foreman.Size.ControlHeightRegular", "Foreman.Type.Callout.FontSize",
        ];

        //Thickness.PickCellGap is deliberately left out of the scaled-resources override - the 2.5pt gap
        //between cells stays 2.5pt here too (Thickness tokens do not scale, addendum §11).
        private static StackPanel BuildPickCompositesScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in PickCompositesScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildPickCellStatesTile());
            container.Children.Add(BuildPickGridTile());
            return container;
        }

        private static ChooserCell KitChooserCell(bool selected = false, IImage? icon = null, string? name = null) => new() {
            Theme = KitTheme("Kit.ChooserCell"), IsSelected = selected, Icon = icon, Label = name, FullName = name,
        };

        //Addendum §04 state grid: Normal / Hover / Selected / Selected+focused / Disabled.
        private static StackPanel BuildChooserCellStatesTile() {
            IImage art = PickCellIconArt(ThemeResources.ResolveBrush("Foreman.Color.Accent", Colors.SteelBlue));

            ChooserCell Cell(bool selected) => KitChooserCell(selected: selected, icon: art, name: "Assembling machine 3");
            ChooserCell normal = Cell(false);
            ChooserCell hover = Cell(false);
            hover.Classes.Add("gallery-hover");
            ChooserCell selected = Cell(true);
            ChooserCell selectedFocused = Cell(true);
            selectedFocused.TemplateApplied += (_, _) => FocusRing.SetShowAlways(selectedFocused, true);
            ChooserCell disabled = Cell(false);
            disabled.IsEnabled = false;

            var row = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { normal, hover, selected, selectedFocused, disabled },
            };

            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Normal · Hover · Selected · Selected+focused · Disabled", "caption", "tertiary"), row },
            };
        }

        //Two "Group · Subgroup" headings, matching the addendum's own search-results figure.
        private static StackPanel BuildChooserGridTile() {
            IImage plate = PickCellIconArt(ThemeResources.ResolveBrush("Foreman.Color.Accent", Colors.SteelBlue));
            IImage smelted = PickCellIconArt(ThemeResources.ResolveBrush("Foreman.Color.Warning", Colors.Orange));

            var smelting = new ChooserSection { Group = "Production", Subgroup = "Smelting" };
            smelting.Items.Add(new ChooserItem { Icon = plate, Name = "Iron plate" });
            smelting.Items.Add(new ChooserItem { Icon = plate, Name = "Copper plate" });
            smelting.Items.Add(new ChooserItem { Icon = plate, Name = "Steel plate" });

            var metallurgy = new ChooserSection { Group = "Production", Subgroup = "Metallurgy" };
            metallurgy.Items.Add(new ChooserItem { Icon = smelted, Name = "Casting iron plate" });
            metallurgy.Items.Add(new ChooserItem { Icon = smelted, Name = "Molten iron from lava" });

            var grid = new ChooserGrid { Theme = KitTheme("Kit.ChooserGrid"), Width = 360, Height = 260, Query = "plate" };
            grid.Sections.Add(smelting);
            grid.Sections.Add(metallurgy);
            grid.SelectedItem = smelting.Items[0];

            return new StackPanel { Spacing = 8, Children = { KitText("Chooser grid", "caption", "tertiary"), grid } };
        }

        private static readonly string[] ChooserCompositesScaledSizeKeys = [
            "Foreman.Size.ChooserCell", "Foreman.Size.BrowserCell", "Foreman.Size.BrowserCellLabelHeight",
            "Foreman.Type.Caption.FontSize", "Foreman.Type.SectionHeader.FontSize",
        ];

        private static StackPanel BuildChooserCompositesScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in ChooserCompositesScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildChooserCellStatesTile());
            container.Children.Add(BuildChooserGridTile());
            return container;
        }

        private static Button KitRowButton(string text, ButtonRowRole role) {
            var button = KitButton(text);
            ButtonRow.SetRole(button, role);
            return button;
        }

        private static ButtonRow KitButtonRow(params Button[] buttons) {
            var row = new ButtonRow { Theme = KitTheme("Kit.ButtonRow") };
            foreach (Button b in buttons)
                row.Items.Add(b);
            return row;
        }

        //Addendum §05: Scrim + Sheet frame over a mock window area, the "Export an image" sheet.
        private static Border BuildSheetFrameTile() {
            var field = new TextBox { Theme = KitTheme("Kit.TextField"), Text = "2×" };
            var content = new StackPanel { Spacing = 8, Children = { field } };
            var buttons = KitButtonRow(KitRowButton("Cancel", ButtonRowRole.Cancel), KitRowButton("Export…", ButtonRowRole.Default));

            var sheet = new SheetFrame {
                Theme = KitTheme("Kit.SheetFrame"),
                Title = "Export an image",
                Content = content,
                Buttons = buttons,
                IsOpen = true,
            };

            var mockWindow = new Panel {
                Width = 420,
                Height = 260,
                Children = { sheet },
            };
            return new Border {
                Width = 420, Height = 260,
                Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White),
                BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.BorderStrong", Colors.Black),
                BorderThickness = new Thickness(1),
                Child = mockWindow,
            };
        }

        //Addendum §05: the four ButtonRow shapes, in the mock's order.
        private static StackPanel BuildButtonRowShapesTile() {
            ButtonRow oneButton = KitButtonRow(KitRowButton("Done", ButtonRowRole.Default));
            ButtonRow twoButton = KitButtonRow(KitRowButton("Cancel", ButtonRowRole.Cancel), KitRowButton("Save", ButtonRowRole.Default));
            ButtonRow threeButton = KitButtonRow(
                KitRowButton("Don't Save", ButtonRowRole.Other),
                KitRowButton("Cancel", ButtonRowRole.Cancel),
                KitRowButton("Save", ButtonRowRole.Default));
            ButtonRow destructive = KitButtonRow(KitRowButton("Discard", ButtonRowRole.Destructive), KitRowButton("Cancel", ButtonRowRole.Cancel));
            ButtonRow helper = KitButtonRow(
                KitRowButton("Reset to defaults", ButtonRowRole.Helper),
                KitRowButton("Cancel", ButtonRowRole.Cancel),
                KitRowButton("Apply", ButtonRowRole.Default));

            var stack = new StackPanel { Width = 340, Spacing = 12 };
            foreach (ButtonRow row in new[] { oneButton, twoButton, threeButton, destructive, helper })
                stack.Children.Add(row);
            return stack;
        }

        //Addendum §05: a two-button destructive alert and a three-button alert (destructive leading of Cancel).
        private static StackPanel BuildAlertFramesTile() {
            AlertFrame twoButton = KitAlertFrame(
                "Discard changes to this graph?",
                "You have unsaved edits made since the last save. Discarding cannot be undone.",
                KitButtonRow(KitRowButton("Discard", ButtonRowRole.Destructive), KitRowButton("Cancel", ButtonRowRole.Cancel)));

            AlertFrame threeButton = KitAlertFrame(
                "Save changes before closing?",
                "Your changes will be lost if you don't save them.",
                KitButtonRow(
                    KitRowButton("Don't Save", ButtonRowRole.Other),
                    KitRowButton("Cancel", ButtonRowRole.Cancel),
                    KitRowButton("Save", ButtonRowRole.Default)));

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { AlertWindowFrame(twoButton), AlertWindowFrame(threeButton) } };
            return new StackPanel { Spacing = 8, Children = { KitText("Alert frame", "caption", "tertiary"), row } };
        }

        private static AlertFrame KitAlertFrame(string headline, string informative, ButtonRow buttons) => new() {
            Theme = KitTheme("Kit.AlertFrame"), Icon = AppIconAsset(), Headline = headline, InformativeText = informative, Buttons = buttons, IsOpen = true,
        };

        //The real app icon (AboutWindow renders the same asset), confirmed to decode headless.
        private static readonly Lazy<Bitmap> appIconAsset = new(() => {
            using System.IO.Stream stream = AssetLoader.Open(new Uri("avares://Foreman.Mac/Assets/unspoiled-icon-512.png"));
            return new Bitmap(stream);
        });

        private static Bitmap AppIconAsset() => appIconAsset.Value;

        private static Border AlertWindowFrame(AlertFrame alert) => new() {
            Width = 340, Height = 180,
            Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White),
            BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.BorderStrong", Colors.Black),
            BorderThickness = new Thickness(1),
            Child = alert,
        };

        private static readonly string[] SheetCompositesScaledSizeKeys = [
            "Foreman.Metrics.SheetWidthAlert", "Foreman.Metrics.SheetWidthStandard", "Foreman.Metrics.SheetWidthWide",
            "Foreman.Size.ControlHeightRegular", "Foreman.Type.EmphasizedBody.FontSize", "Foreman.Type.Callout.FontSize",
            "Foreman.Metrics.ButtonMinWidth",
        ];

        private static StackPanel BuildSheetCompositesScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in SheetCompositesScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildSheetFrameTile());
            container.Children.Add(BuildButtonRowShapesTile());
            container.Children.Add(BuildAlertFramesTile());
            return container;
        }

        //Anchored below the button, holding a Kit.FormGroup - the view-options checklist the popover replaces.
        private static Border BuildPopoverFrameTile() {
            var anchor = new Button { Theme = KitTheme("Kit.Button"), Content = "View options" };

            var group = new FormGroup { Theme = KitTheme("Kit.FormGroup"), Title = "Show on nodes" };
            group.Items.Add(new CheckBox { Theme = KitTheme("Kit.CheckBox"), Content = "Rates", IsChecked = true });
            group.Items.Add(new CheckBox { Theme = KitTheme("Kit.CheckBox"), Content = "Machine counts", IsChecked = true });
            group.Items.Add(new CheckBox { Theme = KitTheme("Kit.CheckBox"), Content = "Module icons" });

            var popover = new PopoverFrame { Theme = KitTheme("Kit.PopoverFrame"), Content = group, Anchor = anchor };
            anchor.Click += (_, _) => popover.IsOpen = true;

            var host = new PopoverHost { Width = 320, Height = 220 };
            Avalonia.Controls.Canvas.SetLeft(anchor, 40);
            Avalonia.Controls.Canvas.SetTop(anchor, 20);
            host.Surface.Children.Add(anchor);
            host.Surface.Children.Add(popover);
            popover.IsOpen = true;

            return new Border {
                Width = 320, Height = 220,
                Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White),
                BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.BorderStrong", Colors.Black),
                BorderThickness = new Thickness(1),
                Child = host,
            };
        }

        //Anchor pinned at the top-left corner - the tail clamps to its 26pt minimum offset instead of
        //centering under the anchor, and the frame itself clamps flush to the host's edge.
        private static Border BuildPopoverFrameCornerTile() {
            var anchor = new Button { Theme = KitTheme("Kit.Button"), Content = "•" };
            var content = new TextBlock { Theme = KitTheme("Kit.Text"), Text = "Clamped near the corner" };
            var popover = new PopoverFrame { Theme = KitTheme("Kit.PopoverFrame"), Content = content, Anchor = anchor };
            anchor.Click += (_, _) => popover.IsOpen = true;

            var host = new PopoverHost { Width = 220, Height = 140 };
            Avalonia.Controls.Canvas.SetLeft(anchor, 4);
            Avalonia.Controls.Canvas.SetTop(anchor, 4);
            host.Surface.Children.Add(anchor);
            host.Surface.Children.Add(popover);
            popover.IsOpen = true;

            return new Border {
                Width = 220, Height = 140,
                Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White),
                BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.BorderStrong", Colors.Black),
                BorderThickness = new Thickness(1),
                Child = host,
            };
        }

        private static readonly string[] RadialRingGlyphKeys = ["RadialCancel", "Recipe", "Item", "RadialSource", "RadialOutput", "RadialPlant"];
        private static readonly string[] RadialRingLabels = ["Cancel", "Recipe", "Item", "Source", "Output", "Plant"];

        //One armed slice (index 2, "Item") - the addendum's own worked example.
        private static Border BuildRadialRingTile() {
            var ring = new RadialRing { Theme = KitTheme("Kit.RadialRing") };
            for (int i = 0; i < RadialRingGlyphKeys.Length; i++)
                ring.Items.Add(new RadialItem { GlyphKey = RadialRingGlyphKeys[i], Label = RadialRingLabels[i] });
            ring.IsOpen = true;
            ring.ArmedIndex = 2;

            return new Border {
                Width = 220, Height = 220,
                Background = ThemeResources.ResolveBrush("Foreman.Color.CanvasBackground", Colors.White),
                BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.BorderStrong", Colors.Black),
                BorderThickness = new Thickness(1),
                Child = ring,
            };
        }

        private static readonly string[] PopoverAndRadialScaledSizeKeys = [
            "Foreman.Type.SectionHeader.FontSize", "Foreman.Type.Callout.FontSize",
            "Foreman.Size.ControlHeightRegular", "Foreman.Metrics.ButtonMinWidth",
        ];

        //The popover's content scales with these keys; the ring row sits alongside it unscaled - it's
        //excluded from ScaleContract entirely, so its row proves the ring stays 220 regardless.
        private static StackPanel BuildPopoverAndRadialScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in PopoverAndRadialScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildPopoverFrameTile());
            container.Children.Add(BuildRadialRingTile());
            return container;
        }

        private static StatePanel KitStatePanel(StatePanelShape shape, string headline, string sentence, ButtonRow? buttons = null, double? progress = null) =>
            new() {
                Theme = KitTheme("Kit.StatePanel"),
                Shape = shape,
                Headline = headline,
                Sentence = sentence,
                Buttons = buttons,
                Progress = progress,
            };

        private static Border StatePanelFrame(Control panel, double width, double height) =>
            new() {
                Width = width, Height = height,
                Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White),
                BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.BorderStrong", Colors.Black),
                BorderThickness = new Thickness(1),
                Child = panel,
            };

        //Unlike StatePanelFrame, this draws nothing: a real toolbar sits directly on its own titlebar material with no rule at either end (addendum §08 Grouping row).
        private static Border ToolbarRowFrame(Control row, double width, double height) =>
            new() { Width = width, Height = height, Child = row };

        //Addendum §07: inspector no-selection (one button) beside a new-graph empty state (two buttons).
        private static StackPanel BuildStatePanelEmptyTile() {
            StatePanel inspector = KitStatePanel(StatePanelShape.Empty, "No node selected",
                "Select a node on the canvas to edit its recipe, building and modules.",
                KitButtonRow(KitRowButton("Add Recipe…", ButtonRowRole.Default)));
            StatePanel newGraph = KitStatePanel(StatePanelShape.Empty, "This graph is empty",
                "Add a recipe or an item to start planning. You can also paste a graph from the clipboard.",
                KitButtonRow(KitRowButton("Add Item…", ButtonRowRole.Other), KitRowButton("Add Recipe…", ButtonRowRole.Default)));

            var row = new StackPanel {
                Orientation = Orientation.Horizontal, Spacing = 14,
                Children = { StatePanelFrame(inspector, 280, 260), StatePanelFrame(newGraph, 360, 260) },
            };
            return new StackPanel { Spacing = 8, Children = { KitText("State panel — .empty", "caption", "tertiary"), row } };
        }

        private static StackPanel BuildStatePanelNoResultsTile() {
            StatePanel panel = KitStatePanel(StatePanelShape.NoResults, "No results for “beacn”",
                "Nothing matches this search. Two filters are hiding results.",
                KitButtonRow(KitRowButton("Clear Search", ButtonRowRole.Helper), KitRowButton("Clear Filters", ButtonRowRole.Default)));

            return new StackPanel { Spacing = 8, Children = { KitText("State panel — .no-results", "caption", "tertiary"), StatePanelFrame(panel, 420, 260) } };
        }

        //A .compact instance is forced by giving it a region under RegionMinHeight (196pt) - the panel's own MeasureOverride decision.
        private static StackPanel BuildStatePanelLoadingTile() {
            StatePanel full = KitStatePanel(StatePanelShape.Loading, "Loading Factorio data",
                "Reading Space Age — mod 3 of 7.", KitButtonRow(KitRowButton("Cancel", ButtonRowRole.Cancel)), progress: 42);
            StatePanel compact = KitStatePanel(StatePanelShape.Loading, "Solving", "Solving — 62 %",
                KitButtonRow(KitRowButton("Cancel", ButtonRowRole.Cancel)));

            var row = new StackPanel {
                Orientation = Orientation.Horizontal, Spacing = 14,
                Children = { StatePanelFrame(full, 300, 260), StatePanelFrame(compact, 280, 90) },
            };
            return new StackPanel { Spacing = 8, Children = { KitText("State panel — .loading, full & .compact", "caption", "tertiary"), row } };
        }

        private static StackPanel BuildStatePanelErrorTile() {
            StatePanel panel = KitStatePanel(StatePanelShape.Error, "Couldn’t read that save file",
                "The file ends partway through the blueprint section. It may have been copied while the game was writing it.",
                KitButtonRow(KitRowButton("Show in Finder", ButtonRowRole.Other), KitRowButton("Choose Another…", ButtonRowRole.Other), KitRowButton("Try Again", ButtonRowRole.Default)));

            return new StackPanel { Spacing = 8, Children = { KitText("State panel — .error", "caption", "tertiary"), StatePanelFrame(panel, 420, 260) } };
        }

        private static readonly string[] StatePanelScaledSizeKeys = [
            "Foreman.Size.GlyphState", "Foreman.Metrics.StatePanelMaxWidth", "Foreman.Metrics.StatePanelSentenceMaxWidth",
            "Foreman.Type.EmphasizedBody.FontSize", "Foreman.Type.Callout.FontSize",
            "Foreman.Size.Glyph16", "Foreman.Size.GlyphMicro",
            "Foreman.Size.SpinnerLarge", "Foreman.Size.SpinnerSmall", "Foreman.Size.SpinnerStrokeSmall",
            "Foreman.Size.ProgressBarWidth", "Foreman.Size.ProgressBarHeight",
        ];

        private static StackPanel BuildStatePanelScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in StatePanelScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildStatePanelErrorTile());
            return container;
        }

        //Slot delegation (Task 7): the state panel fills the StateContent slot each earlier composite already exposes.
        private static StackPanel BuildChooserGridNoResultsTile() {
            var grid = new ChooserGrid { Theme = KitTheme("Kit.ChooserGrid"), Width = 360, Height = 260 };
            grid.StateContent = KitStatePanel(StatePanelShape.NoResults, "No results for “beacn”",
                "Nothing matches this search.", KitButtonRow(KitRowButton("Clear Search", ButtonRowRole.Default)));

            return new StackPanel { Spacing = 8, Children = { KitText("Chooser grid — no results", "caption", "tertiary"), grid } };
        }

        private static StackPanel BuildSheetFrameErrorTile() {
            var sheet = new SheetFrame {
                Theme = KitTheme("Kit.SheetFrame"),
                Title = "Factory summary",
                Content = new TextBlock { Text = "summary" },
                Buttons = KitButtonRow(KitRowButton("Cancel", ButtonRowRole.Cancel)),
                IsOpen = true,
            };
            sheet.StateContent = KitStatePanel(StatePanelShape.Error, "Couldn’t build the summary",
                "The graph has an unresolved recipe.", KitButtonRow(KitRowButton("Try Again", ButtonRowRole.Default)));

            var mockWindow = new Panel { Width = 420, Height = 260, Children = { sheet } };
            return new StackPanel {
                Spacing = 8,
                Children = { KitText("Sheet frame — error", "caption", "tertiary"), StatePanelFrame(mockWindow, 420, 260) },
            };
        }

        private static StackPanel BuildFormGroupLoadFailureTile() {
            var group = new FormGroup { Theme = KitTheme("Kit.FormGroup"), Title = "Appearance", IsBoxed = true, Width = 340 };
            group.StateContent = KitStatePanel(StatePanelShape.Error, "Couldn’t load this pane",
                "Settings failed to read the current profile.", KitButtonRow(KitRowButton("Try Again", ButtonRowRole.Default)));

            return new StackPanel { Spacing = 8, Children = { KitText("Form group — Settings pane load failure", "caption", "tertiary"), group } };
        }

        //Addendum §02 states row: "Unavailable recipe -> the Recipe group renders with Kit.StatePanel.error in its body."
        //The group's Content is a FormGroup; the state panel fills that body's own StateContent slot (Task 7 delegation).
        private static StackPanel BuildInspectorGroupBodyStateTile() {
            InspectorGroup group = KitInspectorGroup("Recipe", true, []);
            var body = (FormGroup)group.Content!;
            body.StateContent = KitStatePanel(StatePanelShape.Error, "Recipe unavailable",
                "This recipe was removed by a mod update.", KitButtonRow(KitRowButton("Choose Recipe…", ButtonRowRole.Default)));

            var frame = new Border {
                Width = 276,
                Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White),
                BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.BorderStrong", Colors.Black),
                BorderThickness = new Thickness(1),
                Child = group,
            };
            return new StackPanel { Spacing = 8, Children = { KitText("Inspector group — body delegation (State panel)", "caption", "tertiary"), frame } };
        }

        private static ZoomStepper KitZoomStepper(double value = 140) =>
            new() { Theme = KitTheme("Kit.ZoomStepper"), Value = value };

        private static StackPanel ZoomStepperColumn(string caption, ZoomStepper stepper) =>
            new() { Spacing = 6, Children = { KitText(caption, "caption", "tertiary"), stepper } };

        //Addendum §08: the readout swaps its 42pt box for the number, a small spinner mid-solve, or an alert glyph on solve failure - the − and + stay live in every state.
        private static StackPanel BuildZoomStepperTile() {
            ZoomStepper rest = KitZoomStepper();

            ZoomStepper hovered = KitZoomStepper();
            hovered.TemplateApplied += (_, _) =>
                hovered.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Readout").Classes.Add("gallery-hover");

            ZoomStepper solving = KitZoomStepper();
            solving.IsSolving = true;

            ZoomStepper error = KitZoomStepper();
            error.HasSolveError = true;

            var row = new StackPanel {
                Orientation = Orientation.Horizontal, Spacing = 16,
                Children = {
                    ZoomStepperColumn("At rest", rest),
                    ZoomStepperColumn("Readout hovered", hovered),
                    ZoomStepperColumn("Solving", solving),
                    ZoomStepperColumn("Solve failed", error),
                },
            };
            return new StackPanel { Spacing = 8, Children = { KitText("Zoom stepper", "caption", "tertiary"), row } };
        }

        //The stepper owns its error popover (addendum §08): opened here once the template exists, carrying a host-supplied Kit.StatePanel.error, the same slot a real solve failure fills.
        private static Border BuildZoomStepperPopoverTile() {
            ZoomStepper stepper = KitZoomStepper();
            var errorPanel = KitStatePanel(StatePanelShape.Error, "Solve failed",
                "Beacon count must be 0 or more.",
                KitButtonRow(KitRowButton("Fix It", ButtonRowRole.Default)));
            stepper.ErrorPopoverContent = errorPanel;
            stepper.TemplateApplied += (_, _) => {
                stepper.HasSolveError = true;
                stepper.IsErrorPopoverOpen = true;
            };

            var host = new PopoverHost { Width = 320, Height = 160 };
            Avalonia.Controls.Canvas.SetLeft(stepper, 40);
            Avalonia.Controls.Canvas.SetTop(stepper, 20);
            host.Surface.Children.Add(stepper);

            return new Border {
                Width = 320, Height = 160,
                Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White),
                BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.BorderStrong", Colors.Black),
                BorderThickness = new Thickness(1),
                Child = host,
            };
        }

        private static readonly string[] ZoomStepperScaledSizeKeys = [
            "Foreman.Size.ControlHeightRegular", "Foreman.Size.ZoomReadoutWidth", "Foreman.Size.ZoomReadoutHeight",
            "Foreman.Size.ZoomStepperMicroButtonWidth", "Foreman.Size.ZoomStepperMicroButtonHeight",
            "Foreman.Size.GlyphMicro", "Foreman.Size.SpinnerSmall", "Foreman.Size.SpinnerStrokeSmall",
            "Foreman.Type.Caption.FontSize",
        ];

        private static StackPanel BuildZoomStepperScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in ZoomStepperScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(BuildZoomStepperTile());
            return container;
        }

        //Addendum §08: title · groups · flexible gap · search · zoom, collapsing in three measured steps as the row narrows.
        private static ToolbarRow KitToolbarRow(bool searchLoading = false) {
            var row = new ToolbarRow { Theme = KitTheme("Kit.ToolbarRow"), Title = "Untitled graph" };
            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.Document, Items = [KitToolbarItem("New", "New"), KitToolbarItem("Open", "Open"), KitToolbarItem("Save", "Save")] });
            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.Add, Items = [KitToolbarItem("Recipe", "Recipe"), KitToolbarItem("Item", "Item")] });
            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.Arrange, Items = [KitToolbarItem("Align", "Align")] });
            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.ShareView, Items = [KitToolbarItem("Export", "Export"), KitToolbarItem("View", "View")] });
            row.SearchField.IsLoading = searchLoading;
            return row;
        }

        private static StackPanel BuildToolbarRowTile() {
            return new StackPanel {
                Spacing = 12,
                Children = {
                    KitText("Toolbar row — full width", "caption", "tertiary"),
                    ToolbarRowFrame(KitToolbarRow(searchLoading: true), 1400, 52),
                    KitText("Toolbar row — labels dropped, search shrunk", "caption", "tertiary"),
                    ToolbarRowFrame(KitToolbarRow(), 1000, 52),
                    KitText("Toolbar row — overflowed", "caption", "tertiary"),
                    ToolbarRowFrame(KitToolbarRow(), 320, 52),
                },
            };
        }

        private static readonly string[] ToolbarRowScaledSizeKeys = [
            "Foreman.Size.ToolbarRowHeight", "Foreman.Size.ToolbarItemWidth", "Foreman.Size.ToolbarItemHeight", "Foreman.Size.ToolbarItemIconOnlyWidth",
            "Foreman.Type.Legacy.ToolbarItem.Label.FontSize", "Foreman.Size.Glyph17",
            "Foreman.Metrics.SearchFieldNaturalWidth", "Foreman.Metrics.SearchFieldMinWidth",
            "Foreman.Size.ZoomReadoutWidth", "Foreman.Size.ZoomReadoutHeight", "Foreman.Size.ZoomStepperMicroButtonWidth", "Foreman.Size.ZoomStepperMicroButtonHeight",
        ];

        private static StackPanel BuildToolbarRowScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in ToolbarRowScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            container.Children.Add(KitText("Toolbar row (150%)", "caption", "tertiary"));
            container.Children.Add(ToolbarRowFrame(KitToolbarRow(), 1400, 78));
            return container;
        }

        private static readonly PaletteFooterHint[] PaletteFooterHintsFull = [
            new() { KeyText = "↩", KeyAccessibleName = "Return", Description = "Add" },
            new() { KeyText = "⌥↩", KeyAccessibleName = "Option Return", Description = "Add as source" },
            new() { KeyText = "esc", KeyAccessibleName = "Escape", Description = "Close" },
        ];

        private static readonly PaletteFooterHint[] PaletteFooterHintsCloseOnly = [
            new() { KeyText = "esc", KeyAccessibleName = "Escape", Description = "Close" },
        ];

        //Addendum §09's quick-add palette: the top row is selected by default, hosted in a Kit.PopoverFrame in the real app.
        private static PalettePanel KitPalettePanel(PaletteFooterHint[] hints, params (string Name, string Kind)[] results) {
            var panel = new PalettePanel { Theme = KitTheme("Kit.PalettePanel") };
            IBrush swatch = ThemeResources.ResolveBrush("Foreman.Color.Accent", Colors.SteelBlue);
            foreach ((string name, string kind) in results)
                panel.Results.Add(new PaletteResult(name, kind, swatch));
            foreach (PaletteFooterHint hint in hints)
                panel.FooterHints.Add(hint);
            return panel;
        }

        //Hosts a palette panel example in the same PopoverFrame + PopoverHost structure the real quick-add trigger opens against.
        private static Border PalettePopoverTile(PalettePanel panel, double width, double height) {
            var anchor = new Button { Theme = KitTheme("Kit.Button"), Content = "Quick Add" };
            var popover = new PopoverFrame { Theme = KitTheme("Kit.PopoverFrame"), Content = panel, Anchor = anchor };
            anchor.Click += (_, _) => popover.IsOpen = true;

            var host = new PopoverHost { Width = width, Height = height };
            Avalonia.Controls.Canvas.SetLeft(anchor, 24);
            Avalonia.Controls.Canvas.SetTop(anchor, 20);
            host.Surface.Children.Add(anchor);
            host.Surface.Children.Add(popover);
            popover.IsOpen = true;

            return new Border {
                Width = width, Height = height,
                Background = ThemeResources.ResolveBrush("Foreman.Color.WindowBackground", Colors.White),
                BorderBrush = ThemeResources.ResolveBrush("Foreman.Color.BorderStrong", Colors.Black),
                BorderThickness = new Thickness(1),
                Child = host,
            };
        }

        private static StackPanel BuildPalettePanelTile() {
            PalettePanel panel = KitPalettePanel(PaletteFooterHintsFull,
                ("Iron plate", "item"),
                ("Iron plate", "recipe"),
                ("Iron plate (legendary)", "recipe"),
                ("Casting iron plate", "recipe"));
            panel.Search.Text = "iron pl";

            return new StackPanel { Spacing = 8, Children = { KitText("Palette panel", "caption", "tertiary"), PalettePopoverTile(panel, 320, 260) } };
        }

        //Search still loading - the leading glyph has already swapped for the spinner.
        private static StackPanel BuildPalettePanelNoResultsTile() {
            PalettePanel panel = KitPalettePanel(PaletteFooterHintsCloseOnly);
            panel.Search.Text = "beacn";
            panel.Search.IsLoading = true;

            return new StackPanel { Spacing = 8, Children = { KitText("Palette panel — no results", "caption", "tertiary"), PalettePopoverTile(panel, 320, 180) } };
        }

        private static readonly string[] PalettePanelScaledSizeKeys = [
            "Foreman.Size.PaletteWidth", "Foreman.Size.PaletteRowIcon", "Foreman.Size.ListRowHeight",
            "Foreman.Type.Callout.FontSize", "Foreman.Type.Caption.FontSize",
        ];

        private static StackPanel BuildPalettePanelScaledTile() {
            var container = new StackPanel { Spacing = 16 };
            foreach (string key in PalettePanelScaledSizeKeys)
                if (Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    container.Resources[key] = value * 1.5;

            PalettePanel panel = KitPalettePanel(PaletteFooterHintsFull, ("Iron plate", "item"), ("Iron plate", "recipe"));
            panel.Search.Text = "iron pl";

            container.Children.Add(KitText("Palette panel (150%)", "caption", "tertiary"));
            container.Children.Add(PalettePopoverTile(panel, 460, 320));
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

        private static Viewbox KitGlyph(string name, int size, params string[] classes) {
            Application.Current!.TryGetResource($"Kit.Glyph.{name}.{size}", null, out object? geometry);
            var path = new Path { Theme = KitTheme("Kit.Glyph"), Data = geometry as Geometry };
            foreach (string cls in classes)
                path.Classes.Add(cls);

            var box = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both, Child = path };
            IObservable<object?> scaledSize = box.GetResourceObservable($"Foreman.Size.Glyph{size}");
            box.Bind(Viewbox.WidthProperty, scaledSize);
            box.Bind(Viewbox.HeightProperty, scaledSize);
            return box;
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
