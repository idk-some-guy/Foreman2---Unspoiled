using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    //A slice arms when the pointer crosses the hub radius into its wedge and stays armed until it crosses out again; arrows/diagonals arm the same way, any other key opens a context menu of the same six items (A24). Fixed 220pt across every ScaleContract step.
    public class RadialRing : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(RadialRing);

        public static readonly StyledProperty<int> ArmedIndexProperty =
            AvaloniaProperty.Register<RadialRing, int>(nameof(ArmedIndex), -1);

        public static readonly StyledProperty<bool> IsOpenProperty =
            AvaloniaProperty.Register<RadialRing, bool>(nameof(IsOpen));

        public static readonly DirectProperty<RadialRing, bool> CanOpenProperty =
            AvaloniaProperty.RegisterDirect<RadialRing, bool>(nameof(CanOpen), o => o.canOpen);

        public int ArmedIndex { get => GetValue(ArmedIndexProperty); set => SetValue(ArmedIndexProperty, value); }
        public bool IsOpen { get => GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }

        //A25: a link drag with fewer than two outcomes must never open a ring with disabled slices.
        public bool CanOpen => canOpen;

        public AvaloniaList<RadialItem> Items { get; } = [];

        public event EventHandler<int>? Committed;
        public event EventHandler? Dismissed;
        public event EventHandler<IReadOnlyList<RadialItem>>? ContextMenuRequested;

        private bool canOpen;
        private Canvas? root;
        private readonly List<Path> sliceVisuals = [];
        private readonly List<(Path Icon, TextBlock Label)> iconVisuals = [];
        private readonly List<StackPanel> cellVisuals = [];
        private Ellipse? cursorDot;
        private Ellipse? cursorHalo;
        private readonly List<IDisposable> tokenSubs = [];
        private readonly List<IDisposable> glyphSubs = [];

        private double outerRadius = 100;
        private double hubRadius = 38;
        private double diameter = 220;
        private double labelFontSize = 11;
        private FontWeight labelWeight = FontWeight.Normal;
        private IBrush sliceFill = Brushes.Transparent;
        private IBrush border = Brushes.Transparent;
        private IBrush wellBackground = Brushes.Transparent;
        private IBrush accentSoft = Brushes.Transparent;
        private IBrush accent = Brushes.Transparent;
        private IBrush accentText = Brushes.Transparent;
        private IBrush textSecondary = Brushes.Transparent;

        private const double CursorDotDiameter = 8;
        private const double CursorHaloDiameter = 14;
        private const double IconMasterSize = 17;

        static RadialRing() {
            FocusableProperty.OverrideDefaultValue<RadialRing>(true);
            ArmedIndexProperty.Changed.AddClassHandler<RadialRing>((r, _) => r.UpdateArmedVisuals());
            IsOpenProperty.Changed.AddClassHandler<RadialRing>((r, e) => r.ApplyOpenState((bool)e.NewValue!));
        }

        public RadialRing() {
            IsHitTestVisible = IsOpen;
            Items.CollectionChanged += OnItemsChanged;
        }

        private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            RecomputeCanOpen();
            RebuildVisuals();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            root = e.NameScope.Find<Canvas>("PART_Root");
            RebuildVisuals();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        private void Subscribe() {
            Unsubscribe();
            tokenSubs.Add(this.GetResourceObservable("Foreman.Size.RadialOuter").Subscribe(new ResourceSinks.DoubleSink(v => { outerRadius = v; RebuildVisuals(); }, outerRadius)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Size.RadialHub").Subscribe(new ResourceSinks.DoubleSink(v => { hubRadius = v; RebuildVisuals(); }, hubRadius)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Size.RadialDiameter").Subscribe(new ResourceSinks.DoubleSink(v => { diameter = v; RebuildVisuals(); }, diameter)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Type.RadialLabel.FontSize").Subscribe(new ResourceSinks.DoubleSink(v => { labelFontSize = v; RebuildVisuals(); }, labelFontSize)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Type.RadialLabel.FontWeight").Subscribe(new ResourceSinks.WeightSink(v => { labelWeight = v; RebuildVisuals(); }, labelWeight)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Color.PopoverMaterial").Subscribe(new ResourceSinks.BrushSink(v => { sliceFill = v; RebuildVisuals(); }, sliceFill)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Color.BorderStrong").Subscribe(new ResourceSinks.BrushSink(v => { border = v; RebuildVisuals(); }, border)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Color.WellBackground").Subscribe(new ResourceSinks.BrushSink(v => { wellBackground = v; RebuildVisuals(); }, wellBackground)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Color.AccentSoft").Subscribe(new ResourceSinks.BrushSink(v => { accentSoft = v; UpdateArmedVisuals(); }, accentSoft)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Color.Accent").Subscribe(new ResourceSinks.BrushSink(v => { accent = v; UpdateArmedVisuals(); }, accent)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Color.AccentText").Subscribe(new ResourceSinks.BrushSink(v => { accentText = v; UpdateArmedVisuals(); }, accentText)));
            tokenSubs.Add(this.GetResourceObservable("Foreman.Color.TextSecondary").Subscribe(new ResourceSinks.BrushSink(v => { textSecondary = v; UpdateArmedVisuals(); }, textSecondary)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in tokenSubs)
                sub.Dispose();
            tokenSubs.Clear();
            foreach (IDisposable sub in glyphSubs)
                sub.Dispose();
            glyphSubs.Clear();
        }

        private void RecomputeCanOpen() => SetAndRaise(CanOpenProperty, ref canOpen, Items.Count(i => i.IsEnabled) >= 2);

        //A fixed 10pt margin beyond the 100pt outer radius, not derived from it - matches the mock's own 0..220 viewBox regardless of the radii tokens.
        private double Center => diameter / 2;

        private void RebuildVisuals() {
            if (root is null)
                return;

            Width = diameter;
            Height = diameter;
            root.Width = diameter;
            root.Height = diameter;
            root.Children.Clear();
            sliceVisuals.Clear();
            iconVisuals.Clear();
            cellVisuals.Clear();
            foreach (IDisposable sub in glyphSubs)
                sub.Dispose();
            glyphSubs.Clear();

            for (int i = 0; i < RadialGeometry.SliceCount; i++) {
                var slice = new Path { Data = BuildSliceGeometry(i, Center), IsHitTestVisible = false };
                root.Children.Add(slice);
                sliceVisuals.Add(slice);
            }

            var hub = new Ellipse { Width = hubRadius * 2, Height = hubRadius * 2, Fill = wellBackground, IsHitTestVisible = false };
            Canvas.SetLeft(hub, Center - hubRadius);
            Canvas.SetTop(hub, Center - hubRadius);
            root.Children.Add(hub);

            const double iconCellWidth = 44;
            const double iconCellHeight = 32;
            for (int i = 0; i < RadialGeometry.SliceCount; i++) {
                RadialItem? item = i < Items.Count ? Items[i] : null;

                //Design box: a Viewbox around a fixed 17pt master Path with Stretch=None, never Stretch=Uniform - Uniform would fit the geometry's own bounds and inflate the glyph.
                var icon = new Path {
                    Width = IconMasterSize, Height = IconMasterSize,
                    Stretch = Stretch.None,
                    Stroke = textSecondary,
                    StrokeThickness = 1.5,
                    StrokeLineCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                };
                var iconBox = new Viewbox { Width = IconMasterSize, Height = IconMasterSize, Stretch = Stretch.Uniform, Child = icon };
                if (!string.IsNullOrEmpty(item?.GlyphKey)) {
                    glyphSubs.Add(this.GetResourceObservable($"Kit.Glyph.{item!.GlyphKey}.17")
                        .Subscribe(new ResourceSinks.Sink<Geometry?>(g => icon.Data = g, null)));
                }

                var label = new TextBlock {
                    Text = item?.Label,
                    FontSize = labelFontSize,
                    FontWeight = labelWeight,
                    Foreground = textSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    IsHitTestVisible = false,
                };
                var cell = new StackPanel {
                    Width = iconCellWidth,
                    Height = iconCellHeight,
                    Spacing = 2,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { iconBox, label },
                };
                AutomationProperties.SetName(cell, item?.Label);

                Point center = RadialGeometry.PointOnCircle(69, RadialGeometry.SliceCenter(i)) + new Point(Center, Center);
                Canvas.SetLeft(cell, center.X - (iconCellWidth / 2));
                Canvas.SetTop(cell, center.Y - (iconCellHeight / 2));
                root.Children.Add(cell);
                iconVisuals.Add((icon, label));
                cellVisuals.Add(cell);
            }

            cursorHalo = new Ellipse { Width = CursorHaloDiameter, Height = CursorHaloDiameter, Stroke = accentSoft, StrokeThickness = 3, IsHitTestVisible = false, IsVisible = false };
            cursorDot = new Ellipse { Width = CursorDotDiameter, Height = CursorDotDiameter, Fill = accent, IsHitTestVisible = false, IsVisible = false };
            root.Children.Add(cursorHalo);
            root.Children.Add(cursorDot);

            UpdateArmedVisuals();
            if (IsOpen)
                ApplySweep(true);
        }

        private StreamGeometry BuildSliceGeometry(int index, double center) {
            double start = RadialGeometry.SliceStart(index);
            double end = start + RadialGeometry.SliceSpan;
            Point At(double radius, double angle) => RadialGeometry.PointOnCircle(radius, angle) + new Point(center, center);

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open()) {
                ctx.BeginFigure(At(hubRadius, start), true);
                ctx.LineTo(At(outerRadius, start), true);
                ctx.ArcTo(At(outerRadius, end), new Size(outerRadius, outerRadius), 0, false, SweepDirection.Clockwise, true);
                ctx.LineTo(At(hubRadius, end), true);
                ctx.ArcTo(At(hubRadius, start), new Size(hubRadius, hubRadius), 0, false, SweepDirection.CounterClockwise, true);
                ctx.EndFigure(true);
            }
            return geometry;
        }

        //Colour and stroke weight change together, so the armed slice survives Increase Contrast (A18).
        private void UpdateArmedVisuals() {
            for (int i = 0; i < sliceVisuals.Count; i++) {
                bool armed = i == ArmedIndex;
                sliceVisuals[i].Fill = armed ? accentSoft : sliceFill;
                sliceVisuals[i].Stroke = armed ? accent : border;
                sliceVisuals[i].StrokeThickness = armed ? 2 : 1;

                if (i >= iconVisuals.Count)
                    continue;
                (Path icon, TextBlock label) = iconVisuals[i];
                icon.Stroke = armed ? accent : textSecondary;
                label.Foreground = armed ? accentText : textSecondary;
            }

            if (cursorDot is not null)
                cursorDot.Fill = accent;
            if (cursorHalo is not null)
                cursorHalo.Stroke = accentSoft;
        }

        private void ApplyOpenState(bool open) {
            if (open) {
                ArmedIndex = -1;
                if (cursorDot is not null)
                    cursorDot.IsVisible = false;
                if (cursorHalo is not null)
                    cursorHalo.IsVisible = false;
            }
            TimeSpan duration = open ? Motion.RadialOpenDuration : Motion.RadialCloseDuration;
            Avalonia.Animation.Easings.Easing easing = open ? Motion.RadialOpenEasing : Motion.LeavingEasing;
            Transitions = Motion.Transition(TransitionKind.Scale, duration, easing);
            ApplySweep(open);

            PseudoClasses.Set(":open", open);
            IsHitTestVisible = open;
            if (open)
                Focus();
        }

        //Slices wipe in clockwise over RadialSweepDuration, staggered by index; Reduce Motion collapses every slice to the same opacity-only fade at zero delay.
        private void ApplySweep(bool open) {
            if (!open)
                return;
            for (int i = 0; i < sliceVisuals.Count; i++) {
                Path slice = sliceVisuals[i];
                TimeSpan delay = Motion.ReduceMotion
                    ? TimeSpan.Zero
                    : TimeSpan.FromMilliseconds(Motion.RadialSweepDuration.TotalMilliseconds / RadialGeometry.SliceCount * i);
                slice.Opacity = 0;
                slice.Transitions = Motion.Transition(TransitionKind.Fade, Motion.RadialSweepDuration, Motion.RadialOpenEasing, delay);
                slice.Opacity = 1;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            if (!IsOpen)
                return;
            Point position = e.GetPosition(this);
            UpdateCursorPosition(position);
            Arm(position);
        }

        //The cursor dot and its halo are the only visuals this touches - everything else keeps the position RebuildVisuals gave it.
        private void UpdateCursorPosition(Point position) {
            if (cursorDot is null || cursorHalo is null)
                return;
            cursorDot.IsVisible = true;
            cursorHalo.IsVisible = true;
            Canvas.SetLeft(cursorDot, position.X - (CursorDotDiameter / 2));
            Canvas.SetTop(cursorDot, position.Y - (CursorDotDiameter / 2));
            Canvas.SetLeft(cursorHalo, position.X - (CursorHaloDiameter / 2));
            Canvas.SetTop(cursorHalo, position.Y - (CursorHaloDiameter / 2));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (!IsOpen)
                return;
            e.Handled = true;
            CommitOrDismiss();
        }

        //Tracks direction continuously once past the hub, wedge to wedge; re-entering the hub clears the arm.
        private void Arm(Point position) {
            Point vector = position - new Point(Center, Center);
            double radius = RadialGeometry.Length(vector);
            if (radius < hubRadius) {
                ArmedIndex = -1;
                return;
            }
            int index = RadialGeometry.SliceForAngle(RadialGeometry.AngleOf(vector));
            if (index < Items.Count && Items[index].IsEnabled)
                ArmedIndex = index;
        }

        private void CommitOrDismiss() {
            if (ArmedIndex >= 0 && ArmedIndex < Items.Count && Items[ArmedIndex].IsEnabled) {
                Items[ArmedIndex].Command?.Execute(null);
                Committed?.Invoke(this, ArmedIndex);
            } else {
                Dismissed?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (!IsOpen)
                return;

            if (KeyAngle(e.Key) is double angle) {
                int index = RadialGeometry.SliceForAngle(angle);
                if (index < Items.Count && Items[index].IsEnabled)
                    ArmedIndex = index;
                e.Handled = true;
                return;
            }

            e.Handled = true;
            if (e.Key == Key.Return) {
                CommitOrDismiss();
                return;
            }

            //Escape and any other key dismiss and reopen the same six items as a context menu - A24's accessible fallback since a direction gesture has no accessible equivalent.
            Dismissed?.Invoke(this, EventArgs.Empty);
            ContextMenuRequested?.Invoke(this, [.. Items]);
        }

        //Numpad follows a physical keypad's compass layout (8=N, 6=E, 2=S, 4=W, 7/9/1/3 the diagonals); arrow keys cover the four cardinals the same way.
        private static double? KeyAngle(Key key) => key switch {
            Key.Up or Key.NumPad8 => 270,
            Key.Right or Key.NumPad6 => 0,
            Key.Down or Key.NumPad2 => 90,
            Key.Left or Key.NumPad4 => 180,
            Key.NumPad9 => 315,
            Key.NumPad3 => 45,
            Key.NumPad1 => 135,
            Key.NumPad7 => 225,
            _ => null,
        };

        protected override AutomationPeer OnCreateAutomationPeer() => new RadialRingAutomationPeer(this);

        //One child peer per wedge cell in the clockwise build order - the default control walk does not surface plain StackPanels as automation children.
        private sealed class RadialRingAutomationPeer(RadialRing owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
            protected override string? GetNameCore() => "Actions";
            protected override IReadOnlyList<AutomationPeer> GetChildrenCore() =>
                [.. owner.cellVisuals.Select(ControlAutomationPeer.CreatePeerForElement)];
        }
    }
}
