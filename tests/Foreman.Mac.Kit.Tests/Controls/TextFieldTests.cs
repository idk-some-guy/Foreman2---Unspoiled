using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class TextFieldTests {
        private static ControlTheme KitTextFieldTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.TextField", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static TextBox Themed() => new() { Theme = KitTextFieldTheme() };

        private static Border BorderElement(TextBox field) =>
            field.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");

        [AvaloniaFact]
        public void KitTextField_IsAControlThemeTargetingTextBox() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(TextBox), KitTextFieldTheme().TargetType);
        }

        [AvaloniaFact]
        public void InnerLeftContent_SitsBeforeTheText_InsideTheBorder() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            field.Width = 84;
            var hash = new TextBlock { Text = "#" };
            field.InnerLeftContent = hash;
            field.Text = "FF9500";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Assert.True(hash.IsEffectivelyVisible);
            Assert.Contains(BorderElement(field), hash.GetVisualAncestors());
            TextPresenter presenter = field.GetVisualDescendants().OfType<TextPresenter>().Single();
            double hashRight = hash.TranslatePoint(new Point(hash.Bounds.Width, 0), field)!.Value.X;
            Assert.True(hashRight <= presenter.TranslatePoint(new Point(0, 0), field)!.Value.X);
            Assert.True(hash.Bounds.Width > 0);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ControlHeightRegular", Application.Current.ActualThemeVariant, out object? height);
            Application.Current!.TryGetResource("Foreman.Metrics.TextFieldMinWidth", Application.Current.ActualThemeVariant, out object? minWidth);
            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);

            Assert.Equal(Assert.IsType<double>(height), field.Height);
            Assert.Equal(Assert.IsType<double>(minWidth), field.MinWidth);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), field.CornerRadius);
        }

        [AvaloniaFact]
        public void Padding_IsTheTextFieldPaddingToken() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new Thickness(8, 0), field.Padding);
        }

        [AvaloniaFact]
        public void Background_IsWellBackground() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.WellBackground").ToImmutable(), BorderElement(field).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Border").ToImmutable(), BorderElement(field).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void Hover_BorderStrengthens() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(field).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), BorderElement(field).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void Placeholder_ShowsTextTertiaryAndHidesOnceFilled() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            field.PlaceholderText = "Item name";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            var placeholder = field.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Placeholder");
            Assert.Equal("Item name", placeholder.Text);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), placeholder.Foreground?.ToImmutable());
            Assert.True(placeholder.IsVisible);

            field.Text = "Iron Plate";
            Dispatcher.UIThread.RunJobs();

            Assert.False(placeholder.IsVisible);
        }

        [AvaloniaFact]
        public void Focused_ShowsRingAndAccentCaretWithoutChangingBorder() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            var restBorder = BorderElement(field).BorderBrush!.ToImmutable();

            fixture.StateOf(field).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(field));
            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            Assert.Equal(((CornerRadius)radius!).TopLeft + (double)offset!, ring.RadiusX);

            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), field.CaretBrush?.ToImmutable());
            Assert.Equal(restBorder, BorderElement(field).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            fixture.Window.Content = field;

            field.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), field.Opacity);
        }

        [AvaloniaFact]
        public void SelectAll_ShowsAccentSoftHighlightAcrossTheWholeText() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            field.Text = "iron plate";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            field.SelectAll();
            Dispatcher.UIThread.RunJobs();

            var presenter = field.GetVisualDescendants().OfType<TextPresenter>().Single(p => p.Name == "PART_TextPresenter");
            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), presenter.SelectionBrush?.ToImmutable());
            Assert.Equal(0, field.SelectionStart);
            Assert.Equal("iron plate".Length, field.SelectionEnd);
        }

        [AvaloniaFact]
        public void PointerDrag_SelectsARangeOfText() {
            using var fixture = new KitFixture();
            TextBox field = Themed();
            field.Text = "iron plate";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(field).Drag(
                new Point(10, field.Bounds.Height / 2),
                new Point(field.Bounds.Width - 10, field.Bounds.Height / 2));

            Assert.NotEqual(field.SelectionStart, field.SelectionEnd);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class TextFieldScaleTests {
        [AvaloniaFact]
        public void HeightAndMinWidth_TrackScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.TextField", null, out object? theme));
            var field = new TextBox { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(22, field.Height);
            Assert.Equal(84, field.MinWidth);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33, field.Height);
            Assert.Equal(126, field.MinWidth);
        }
    }
}
