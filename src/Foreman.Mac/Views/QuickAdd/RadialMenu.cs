using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Foreman.Mac.Canvas.Panels;
using System;
using System.Collections.Generic;
using AvaloniaCanvas = Avalonia.Controls.Canvas;

namespace Foreman.Mac.Views.QuickAdd {
    public enum RadialChoice { Add, Passthrough, Cancel }

    //Ports the link-drag release chooser (reference §11 "#radial"): three fixed wedges around a hub.
    public sealed class RadialMenu : AvaloniaCanvas, IPanelInitialFocus {
        private const double CenterCoord = 100;
        private const double HubRadius = 36;
        private const double OuterRadius = 92;
        private const double SliceSpanDeg = 120;
        private const int ArcPointsPerSlice = 24;
        private static readonly Point Center = new(CenterCoord, CenterCoord);

        //Start/end angles (degrees, atan2 convention) match the mock's slice boundaries at (20.3,54)/(179.7,54)/(100,192).
        private static readonly (RadialChoice Choice, double StartAngle, double EndAngle, string IconKey, string Label, Point Anchor)[] SliceDefinitions = [
            (RadialChoice.Add, 210, 330, "Foreman.Icon.RadialAdd", "Add…", new Point(100, 36)),
            (RadialChoice.Passthrough, 330, 450, "Foreman.Icon.RadialPassthrough", "Passthrough", new Point(155, 132)),
            (RadialChoice.Cancel, 90, 210, "Foreman.Icon.RadialCancel", "Cancel", new Point(45, 132)),
        ];

        public event EventHandler<RadialChoice>? Chosen;

        private readonly Dictionary<RadialChoice, (Path Slice, Path Icon, TextBlock Label)> slices = [];
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

        public RadialMenu() {
            Width = 200;
            Height = 200;
            Focusable = true;
            Background = Brushes.Transparent; //slices are IsHitTestVisible=false (we resolve hits ourselves); this keeps the whole control hit-testable

            foreach ((RadialChoice choice, double startAngle, double endAngle, string iconKey, string label, Point anchor) in SliceDefinitions) {
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
                    IsHitTestVisible = false,
                };
                var group = new StackPanel { Spacing = 2, IsHitTestVisible = false };
                group.Children.Add(icon);
                group.Children.Add(text);
                SetLeft(group, anchor.X - 20);
                SetTop(group, anchor.Y - 15);
                Children.Add(group);

                slices[choice] = (body, icon, text);
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
            (Path slice, Path icon, TextBlock label) = slices[choice];
            slice.Classes.Set("hover", hovered);
            icon.Classes.Set("hover", hovered);
            label.Classes.Set("hover", hovered);
        }

        //Polar maths, not FillContains/InputHitTest - both are unreliable for arc-based geometry here.
        internal static RadialChoice? ResolveChoice(Point point) {
            double dx = point.X - CenterCoord;
            double dy = point.Y - CenterCoord;
            double radius = Math.Sqrt((dx * dx) + (dy * dy));
            if (radius <= HubRadius || radius > OuterRadius)
                return null;

            double angle = ((Math.Atan2(dy, dx) * 180 / Math.PI) + 360) % 360;
            foreach ((RadialChoice choice, double startAngle, double _, string _, string _, Point _) in SliceDefinitions) {
                double span = (angle - startAngle + 360) % 360;
                if (span < SliceSpanDeg)
                    return choice;
            }
            return null;
        }

        protected override void OnPointerMoved(PointerEventArgs e) {
            base.OnPointerMoved(e);
            HoveredSlice = ResolveChoice(e.GetPosition(this));
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e) {
            base.OnPointerReleased(e);
            if (ResolveChoice(e.GetPosition(this)) is RadialChoice choice) {
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
    }
}
