using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using Xunit;
using SpinnerControl = Foreman.Mac.Kit.Controls.Spinner;
using ProgressBarControl = Foreman.Mac.Kit.Controls.ProgressBar;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitStatePanelTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;


        private static ControlTheme KitStatePanelTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.StatePanel", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ControlTheme KitButtonRowTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ButtonRow", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ControlTheme KitButtonTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Button", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Button Btn(string text, ButtonRowRole role = ButtonRowRole.Default) {
            var button = new Button { Theme = KitButtonTheme(), Content = text };
            ButtonRow.SetRole(button, role);
            return button;
        }

        private static ButtonRow OneButtonRow(string text = "Add Recipe…") {
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(Btn(text));
            return row;
        }

        private static StatePanel NewPanel(StatePanelShape shape, string headline = "No node selected",
            string sentence = "Select a node on the canvas.", ButtonRow? buttons = null, double? progress = null) =>
            new() {
                Theme = KitStatePanelTheme(),
                Shape = shape,
                Headline = headline,
                Sentence = sentence,
                Buttons = buttons ?? (shape == StatePanelShape.Loading ? null : OneButtonRow()),
                Progress = progress,
            };

        private static Window Shown(StatePanel panel, double width = 400, double height = 400) {
            var window = new Window { Width = width, Height = height, Content = panel };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static Path Glyph(StatePanel panel, string name = "PART_Glyph") =>
            panel.GetVisualDescendants().OfType<Path>().Single(p => p.Name == name);

        private static Viewbox GlyphViewbox(StatePanel panel, string name) =>
            panel.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == name);

        private static SpinnerControl SpinnerAt(StatePanel panel, string name) =>
            panel.GetVisualDescendants().OfType<SpinnerControl>().Single(s => s.Name == name);

        private static TextBlock TextAt(StatePanel panel, string name) =>
            panel.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == name);

        [AvaloniaFact]
        public void Empty_Builds_GlyphIsThirtyTwoInEmptyGraph() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Shown(panel);

            Path glyph = Glyph(panel);
            Assert.True(glyph.IsVisible);
            Assert.Equal(32, glyph.Width);
            Assert.Equal(32, glyph.Height);
            Assert.False(SpinnerAt(panel, "PART_Spinner").IsVisible);
        }

        [AvaloniaFact]
        public void NoResults_Builds() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.NoResults, headline: "No results for “beacn”", sentence: "Nothing matches this search.");
            Shown(panel);

            Assert.True(Glyph(panel).IsVisible);
        }

        [AvaloniaFact]
        public void Error_Builds_GlyphStrokeResolvesErrorColor() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Error, headline: "Couldn’t read that save file", sentence: "The file ends partway through.");
            Shown(panel);

            Path glyph = Glyph(panel);
            Assert.True(glyph.IsVisible);
            IBrush error = fixture.Brush("Foreman.Color.Error");
            Assert.Equal(((ISolidColorBrush)error).Color, ((ISolidColorBrush)glyph.Stroke!).Color);
        }

        [AvaloniaFact]
        public void Loading_Builds_NoGlyph_SpinnerLargeInTheSlot() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Loading, headline: "Loading Factorio data", sentence: "Reading Space Age — mod 3 of 7.");
            Shown(panel);

            Assert.False(Glyph(panel).IsVisible);
            SpinnerControl spinner = SpinnerAt(panel, "PART_Spinner");
            Assert.True(spinner.IsVisible);
            Assert.Contains("lg", spinner.Classes);
        }

        [AvaloniaFact]
        public void Empty_WithZeroButtons_ThrowsOnBuild() {
            using var fixture = new KitFixture();
            var panel = new StatePanel { Theme = KitStatePanelTheme(), Shape = StatePanelShape.Empty, Headline = "x", Sentence = "y" };
            Assert.Throws<InvalidOperationException>(() => Shown(panel));
        }

        [AvaloniaFact]
        public void NoResults_WithZeroButtons_ThrowsOnBuild() {
            using var fixture = new KitFixture();
            var panel = new StatePanel { Theme = KitStatePanelTheme(), Shape = StatePanelShape.NoResults, Headline = "x", Sentence = "y" };
            Assert.Throws<InvalidOperationException>(() => Shown(panel));
        }

        [AvaloniaFact]
        public void Error_WithZeroButtons_ThrowsOnBuild() {
            using var fixture = new KitFixture();
            var panel = new StatePanel { Theme = KitStatePanelTheme(), Shape = StatePanelShape.Error, Headline = "x", Sentence = "y" };
            Assert.Throws<InvalidOperationException>(() => Shown(panel));
        }

        [AvaloniaFact]
        public void Loading_WithZeroButtons_DoesNotThrow() {
            using var fixture = new KitFixture();
            var panel = new StatePanel { Theme = KitStatePanelTheme(), Shape = StatePanelShape.Loading, Headline = "x", Sentence = "y" };
            Shown(panel);
        }

        [AvaloniaFact]
        public void ButtonsRow_IsForcedCentered_WhenWired() {
            using var fixture = new KitFixture();
            ButtonRow row = OneButtonRow();
            StatePanel panel = NewPanel(StatePanelShape.Empty, buttons: row);
            Shown(panel);

            Assert.True(row.IsCentered);
        }

        [AvaloniaFact]
        public void Headline_IsEmphasizedBodyStyle() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Shown(panel);

            TextBlock headline = TextAt(panel, "PART_Headline");
            Assert.Contains("headline", headline.Classes);
            Assert.Equal("No node selected", headline.Text);
        }

        [AvaloniaFact]
        public void Sentence_IsCalloutStyle_MaxWidth280_UpToThreeLines() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Shown(panel);

            TextBlock sentence = TextAt(panel, "PART_Sentence");
            Assert.Contains("callout", sentence.Classes);
            Assert.Contains("secondary", sentence.Classes);
            Assert.Equal(280, sentence.MaxWidth);
            Assert.Equal(3, sentence.MaxLines);
            Assert.Equal(TextWrapping.Wrap, sentence.TextWrapping);
        }

        [AvaloniaFact]
        public void RegionUnderMinimum_AutoSwitchesToCompact_NoHeadline_HorizontalLine() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Loading);
            Shown(panel, width: 200, height: 80);

            Assert.True(panel.IsCompact);
            var compact = panel.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_CompactLayout");
            var full = panel.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_FullLayout");
            Assert.True(compact.IsVisible);
            Assert.False(full.IsVisible);
            Assert.Equal(3, compact.ColumnDefinitions.Count);
        }

        [AvaloniaFact]
        public void RegionAtOrAboveMinimum_StaysFull() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Shown(panel, width: 400, height: 400);

            Assert.False(panel.IsCompact);
        }

        [AvaloniaFact]
        public void Loading_DoesNotAppear_BeforeStatePanelDelay() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Loading);
            Shown(panel);

            AnimationClock.Settle(200);
            Assert.Equal(0, panel.Opacity);

            AnimationClock.Settle(650);
            Assert.Equal(1, panel.Opacity);
        }

        [AvaloniaFact]
        public void LoadingDelay_AfterTheGrace_PumpsWithZeroLayoutGrowth() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Loading);
            Shown(panel);

            AnimationClock.Settle(650);
            Assert.Equal(1, panel.Opacity);

            int layouts = 0;
            panel.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);

            Assert.Equal(0, layouts);
        }

        [AvaloniaFact]
        public void NonLoadingShape_AppearsImmediately_NoDelay() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Shown(panel);

            AnimationClock.Settle(250);
            Assert.Equal(1, panel.Opacity);
        }

        [AvaloniaFact]
        public void ReduceMotion_KeepsCrossFadeAndDelay_SpinnerSlows() {
            Motion.ReduceMotion = true;
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Loading);
            Shown(panel);

            var opacity = Assert.Single(panel.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.StatePanelDelay, opacity.Delay);
        }

        [AvaloniaFact]
        public void Progress_Set_ShowsDeterminateBar() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Loading, progress: 62);
            Shown(panel);

            var bar = panel.GetVisualDescendants().OfType<ProgressBarControl>().Single(b => b.Name == "PART_Progress");
            Assert.True(bar.IsVisible);
            Assert.False(bar.IsIndeterminate);
            Assert.Equal(62, bar.Value);
        }

        [AvaloniaFact]
        public void Progress_Null_Loading_ShowsSpinnerOnly_NeverAProgressBar() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Loading, progress: null);
            Shown(panel);

            var bar = panel.GetVisualDescendants().OfType<ProgressBarControl>().Single(b => b.Name == "PART_Progress");
            Assert.False(bar.IsVisible);
            Assert.True(SpinnerAt(panel, "PART_Spinner").IsVisible);
        }

        [AvaloniaFact]
        public void Panel_DeclaresNoBackgroundBrush() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Shown(panel);

            Assert.Null(panel.Background);
        }

        [AvaloniaFact]
        public void Automation_RoleGroupStatusPoliteLiveRegion_GlyphIsHidden() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Shown(panel);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(panel);
            Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
            Assert.Equal("status", peer.GetLocalizedControlType());
            Assert.Equal(AutomationLiveSetting.Polite, peer.GetLiveSetting());
            Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(Glyph(panel)));
        }

        [AvaloniaFact]
        public void Scale150_GlyphViewboxResolves48_AndThePathRendersAtTheScaledSize() {
            using var fixture = new KitFixture();
            Foreman.Mac.Kit.Appearance.ScaleContract.Apply(1.5);
            try {
                StatePanel panel = NewPanel(StatePanelShape.Empty);
                Window window = Shown(panel);

                Viewbox box = GlyphViewbox(panel, "PART_GlyphViewbox");
                Assert.Equal(48, box.Bounds.Width);
                Assert.Equal(48, box.Bounds.Height);

                //The Path itself never scales - its master geometry stays 32; the Viewbox is what carries it to 48 on screen.
                Path pathEl = Glyph(panel);
                Assert.Equal(32, pathEl.Width);
                Point topLeft = pathEl.TranslatePoint(new Point(0, 0), window) ?? default;
                Point bottomRight = pathEl.TranslatePoint(new Point(pathEl.Bounds.Width, pathEl.Bounds.Height), window) ?? default;
                Assert.Equal(48, bottomRight.X - topLeft.X, 0.01);
                Assert.Equal(48, bottomRight.Y - topLeft.Y, 0.01);
            } finally {
                Foreman.Mac.Kit.Appearance.ScaleContract.Apply(1.0);
            }
        }

        [AvaloniaFact]
        public void Scale150_SentenceAndFullLayoutMaxWidthScale() {
            using var fixture = new KitFixture();
            Foreman.Mac.Kit.Appearance.ScaleContract.Apply(1.5);
            try {
                StatePanel panel = NewPanel(StatePanelShape.Empty);
                Shown(panel);

                TextBlock sentence = TextAt(panel, "PART_Sentence");
                Assert.Equal(420, sentence.MaxWidth);
                var full = panel.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_FullLayout");
                Assert.Equal(480, full.MaxWidth);
            } finally {
                Foreman.Mac.Kit.Appearance.ScaleContract.Apply(1.0);
            }
        }

        [AvaloniaFact]
        public void Compact_GlyphUsesGlyph16_ExceptErrorWhichReusesGlyphMicro() {
            using var fixture = new KitFixture();
            StatePanel empty = NewPanel(StatePanelShape.Empty);
            Shown(empty, width: 280, height: 90);
            Assert.Equal(16, Glyph(empty, "PART_CompactGlyph").Width);

            StatePanel error = NewPanel(StatePanelShape.Error);
            Shown(error, width: 280, height: 90);
            Assert.Equal(12, Glyph(error, "PART_CompactGlyph").Width);
        }

        [AvaloniaFact]
        public void Compact_Scale150_GlyphViewboxResolvesTwentyFour_AndThePathRendersAtTheScaledSize() {
            using var fixture = new KitFixture();
            Foreman.Mac.Kit.Appearance.ScaleContract.Apply(1.5);
            try {
                StatePanel panel = NewPanel(StatePanelShape.Empty);
                Window window = Shown(panel, width: 280, height: 90);

                Viewbox box = GlyphViewbox(panel, "PART_CompactGlyphViewbox");
                Assert.Equal(24, box.Bounds.Width);
                Assert.Equal(24, box.Bounds.Height);

                //The Path itself never scales - its master geometry stays 16; the Viewbox is what carries it to 24 on screen.
                Path pathEl = Glyph(panel, "PART_CompactGlyph");
                Assert.Equal(16, pathEl.Width);
                Point topLeft = pathEl.TranslatePoint(new Point(0, 0), window) ?? default;
                Point bottomRight = pathEl.TranslatePoint(new Point(pathEl.Bounds.Width, pathEl.Bounds.Height), window) ?? default;
                Assert.Equal(24, bottomRight.X - topLeft.X, 0.01);
                Assert.Equal(24, bottomRight.Y - topLeft.Y, 0.01);
            } finally {
                Foreman.Mac.Kit.Appearance.ScaleContract.Apply(1.0);
            }
        }

        [AvaloniaFact]
        public void Compact_LongSentence_NarrowHost_RowStaysInsideHost() {
            using var fixture = new KitFixture();
            string longSentence = "This sentence is far too long to fit on one line inside a narrow compact state panel host without trimming.";
            StatePanel panel = NewPanel(StatePanelShape.Empty, sentence: longSentence);
            Window window = Shown(panel, width: 220, height: 80);

            Assert.True(panel.IsCompact);
            var row = panel.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_CompactLayout");
            Assert.True(row.Bounds.Width <= window.ClientSize.Width + 0.01);

            TextBlock sentence = TextAt(panel, "PART_CompactSentence");
            Point topLeft = sentence.TranslatePoint(new Point(0, 0), window) ?? default;
            Assert.True(topLeft.X + sentence.Bounds.Width <= window.ClientSize.Width + 0.01);
        }

        [AvaloniaFact]
        public void Sentence_HasOneFourLineHeight() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Shown(panel);

            Assert.Equal(16.8, TextAt(panel, "PART_Sentence").LineHeight);
        }

        [AvaloniaFact]
        public void LoadingToError_WithinTheDelay_AppearsImmediately() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Loading, buttons: OneButtonRow());
            Shown(panel);

            AnimationClock.Settle(100);
            Assert.Equal(0, panel.Opacity);

            panel.Shape = StatePanelShape.Error;
            AnimationClock.Settle(250);
            Assert.Equal(1, panel.Opacity);
        }

        [AvaloniaFact]
        public void NonLoadingToLoading_GetsAFreshFourHundredMsDelay() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Shown(panel);
            AnimationClock.Settle(250);
            Assert.Equal(1, panel.Opacity);

            panel.Shape = StatePanelShape.Loading;
            AnimationClock.Settle(300);
            Assert.Equal(0, panel.Opacity);
            AnimationClock.Settle(500);
            Assert.Equal(1, panel.Opacity);
        }

        [AvaloniaFact]
        public void Detach_Reattach_ResubscribesTheGlyphResource() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Empty);
            Window window = Shown(panel);

            Path glyphEl = Glyph(panel);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            glyphEl.Data = null;

            window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(glyphEl.Data);
        }

        [AvaloniaFact]
        public void Enter_ActivatesThePrimaryButton_ViaThePanelsOwnTunnelHandler() {
            using var fixture = new KitFixture();
            ButtonRow row = OneButtonRow("Add Recipe…");
            StatePanel panel = NewPanel(StatePanelShape.Empty, buttons: row);
            Shown(panel);

            int invoked = 0;
            row.DefaultInvoked += (_, _) => invoked++;
            fixture.StateOf(panel).KeyDown(Key.Enter);

            Assert.Equal(1, invoked);
        }

        [AvaloniaFact]
        public void MovesFocusOnAppear_DefaultFalse_LeavesFocusAlone() {
            using var fixture = new KitFixture();
            ButtonRow row = OneButtonRow("Add Recipe…");
            StatePanel panel = NewPanel(StatePanelShape.Empty, buttons: row);
            Shown(panel);

            Assert.False(row.ResolveDefaultButton()!.IsFocused);
        }

        [AvaloniaFact]
        public void MovesFocusOnAppear_True_FocusesThePrimaryOnAppear() {
            using var fixture = new KitFixture();
            ButtonRow row = OneButtonRow("Add Recipe…");
            StatePanel panel = NewPanel(StatePanelShape.Empty, buttons: row);
            panel.MovesFocusOnAppear = true;
            Shown(panel);

            Assert.True(row.ResolveDefaultButton()!.IsFocused);
        }

        [AvaloniaFact]
        public void Compact_ShowsOnlyThePrimaryButton_StyledPlain_NotTheWholeRow() {
            using var fixture = new KitFixture();
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(Btn("Cancel", ButtonRowRole.Cancel));
            row.Items.Add(Btn("Try Again", ButtonRowRole.Default));
            StatePanel panel = NewPanel(StatePanelShape.Error, sentence: "Try again?", buttons: row);
            //Clicks need the panel in the fixture's own window - a pointer-driven test can't reach a control in a second, unrelated window.
            //Width stays well above the compact row's natural content width; height alone forces .compact.
            fixture.Window.Width = 400;
            fixture.Window.Height = 90;
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            Assert.True(panel.IsCompact);
            var compactHost = panel.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_CompactButtonsHost");
            Button compactButton = Assert.IsType<Button>(compactHost.Content);
            Assert.Contains("plain", compactButton.Classes);
            Assert.Equal("Try Again", compactButton.Content);
            Assert.NotSame(row, compactHost.Content);

            int invoked = 0;
            row.DefaultInvoked += (_, _) => invoked++;
            fixture.StateOf(compactButton).Click();
            Assert.Equal(1, invoked);
        }
    }
}
