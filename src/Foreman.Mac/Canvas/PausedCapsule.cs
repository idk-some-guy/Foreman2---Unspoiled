using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using System;

namespace Foreman.Mac.Canvas {
    internal sealed class PausedCapsule : Button {
        internal const string Label = "Calculations paused";
        private const double GlyphMaster = 20;
        private const double GlyphGap = 6;
        private const double GlyphStroke = 2.4;

        protected override Type StyleKeyOverride => typeof(Button);

        public PausedCapsule() {
            Theme = KitTheme("Kit.Button");
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Top;
            Margin = new Thickness(0, 12, 0, 0);
            IsVisible = false;
            this.Bind(CornerRadiusProperty, this.GetResourceObservable("Foreman.Size.ControlHeightRegular", value => new CornerRadius(value is double height ? height / 2 : 0)));
            this.Bind(BackgroundProperty, this.GetResourceObservable("Foreman.Color.PopoverMaterial", ToBrush));
            AutomationProperties.SetName(this, Label);
            AutomationProperties.SetHelpText(this, "Resumes calculations.");

            var glyph = new Path {
                Theme = KitTheme("Kit.Glyph"),
                Width = GlyphMaster,
                Height = GlyphMaster,
                StrokeThickness = GlyphStroke,
                IsHitTestVisible = false,
            };
            glyph.Bind(Shape.StrokeProperty, this.GetObservable(ForegroundProperty));
            glyph.Bind(Path.DataProperty, this.GetResourceObservable("Kit.Glyph.Canvas.Paused.20", value => value as Geometry));
            var glyphBox = new Viewbox { Stretch = Stretch.Uniform, Child = glyph };
            glyphBox.Bind(WidthProperty, this.GetResourceObservable("Foreman.Size.GlyphMicro", value => value as double?));
            glyphBox.Bind(HeightProperty, this.GetResourceObservable("Foreman.Size.GlyphMicro", value => value as double?));
            Content = new StackPanel {
                Orientation = Orientation.Horizontal,
                Spacing = GlyphGap,
                Children = {
                    glyphBox,
                    new TextBlock { Text = Label, VerticalAlignment = VerticalAlignment.Center },
                },
            };
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Padding = new Thickness(Space("Foreman.Space.CapsuleLeading"), 0, Space("Foreman.Space.CapsuleTrailing"), 0);
        }

        private double Space(string key) => this.TryFindResource(key, ActualThemeVariant, out object? value) && value is double space ? space : 0;

        private static object? ToBrush(object? value) =>
            value switch {
                Color color => new SolidColorBrush(color),
                IBrush brush => brush,
                _ => null,
            };

        private static ControlTheme? KitTheme(string key) {
            object? theme = null;
            Application.Current?.TryGetResource(key, null, out theme);
            return theme as ControlTheme;
        }
    }
}
