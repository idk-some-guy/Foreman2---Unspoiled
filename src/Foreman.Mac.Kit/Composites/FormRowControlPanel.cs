using Avalonia;
using Avalonia.Controls;
using System;

namespace Foreman.Mac.Kit.Composites {
    //A form row's control, then its unit Gap after it: a filling control runs to the trailing edge less the unit, any other keeps its desired width.
    public sealed class FormRowControlPanel : Panel {
        public static readonly StyledProperty<bool> FillProperty =
            AvaloniaProperty.Register<FormRowControlPanel, bool>(nameof(Fill));

        public static readonly StyledProperty<double> GapProperty =
            AvaloniaProperty.Register<FormRowControlPanel, double>(nameof(Gap), 8);

        static FormRowControlPanel() => AffectsMeasure<FormRowControlPanel>(FillProperty, GapProperty);

        public bool Fill {
            get => GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public double Gap {
            get => GetValue(GapProperty);
            set => SetValue(GapProperty, value);
        }

        private Control? Host => Children.Count > 0 ? Children[0] : null;
        private Control? Unit => Children.Count > 1 ? Children[1] : null;

        private double UnitSpan => Unit is { IsVisible: true } unit ? unit.DesiredSize.Width + Gap : 0;

        protected override Size MeasureOverride(Size availableSize) {
            Unit?.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            double unitSpan = UnitSpan;
            Control? host = Host;
            host?.Measure(new Size(Math.Max(0, availableSize.Width - unitSpan), availableSize.Height));
            double hostWidth = host?.DesiredSize.Width ?? 0;
            double width = Fill && !double.IsInfinity(availableSize.Width) ? availableSize.Width : hostWidth + unitSpan;
            double height = Math.Max(host?.DesiredSize.Height ?? 0, Unit is { IsVisible: true } unit ? unit.DesiredSize.Height : 0);
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize) {
            double unitSpan = UnitSpan;
            double room = Math.Max(0, finalSize.Width - unitSpan);
            double hostWidth = Fill ? room : Math.Min(Host?.DesiredSize.Width ?? 0, room);
            Host?.Arrange(new Rect(0, 0, hostWidth, finalSize.Height));
            if (Unit is { } unit)
                unit.Arrange(new Rect(hostWidth + Gap, 0, unit.DesiredSize.Width, finalSize.Height));
            return finalSize;
        }
    }
}
