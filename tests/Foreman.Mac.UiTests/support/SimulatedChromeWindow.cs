using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Foreman.Mac.Converters;
using System.Globalization;

namespace Foreman.Mac.UiTests.support {
    public class SimulatedChromeWindow : Window {
        public static readonly StyledProperty<Thickness> DecorationMarginProxyProperty =
            AvaloniaProperty.Register<SimulatedChromeWindow, Thickness>(nameof(DecorationMarginProxy));

        public Thickness DecorationMarginProxy {
            get => GetValue(DecorationMarginProxyProperty);
            set => SetValue(DecorationMarginProxyProperty, value);
        }

        public Grid TitleBarRow { get; }
        public StackPanel LeadingGroup { get; }

        public SimulatedChromeWindow() {
            Width = 1200;
            Height = 700;
            ExtendClientAreaTitleBarHeightHint = 52;

            LeadingGroup = new StackPanel {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 4,
            };
            Grid.SetColumn(LeadingGroup, 0);

            TitleBarRow = new Grid {
                Name = "TitleBarRow",
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                Height = ExtendClientAreaTitleBarHeightHint,
                ClipToBounds = true,
            };
            TitleBarRow.Children.Add(LeadingGroup);
            Grid.SetRow(TitleBarRow, 0);

            var canvasHost = new Border();
            Grid.SetRow(canvasHost, 1);

            var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            root.Children.Add(TitleBarRow);
            root.Children.Add(canvasHost);
            Content = root;

            RefreshMargin();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DecorationMarginProxyProperty)
                RefreshMargin();
        }

        private void RefreshMargin() =>
            LeadingGroup.Margin = (Thickness)TrafficLightInsetConverter.Instance.Convert(
                DecorationMarginProxy, typeof(Thickness), null, CultureInfo.InvariantCulture)!;
    }
}
