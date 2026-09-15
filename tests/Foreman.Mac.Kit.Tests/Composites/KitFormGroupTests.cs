using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitFormGroupTests {
        private static ControlTheme KitFormGroupTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.FormGroup", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static FormGroup Themed(string title, params object[] rows) {
            var group = new FormGroup { Theme = KitFormGroupTheme(), Title = title };
            foreach (object row in rows)
                group.Items.Add(row);
            return group;
        }

        private static Border Frame(FormGroup group) =>
            group.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");

        private static TextBlock TitleText(FormGroup group) =>
            group.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");

        private static StackPanel Stack(FormGroup group) =>
            group.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Stack");

        [AvaloniaFact]
        public void KitFormGroup_IsAControlThemeTargetingFormGroup() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(FormGroup), KitFormGroupTheme().TargetType);
        }

        [AvaloniaFact]
        public void Title_UsesSectionHeaderTypeNoTrackingAndTheInputStringUnchanged() {
            using var fixture = new KitFixture();
            FormGroup group = Themed("canvas settings", new TextBlock { Text = "row" });
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontWeight", Application.Current.ActualThemeVariant, out object? weight);

            TextBlock title = TitleText(group);
            Assert.Equal(Assert.IsType<double>(size), title.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), title.FontWeight);
            Assert.Equal(0.0, title.LetterSpacing);
            Assert.Equal("canvas settings", title.Text);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), title.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Default_HasNoBackgroundBorderOrPadding() {
            using var fixture = new KitFixture();
            FormGroup group = Themed("Canvas", new TextBlock { Text = "row" });
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Border frame = Frame(group);
            Assert.Null(frame.Background);
            Assert.Null(frame.BorderBrush);
            Assert.Equal(default, frame.Padding);
        }

        [AvaloniaFact]
        public void Boxed_AddsWellBackgroundBorderRadiusCardAndGroupBoxPadding() {
            using var fixture = new KitFixture();
            FormGroup group = Themed("Canvas", new TextBlock { Text = "row" });
            group.IsBoxed = true;
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Border frame = Frame(group);
            Assert.Equal(fixture.Brush("Foreman.Color.WellBackground").ToImmutable(), frame.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Border").ToImmutable(), frame.BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(1), frame.BorderThickness);

            Application.Current!.TryGetResource("Foreman.Radius.Card", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), frame.CornerRadius);

            Application.Current!.TryGetResource("Foreman.Thickness.GroupBoxPadding", Application.Current.ActualThemeVariant, out object? padding);
            Assert.Equal(Assert.IsType<Thickness>(padding), frame.Padding);
        }

        [AvaloniaFact]
        public void TitleGap_IsSpace2Eight() {
            using var fixture = new KitFixture();
            FormGroup group = Themed("Canvas", new TextBlock { Text = "row 1" }, new TextBlock { Text = "row 2" });
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Space.Space2", Application.Current.ActualThemeVariant, out object? gap);
            Assert.Equal(Assert.IsType<double>(gap), Stack(group).Spacing);
        }

        [AvaloniaFact]
        public void RowGap_IsSpace2Eight_WithNoSeparatorBetweenRows() {
            using var fixture = new KitFixture();
            FormGroup group = Themed("Canvas", new TextBlock { Text = "row 1" }, new TextBlock { Text = "row 2" });
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Space.Space2", Application.Current.ActualThemeVariant, out object? gap);

            ItemsPresenter presenter = group.GetVisualDescendants().OfType<ItemsPresenter>().Single();
            Panel itemsPanel = Assert.IsAssignableFrom<Panel>(presenter.Panel);
            Assert.Equal(Assert.IsType<double>(gap), Assert.IsType<StackPanel>(itemsPanel).Spacing);
            Assert.DoesNotContain(itemsPanel.Children, c => c is Border);
        }

        [AvaloniaFact]
        public void DefaultMargin_BottomIsSpace4Sixteen() {
            using var fixture = new KitFixture();
            FormGroup group = Themed("Canvas", new TextBlock { Text = "row" });
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Space.Space4", Application.Current.ActualThemeVariant, out object? gap);
            Assert.Equal(Assert.IsType<double>(gap), group.Margin.Bottom);
        }

        [AvaloniaFact]
        public void Content_HostsEveryRow() {
            using var fixture = new KitFixture();
            var row1 = new TextBlock { Text = "Default node direction" };
            var row2 = new TextBlock { Text = "Node width" };
            FormGroup group = Themed("Canvas", row1, row2);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(group.GetVisualDescendants(), v => ReferenceEquals(v, row1));
            Assert.Contains(group.GetVisualDescendants(), v => ReferenceEquals(v, row2));
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class KitFormGroupScaleTests {
        [AvaloniaFact]
        public void TitleFontSize_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.FormGroup", null, out object? theme));
            var group = new FormGroup { Theme = (ControlTheme)theme!, Title = "Canvas", IsBoxed = true };
            group.Items.Add(new TextBlock { Text = "row" });
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontSize", Application.Current.ActualThemeVariant, out object? baseSize);
            TextBlock title = group.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Title");
            Assert.Equal(baseSize, title.FontSize);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontSize", Application.Current.ActualThemeVariant, out object? scaledSize);
            Assert.Equal((double)baseSize! * 1.5, (double)scaledSize!);
            Assert.Equal(scaledSize, title.FontSize);
        }
    }
}
