using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class KeyCapTests {
        private static ControlTheme KitKeyCapTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.KeyCap", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static KeyCap Themed(string text, string accessibleName) =>
            new() { Theme = KitKeyCapTheme(), Text = text, AccessibleName = accessibleName };

        private static Border Frame(KeyCap cap) => cap.GetVisualDescendants().OfType<Border>().Single();

        [AvaloniaFact]
        public void KitKeyCap_IsAControlThemeTargetingKeyCap() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(KeyCap), KitKeyCapTheme().TargetType);
        }

        [AvaloniaFact]
        public void Bounds_AreEighteenPointMinimumSquareRadiusFour() {
            using var fixture = new KitFixture();
            KeyCap cap = Themed("⌘", "Command");
            fixture.Window.Content = cap;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(18, cap.MinWidth);
            Assert.Equal(18, cap.MinHeight);

            Application.Current!.TryGetResource("Foreman.Radius.KeyCap", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Frame(cap).CornerRadius);
            Assert.Equal(new CornerRadius(4), Assert.IsType<CornerRadius>(radius));
        }

        [AvaloniaFact]
        public void Frame_ResolvesToControlBackgroundAndBorderStrong() {
            using var fixture = new KitFixture();
            KeyCap cap = Themed("K", "K");
            fixture.Window.Content = cap;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackground").ToImmutable(), Frame(cap).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), Frame(cap).BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(1), Frame(cap).BorderThickness);
        }

        [AvaloniaFact]
        public void BottomEdge_IsAOnePointBoxShadow() {
            using var fixture = new KitFixture();
            KeyCap cap = Themed("K", "K");
            fixture.Window.Content = cap;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Shadow.KeyCapEdge", Application.Current.ActualThemeVariant, out object? shadow);
            Assert.Equal(shadow, Frame(cap).BoxShadow);
        }

        [AvaloniaFact]
        public void Label_IsCaptionSixHundredWeight() {
            using var fixture = new KitFixture();
            KeyCap cap = Themed("⌘", "Command");
            fixture.Window.Content = cap;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = cap.GetVisualDescendants().OfType<TextBlock>().Single();
            Application.Current!.TryGetResource("Foreman.Type.Caption.FontSize", Application.Current.ActualThemeVariant, out object? fontSize);
            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontWeight", Application.Current.ActualThemeVariant, out object? weight);
            Assert.Equal(Assert.IsType<double>(fontSize), label.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), label.FontWeight);
            Assert.Equal("⌘", label.Text);
        }

        [AvaloniaFact]
        public void KeyCap_IsNeverFocusable() {
            using var fixture = new KitFixture();
            KeyCap cap = Themed("⌘", "Command");
            Assert.False(cap.Focusable);
            Assert.False(cap.IsTabStop);
        }

        [AvaloniaFact]
        public void AutomationName_IsTheSpelledOutAccessibleName() {
            using var fixture = new KitFixture();
            KeyCap cap = Themed("⌘", "Command");
            fixture.Window.Content = cap;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Command", AutomationProperties.GetName(cap));
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class KeyCapScaleTests {
        [AvaloniaFact]
        public void MinSquare_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.KeyCap", null, out object? theme));
            var cap = new KeyCap { Theme = (ControlTheme)theme!, Text = "K" };
            fixture.Window.Content = cap;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(18, cap.MinWidth);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(27, cap.MinWidth);
        }
    }
}
