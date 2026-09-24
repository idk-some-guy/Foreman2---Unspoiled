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
            StatePanel panel = NewPanel(StatePanelShape.Loading, progress: 0.62);
            Shown(panel);

            var bar = panel.GetVisualDescendants().OfType<ProgressBarControl>().Single(b => b.Name == "PART_Progress");
            Assert.True(bar.IsVisible);
            Assert.False(bar.IsIndeterminate);
            Assert.Equal(0.62, bar.Value);
            Assert.Equal(1, bar.Maximum);
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
        public void Compact_GlyphIsSixteen_ErrorDrawsTheAlertSixteenMaster() {
            using var fixture = new KitFixture();
            StatePanel empty = NewPanel(StatePanelShape.Empty);
            Shown(empty, width: 280, height: 90);
            Assert.Equal(16, Glyph(empty, "PART_CompactGlyph").Width);

            StatePanel error = NewPanel(StatePanelShape.Error);
            Shown(error, width: 280, height: 90);
            Path glyph = Glyph(error, "PART_CompactGlyph");
            Assert.Equal(16, glyph.Width);
            Assert.True(error.TryFindResource("Kit.Glyph.Alert.16", error.ActualThemeVariant, out object? alert16));
            Assert.Same(alert16, glyph.Data);
            Assert.Equal(16, GlyphViewbox(error, "PART_CompactGlyphViewbox").Bounds.Width);
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
        public void Reannounce_OnAShownError_RaisesTheLiveRegionNameChange_WithoutRefadingOrRestartingTransitions() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Error, headline: "Couldn’t search this preset");
            Shown(panel);
            AnimationClock.Settle(250);
            Assert.Equal(1, panel.Opacity);
            Transitions? transitions = panel.Transitions;

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(panel);
            var raised = new System.Collections.Generic.List<AutomationPropertyChangedEventArgs>();
            peer.PropertyChanged += (_, e) => raised.Add(e);
            int opacityWrites = 0;
            panel.PropertyChanged += (_, e) => { if (e.Property == Visual.OpacityProperty) opacityWrites++; };

            panel.Reannounce();

            AutomationPropertyChangedEventArgs change = Assert.Single(raised);
            Assert.Equal(AutomationElementIdentifiers.NameProperty, change.Property);
            Assert.Equal("Couldn’t search this preset", change.NewValue);
            Assert.Equal(1, panel.Opacity);
            Assert.Equal(0, opacityWrites);
            Assert.Same(transitions, panel.Transitions);
        }

        [AvaloniaFact]
        public void LoadingDelay_Zero_AppearsWithoutTheGrace_TheDefaultIsStatePanelDelay() {
            using var fixture = new KitFixture();
            Assert.Equal(Motion.StatePanelDelay, NewPanel(StatePanelShape.Loading).LoadingDelay);
            StatePanel panel = NewPanel(StatePanelShape.Loading);
            panel.LoadingDelay = TimeSpan.Zero;
            Shown(panel);

            var opacity = Assert.Single(panel.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(TimeSpan.Zero, opacity.Delay);
            AnimationClock.Settle(250);
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
        public void Error_TwoButtonsWiderThanTheHost_WrapInsteadOfOverflowing() {
            using var fixture = new KitFixture();
            var row = new ButtonRow { Theme = KitButtonRowTheme() };
            row.Items.Add(Btn("Open Presets…", ButtonRowRole.Other));
            row.Items.Add(Btn("Pick Another Recipe…", ButtonRowRole.Default));
            StatePanel panel = NewPanel(StatePanelShape.Error, buttons: row);
            //280pt: above Kit.StatePanel's 260pt full-layout floor, wide enough per-button but forcing the cluster to wrap.
            Shown(panel, width: 280, height: 200);

            Assert.False(panel.IsCompact);
            AssertRowWrappedInsideHost(panel, row);
        }

        [AvaloniaFact]
        public void Error_TwoButtonsWiderThanTheHost_At150Percent_WrapInsteadOfOverflowing() {
            using var fixture = new KitFixture();
            Foreman.Mac.Kit.Appearance.ScaleContract.Apply(1.5);
            try {
                var row = new ButtonRow { Theme = KitButtonRowTheme() };
                row.Items.Add(Btn("Open Presets…", ButtonRowRole.Other));
                row.Items.Add(Btn("Pick Another Recipe…", ButtonRowRole.Default));
                StatePanel panel = NewPanel(StatePanelShape.Error, buttons: row);
                Shown(panel, width: 390, height: 300);

                Assert.False(panel.IsCompact);
                AssertRowWrappedInsideHost(panel, row);
            } finally {
                Foreman.Mac.Kit.Appearance.ScaleContract.Apply(1.0);
            }
        }

        private static void AssertRowWrappedInsideHost(StatePanel panel, ButtonRow row) {
            Button[] buttons = [.. row.Items.OfType<Button>()];
            foreach (Button b in buttons) {
                Point topLeft = b.TranslatePoint(new Point(0, 0), panel) ?? default;
                Assert.True(topLeft.X >= 0, $"{b.Content}: left edge clipped at {topLeft.X}");
                Assert.True(topLeft.X + b.Bounds.Width <= panel.Bounds.Width + 0.5,
                    $"{b.Content}: right edge at {topLeft.X + b.Bounds.Width} overflows panel width {panel.Bounds.Width}");
                Assert.Equal(b.DesiredSize.Width, b.Bounds.Width, 0.5);
                double expectedX = (panel.Bounds.Width - b.Bounds.Width) / 2;
                Assert.True(System.Math.Abs(topLeft.X - expectedX) < 1,
                    $"{b.Content}: not centred on its line, expected X~{expectedX}, got {topLeft.X}");
            }

            Button def = Assert.Single(buttons, b => ButtonRow.GetRole(b) == ButtonRowRole.Default);
            Assert.True(buttons.Where(b => b != def).All(b => b.Bounds.Y < def.Bounds.Y),
                $"default button ({def.Content}) did not keep the last line");

            double controlHeight = ResolveDouble("Foreman.Size.ControlHeightRegular");
            double gap = ResolveDouble("Foreman.Space.Space2");
            Assert.True(row.Bounds.Height >= 2 * controlHeight + gap - 0.5,
                $"expected the row to wrap to two lines, got height {row.Bounds.Height}");
        }

        private static double ResolveDouble(string key) {
            Assert.True(Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value));
            return Assert.IsType<double>(value);
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

        private static Window ShownInFixture(KitFixture fixture, StatePanel panel, double width, double height) {
            fixture.Window.Width = width;
            fixture.Window.Height = height;
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();
            return fixture.Window;
        }

        private static StatePanel HostRowPanel(ButtonRow? hostRow, StatePanelShape shape = StatePanelShape.Error) =>
            new() {
                Theme = KitStatePanelTheme(),
                Shape = shape,
                Sentence = "Couldn’t save the image — the folder is read-only or no longer there.",
                ExternalButtons = hostRow,
            };

        [AvaloniaFact]
        public void ExternalButtons_ANonEmptyHostRow_SatisfiesTheRequiredButton() {
            using var fixture = new KitFixture();
            var hostRow = new ButtonRow { Theme = KitButtonRowTheme(), Items = { Btn("Cancel", ButtonRowRole.Cancel), Btn("Try Again") } };
            var hostPanel = new StackPanel { Children = { hostRow } };
            StatePanel panel = HostRowPanel(hostRow);
            fixture.Window.Content = new StackPanel { Children = { panel, hostPanel } };
            Dispatcher.UIThread.RunJobs();

            Assert.Same(hostPanel, hostRow.Parent);
            Assert.False(hostRow.IsCentered);
        }

        [AvaloniaFact]
        public void ExternalButtons_AnEmptyHostRow_ThrowsOnBuild() {
            using var fixture = new KitFixture();
            StatePanel panel = HostRowPanel(new ButtonRow { Theme = KitButtonRowTheme() });

            Assert.Throws<InvalidOperationException>(() => Shown(panel));
        }

        [AvaloniaFact]
        public void ExternalButtons_AHostButton_SatisfiesTheRequiredButton() {
            using var fixture = new KitFixture();
            var hostButton = Btn("Try Again");
            var panel = new StatePanel {
                Theme = KitStatePanelTheme(),
                Shape = StatePanelShape.Error,
                Sentence = "Couldn’t save the image.",
                ExternalButtons = hostButton,
            };

            Shown(panel);

            Assert.Empty(panel.GetVisualDescendants().OfType<Button>());
        }

        [AvaloniaFact]
        public void ExternalButtons_AndItsOwnRow_Together_ThrowOnBuild() {
            using var fixture = new KitFixture();
            StatePanel panel = HostRowPanel(OneButtonRow("Try Again"));
            panel.Buttons = OneButtonRow("Try Again");

            Assert.Throws<InvalidOperationException>(() => Shown(panel));
        }

        [AvaloniaFact]
        public void ExternalButtons_CompactAndFull_DrawNoButtonOfTheirOwn() {
            using var fixture = new KitFixture();
            var hostRow = new ButtonRow { Theme = KitButtonRowTheme(), Items = { Btn("Cancel", ButtonRowRole.Cancel), Btn("Try Again") } };
            StatePanel panel = HostRowPanel(hostRow);
            panel.Headline = "Couldn’t save the image";

            ShownInFixture(fixture, panel, 400, 90);
            Assert.True(panel.IsCompact);
            Assert.Empty(panel.GetVisualDescendants().OfType<Button>());

            ShownInFixture(fixture, panel, 400, 400);
            Assert.False(panel.IsCompact);
            Assert.Empty(panel.GetVisualDescendants().OfType<Button>());
            Assert.Null(hostRow.Parent);
            Assert.False(hostRow.IsCentered);
        }

        [AvaloniaFact]
        public void ForceCompact_KeepsTheCompactShape_InARegionAboveTheMinimum() {
            using var fixture = new KitFixture();
            StatePanel panel = NewPanel(StatePanelShape.Loading);
            panel.ForceCompact = true;
            Shown(panel, width: 400, height: 400);

            Assert.True(panel.IsCompact);

            panel.ForceCompact = false;
            Dispatcher.UIThread.RunJobs();

            Assert.False(panel.IsCompact);
        }

        private static StatePanel CompactStrip(string sentence, double? progress = null, StatePanelShape shape = StatePanelShape.Loading) {
            StatePanel panel = NewPanel(shape, sentence: sentence, progress: progress);
            panel.ForceCompact = true;
            return panel;
        }

        //The strip sits at the sheet's content width, as the host lays it out.
        private static Window ShownAsStrip(StatePanel panel, double width = 320) {
            var window = new Window { Width = width, Height = 400, Content = new StackPanel { Children = { panel } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static Grid CompactLayout(StatePanel panel) =>
            panel.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_CompactLayout");

        private static double LeftIn(Visual visual, Visual root) => visual.TranslatePoint(new Point(0, 0), root)!.Value.X;
        private static double TopIn(Visual visual, Visual root) => visual.TranslatePoint(new Point(0, 0), root)!.Value.Y;

        private const string FourLineSentence =
            "Importing “Space Age + Krastorio 2 + Bob’s Metals, Chemicals and Plates + Angel’s Refining + Angel’s Smelting + Angel’s Petrochemicals” — fluid boxes, recipe categories, technologies and every module limitation (5 of 7).";

        [AvaloniaFact]
        public void Compact_Sentence_WrapsToThreeLinesThenEllipsis_FullTextInTheTooltip() {
            using var fixture = new KitFixture();
            StatePanel panel = CompactStrip("Couldn’t import “Space Age” — the data dump from Factorio stopped partway through.", shape: StatePanelShape.Empty);
            ShownAsStrip(panel);

            TextBlock sentence = TextAt(panel, "PART_CompactSentence");
            Assert.Equal(TextWrapping.Wrap, sentence.TextWrapping);
            Assert.Equal(TextTrimming.CharacterEllipsis, sentence.TextTrimming);
            Assert.Equal(3, sentence.MaxLines);
            Assert.Contains("secondary", sentence.Classes);
            Assert.True(sentence.TextLayout.TextLines.Count > 1);
            Assert.True(panel.Bounds.Height > sentence.TextLayout.TextLines[0].Height * 1.5);
            Assert.Equal(panel.Sentence, ToolTip.GetTip(sentence));

            panel.Sentence = FourLineSentence;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, sentence.TextLayout.TextLines.Count);
            Assert.Equal(FourLineSentence, ToolTip.GetTip(sentence));
        }

        [AvaloniaFact]
        public void Compact_WithAKnownTotal_StacksTheSentenceOverAFullWidthBar_AndDropsTheSpinner() {
            using var fixture = new KitFixture();
            StatePanel panel = CompactStrip("Rendering the graph — 43 %.", progress: 0.43);
            ShownAsStrip(panel);

            Grid layout = CompactLayout(panel);
            Assert.DoesNotContain(layout.GetVisualDescendants(), v => v is SpinnerControl);
            ProgressBarControl bar = Assert.Single(layout.GetVisualDescendants().OfType<ProgressBarControl>());
            Assert.False(bar.IsIndeterminate);
            Assert.Equal(0.43, bar.Value);
            Assert.Equal(6, bar.Bounds.Height);
            Assert.Equal(panel.Bounds.Width, bar.Bounds.Width, 0.5);
            Assert.Equal(320, panel.Bounds.Width, 0.5);
            Border indicator = bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
            Assert.Equal(320 * 0.43, indicator.Width, 0.5);

            TextBlock sentence = TextAt(panel, "PART_CompactSentence");
            Assert.Equal(LeftIn(panel, panel), LeftIn(sentence, panel), 0.5);
            Assert.Equal(TopIn(sentence, panel) + sentence.Bounds.Height + 8, TopIn(bar, panel), 0.5);
            Assert.Equal(panel.Sentence, AutomationProperties.GetName(bar));
        }

        [AvaloniaFact]
        public void Compact_WithoutATotal_SpinnerInASixteenSlot_AlignedToTheFirstLine_AndNoBar() {
            using var fixture = new KitFixture();
            StatePanel panel = CompactStrip("Reading the save — waiting for Factorio to finish writing the mod list before technologies can be read.");
            ShownAsStrip(panel);

            Grid layout = CompactLayout(panel);
            Assert.DoesNotContain(layout.GetVisualDescendants(), v => v is ProgressBarControl);
            SpinnerControl spinner = SpinnerAt(panel, "PART_CompactSpinner");
            Assert.True(spinner.IsEffectivelyVisible);
            Assert.Equal(14, spinner.Bounds.Width, 0.5);
            Panel slot = panel.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_CompactGlyphBox");
            Assert.Equal(16, slot.Bounds.Width, 0.5);
            Assert.Equal(16, slot.Bounds.Height, 0.5);

            TextBlock sentence = TextAt(panel, "PART_CompactSentence");
            Assert.True(sentence.TextLayout.TextLines.Count > 1);
            Assert.Equal(24, LeftIn(sentence, panel) - LeftIn(panel, panel), 0.5);
            Assert.Equal(TopIn(sentence, panel) + 0.5, TopIn(slot, panel), 0.5);
        }

        [AvaloniaFact]
        public void Compact_Loading_NeverShrinksWithinOneOperation_AndResetsOnceHidden() {
            using var fixture = new KitFixture();
            StatePanel panel = CompactStrip(FourLineSentence, progress: 0.2);
            var host = new StackPanel { Children = { panel } };
            var window = new Window { Width = 320, Height = 400, Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            double tallest = panel.Bounds.Height;

            panel.Sentence = "Importing — recipes (6 of 7).";
            panel.Progress = 0.9;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(tallest, panel.Bounds.Height, 0.5);

            host.Children.Remove(panel);
            Dispatcher.UIThread.RunJobs();
            host.Children.Add(panel);
            Dispatcher.UIThread.RunJobs();
            Assert.True(panel.Bounds.Height < tallest - 1);
        }

        [AvaloniaFact]
        public void Compact_ProgressBackToNullMidOperation_KeepsTheBarAtItsLastValue() {
            using var fixture = new KitFixture();
            StatePanel panel = CompactStrip("Importing “Space Age” — recipes (3 of 7).", progress: 0.43);
            ShownAsStrip(panel);

            panel.Progress = null;
            Dispatcher.UIThread.RunJobs();

            ProgressBarControl bar = Assert.Single(CompactLayout(panel).GetVisualDescendants().OfType<ProgressBarControl>());
            Assert.True(bar.IsEffectivelyVisible);
            Assert.Equal(0.43, bar.Value);
            Assert.DoesNotContain(CompactLayout(panel).GetVisualDescendants(), v => v is SpinnerControl);
        }

        private const string OneLineSentence = "Importing — recipes (1 of 7).";
        private const string TwoLineSentence = "Importing “Space Age” — recipes, technologies and module limits (4 of 7).";

        [AvaloniaTheory]
        [InlineData(OneLineSentence, TwoLineSentence)]
        [InlineData(TwoLineSentence, OneLineSentence)]
        public void Compact_ASentenceChangingItsLineCount_MovesTheBar_WithoutCuttingTheFillEase(string before, string after) {
            using var fixture = new KitFixture();
            StatePanel panel = CompactStrip(before, progress: 0.2);
            ShownAsStrip(panel);
            ProgressBarControl bar = Assert.Single(CompactLayout(panel).GetVisualDescendants().OfType<ProgressBarControl>());
            TextBlock sentence = TextAt(panel, "PART_CompactSentence");
            int linesBefore = sentence.TextLayout.TextLines.Count;
            double lineHeight = sentence.TextLayout.TextLines[0].Height;
            double topBefore = TopIn(bar, panel);

            panel.Sentence = after;
            panel.Progress = 0.8;
            Dispatcher.UIThread.RunJobs();

            int linesAfter = sentence.TextLayout.TextLines.Count;
            Assert.Equal(1, Math.Abs(linesAfter - linesBefore));
            double moved = TopIn(bar, panel) - topBefore;
            Assert.Equal(linesAfter - linesBefore, Math.Sign(moved));
            Assert.InRange(Math.Abs(moved), 1, lineHeight + 0.5);
            Border fill = bar.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
            Assert.True(fill.Width < 320 * 0.8 - 1);

            AnimationClock.Settle(400);
            Assert.Equal(320 * 0.8, fill.Width, 0.5);
        }

        [AvaloniaFact]
        public void Compact_ProgressNeverMovesBackwardWithinOneOperation() {
            using var fixture = new KitFixture();
            StatePanel panel = CompactStrip("Importing “Space Age” — recipes (3 of 7).", progress: 0.6);
            ShownAsStrip(panel);

            panel.Progress = 0.4;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0.6, Assert.Single(CompactLayout(panel).GetVisualDescendants().OfType<ProgressBarControl>()).Value);
        }
    }
}
