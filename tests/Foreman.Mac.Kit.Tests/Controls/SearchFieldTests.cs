using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;
using Spinner = Foreman.Mac.Kit.Controls.Spinner;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class SearchFieldTests {
        private static ControlTheme KitSearchFieldTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.SearchField", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static SearchField Themed() => new() { Theme = KitSearchFieldTheme() };

        private static Path SearchGlyph(SearchField field) =>
            field.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_SearchGlyph");

        private static Button ClearButton(SearchField field) =>
            field.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Clear");

        [AvaloniaFact]
        public void KitSearchField_IsAControlThemeTargetingSearchField() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(SearchField), KitSearchFieldTheme().TargetType);
        }

        [AvaloniaFact]
        public void Widths_ComeFromMetricsTokens() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Metrics.SearchFieldMinWidth", Application.Current.ActualThemeVariant, out object? min);
            Application.Current!.TryGetResource("Foreman.Metrics.SearchFieldNaturalWidth", Application.Current.ActualThemeVariant, out object? natural);
            Application.Current!.TryGetResource("Foreman.Radius.Pill", Application.Current.ActualThemeVariant, out object? radius);

            Assert.Equal(Assert.IsType<double>(min), field.MinWidth);
            Assert.Equal(Assert.IsType<double>(natural), field.Width);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), field.CornerRadius);
        }

        [AvaloniaFact]
        public void Placeholder_IsExactlySearch() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            Assert.Equal("Search", field.PlaceholderText);
        }

        [AvaloniaFact]
        public void LeadingGlyph_IsSearch13AndStaysTertiaryWhenFilled() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Kit.Glyph.Search.13", null, out object? geometry);
            Application.Current!.TryGetResource("Foreman.Size.SearchFieldGlyph", Application.Current.ActualThemeVariant, out object? size);

            Path glyph = SearchGlyph(field);
            Assert.Equal(((Geometry)geometry!).Bounds, glyph.Data!.Bounds);
            Assert.Equal(Assert.IsType<double>(size), glyph.Width);
            Assert.Null(glyph.Fill);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), glyph.Stroke?.ToImmutable());

            field.Text = "electric furnace";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), glyph.Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void ClearButton_OnlyVisibleWhenFilledAndHasAtLeastA16PtHitBox() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Button clear = ClearButton(field);
            Assert.Equal(0, clear.Opacity);

            fixture.StateOf(field).Type("iron");
            AnimationClock.Settle();

            Assert.Equal(1, clear.Opacity);
            Assert.True(clear.Width >= 16);
            Assert.True(clear.Height >= 16);
        }

        [AvaloniaFact]
        public void ClearButton_ClickClearsTextAndInvokesClearCommand() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            bool invoked = false;
            field.ClearCommand = new RelayCommand(() => invoked = true);
            field.Text = "belt";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(ClearButton(field)).Click();

            Assert.Equal(string.Empty, field.Text);
            Assert.True(invoked);
        }

        [AvaloniaFact]
        public void ClearButtonGlyphs_RenderAtNativeSizeSoTheKnockoutStaysInsideTheDisc() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            field.Text = "belt";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Path disc = ClearButton(field).GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Disc");
            Path knockout = ClearButton(field).GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Knockout");

            //Both glyphs are authored on the 16pt grid already; stretching would blow the knockout past the disc.
            Assert.Equal(Stretch.None, disc.Stretch);
            Assert.Equal(Stretch.None, knockout.Stretch);
        }

        [AvaloniaFact]
        public void Escape_OnNonEmptyField_ClearsWithoutMovingFocus() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            field.Text = "furnace";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(field).Focus();

            fixture.StateOf(field).KeyDown(Key.Escape);

            Assert.Equal(string.Empty, field.Text);
            Assert.True(field.IsFocused);
        }

        [AvaloniaFact]
        public void Escape_OnEmptyField_ResignsFocus() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(field).Focus();
            Assert.True(field.IsFocused);

            fixture.StateOf(field).KeyDown(Key.Escape);

            Assert.False(field.IsFocused);
        }

        [AvaloniaFact]
        public void DownArrow_RaisesMoveDownRequestedAndKeepsText() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            field.Text = "belt";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(field).Focus();

            bool raised = false;
            field.MoveDownRequested += (_, _) => raised = true;

            fixture.StateOf(field).KeyDown(Key.Down);

            Assert.True(raised);
            Assert.Equal("belt", field.Text);
        }

        [AvaloniaFact]
        public void Return_RaisesSearchCommittedOnceAndKeepsText() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            field.Text = "belt";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(field).Focus();

            int raised = 0;
            field.SearchCommitted += (_, _) => raised++;

            fixture.StateOf(field).KeyDown(Key.Return);

            Assert.Equal(1, raised);
            Assert.Equal("belt", field.Text);
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;

            field.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), field.Opacity);
        }

        [AvaloniaFact]
        public void Placeholder_HidesWhileTypingAndReturnsAfterEscapeClears() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(field).Focus();

            var placeholder = field.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Placeholder");
            Assert.True(placeholder.IsVisible);

            fixture.StateOf(field).Type("iron");

            Assert.False(placeholder.IsVisible);
            Assert.Equal("iron", field.Text);

            fixture.StateOf(field).KeyDown(Key.Escape);

            Assert.True(placeholder.IsVisible);
        }

        [AvaloniaFact]
        public void SelectAll_ShowsAccentSoftHighlightAcrossTheWholeText() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
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
            SearchField field = Themed();
            field.Text = "iron plate";
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(field).Drag(
                new Point(10, field.Bounds.Height / 2),
                new Point(field.Bounds.Width - 10, field.Bounds.Height / 2));

            Assert.NotEqual(field.SelectionStart, field.SelectionEnd);
        }

        private static Viewbox LeadingGlyphBox(SearchField field) =>
            field.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_LeadingGlyphBox");

        private static Spinner LoadingSpinner(SearchField field) =>
            field.GetVisualDescendants().OfType<Spinner>().Single(s => s.Name == "PART_LoadingSpinner");

        [AvaloniaFact]
        public void IsLoading_DoesNotSwapDuringTheGracePeriod() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            field.IsLoading = true;
            Dispatcher.UIThread.RunJobs();

            Assert.True(LeadingGlyphBox(field).IsVisible);
            Assert.False(LoadingSpinner(field).IsVisible);
        }

        [AvaloniaFact]
        public void IsLoading_SwapsToAnEighteenPointSpinnerAfterTheStatePanelDelay() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            field.IsLoading = true;
            AnimationClock.Settle(450);

            Assert.False(LeadingGlyphBox(field).IsVisible);
            Spinner spinner = LoadingSpinner(field);
            Assert.True(spinner.IsVisible);

            Application.Current!.TryGetResource("Foreman.Size.Spinner", Application.Current.ActualThemeVariant, out object? size);
            Assert.Equal(Assert.IsType<double>(size), spinner.Diameter);
        }

        [AvaloniaFact]
        public void NonLoadingField_SpinnerCarriesNoAnimatingClass() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("animating", LoadingSpinner(field).Classes);
        }

        [AvaloniaFact]
        public void NonLoadingField_SpinnerDoesNotRegainTheAnimatingClassAfterReattach() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            fixture.Window.Content = null;
            Dispatcher.UIThread.RunJobs();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain("animating", LoadingSpinner(field).Classes);
        }

        [AvaloniaFact]
        public void LoadingField_SpinnerCarriesTheAnimatingClassOnlyAfterTheGracePeriod() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            field.IsLoading = true;
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("animating", LoadingSpinner(field).Classes);

            AnimationClock.Settle(450);
            Assert.Contains("animating", LoadingSpinner(field).Classes);
        }

        [AvaloniaFact]
        public void IsLoading_SetFalseBeforeTheGracePeriod_NeverSwaps() {
            using var fixture = new KitFixture();
            SearchField field = Themed();
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            field.IsLoading = true;
            Dispatcher.UIThread.RunJobs();
            field.IsLoading = false;
            AnimationClock.Settle(450);

            Assert.True(LeadingGlyphBox(field).IsVisible);
            Assert.False(LoadingSpinner(field).IsVisible);
        }

        private sealed class RelayCommand(System.Action execute) : System.Windows.Input.ICommand {
            public event System.EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => execute();
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class SearchFieldScaleTests {
        [AvaloniaFact]
        public void Widths_TrackScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.SearchField", null, out object? theme));
            var field = new SearchField { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(120, field.MinWidth);
            Assert.Equal(220, field.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(180, field.MinWidth);
            Assert.Equal(330, field.Width);
        }

        [AvaloniaFact]
        public void SearchGlyphViewbox_TracksScaleContractWhileThePathStaysAtItsMasterSize() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.SearchField", null, out object? theme));
            var field = new SearchField { Theme = Assert.IsType<ControlTheme>(theme) };
            fixture.Window.Content = field;
            Dispatcher.UIThread.RunJobs();

            Path glyph = field.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_SearchGlyph");
            Viewbox box = field.GetVisualDescendants().OfType<Viewbox>().Single();
            Assert.Equal(13, box.Bounds.Width);
            Assert.Equal(13, glyph.Bounds.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(20, box.Bounds.Width);
            Assert.Equal(13, glyph.Bounds.Width);
        }
    }
}
