using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Foreman.Mac.Canvas.Panels;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using System;
using System.Collections.Generic;
using AvaloniaCanvas = Avalonia.Controls.Canvas;

namespace Foreman.Mac.Views.QuickAdd {
    public enum RadialChoice { Add, Source, Output, Passthrough, Spoil, Unspoil, Plant, Unplant, Cancel }

    //Ports the link-drag release chooser (reference §11 "#radial"); caller-picked N slices, Add centred at top, Cancel last.
    public sealed class RadialMenu : AvaloniaCanvas, IPanelInitialFocus, IPanelClosingTransition {
        private const double CenterCoord = 130;
        private const double HubRadius = 36;
        private const double OuterRadius = 92;
        private const double LabelRadius = 66;
        private const double LabelWidth = 72;
        private const int ArcPointsPerSlice = 24;
        private static readonly Point Center = new(CenterCoord, CenterCoord);

        private static readonly Dictionary<RadialChoice, (string IconKey, string Label)> Meta = new() {
            [RadialChoice.Add] = ("Foreman.Icon.RadialAdd", "Add…"),
            [RadialChoice.Source] = ("Foreman.Icon.RadialSource", "Source"),
            [RadialChoice.Output] = ("Foreman.Icon.RadialOutput", "Output"),
            [RadialChoice.Passthrough] = ("Foreman.Icon.RadialPassthrough", "Passthrough"),
            [RadialChoice.Spoil] = ("Foreman.Icon.RadialSpoil", "Spoil"),
            [RadialChoice.Unspoil] = ("Foreman.Icon.RadialUnspoil", "UnSpoil"),
            [RadialChoice.Plant] = ("Foreman.Icon.RadialPlant", "Plant"),
            [RadialChoice.Unplant] = ("Foreman.Icon.RadialUnplant", "UnPlant"),
            [RadialChoice.Cancel] = ("Foreman.Icon.RadialCancel", "Cancel"),
        };

        public event EventHandler<RadialChoice>? Chosen;

        public IReadOnlyList<RadialChoice> Slices { get; }

        private readonly Dictionary<RadialChoice, (Path Slice, Path Icon, TextBlock Label)> sliceControls = [];
        private RadialChoice? hoveredSlice;

        //Test seam, also drives the .hover visuals below.
        public RadialChoice? HoveredSlice {
            get => hoveredSlice;
            private set {
                if (hoveredSlice == value)
                    return;
                if (hoveredSlice is RadialChoice previous)
                    SetHovered(previous, false);
                hoveredSlice = value;
                if (hoveredSlice is RadialChoice current)
                    SetHovered(current, true);
            }
        }

        public RadialMenu(IReadOnlyList<RadialChoice> choices) {
            Slices = choices;
            Width = 260;
            Height = 260;
            Focusable = true;
            Background = Brushes.Transparent; //slices are IsHitTestVisible=false (we resolve hits ourselves); this keeps the whole control hit-testable

            //Opens with a scale+fade in, carried by the Transitions set here to Opacity 1/scale 1 once
            //attached - see AttachedToVisualTree below.
            Opacity = 0;
            RenderTransform = new ScaleTransform(Motion.RadialOpenScale, Motion.RadialOpenScale);
            Transitions = Motion.Transition(TransitionKind.Scale, Motion.RadialOpenDuration, Motion.RadialOpenEasing);
            AttachedToVisualTree += (_, _) => {
                Opacity = 1;
                RenderTransform = new ScaleTransform(1, 1);
            };
            ThemeAware.Attach(this, () => Transitions = Motion.Transition(TransitionKind.Scale, Motion.RadialOpenDuration, Motion.RadialOpenEasing));

            for (int i = 0; i < choices.Count; i++) {
                RadialChoice choice = choices[i];
                (double startAngle, double endAngle) = SliceAngles(i, choices.Count);
                (string iconKey, string label) = Meta[choice];

                var body = new Path { Data = BuildSliceGeometry(startAngle, endAngle), IsHitTestVisible = false, Classes = { "foreman-radial-slice" } };
                Children.Add(body);

                var icon = new Path {
                    Data = ResolveIconGeometry(iconKey),
                    Width = 19,
                    Height = 19,
                    Stretch = Stretch.Uniform,
                    Classes = { "foreman-radial-icon" },
                    IsHitTestVisible = false,
                };
                var text = new TextBlock {
                    Text = label,
                    Classes = { "foreman-radial-label" },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = LabelWidth,
                    IsHitTestVisible = false,
                };
                var group = new StackPanel { Spacing = 2, Width = LabelWidth, IsHitTestVisible = false };
                group.Children.Add(icon);
                group.Children.Add(text);

                Point anchor = PointOnCircle(LabelRadius, (startAngle + endAngle) / 2);
                SetLeft(group, anchor.X - (LabelWidth / 2));
                SetTop(group, anchor.Y - 15);
                Children.Add(group);

                sliceControls[choice] = (body, icon, text);
            }

            var hub = new Ellipse { Width = HubRadius * 2, Height = HubRadius * 2, IsHitTestVisible = false, Classes = { "foreman-radial-hub" } };
            SetLeft(hub, CenterCoord - HubRadius);
            SetTop(hub, CenterCoord - HubRadius);
            Children.Add(hub);

            var cursorDot = new Ellipse { Width = 8, Height = 8, IsHitTestVisible = false, Classes = { "foreman-radial-cursor" } };
            SetLeft(cursorDot, CenterCoord - 4);
            SetTop(cursorDot, CenterCoord - 4);
            Children.Add(cursorDot);
        }

        //Index 0 (Add) centred at the top (270°); each following index continues clockwise by one span.
        private static (double Start, double End) SliceAngles(int index, int count) {
            double span = 360.0 / count;
            double mid = 270 + (index * span);
            return (mid - (span / 2), mid + (span / 2));
        }

        //Line segments only, no ArcSegment - Avalonia 12.1.2 renders/hit-tests arcs as flat chords.
        private static StreamGeometry BuildSliceGeometry(double startAngle, double endAngle) {
            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open()) {
                ctx.BeginFigure(PointOnCircle(HubRadius, startAngle), isFilled: true);
                for (int i = 0; i <= ArcPointsPerSlice; i++)
                    ctx.LineTo(PointOnCircle(OuterRadius, AngleAt(startAngle, endAngle, i)), isStroked: true);
                for (int i = ArcPointsPerSlice; i >= 0; i--)
                    ctx.LineTo(PointOnCircle(HubRadius, AngleAt(startAngle, endAngle, i)), isStroked: true);
                ctx.EndFigure(isClosed: true);
            }
            return geometry;
        }

        private static double AngleAt(double startAngle, double endAngle, int step) =>
            startAngle + ((endAngle - startAngle) * step / ArcPointsPerSlice);

        private static Point PointOnCircle(double radius, double angleDeg) {
            double angleRad = angleDeg * Math.PI / 180;
            return new Point(Center.X + (radius * Math.Cos(angleRad)), Center.Y + (radius * Math.Sin(angleRad)));
        }

        private static Geometry? ResolveIconGeometry(string key) {
            Application.Current!.TryGetResource(key, ThemeVariant.Light, out object? resource);
            return resource as Geometry;
        }

        private void SetHovered(RadialChoice choice, bool hovered) {
            (Path slice, Path icon, TextBlock label) = sliceControls[choice];
            slice.Classes.Set("hover", hovered);
            icon.Classes.Set("hover", hovered);
            label.Classes.Set("hover", hovered);
        }

        //Polar maths, not FillContains/InputHitTest - both are unreliable for arc-based geometry here. Pure
        //function of the caller's own slice list, so a plain [Fact] can exercise it without an Application.
        internal static RadialChoice? ResolveChoice(Point point, IReadOnlyList<RadialChoice> choices) {
            double radius = RadiusFromCenter(point);
            if (radius <= HubRadius || radius > OuterRadius)
                return null;

            double dx = point.X - CenterCoord;
            double dy = point.Y - CenterCoord;
            double angle = ((Math.Atan2(dy, dx) * 180 / Math.PI) + 360) % 360;
            double span = 360.0 / choices.Count;
            for (int i = 0; i < choices.Count; i++) {
                (double startAngle, _) = SliceAngles(i, choices.Count);
                double normalizedStart = ((startAngle % 360) + 360) % 360;
                double offset = (angle - normalizedStart + 360) % 360;
                if (offset < span)
                    return choices[i];
            }
            return null;
        }

        //A press anywhere past the ring - inside the 260x260 square or outside it - should cancel; only the
        //hub (radius <= HubRadius) is a no-op.
        internal static bool IsOutsideOuterRadius(Point point) => RadiusFromCenter(point) > OuterRadius;

        private static double RadiusFromCenter(Point point) {
            double dx = point.X - CenterCoord;
            double dy = point.Y - CenterCoord;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            HoveredSlice = ResolveChoice(e.GetPosition(this), Slices);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (ResolveChoice(e.GetPosition(this), Slices) is RadialChoice choice) {
                e.Handled = true;
                Chosen?.Invoke(this, choice);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape) {
                e.Handled = true;
                Chosen?.Invoke(this, RadialChoice.Cancel);
            }
        }

        public void FocusInitialControl() => Focus();

        public TimeSpan ClosingDuration => TimeSpan.FromMilliseconds(80);

        public void BeginClosing() {
            Transitions = Motion.Transition(TransitionKind.Fade, Motion.RadialCloseDuration, Motion.LeavingEasing);
            Classes.Add("closing");
        }
    }
}
