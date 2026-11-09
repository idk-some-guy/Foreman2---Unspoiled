using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class TrimmedTooltipTests {
        private const string Long = "Sulfuric acid barrelling and unbarrelling with the long recipe name that cannot fit";

        private static ControlTheme Theme(string key) {
            Assert.True(Application.Current!.TryGetResource(key, null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static TextBlock Plain(string text) {
            var block = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
            TrimmedTooltip.SetEnabled(block, true);
            return block;
        }

        private static void Host(KitFixture fixture, Control content, double width = 120) {
            fixture.Window.Content = new Border { Width = width, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = content };
            Dispatcher.UIThread.RunJobs();
        }

        private static TextBlock Part(Visual root, string name) =>
            root.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == name);

        private static void AssertFollowsTrim(TextBlock text, Control tipHost, string longText, string shortText, System.Action<string> setText) {
            Assert.Contains(text.TextLayout.TextLines, l => l.HasCollapsed);
            Assert.Equal(longText, ToolTip.GetTip(tipHost)?.ToString());

            setText(shortText);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(text.TextLayout.TextLines, l => l.HasCollapsed);
            Assert.Null(ToolTip.GetTip(tipHost));

            setText(longText);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(longText, ToolTip.GetTip(tipHost)?.ToString());
        }

        [AvaloniaFact]
        public void ATipTheHostSetItself_SurvivesFitTrimAndSwitchingOff() {
            using var fixture = new KitFixture();
            TextBlock text = Plain(Long);
            ToolTip.SetTip(text, "Chosen by the host");
            Host(fixture, text);
            Assert.Equal("Chosen by the host", ToolTip.GetTip(text));

            text.Text = "Iron";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Chosen by the host", ToolTip.GetTip(text));

            text.Text = Long;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Chosen by the host", ToolTip.GetTip(text));

            TrimmedTooltip.SetEnabled(text, false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Chosen by the host", ToolTip.GetTip(text));
        }

        [AvaloniaFact]
        public void ATrimmedTip_ShowsTheFullTextToASightedUser() {
            using var fixture = new KitFixture();
            TextBlock text = Plain(Long);
            Host(fixture, text);

            var tip = new ToolTip { Theme = Theme("Kit.Tooltip"), Content = ToolTip.GetTip(text) };
            fixture.Window.Content = new Border { Width = 400, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = tip };
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Long, tip.GetVisualDescendants().OfType<TextBlock>().Single().Text);
        }

        [AvaloniaFact]
        public void SwitchingItOff_ClearsTheTip() {
            using var fixture = new KitFixture();
            TextBlock text = Plain(Long);
            Host(fixture, text);
            Assert.Equal(Long, ToolTip.GetTip(text)?.ToString());

            TrimmedTooltip.SetEnabled(text, false);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(ToolTip.GetTip(text));
        }

        [AvaloniaFact]
        public void PaletteRow_Label_TipsOnlyWhileTrimmed() {
            using var fixture = new KitFixture();
            var row = new PaletteRow { Theme = Theme("Kit.PaletteRow"), Label = Long, Kind = "item", Icon = Brushes.SteelBlue };
            Host(fixture, row, 200);

            AssertFollowsTrim(Part(row, "PART_Label"), Part(row, "PART_Label"), Long, "Iron", s => row.Label = s);
        }

        [AvaloniaFact]
        public void RecentRow_Name_TipsOnlyWhileTrimmed() {
            using var fixture = new KitFixture();
            var row = new RecentRow { Theme = Theme("Kit.RecentRow"), Label = Long };
            Host(fixture, row, 200);

            AssertFollowsTrim(Part(row, "PART_Name"), Part(row, "PART_Name"), Long, "Iron", s => row.Label = s);
        }

        [AvaloniaFact]
        public void PopUp_DisplayText_TipsOnlyWhileTrimmed() {
            using var fixture = new KitFixture();
            var popup = new PopUp { Theme = Theme("Kit.PopUp") };
            popup.Items.Add(Long);
            popup.Items.Add("Iron");
            popup.SelectedItem = Long;
            Host(fixture, popup, 120);

            TextBlock text = popup.GetVisualDescendants().OfType<TextBlock>().First(t => t.TextTrimming == TextTrimming.CharacterEllipsis);
            AssertFollowsTrim(text, text, Long, "Iron", s => popup.SelectedItem = s);
        }

        [AvaloniaFact]
        public void StatePanel_Headline_TipsOnlyWhileTrimmed() {
            using var fixture = new KitFixture();
            var buttons = new ButtonRow { Theme = Theme("Kit.ButtonRow") };
            buttons.Items.Add(new Button { Theme = Theme("Kit.Button"), Content = "Add" });
            var panel = new StatePanel { Theme = Theme("Kit.StatePanel"), Shape = StatePanelShape.Empty, Headline = Long, Buttons = buttons };
            Host(fixture, panel, 280);

            TextBlock headline = Part(panel, "PART_Headline");
            AssertFollowsTrim(headline, headline, Long, "None", s => panel.Headline = s);
        }
    }
}
