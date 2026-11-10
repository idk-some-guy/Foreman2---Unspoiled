using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitPaletteRowTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitPaletteRowTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PaletteRow", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ControlTheme KitPalettePanelTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PalettePanel", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static PaletteRow NewRow(string label = "Iron plate", string kind = "item") =>
            new() { Theme = KitPaletteRowTheme(), Label = label, Kind = kind, Icon = Brushes.SteelBlue };

        private static Window Shown(Control content, double width = 320, double height = 240) {
            var window = new Window { Width = width, Height = height, Content = content };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        private static readonly PaletteResult[] FourResults = [
            new("Iron plate", "item", Brushes.SteelBlue),
            new("Iron plate", "recipe", Brushes.SteelBlue),
            new("Iron plate (legendary)", "recipe", Brushes.SteelBlue),
            new("Casting iron plate", "recipe", Brushes.SteelBlue),
        ];

        private static PalettePanel NewPanel(params PaletteResult[] results) {
            var panel = new PalettePanel { Theme = KitPalettePanelTheme() };
            foreach (PaletteResult r in results)
                panel.Results.Add(r);
            return panel;
        }

        private static PaletteRow[] Rows(PalettePanel panel) =>
            [.. panel.GetVisualDescendants().OfType<PaletteRow>()];

        private static SearchField PlainSearchField() {
            Assert.True(Application.Current!.TryGetResource("Kit.SearchField", null, out object? theme));
            return new SearchField { Theme = Assert.IsType<ControlTheme>(theme) };
        }

        [AvaloniaFact]
        public void KitPaletteRow_IsAControlThemeTargetingPaletteRow() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(PaletteRow), KitPaletteRowTheme().TargetType);
        }

        [AvaloniaFact]
        public void RootBackground_FollowsTheControlsOwnBackgroundProperty() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            row.Background = Brushes.SteelBlue;
            Shown(row);

            Border frame = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            Assert.Equal(Brushes.SteelBlue.ToImmutable(), frame.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Height_ResolvesListRowHeight() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            Shown(row);

            Assert.Equal(26, row.Bounds.Height);
        }

        [AvaloniaFact]
        public void Icon_ResolvesPaletteRowIconSize() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            Shown(row);

            Border icon = row.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Background is not null && ReferenceEquals(b.Background, Brushes.SteelBlue));
            Assert.Equal(22, icon.Bounds.Width);
            Assert.Equal(22, icon.Bounds.Height);
        }

        [AvaloniaFact]
        public void Label_UsesCalloutTypography() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            Shown(row);

            TextBlock label = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
            Assert.Equal(12, label.FontSize);
            Assert.Equal("Iron plate", label.Text);
        }

        [AvaloniaFact]
        public void Kind_UsesCaptionTypography_AndNeverTruncates() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            Shown(row);

            TextBlock kind = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Kind");
            Assert.Equal(11, kind.FontSize);
            Assert.Equal(TextTrimming.None, kind.TextTrimming);
            Assert.Equal("item", kind.Text);
        }

        [AvaloniaFact]
        public void Click_RaisesActivated() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            bool activated = false;
            row.Activated += (_, _) => activated = true;
            fixture.StateOf(row).Click();

            Assert.True(activated);
        }

        [AvaloniaFact]
        public void SelectedAndKey_ShowsAccentFill_AndSemiBoldAccentText() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            row.IsSelected = true;
            Shown(row);
            AnimationClock.Settle();

            Border selectionFill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionFill");
            Border fill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
            TextBlock label = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");

            Assert.Equal(1, selectionFill.Opacity);
            Assert.Equal(0, fill.Opacity);
            Assert.Equal(FontWeight.SemiBold, label.FontWeight);
        }

        [AvaloniaFact]
        public void SelectedButNotKey_ShowsSelectionInactiveFill_WithNormalWeightText() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            row.IsSelected = true;
            row.IsKey = false;
            Shown(row);
            AnimationClock.Settle();

            Border selectionFill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionFill");
            Border fill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
            TextBlock label = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");

            Assert.Equal(0, selectionFill.Opacity);
            Assert.Equal(1, fill.Opacity);
            Assert.Equal(FontWeight.Normal, label.FontWeight);
        }

        [AvaloniaFact]
        public void SelectedWeight_ResolvesTheSameTokenListRowUses() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            row.IsSelected = true;
            Shown(row);
            AnimationClock.Settle();

            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontWeight", Application.Current.ActualThemeVariant, out object? weight);
            TextBlock label = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
            Assert.Equal(Assert.IsType<FontWeight>(weight), label.FontWeight);
        }

        [AvaloniaFact]
        public void LongLabel_ShrinksToFitTheRow_WhileKindKeepsItsDesiredWidth() {
            using var fixture = new KitFixture();
            const string longName = "A very long recipe name that will not fit in the row at all";

            PaletteRow narrow = NewRow(longName, "recipe");
            Shown(narrow, width: 160);
            PaletteRow wide = NewRow(longName, "recipe");
            Shown(wide, width: 500);

            TextBlock narrowLabel = narrow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
            TextBlock wideLabel = wide.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Label");
            TextBlock narrowKind = narrow.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Kind");
            TextBlock wideKind = wide.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Kind");

            Assert.True(narrowLabel.Bounds.Width < wideLabel.Bounds.Width);
            Assert.Equal(TextTrimming.CharacterEllipsis, narrowLabel.TextTrimming);
            Assert.Equal(narrowKind.Bounds.Width, wideKind.Bounds.Width, 1);
            Assert.Equal("recipe", narrowKind.Text);
        }

        [AvaloniaFact]
        public void Hover_IsSuppressedOnASelectedRow() {
            using var fixture = new KitFixture();
            PaletteRow row = NewRow();
            row.IsSelected = true;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            fixture.StateOf(row).Hover();
            AnimationClock.Settle();

            Border selectionFill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionFill");
            Border fill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
            Assert.Equal(1, selectionFill.Opacity);
            Assert.Equal(0, fill.Opacity);
        }

        [AvaloniaFact]
        public void KitPalettePanel_IsAControlThemeTargetingPalettePanel() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(PalettePanel), KitPalettePanelTheme().TargetType);
        }

        private static Border Root(PalettePanel panel) =>
            panel.GetVisualDescendants().OfType<Border>().Single(p => p.Name == "PART_Root");

        [AvaloniaFact]
        public void PalettePanel_BackgroundResolvesPaletteMaterial_AndSwapsToOpaqueUnderReduceTransparency() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            Assert.Equal(fixture.Brush("Foreman.Color.PaletteMaterial").ToImmutable(), Root(panel).Background?.ToImmutable());

            var hub = new AppearanceHub((Application)Application.Current!, fixture.Appearance);
            fixture.Appearance.ReduceTransparency = true;
            fixture.Appearance.RaiseChanged();

            Assert.Equal(fixture.Brush("Foreman.Color.PaletteMaterialOpaque").ToImmutable(), Root(panel).Background?.ToImmutable());

            hub.Dispose();
        }

        [AvaloniaFact]
        public void Width_ResolvesPaletteWidth() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            Assert.Equal(272, panel.Bounds.Width);
        }

        [AvaloniaTheory]
        [InlineData("Light")]
        [InlineData("Dark")]
        public void OutermostTemplateChild_IsAOnePointBorderStrongHairlineAtPopoverRadius_InsideThePaletteWidth(string variantName) {
            using var fixture = new KitFixture();
            ThemeVariant variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            PalettePanel panel = NewPanel(FourResults);
            Window window = Shown(panel);
            window.RequestedThemeVariant = variant;
            Dispatcher.UIThread.RunJobs();

            Border outer = Assert.IsType<Border>(panel.GetVisualChildren().Single());
            Assert.Same(Root(panel), outer);
            Assert.Equal(new Thickness(1), outer.BorderThickness);
            Assert.True(Application.Current!.TryGetResource("Foreman.Color.BorderStrong", variant, out object? strong));
            IBrush expected = strong is Color c ? new SolidColorBrush(c) : Assert.IsAssignableFrom<IBrush>(strong);
            Assert.Equal(expected.ToImmutable(), outer.BorderBrush?.ToImmutable());
            Assert.True(Application.Current.TryGetResource("Foreman.Radius.Popover", variant, out object? radius));
            Assert.Equal(Assert.IsType<CornerRadius>(radius), outer.CornerRadius);
            Assert.True(Application.Current.TryGetResource("Foreman.Size.PaletteWidth", variant, out object? width));
            Assert.Equal(Assert.IsType<double>(width), panel.Bounds.Width);
            Assert.Equal(panel.Bounds.Width, outer.Bounds.Width);
        }

        [AvaloniaFact]
        public void FirstRowIsSelectedOnOpen_AndReturnCommitsWithoutAnyArrowPress() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            Assert.Equal(FourResults[0], panel.SelectedItem);

            PaletteResult? committed = null;
            panel.Committed += (_, item) => committed = item;
            fixture.StateOf(panel.Search).KeyDown(Key.Return);

            Assert.Equal(FourResults[0], committed);
        }

        [AvaloniaFact]
        public void ArrowDown_MovesSelection_WrappingBackToTheFirstRow() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            for (int i = 1; i < FourResults.Length; i++) {
                fixture.StateOf(panel.Search).KeyDown(Key.Down);
                Assert.Equal(FourResults[i], panel.SelectedItem);
            }
            fixture.StateOf(panel.Search).KeyDown(Key.Down);
            Assert.Equal(FourResults[0], panel.SelectedItem);
        }

        [AvaloniaFact]
        public void ArrowUp_FromTheFirstRow_WrapsToTheLastRow() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            fixture.StateOf(panel.Search).KeyDown(Key.Up);

            Assert.Equal(FourResults[^1], panel.SelectedItem);
        }

        [AvaloniaFact]
        public void OptionReturn_CommitsTheSelectionAsASource() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            PaletteResult? committed = null;
            panel.CommittedAsSource += (_, item) => committed = item;
            fixture.StateOf(panel.Search).KeyDown(Key.Return, KeyModifiers.Alt);

            Assert.Equal(FourResults[0], committed);
        }

        [AvaloniaFact]
        public void OptionClick_CommitsTheRowAsASource_AndAPlainClickCommitsNormally() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            PalettePanel panel = NewPanel(FourResults);
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();
            PaletteResult? plain = null;
            PaletteResult? asSource = null;
            panel.Committed += (_, item) => plain = item;
            panel.CommittedAsSource += (_, item) => asSource = item;

            fixture.StateOf(Rows(panel)[2]).Click(RawInputModifiers.Alt);

            Assert.Equal(FourResults[2], asSource);
            Assert.Null(plain);

            asSource = null;
            fixture.StateOf(Rows(panel)[1]).Click();

            Assert.Equal(FourResults[1], plain);
            Assert.Null(asSource);
        }

        [AvaloniaFact]
        public void Escape_RaisesCloseRequested() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            bool closed = false;
            panel.CloseRequested += (_, _) => closed = true;
            fixture.StateOf(panel.Search).KeyDown(Key.Escape);

            Assert.True(closed);
        }

        [AvaloniaFact]
        public void NoOpRequery_WithAnIdenticalResultSet_ReplaysNothing() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);
            AnimationClock.Settle();

            PaletteRow[] before = Rows(panel);
            Assert.Equal(FourResults.Length, before.Length);

            panel.Results.Clear();
            foreach (PaletteResult r in FourResults)
                panel.Results.Add(r);
            Dispatcher.UIThread.RunJobs();

            PaletteRow[] after = Rows(panel);
            Assert.Equal(before.Length, after.Length);
            for (int i = 0; i < before.Length; i++)
                Assert.Same(before[i], after[i]);
        }

        [AvaloniaFact]
        public void EnteringRow_StartsAtOpacityZero_AndSettlesToOne() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults[0]);
            Shown(panel);
            AnimationClock.Settle();

            panel.Results.Add(FourResults[1]);
            Dispatcher.UIThread.RunJobs();

            PaletteRow entering = Rows(panel)[1];
            Assert.Equal(0, entering.Opacity);

            AnimationClock.Settle();
            Assert.Equal(1, entering.Opacity);
        }

        [AvaloniaFact]
        public void RequeryWithAnUnchangedFirstItem_StillSelectsTheNewTopRow() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults[0], FourResults[1]);
            Shown(panel);
            AnimationClock.Settle();

            panel.Results.Clear();
            panel.Results.Add(FourResults[0]);
            panel.Results.Add(FourResults[2]);
            Dispatcher.UIThread.RunJobs();

            Assert.True(Rows(panel)[0].IsSelected);
        }

        [AvaloniaFact]
        public void DetachBetweenEditAndPost_ReplaysNothing() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults[0]);
            Window window = Shown(panel);
            AnimationClock.Settle();

            panel.Results.Add(FourResults[1]);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();

            Assert.Single(Rows(panel));
        }

        [AvaloniaFact]
        public void ReattachAfterADetach_ResumesTheStalledRebuild() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults[0]);
            Window window = Shown(panel);
            AnimationClock.Settle();

            panel.Results.Add(FourResults[1]);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Single(Rows(panel));

            window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, Rows(panel).Length);
        }

        [AvaloniaFact]
        public void ListHostIsKey_InheritsFromTheHostingTree_AndPropagatesToRows() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Window window = Shown(panel);
            AnimationClock.Settle();

            Assert.All(Rows(panel), r => Assert.True(r.IsKey));

            window.SetValue(ListHost.IsKeyProperty, false);
            Dispatcher.UIThread.RunJobs();

            Assert.All(Rows(panel), r => Assert.False(r.IsKey));

            window.SetValue(ListHost.IsKeyProperty, true);
            Dispatcher.UIThread.RunJobs();

            Assert.All(Rows(panel), r => Assert.True(r.IsKey));
        }

        [AvaloniaFact]
        public void FlushSearch_WidthSpansThePanelInsideItsHairline() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            Assert.Equal(panel.Bounds.Width - 2, panel.Search.Bounds.Width);
            Assert.Equal(270, panel.Search.Bounds.Width);
        }

        [AvaloniaFact]
        public void FlushSearch_FontSizeResolvesBodyToken_PlainFieldStaysCallout() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            Application.Current!.TryGetResource("Foreman.Type.Body.FontSize", Application.Current.ActualThemeVariant, out object? bodySize);
            Assert.Equal(Assert.IsType<double>(bodySize), panel.Search.FontSize);

            SearchField plain = PlainSearchField();
            Shown(plain);
            Application.Current!.TryGetResource("Foreman.Type.Callout.FontSize", Application.Current.ActualThemeVariant, out object? calloutSize);
            Assert.Equal(Assert.IsType<double>(calloutSize), plain.FontSize);
        }

        [AvaloniaFact]
        public void HeaderAndFooterRules_MatchTheBorderTokenAndPadding() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            StackPanel footer = panel.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Footer");
            Border footerFrame = Assert.IsType<Border>(footer.Parent);
            Border headerBorder = panel.Search.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");

            Assert.Equal(new Thickness(0, 0, 0, 1), panel.Search.BorderThickness);
            Assert.Equal(fixture.Brush("Foreman.Color.Border").ToImmutable(), headerBorder.BorderBrush?.ToImmutable());
            Assert.Equal(12, panel.Search.Padding.Left);
            Assert.Equal(new Thickness(0, 1, 0, 0), footerFrame.BorderThickness);
            Assert.Equal(fixture.Brush("Foreman.Color.Border").ToImmutable(), footerFrame.BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void FlushHover_KeepsTheHeaderRuleBrush() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Border headerBorder = panel.Search.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            fixture.StateOf(panel.Search).Hover();
            AnimationClock.Settle();

            Assert.True(panel.Search.IsPointerOver);
            Assert.Equal(fixture.Brush("Foreman.Color.Border").ToImmutable(), headerBorder.BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void PlainSearchField_HoverSwapsTheRuleToBorderStrong() {
            using var fixture = new KitFixture();
            SearchField field = PlainSearchField();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Border borderElement = field.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            fixture.StateOf(field).Hover();
            AnimationClock.Settle();

            Assert.True(field.IsPointerOver);
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), borderElement.BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void LeavingRow_FadesOverCrossFadeDuration_ThenTheWrapIsRemoved() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults[0], FourResults[1]);
            Shown(panel);
            AnimationClock.Settle();

            PaletteRow leaving = Rows(panel)[1];
            panel.Results.RemoveAt(1);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(leaving, panel.GetVisualDescendants().OfType<PaletteRow>());
            Assert.IsType<StackPanel>(leaving.Parent);

            AnimationClock.Settle();

            Assert.DoesNotContain(leaving, panel.GetVisualDescendants().OfType<PaletteRow>());
            Assert.Equal(0, leaving.Opacity);
        }

        [AvaloniaFact]
        public void HeightReflow_ClearsTheExplicitHeightAfterSettling() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults[0]);
            Shown(panel);
            AnimationClock.Settle();

            panel.Results.Add(FourResults[1]);
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.True(double.IsNaN(panel.Height));
        }

        [AvaloniaFact]
        public void RealRequery_NewRowsCarryAStaggeredEntranceFade() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults[0]);
            Shown(panel);
            AnimationClock.Settle();

            panel.Results.Add(FourResults[1]);
            Dispatcher.UIThread.RunJobs();

            PaletteRow[] rows = Rows(panel);
            Assert.Equal(2, rows.Length);
            var opacity = Assert.Single(rows[1].Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.EntranceDuration.TotalMilliseconds, opacity.Duration.TotalMilliseconds);
            Assert.Equal(Motion.StaggerDelay(1), opacity.Delay);
        }

        [AvaloniaFact]
        public void ReduceMotion_YieldsEmptyTransitions_AndFullOpacity() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);

            foreach (PaletteRow row in Rows(panel)) {
                Assert.Empty(row.Transitions!);
                Assert.Equal(1, row.Opacity);
            }
        }

        [AvaloniaFact]
        public void LiveReduceMotion_RePushesTransitionsOnAnAlreadyOpenPanel() {
            using var fixture = new KitFixture();
            AppearanceHub? previousCurrent = AppearanceHub.Current;
            var hub = new AppearanceHub(Application.Current!, fixture.Appearance);
            AppearanceHub.Current = hub;
            try {
                PalettePanel panel = NewPanel(FourResults);
                Shown(panel);

                Assert.NotEmpty(Rows(panel)[0].Transitions!);

                fixture.Appearance.ReduceMotion = true;
                fixture.Appearance.RaiseChanged();
                Dispatcher.UIThread.RunJobs();

                Assert.All(Rows(panel), r => Assert.Empty(r.Transitions!));
                Assert.All(Rows(panel), r => Assert.Equal(1, r.Opacity));

                fixture.Appearance.ReduceMotion = false;
                fixture.Appearance.RaiseChanged();
                Dispatcher.UIThread.RunJobs();

                Assert.All(Rows(panel), r => Assert.NotEmpty(r.Transitions!));
            } finally {
                hub.Dispose();
                AppearanceHub.Current = previousCurrent;
            }
        }

        [AvaloniaFact]
        public void NoResults_RendersACompactStatePanel_AndKeepsTheSearchHeader() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel();
            panel.NoResultsButtons = HostButtons("Browse All…");
            panel.Search.Text = "beacn";
            Shown(panel);
            Dispatcher.UIThread.RunJobs();

            StatePanel state = panel.GetVisualDescendants().OfType<StatePanel>().Single();
            Assert.Equal(StatePanelShape.NoResults, state.Shape);
            Assert.True(state.IsCompact);
            Assert.True(panel.Search.IsVisible);
        }

        private static ButtonRow HostButtons(params string[] labels) {
            Assert.True(Application.Current!.TryGetResource("Kit.ButtonRow", null, out object? rowTheme));
            var row = new ButtonRow { Theme = Assert.IsType<ControlTheme>(rowTheme) };
            for (int i = 0; i < labels.Length; i++) {
                var button = new Button { Content = labels[i] };
                ButtonRow.SetRole(button, i == labels.Length - 1 ? ButtonRowRole.Default : ButtonRowRole.Other);
                row.Items.Add(button);
            }
            return row;
        }

        [AvaloniaFact]
        public void NoResults_ShowsTheHostsHeadlineSentenceAndButtons() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel();
            panel.NoResultsHeadline = "No matches for “beacn”";
            panel.NoResultsSentence = "Nothing in this preset matches the search.";
            ButtonRow buttons = HostButtons("Browse All…");
            panel.NoResultsButtons = buttons;
            panel.Search.Text = "beacn";
            Shown(panel);
            Dispatcher.UIThread.RunJobs();

            StatePanel state = panel.GetVisualDescendants().OfType<StatePanel>().Single(s => s.IsEffectivelyVisible);
            Assert.Equal("No matches for “beacn”", state.Headline);
            Assert.Equal("Nothing in this preset matches the search.", state.Sentence);
            Assert.Same(buttons, state.Buttons);
            Assert.Contains(panel.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, "Browse All…") && b.IsEffectivelyVisible);
        }

        [AvaloniaFact]
        public void NoResults_CarriesNoBuiltInCopy() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel();
            panel.Search.Text = "beacn";
            Shown(panel);
            Dispatcher.UIThread.RunJobs();

            Assert.All(panel.GetVisualDescendants().OfType<StatePanel>(), s => {
                Assert.Null(s.Headline);
                Assert.Null(s.Buttons);
            });
        }

        [Fact]
        public void PalettePanelSource_HoldsNoSurfaceCopy() {
            string dir = AppContext.BaseDirectory;
            while (!System.IO.Directory.Exists(System.IO.Path.Combine(dir, "src", "Foreman.Mac.Kit")))
                dir = System.IO.Path.GetDirectoryName(dir) ?? throw new InvalidOperationException("repo root not found");
            string text = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "src", "Foreman.Mac.Kit", "Composites", "PalettePanel.cs"));
            Assert.DoesNotContain("Clear Search", text);
            Assert.DoesNotContain("No results", text);
        }

        [AvaloniaFact]
        public void HostCopy_ChangedWhileShown_UpdatesTheNoResultsPanel() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel();
            panel.NoResultsButtons = HostButtons("Browse All…");
            panel.Search.Text = "beacn";
            Shown(panel);
            panel.NoResultsSentence = "Only recipes that make Iron Plate are listed while a link is being dragged.";
            ButtonRow wider = HostButtons("Browse All…", "Remove Constraint");
            panel.NoResultsButtons = wider;
            Dispatcher.UIThread.RunJobs();

            StatePanel state = panel.GetVisualDescendants().OfType<StatePanel>().Single(s => s.IsEffectivelyVisible);
            Assert.Equal("Only recipes that make Iron Plate are listed while a link is being dragged.", state.Sentence);
            Assert.Same(wider, state.Buttons);
        }

        private static bool IsOnScreen(Control control) => control.IsEffectivelyVisible && TopLevel.GetTopLevel(control) is not null;

        [AvaloniaFact]
        public void EmptyContent_ShowsWhileTheQueryIsEmpty_AndGivesWayToNoResultsOnceTyped() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel();
            var empty = new TextBlock { Text = "Search to add a node" };
            panel.EmptyContent = empty;
            panel.NoResultsButtons = HostButtons("Browse All…");
            Shown(panel);
            Dispatcher.UIThread.RunJobs();

            Assert.True(IsOnScreen(empty));
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<StatePanel>(), s => s.IsEffectivelyVisible);

            panel.Search.Text = "beacn";
            Dispatcher.UIThread.RunJobs();

            Assert.False(IsOnScreen(empty));
            Assert.Contains(panel.GetVisualDescendants().OfType<StatePanel>(), s => s.IsEffectivelyVisible && s.Shape == StatePanelShape.NoResults);
        }

        [AvaloniaFact]
        public void EmptyContent_HidesOnceResultsArrive() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel();
            var empty = new TextBlock { Text = "Search to add a node" };
            panel.EmptyContent = empty;
            Shown(panel);
            foreach (PaletteResult r in FourResults)
                panel.Results.Add(r);
            Dispatcher.UIThread.RunJobs();

            Assert.False(IsOnScreen(empty));
            Assert.Equal(4, Rows(panel).Length);
        }

        [AvaloniaFact]
        public void EmptyContent_StatePanel_RendersCompactInsideThePanel() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel();
            var empty = new StatePanel { Shape = StatePanelShape.Empty, Headline = "Search to add a node", Sentence = "Type a few letters.", Buttons = HostButtons("Browse All…") };
            Assert.True(Application.Current!.TryGetResource("Kit.StatePanel", null, out object? stateTheme));
            empty.Theme = Assert.IsType<ControlTheme>(stateTheme);
            panel.EmptyContent = empty;
            Shown(panel);
            Dispatcher.UIThread.RunJobs();

            Assert.True(empty.IsCompact);
        }

        [AvaloniaFact]
        public void HeaderAccessory_SitsBetweenTheSearchFieldAndTheRows() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            var chip = new Border { Width = 120, Height = 20 };
            panel.HeaderAccessory = chip;
            Shown(panel);
            Dispatcher.UIThread.RunJobs();

            double searchBottom = panel.Search.TranslatePoint(new Point(0, panel.Search.Bounds.Height), panel)!.Value.Y;
            double chipTop = chip.TranslatePoint(new Point(0, 0), panel)!.Value.Y;
            double chipBottom = chip.TranslatePoint(new Point(0, chip.Bounds.Height), panel)!.Value.Y;
            double firstRowTop = Rows(panel)[0].TranslatePoint(new Point(0, 0), panel)!.Value.Y;
            Assert.True(chipTop >= searchBottom, $"accessory top {chipTop} above the field's bottom {searchBottom}");
            Assert.True(firstRowTop >= chipBottom, $"first row top {firstRowTop} above the accessory's bottom {chipBottom}");
        }

        [AvaloniaFact]
        public void HeaderAccessory_Cleared_LeavesNoGapUnderTheField() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);
            Dispatcher.UIThread.RunJobs();
            double before = Rows(panel)[0].TranslatePoint(new Point(0, 0), panel)!.Value.Y;

            panel.HeaderAccessory = new Border { Width = 120, Height = 20 };
            Dispatcher.UIThread.RunJobs();
            panel.HeaderAccessory = null;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, Rows(panel)[0].TranslatePoint(new Point(0, 0), panel)!.Value.Y, 0.5);
        }

        [AvaloniaFact]
        public void FooterHints_RenderAKeyCapAndDescriptionPerHint() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            panel.FooterHints.Add(new PaletteFooterHint { KeyText = "esc", KeyAccessibleName = "Escape", Description = "Close" });
            Shown(panel);
            Dispatcher.UIThread.RunJobs();

            KeyCap cap = panel.GetVisualDescendants().OfType<KeyCap>().Single();
            Assert.Equal("esc", cap.Text);
            Assert.Equal("Escape", AutomationProperties.GetName(cap));
        }

        [AvaloniaFact]
        public void Padding_MatchesTheFigure_WithNoWastedHeight() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            panel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
            Shown(panel);
            AnimationClock.Settle();

            Panel header = panel.GetVisualDescendants().OfType<Panel>().Single(p => p.Name == "PART_Header");
            StackPanel track = panel.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Track");
            StackPanel footer = panel.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Footer");
            Border footerFrame = Assert.IsType<Border>(footer.Parent);

            Assert.Equal(9, panel.Search.Padding.Top);
            Assert.Equal(new Thickness(6, 4, 6, 4), track.Margin);
            Assert.Equal(6, footer.Margin.Bottom);
            Assert.Equal(1 + header.Bounds.Height + track.Bounds.Height + 8 + footerFrame.Bounds.Height + 1, panel.Bounds.Height, 1);

            PaletteRow[] rows = Rows(panel);
            Assert.Equal(1, rows[1].Bounds.Top - rows[0].Bounds.Bottom);
        }

        [AvaloniaFact]
        public void Pumping_AfterSettling_ProducesNoFurtherLayouts() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults);
            Shown(panel);
            AnimationClock.Settle(300);

            int layouts = 0;
            panel.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);

            Assert.Equal(0, layouts);
        }

        [AvaloniaFact]
        public void Pumping_RequeryThroughSeveralResultSets_SettlesToABoundedLayoutCount() {
            using var fixture = new KitFixture();
            PalettePanel panel = NewPanel(FourResults[0]);
            Shown(panel);

            int layouts = 0;
            panel.LayoutUpdated += (_, _) => layouts++;

            foreach (PaletteResult[] step in new[] { FourResults[..2], FourResults[..3], FourResults, FourResults[..1] }) {
                panel.Results.Clear();
                foreach (PaletteResult r in step)
                    panel.Results.Add(r);
                AnimationClock.Settle();
            }

            Assert.True(layouts <= 40, $"PalettePanel laid out {layouts} times settling four requeries.");
        }

        [Collection(KitScaleGroup.Name)]
        public sealed class ScaleTests {
            [AvaloniaFact]
            public void PanelWidthAndRowMetrics_TrackScaleContract() {
                using var fixture = new KitFixture();
                PalettePanel panel = NewPanel(FourResults);
                Shown(panel);
                ScaleContract.Apply(1.5);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(408, panel.Bounds.Width);
                Assert.Equal(panel.Bounds.Width - 2, panel.Search.Bounds.Width);
                PaletteRow row = Rows(panel)[0];
                Assert.Equal(39, row.Bounds.Height);
                Border icon = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Icon");
                Assert.Equal(33, icon.Bounds.Width);
            }

            [AvaloniaFact]
            public void PanelWidth_FollowsAScaleChange_DuringAHeightReflow_InAnUnconstrainedHost() {
                using var fixture = new KitFixture();
                PalettePanel panel = NewPanel(FourResults[0]);
                var host = new Canvas();
                host.Children.Add(panel);
                Shown(host);
                AnimationClock.Settle();
                panel.Results.Add(FourResults[1]);
                Dispatcher.UIThread.RunJobs();

                ScaleContract.Apply(1.5);
                Dispatcher.UIThread.RunJobs();

                Assert.True(panel.TryFindResource("Foreman.Size.PaletteWidth", panel.ActualThemeVariant, out object? width));
                Assert.Equal(Assert.IsType<double>(width), panel.Bounds.Width, 0.5);
                Assert.Equal(panel.Bounds.Width, panel.DesiredSize.Width, 0.5);
            }
        }
    }
}
