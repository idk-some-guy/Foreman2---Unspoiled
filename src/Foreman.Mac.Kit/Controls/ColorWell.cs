using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;
using System;
using System.Collections.Generic;

namespace Foreman.Mac.Kit.Controls {
    //A swatch and chevron in one hit target that opens a Kit.ColorPopover; requires a PopoverHost ancestor to open.
    public class ColorWell : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(ColorWell);

        public static readonly StyledProperty<Color> ColorProperty =
            AvaloniaProperty.Register<ColorWell, Color>(nameof(Color), Colors.Black, defaultBindingMode: BindingMode.TwoWay);

        public static readonly StyledProperty<bool> AllowsNoneProperty =
            AvaloniaProperty.Register<ColorWell, bool>(nameof(AllowsNone));

        //Draws partial alpha over the checkerboard; the alpha itself is edited elsewhere.
        public static readonly StyledProperty<bool> AllowsAlphaProperty =
            AvaloniaProperty.Register<ColorWell, bool>(nameof(AllowsAlpha));

        public static readonly StyledProperty<bool> IsOpenProperty =
            AvaloniaProperty.Register<ColorWell, bool>(nameof(IsOpen));

        //Falls back to the automation name a Kit.FormRow gives its control.
        public static readonly StyledProperty<string?> AccessibleLabelProperty =
            AvaloniaProperty.Register<ColorWell, string?>(nameof(AccessibleLabel));

        public Color Color { get => GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
        public bool AllowsNone { get => GetValue(AllowsNoneProperty); set => SetValue(AllowsNoneProperty, value); }
        public bool AllowsAlpha { get => GetValue(AllowsAlphaProperty); set => SetValue(AllowsAlphaProperty, value); }
        public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
        public string? AccessibleLabel { get => GetValue(AccessibleLabelProperty); set => SetValue(AccessibleLabelProperty, value); }

        internal ColorPopover? Popover { get; private set; }
        internal PopoverFrame? Frame { get; private set; }

        private Border? swatch;
        private Border? fill;
        private Border? noneFill;
        private Viewbox? noneBox;
        private SwatchChecker? checker;
        private PopoverHost? host;
        private Color openColor;
        private byte lastAlpha = 255;
        private double swatchInset = 2;
        private IDisposable? insetSub;

        static ColorWell() {
            FocusableProperty.OverrideDefaultValue<ColorWell>(true);
            ColorProperty.Changed.AddClassHandler<ColorWell>((w, _) => w.OnColorChanged());
            AllowsNoneProperty.Changed.AddClassHandler<ColorWell>((w, _) => w.ApplySwatch());
            AllowsAlphaProperty.Changed.AddClassHandler<ColorWell>((w, _) => w.ApplySwatch());
            IsOpenProperty.Changed.AddClassHandler<ColorWell>((w, e) => w.OnIsOpenChanged((bool)e.NewValue!));
        }

        internal string? ResolvedLabel => AccessibleLabel ?? AutomationProperties.GetName(this);

        //Palette name, else "Hex RRGGBB"; "None" at alpha 0; with alpha, the opacity in percent.
        internal string AccessibleValue {
            get {
                if (AllowsNone && Color.A == 0)
                    return "None";
                string name = ColorPopover.NameOf(Color) ?? $"Hex {ColorPopover.ToHex(Color)}";
                return AllowsAlpha ? $"{name}, {Math.Round(Color.A / 2.55)} percent opacity" : name;
            }
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            swatch = e.NameScope.Find<Border>("PART_Swatch");
            fill = e.NameScope.Find<Border>("PART_Fill");
            noneFill = e.NameScope.Find<Border>("PART_NoneFill");
            noneBox = e.NameScope.Find<Viewbox>("PART_NoneBox");
            checker = e.NameScope.Find<SwatchChecker>("PART_Checker");
            PseudoClasses.Set(":open", IsOpen);
            ApplyInset();
            ApplySwatch();
        }

        private void ApplyInset() {
            if (swatch is not null)
                swatch.Margin = new Thickness(swatchInset, swatchInset, 0, swatchInset);
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            insetSub?.Dispose();
            insetSub = null;
            IsOpen = false;
            if (Frame is { } frame) {
                frame.Closed -= OnFrameClosed;
                frame.GotFocus -= OnFrameGotFocus;
                frame.Cancelled -= OnFrameCancelled;
                host?.Remove(frame);
            }
            if (Popover is { } popover)
                popover.CommitRequested -= OnCommitRequested;
            Frame = null;
            Popover = null;
            host = null;
        }

        private void OnColorChanged() {
            if (Color.A > 0)
                lastAlpha = Color.A;
            ApplySwatch();
        }

        private void ApplySwatch() {
            if (fill is null)
                return;
            bool none = AllowsNone && Color.A == 0;
            Color drawn = AllowsAlpha ? Color : Color.FromArgb(255, Color.R, Color.G, Color.B);
            fill.Background = new ImmutableSolidColorBrush(drawn);
            fill.IsVisible = !none;
            if (checker is not null)
                checker.IsVisible = !none && AllowsAlpha && Color.A < 255;
            if (noneFill is not null)
                noneFill.IsVisible = none;
            if (noneBox is not null)
                noneBox.IsVisible = none;
        }

        //Opened before it is attached (a gallery tile), the well opens once it has a host.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            insetSub?.Dispose();
            insetSub = this.GetResourceObservable("Foreman.Size.ColorWellSwatchInset").Subscribe(new ResourceSinks.DoubleSink(v => { swatchInset = v; ApplyInset(); }, swatchInset));
            if (IsOpen && Frame is null)
                OpenPopover();
        }

        private void OnIsOpenChanged(bool open) {
            PseudoClasses.Set(":open", open);
            if (open && VisualRoot is not null)
                OpenPopover();
            else if (Frame is { IsOpen: true } frame)
                frame.IsOpen = false;
        }

        //Built once per host attach; the host defers the Surface add when a layout pass is in flight.
        private void OpenPopover() {
            if (this.FindAncestorOfType<PopoverHost>() is not { } found)
                throw new InvalidOperationException("ColorWell requires a PopoverHost ancestor to open its popover.");
            openColor = Color;
            if (Frame is { } existing && Popover is { } popover) {
                Configure(popover);
                existing.IsOpen = true;
                return;
            }
            if (FindTheme("Kit.PopoverFrame") is not { } frameTheme || FindTheme("Kit.ColorPopover") is not { } popoverTheme) {
                IsOpen = false;
                return;
            }

            var body = new ColorPopover { Theme = popoverTheme };
            Configure(body);
            body[!!ColorPopover.ColorProperty] = this[!!ColorProperty];
            body.CommitRequested += OnCommitRequested;
            var frame = new PopoverFrame { Theme = frameTheme, Anchor = this, Content = body };
            frame.Bind(PopoverFrame.PanelWidthProperty, frame.GetResourceObservable("Foreman.Size.PopoverWidth"));
            frame.Closed += OnFrameClosed;
            frame.GotFocus += OnFrameGotFocus;
            frame.Cancelled += OnFrameCancelled;
            Popover = body;
            Frame = frame;
            host = found;
            found.Add(frame, () => {
                if (!IsOpen)
                    return;
                //Templated before it opens, so the first open runs the kit transition instead of snapping.
                frame.ApplyTemplate();
                frame.IsOpen = true;
            });
        }

        private void Configure(ColorPopover popover) {
            popover.AllowsNone = AllowsNone;
            popover.AllowsAlpha = AllowsAlpha;
            popover.Label = ResolvedLabel;
            popover.RestoreAlpha = lastAlpha;
        }

        private ControlTheme? FindTheme(string key) =>
            this.TryFindResource(key, null, out object? theme) ? theme as ControlTheme : null;

        //The frame focuses itself once it opens; focus moves on to the selected swatch from there.
        private void OnFrameGotFocus(object? sender, FocusChangedEventArgs e) {
            if (ReferenceEquals(e.Source, Frame) && Frame is { IsOpen: true })
                Popover?.FocusSelection();
        }

        private void OnCommitRequested(object? sender, EventArgs e) => Frame?.Close();

        private void OnFrameClosed(object? sender, EventArgs e) => IsOpen = false;

        private void OnFrameCancelled(object? sender, EventArgs e) => Color = openColor;

        protected override void OnPointerPressed(PointerPressedEventArgs e) {
            base.OnPointerPressed(e);
            if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                return;
            PseudoClasses.Set(":pressed", true);
            IsOpen = true;
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            PseudoClasses.Set(":pressed", false);
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) {
            base.OnPointerCaptureLost(e);
            PseudoClasses.Set(":pressed", false);
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (IsOpen || e.Key is not (Key.Space or Key.Enter or Key.Down))
                return;
            IsOpen = true;
            e.Handled = true;
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ColorWellAutomationPeer(this);

        private sealed class ColorWellAutomationPeer(ColorWell owner) : ControlAutomationPeer(owner), IValueProvider, IInvokeProvider {
            private ColorWell Well => (ColorWell)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ComboBox;
            protected override string GetLocalizedControlTypeCore() => "pop-up button";
            protected override string? GetNameCore() => Well.ResolvedLabel;

            protected override string? GetHelpTextCore() {
                string? help = base.GetHelpTextCore();
                return string.IsNullOrEmpty(help) ? "Opens colors" : help;
            }

            bool IValueProvider.IsReadOnly => true;
            string? IValueProvider.Value => Well.AccessibleValue;
            void IValueProvider.SetValue(string? value) { }

            void IInvokeProvider.Invoke() => Well.IsOpen = true;
        }
    }

    //The alternating squares behind a partially transparent swatch.
    public sealed class SwatchChecker : Control {
        private readonly List<IDisposable> subs = [];

        public double SquareSize { get; private set; } = 4;
        internal Color LightColor { get; private set; } = Colors.White;
        internal Color DarkColor { get; private set; } = Color.Parse("#D1D1D6");

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Foreman.Size.SwatchChecker").Subscribe(new ResourceSinks.DoubleSink(v => { SquareSize = v; InvalidateVisual(); }, SquareSize)));
            subs.Add(this.GetResourceObservable("Foreman.Color.SwatchCheckerA").Subscribe(new ResourceSinks.Sink<Color>(v => { LightColor = v; InvalidateVisual(); }, LightColor)));
            subs.Add(this.GetResourceObservable("Foreman.Color.SwatchCheckerB").Subscribe(new ResourceSinks.Sink<Color>(v => { DarkColor = v; InvalidateVisual(); }, DarkColor)));
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        public override void Render(DrawingContext context) {
            base.Render(context);
            Rect bounds = new(Bounds.Size);
            context.FillRectangle(new ImmutableSolidColorBrush(LightColor), bounds);
            if (SquareSize <= 0)
                return;
            var dark = new ImmutableSolidColorBrush(DarkColor);
            for (int row = 0; row * SquareSize < bounds.Height; row++)
                for (int column = row % 2 == 0 ? 1 : 0; column * SquareSize < bounds.Width; column += 2)
                    context.FillRectangle(dark, new Rect(column * SquareSize, row * SquareSize, SquareSize, SquareSize).Intersect(bounds));
        }
    }
}
