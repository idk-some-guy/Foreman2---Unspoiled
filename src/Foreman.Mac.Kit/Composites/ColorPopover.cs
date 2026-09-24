using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    //A palette entry; a null Color is the None cell.
    public readonly record struct ColorSwatch(string Name, Color? Color);

    //The colour well's popover body: a fixed 8 × 4 swatch grid in choice mode above a Hex row.
    public class ColorPopover : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(ColorPopover);

        public const string HexError = "Enter a hex color like 1E90FF.";

        public static readonly StyledProperty<Color> ColorProperty =
            AvaloniaProperty.Register<ColorPopover, Color>(nameof(Color), Colors.Black, defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<bool> AllowsNoneProperty =
            AvaloniaProperty.Register<ColorPopover, bool>(nameof(AllowsNone));

        public static readonly StyledProperty<bool> AllowsAlphaProperty =
            AvaloniaProperty.Register<ColorPopover, bool>(nameof(AllowsAlpha));

        public static readonly StyledProperty<string?> LabelProperty =
            AvaloniaProperty.Register<ColorPopover, string?>(nameof(Label));

        public Color Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
        public bool AllowsNone { get => GetValue(AllowsNoneProperty); set => SetValue(AllowsNoneProperty, value); }
        public bool AllowsAlpha { get => GetValue(AllowsAlphaProperty); set => SetValue(AllowsAlphaProperty, value); }
        public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }

        //Raised when a cell is committed (Return, Space or a click); the host closes the popover.
        public event EventHandler? CommitRequested;

        //The opacity a colour cell brings back when the colour is at None.
        internal byte RestoreAlpha { get; set; } = 255;

        internal PickGrid? Grid { get; private set; }
        internal TextBox? HexField { get; private set; }
        internal FormRow? HexRow { get; private set; }

        private static readonly ColorSwatch[] Palette = [
            new("White", Rgb(0xFFFFFF)), new("Light gray", Rgb(0xD1D1D6)), new("Gray", Rgb(0xAEAEB2)), new("Medium gray", Rgb(0x8E8E93)),
            new("Dark gray", Rgb(0x636366)), new("Charcoal", Rgb(0x48484A)), new("Graphite", Rgb(0x2C2C2E)), new("Black", Rgb(0x000000)),
            new("Red", Rgb(0xFF3B30)), new("Orange", Rgb(0xFF9500)), new("Yellow", Rgb(0xFFCC00)), new("Green", Rgb(0x34C759)),
            new("Teal", Rgb(0x30B0C7)), new("Blue", Rgb(0x007AFF)), new("Indigo", Rgb(0x5856D6)), new("Purple", Rgb(0xAF52DE)),
            new("Light red", Rgb(0xFF9D97)), new("Light orange", Rgb(0xFFCA80)), new("Light yellow", Rgb(0xFFE680)), new("Light green", Rgb(0x9AE3AC)),
            new("Light teal", Rgb(0x98D8E3)), new("Light blue", Rgb(0x80BDFF)), new("Light indigo", Rgb(0xACAAEB)), new("Light purple", Rgb(0xD7A8EF)),
            new("Dark red", Rgb(0xB22921)), new("Dark orange", Rgb(0xB26800)), new("Dark yellow", Rgb(0xB28F00)), new("Dark green", Rgb(0x248B3E)),
            new("Dark teal", Rgb(0x227B8B)), new("Dark blue", Rgb(0x0055B2)), new("Dark indigo", Rgb(0x3E3C96)), new("Dark purple", Rgb(0x7B399B)),
        ];

        private static readonly ColorSwatch[] PaletteWithNone =
            [new("None", null), .. Palette.Where(s => s.Name != "Charcoal")];

        private IReadOnlyList<ColorSwatch> swatches = Palette;
        private IDisposable? gapSub;
        private Thickness? cellGap;
        private bool focusPending;

        static ColorPopover() {
            ColorProperty.Changed.AddClassHandler<ColorPopover>((p, _) => p.SyncFromColor());
            AllowsNoneProperty.Changed.AddClassHandler<ColorPopover>((p, _) => p.RebuildSlots());
            LabelProperty.Changed.AddClassHandler<ColorPopover>((p, _) => p.ApplyLabel());
        }

        public ColorPopover() {
            AddHandler(KeyDownEvent, OnTabKey, RoutingStrategies.Tunnel);
        }

        public static IReadOnlyList<ColorSwatch> Swatches(bool allowsNone) => allowsNone ? PaletteWithNone : Palette;

        //Matched on RGB across the full palette, Charcoal included.
        public static string? NameOf(Color color) =>
            Palette.FirstOrDefault(s => SameRgb(s.Color!.Value, color)).Name;

        public static string ToHex(Color color) => string.Create(CultureInfo.InvariantCulture, $"{color.R:X2}{color.G:X2}{color.B:X2}");

        //Three or six hex digits in any case, with or without a leading "#".
        public static bool TryParseHex(string? text, out Color rgb) {
            rgb = default;
            string digits = (text ?? "").Trim().TrimStart('#');
            if (digits.Length == 3)
                digits = string.Concat(digits.Select(c => $"{c}{c}"));
            if (digits.Length != 6 || !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint value))
                return false;
            rgb = Rgb(value);
            return true;
        }

        private static Color Rgb(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

        private static bool SameRgb(Color a, Color b) => a.R == b.R && a.G == b.G && a.B == b.B;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            if (Grid is not null) {
                Grid.ChooseRequested -= OnChooseRequested;
                Grid.RemoveHandler(KeyDownEvent, OnGridKeyDown);
                Grid.RemoveHandler(TextInputEvent, OnGridTextInput);
            }
            HexField?.RemoveHandler(KeyDownEvent, OnHexKeyDown);

            Grid = e.NameScope.Find<PickGrid>("PART_Grid");
            HexField = e.NameScope.Find<TextBox>("PART_Hex");
            HexRow = e.NameScope.Find<FormRow>("PART_HexRow");

            if (Grid is not null) {
                Grid.ChooseRequested += OnChooseRequested;
                Grid.AddHandler(KeyDownEvent, OnGridKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
                Grid.AddHandler(TextInputEvent, OnGridTextInput, RoutingStrategies.Bubble, handledEventsToo: true);
            }
            HexField?.AddHandler(KeyDownEvent, OnHexKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
            ApplyCellGap();
            RebuildSlots();
            ApplyLabel();
        }

        //The palette's 4 pt gap is the kit's 2 pt half-gap on each side of a cell.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            gapSub?.Dispose();
            gapSub = this.GetResourceObservable("Foreman.Thickness.Space05").Subscribe(new ResourceSinks.ThicknessSink(v => {
                cellGap = v;
                ApplyCellGap();
            }, default));
        }

        private void ApplyCellGap() {
            if (Grid is not null && cellGap is Thickness gap)
                Grid.Resources["Foreman.Thickness.PickCellGap"] = gap;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            gapSub?.Dispose();
            gapSub = null;
        }

        //Before the first layout the grid has no cells to focus; the focus waits for Loaded.
        internal void FocusSelection() {
            focusPending = !(Grid is { IsEffectivelyVisible: true } grid && grid.Focus(NavigationMethod.Tab));
        }

        protected override void OnLoaded(RoutedEventArgs e) {
            base.OnLoaded(e);
            if (focusPending)
                FocusSelection();
        }

        private void ApplyLabel() {
            string title = $"{Label} colors";
            AutomationProperties.SetName(this, title);
            if (Grid is not null)
                Grid.GroupLabel = title;
        }

        private void RebuildSlots() {
            swatches = Swatches(AllowsNone);
            if (Grid is null)
                return;
            Grid.Slots.Clear();
            foreach (ColorSwatch swatch in swatches)
                Grid.Slots.Add(new PickSlot { Name = swatch.Name, Swatch = swatch.Color, IsNone = swatch.Color is null });
            SyncFromColor();
        }

        private int IndexOf(Color color) {
            bool none = AllowsNone && color.A == 0;
            for (int i = 0; i < swatches.Count; i++) {
                Color? swatch = swatches[i].Color;
                if (none ? swatch is null : swatch is Color rgb && SameRgb(rgb, color))
                    return i;
            }
            return -1;
        }

        private void SyncFromColor() {
            if (Grid is null)
                return;
            Grid.SelectedIndex = IndexOf(Color);
            if (HexField is not null)
                HexField.Text = ToHex(Color);
            if (HexRow is not null)
                HexRow.Error = null;
        }

        //A colour cell keeps the current opacity, or brings back RestoreAlpha from None; without alpha it is opaque.
        private Color Resolve(Color? rgb) {
            if (rgb is not Color chosen)
                return Color.FromArgb(0, Color.R, Color.G, Color.B);
            byte alpha = !AllowsAlpha ? (byte)255 : Color.A > 0 ? Color.A : RestoreAlpha;
            return Color.FromArgb(alpha, chosen.R, chosen.G, chosen.B);
        }

        private void Apply(int index) {
            if (index < 0 || index >= swatches.Count || Grid is null)
                return;
            Color = Resolve(swatches[index].Color);
            Grid.SelectedIndex = index;
        }

        private void OnChooseRequested(object? sender, int index) {
            Apply(index);
            CommitRequested?.Invoke(this, EventArgs.Empty);
        }

        //Selection follows focus: an arrow or type-to-select move previews the colour it lands on.
        private void OnGridKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)
                PreviewFocused();
        }

        private void OnGridTextInput(object? sender, TextInputEventArgs e) => PreviewFocused();

        private void PreviewFocused() {
            if (Grid is { FocusedIndex: >= 0 } grid && grid.FocusedIndex != grid.SelectedIndex)
                Apply(grid.FocusedIndex);
        }

        private void OnHexKeyDown(object? sender, KeyEventArgs e) {
            if (e.Key != Key.Enter)
                return;
            CommitHex();
            e.Handled = true;
        }

        private bool CommitHex() {
            if (HexField is null || HexRow is null)
                return false;
            if (!TryParseHex(HexField.Text, out Color rgb)) {
                HexRow.Error = HexError;
                return false;
            }
            HexRow.Error = null;
            Color = Resolve(rgb);
            HexField.Text = ToHex(Color);
            return true;
        }

        //Tab alternates between the grid and the Hex field and never leaves the popover; leaving an edited field commits it.
        private void OnTabKey(object? sender, KeyEventArgs e) {
            if (e.Key != Key.Tab || Grid is null || HexField is null)
                return;
            e.Handled = true;
            if (e.Source is Visual source && (ReferenceEquals(source, HexField) || HexField.IsVisualAncestorOf(source))) {
                if (!string.Equals(HexField.Text, ToHex(Color), StringComparison.Ordinal) && !CommitHex())
                    return;
                Grid.Focus(NavigationMethod.Tab);
            } else {
                HexField.Focus(NavigationMethod.Tab);
                HexField.SelectAll();
            }
        }
    }
}
