using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    [Collection(KitScaleGroup.Name)]
    public sealed class KitColorWellTests {
        private static readonly Color Orange = Color.Parse("#FF9500");

        private static ControlTheme WellTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ColorWell", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ColorWell Well(Color color, bool allowsNone = false, bool allowsAlpha = false) => new() {
            Theme = WellTheme(),
            Color = color,
            AllowsNone = allowsNone,
            AllowsAlpha = allowsAlpha,
            AccessibleLabel = "Fill",
            Margin = new Thickness(20),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };

        private static ColorWell Hosted(KitFixture fixture, ColorWell well) {
            var host = new PopoverHost();
            host.Children.Add(well);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();
            return well;
        }

        private static T Part<T>(Control control, string name) where T : Control =>
            control.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

        private static object? Token(string key) {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value);
            return value;
        }

        private static Point Offset(Visual child, Visual ancestor) => child.TranslatePoint(new Point(0, 0), ancestor)!.Value;

        [AvaloniaFact]
        public void Box_Is44By22_AtRadiusControl_OnControlBackground_WithAOnePointStrongBorder() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));

            Assert.Equal(new Size(44, 22), well.Bounds.Size);
            Border frame = Part<Border>(well, "PART_Frame");
            Assert.Equal(Token("Foreman.Radius.Control"), frame.CornerRadius);
            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackground").ToImmutable(), frame.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), frame.BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(1), frame.BorderThickness);
        }

        [AvaloniaFact]
        public void Hover_FadesInControlBackgroundHover_OverHoverDuration() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));
            Border state = Part<Border>(well, "PART_StateFill");
            Assert.Equal(0, state.Opacity);

            fixture.StateOf(well).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), state.Background?.ToImmutable());
            DoubleTransition fade = Assert.IsType<DoubleTransition>(Assert.Single(state.Transitions!));
            Assert.Equal(Visual.OpacityProperty, fade.Property);
            Assert.Equal(Motion.HoverDuration, fade.Duration);
            AnimationClock.Settle(300);
            Assert.Equal(1, state.Opacity);
        }

        [AvaloniaFact]
        public void Open_ShowsControlBackgroundPressed() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));

            well.IsOpen = true;
            AnimationClock.Settle(300);

            Border state = Part<Border>(well, "PART_StateFill");
            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundPressed").ToImmutable(), state.Background?.ToImmutable());
            Assert.Equal(1, state.Opacity);
        }

        [AvaloniaFact]
        public void Disabled_DrawsAtDisabledOpacity() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));

            well.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Token("Foreman.Opacity.Disabled"), well.Opacity);
        }

        [AvaloniaFact]
        public void Swatch_Is26By16_InsetTwoInsideTheBorder_AtRadiusSwatch_WithTheSwatchStroke() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));

            Border swatch = Part<Border>(well, "PART_Swatch");
            Assert.Equal(new Size(26, 16), swatch.Bounds.Size);
            Assert.Equal(new Point(2, 2), Offset(swatch, well) - new Point(1, 1));
            Assert.Equal(Token("Foreman.Radius.Swatch"), swatch.CornerRadius);
            Border stroke = Part<Border>(well, "PART_SwatchStroke");
            Assert.Equal(fixture.Brush("Foreman.Color.SwatchStroke").ToImmutable(), stroke.BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(1), stroke.BorderThickness);
            Assert.Equal(swatch.Bounds.Size, stroke.Bounds.Size);
            Border fill = Part<Border>(well, "PART_Fill");
            Assert.Equal(Orange, Assert.IsAssignableFrom<ISolidColorBrush>(fill.Background).Color);
        }

        [AvaloniaFact]
        public void PartialAlpha_WithAllowsAlpha_DrawsTheCheckerUnderTheColour() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Color.FromArgb(102, 0x00, 0x7A, 0xFF), allowsNone: true, allowsAlpha: true));

            SwatchChecker checker = Part<SwatchChecker>(well, "PART_Checker");
            Assert.True(checker.IsVisible);
            Assert.Equal(4, checker.SquareSize);
            Assert.Equal(Token("Foreman.Color.SwatchCheckerA"), checker.LightColor);
            Assert.Equal(Token("Foreman.Color.SwatchCheckerB"), checker.DarkColor);
            Assert.Equal(new Size(26, 16), checker.Bounds.Size);
            Assert.Equal((byte)102, Assert.IsAssignableFrom<ISolidColorBrush>(Part<Border>(well, "PART_Fill").Background).Color.A);

            well.Color = Orange;
            Dispatcher.UIThread.RunJobs();
            Assert.False(checker.IsVisible);
        }

        [AvaloniaFact]
        public void PartialAlpha_WithoutAllowsAlpha_DrawsNoChecker() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Color.FromArgb(102, 0x00, 0x7A, 0xFF)));

            Assert.False(Part<SwatchChecker>(well, "PART_Checker").IsVisible);
        }

        [AvaloniaFact]
        public void AlphaZero_WithAllowsNone_ShowsTheNoneSlashOnWellBackground_AndNoSwatchFill() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Color.FromArgb(0, 0x00, 0x7A, 0xFF), allowsNone: true, allowsAlpha: true));

            Assert.False(Part<Border>(well, "PART_Fill").IsVisible);
            Assert.False(Part<SwatchChecker>(well, "PART_Checker").IsVisible);
            Border noneFill = Part<Border>(well, "PART_NoneFill");
            Assert.True(noneFill.IsVisible);
            Assert.Equal(fixture.Brush("Foreman.Color.WellBackground").ToImmutable(), noneFill.Background?.ToImmutable());
            Viewbox noneBox = Part<Viewbox>(well, "PART_NoneBox");
            Assert.True(noneBox.IsVisible);
            Path slash = noneBox.GetVisualDescendants().OfType<Path>().Single();
            Assert.Equal(Token("Kit.Glyph.NoneSlash.12"), slash.Data);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), slash.Stroke?.ToImmutable());
            Border swatch = Part<Border>(well, "PART_Swatch");
            Point swatchCentre = Offset(swatch, well) + new Point(swatch.Bounds.Width / 2, swatch.Bounds.Height / 2);
            Point glyphCentre = Offset(noneBox, well) + new Point(noneBox.Bounds.Width / 2, noneBox.Bounds.Height / 2);
            Assert.Equal(swatchCentre, glyphCentre);
        }

        [AvaloniaFact]
        public void Chevron_SitsInAFourteenPointTrailingColumn_InTextSecondary() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));

            Border column = Part<Border>(well, "PART_ChevronColumn");
            Assert.Equal(14, column.Bounds.Width);
            Assert.Equal(well.Bounds.Width - 1, Offset(column, well).X + column.Bounds.Width, 3);
            Path chevron = Part<Path>(well, "PART_Chevron");
            Assert.Equal(Token("Kit.Glyph.Chevron.10"), chevron.Data);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), chevron.Fill?.ToImmutable());
        }

        [AvaloniaFact]
        public void Click_OnTheChevronColumn_OpensLikeAClickOnTheSwatch() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));

            fixture.StateOf(Part<Border>(well, "PART_ChevronColumn")).Click();

            Assert.True(well.IsOpen);
        }

        [AvaloniaFact]
        public void Color_BindsTwoWayByDefault() {
            Assert.Equal(Avalonia.Data.BindingMode.TwoWay, ColorWell.ColorProperty.GetMetadata(typeof(ColorWell)).DefaultBindingMode);
        }

        [AvaloniaTheory]
        [InlineData(Key.Space)]
        [InlineData(Key.Enter)]
        [InlineData(Key.Down)]
        public void Keyboard_SpaceReturnOrDown_Opens(Key key) {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));
            Assert.True(well.Focusable);
            Assert.True(well.IsTabStop);

            fixture.StateOf(well).Focus();
            fixture.StateOf(well).KeyDown(key);

            Assert.True(well.IsOpen);
        }

        [AvaloniaFact]
        public void Opening_FocusesTheSelectedSwatch_OrTheFirstCellWhenOffPalette() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));

            well.IsOpen = true;
            AnimationClock.Settle(300);

            PickGrid grid = Assert.IsType<PickGrid>(well.Popover!.Grid);
            Assert.True(grid.IsFocused);
            Assert.Equal(9, grid.FocusedIndex);
            Assert.Equal(9, grid.SelectedIndex);

            well.IsOpen = false;
            well.Color = Color.Parse("#1E90FF");
            well.IsOpen = true;
            AnimationClock.Settle(300);
            Assert.Equal(0, grid.FocusedIndex);
            Assert.Equal(-1, grid.SelectedIndex);
            Assert.Equal(Color.Parse("#1E90FF"), well.Color);
        }

        [AvaloniaFact]
        public void IsOpen_SetBeforeAttach_OpensOnceTheWellHasAHost() {
            using var fixture = new KitFixture();
            ColorWell well = Well(Orange);
            well.IsOpen = true;

            Hosted(fixture, well);
            AnimationClock.Settle(300);

            Assert.True(well.Frame!.IsOpen);
            Assert.Equal(32, well.Popover!.Grid!.Slots.Count);
        }

        [AvaloniaFact]
        public void Closing_ReturnsFocusToTheWell() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));
            well.IsOpen = true;
            AnimationClock.Settle(300);

            well.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(well.IsFocused);
        }

        [AvaloniaFact]
        public void Escape_WithThePopoverOpen_ClosesThePopoverAndStopsThere() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));
            int reachedWindow = 0;
            fixture.Window.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) reachedWindow++; },
                Avalonia.Interactivity.RoutingStrategies.Bubble);
            well.IsOpen = true;
            AnimationClock.Settle(300);

            fixture.StateOf(well.Popover!.Grid!).KeyDown(Key.Escape);

            Assert.False(well.IsOpen);
            Assert.Equal(0, reachedWindow);
        }

        private static AutomationPeer Peer(ColorWell well) => ControlAutomationPeer.CreatePeerForElement(well);

        [AvaloniaFact]
        public void VoiceOver_IsAPopUpButtonNamedByTheRowLabel_WithHintOpensColors() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));

            AutomationPeer peer = Peer(well);
            Assert.Equal(AutomationControlType.ComboBox, peer.GetAutomationControlType());
            Assert.Equal("pop-up button", peer.GetLocalizedControlType());
            Assert.Equal("Fill", peer.GetName());
            Assert.Equal("Opens colors", peer.GetHelpText());
        }

        [AvaloniaTheory]
        [InlineData("#FF9500", false, "Orange")]
        [InlineData("#1E90FF", false, "Hex 1E90FF")]
        [InlineData("#66007AFF", true, "Blue, 40 percent opacity")]
        [InlineData("#00007AFF", true, "None")]
        [InlineData("#48484A", false, "Charcoal")]
        public void VoiceOver_ValueIsThePaletteNameOrHex_PlusOpacityWithAlpha(string hex, bool allowsAlpha, string expected) {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Color.Parse(hex), allowsNone: true, allowsAlpha: allowsAlpha));

            Assert.Equal(expected, ((IValueProvider)Peer(well)).Value);
        }

        [AvaloniaFact]
        public void VoiceOver_Press_OpensThePopover() {
            using var fixture = new KitFixture();
            ColorWell well = Hosted(fixture, Well(Orange));

            ((IInvokeProvider)Peer(well)).Invoke();

            Assert.True(well.IsOpen);
        }

        [AvaloniaFact]
        public void Scale_AtOneFifty_TheWellChevronAndCheckerFollowTheTokens() {
            using var fixture = new KitFixture();
            ScaleContract.Apply(1.5);
            ColorWell well = Hosted(fixture, Well(Color.FromArgb(102, 0x00, 0x7A, 0xFF), allowsNone: true, allowsAlpha: true));

            Assert.Equal(new Size(66, 33), well.Bounds.Size);
            Assert.Equal(21, Part<Border>(well, "PART_ChevronColumn").Bounds.Width);
            Assert.Equal(6, Part<SwatchChecker>(well, "PART_Checker").SquareSize);
        }

        [AvaloniaTheory]
        [InlineData(1.0, 3, 3, 26, 16)]
        [InlineData(1.5, 4, 4, 40, 25)]
        public void Swatch_InsetIsTheScaledToken_TheBorderStaysOnePoint(double step, double x, double y, double width, double height) {
            using var fixture = new KitFixture();
            ScaleContract.Apply(step);
            ColorWell well = Hosted(fixture, Well(Orange));

            Border swatch = Part<Border>(well, "PART_Swatch");
            Point origin = Offset(swatch, well);
            Assert.Equal(x, origin.X, 0.5);
            Assert.Equal(y, origin.Y, 0.5);
            Assert.Equal(width, swatch.Bounds.Width, 0.5);
            Assert.Equal(height, swatch.Bounds.Height, 0.5);
            Assert.Equal(2.0, Token("Foreman.Size.ColorWellSwatchInset") is double inset ? inset / step : double.NaN);
            double scaled = 2.0 * step;
            Assert.Equal(new Thickness(scaled, scaled, 0, scaled), swatch.Margin);
            Assert.Equal(new Thickness(1), Part<Border>(well, "PART_Frame").BorderThickness);
        }
    }
}
