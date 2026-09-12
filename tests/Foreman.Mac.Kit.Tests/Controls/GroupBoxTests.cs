using Avalonia;
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
using GroupBox = Foreman.Mac.Kit.Controls.GroupBox;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class GroupBoxTests {
        private static ControlTheme KitGroupBoxTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.GroupBox", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static GroupBox Themed(string header, object content) =>
            new() { Theme = KitGroupBoxTheme(), Header = header, Content = content };

        private static Border Frame(GroupBox box) =>
            box.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");

        private static TextBlock Title(GroupBox box) =>
            box.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");

        [AvaloniaFact]
        public void KitGroupBox_IsAControlThemeTargetingGroupBox() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(GroupBox), KitGroupBoxTheme().TargetType);
        }

        [AvaloniaFact]
        public void Bounds_ComeFromTokens() {
            using var fixture = new KitFixture();
            GroupBox box = Themed("Canvas", new TextBlock { Text = "row" });
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.WellBackground").ToImmutable(), Frame(box).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Border").ToImmutable(), Frame(box).BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(1), Frame(box).BorderThickness);

            Application.Current!.TryGetResource("Foreman.Radius.Card", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Frame(box).CornerRadius);

            Application.Current!.TryGetResource("Foreman.Thickness.GroupBoxPadding", Application.Current.ActualThemeVariant, out object? padding);
            Assert.Equal(Assert.IsType<Thickness>(padding), Frame(box).Padding);

            Application.Current!.TryGetResource("Foreman.Metrics.GroupBoxMaxWidth", Application.Current.ActualThemeVariant, out object? maxWidth);
            Assert.Equal(Assert.IsType<double>(maxWidth), Frame(box).MaxWidth);
        }

        [AvaloniaFact]
        public void Title_UsesSectionHeaderTypeAndTextSecondary() {
            using var fixture = new KitFixture();
            GroupBox box = Themed("Canvas", new TextBlock { Text = "row" });
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontWeight", Application.Current.ActualThemeVariant, out object? weight);
            Assert.Equal(Assert.IsType<double>(size), Title(box).FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), Title(box).FontWeight);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), Title(box).Foreground?.ToImmutable());
            Assert.Equal("Canvas", Title(box).Text);
        }

        [AvaloniaFact]
        public void Content_HostsTheRows() {
            using var fixture = new KitFixture();
            var row = new TextBlock { Text = "Default node direction" };
            GroupBox box = Themed("Canvas", row);
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(box.GetVisualDescendants(), v => ReferenceEquals(v, row));
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class GroupBoxScaleTests {
        [AvaloniaFact]
        public void MaxWidth_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.GroupBox", null, out object? theme));
            var box = new GroupBox { Theme = (ControlTheme)theme!, Header = "Canvas", Content = new TextBlock { Text = "row" } };
            fixture.Window.Content = box;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(420, box.MaxWidth);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(630, box.MaxWidth);
        }
    }
}
