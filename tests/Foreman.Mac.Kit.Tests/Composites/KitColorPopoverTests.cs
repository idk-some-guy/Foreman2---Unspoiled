using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitColorPopoverTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static readonly Color Orange = Color.Parse("#FF9500");

        private static ControlTheme Theme(string key) {
            Assert.True(Application.Current!.TryGetResource(key, null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ColorWell Opened(KitFixture fixture, Color color, bool allowsNone = false, bool allowsAlpha = false, double top = 20) {
            var well = new ColorWell {
                Theme = Theme("Kit.ColorWell"),
                Color = color,
                AllowsNone = allowsNone,
                AllowsAlpha = allowsAlpha,
                AccessibleLabel = "Fill",
                Margin = new Thickness(150, top, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            var host = new PopoverHost();
            host.Children.Add(well);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();
            well.IsOpen = true;
            AnimationClock.Settle(300);
            return well;
        }

        private static ColorPopover Popover(ColorWell well) => Assert.IsType<ColorPopover>(well.Popover);

        private static PickCell[] Cells(ColorWell well) => [.. Popover(well).Grid!.GetVisualDescendants().OfType<PickCell>()];

        private static Border Panel(PopoverFrame frame) =>
            frame.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Panel");

        private static object? Token(string key) {
            Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out object? value);
            return value;
        }

        [AvaloniaFact]
        public void Frame_IsAKitPopoverFrame272Wide_OpeningBelowTheWell_TitledByTheRowLabel() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);

            PopoverFrame frame = Assert.IsType<PopoverFrame>(well.Frame);
            Assert.True(frame.IsOpen);
            Assert.Same(Theme("Kit.PopoverFrame"), frame.Theme);
            Assert.Equal(272, Panel(frame).Bounds.Width);
            Assert.Equal(PopoverEdge.Bottom, frame.ResolvedPlacement);
            Assert.Equal("Fill colors", AutomationProperties.GetName(frame));
            Assert.Same(well, frame.Anchor);
        }

        [AvaloniaFact]
        public void Frame_FlipsAboveTheWell_WhenThereIsNoRoomBelow() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange, top: 260);

            Assert.Equal(PopoverEdge.Top, well.Frame!.ResolvedPlacement);
        }

        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void Grid_IsAChoiceGridOfEightColumnsAndThirtyTwoCells_AtPitchThirty(bool allowsNone) {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange, allowsNone);

            PickGrid grid = Popover(well).Grid!;
            Assert.True(grid.IsChoiceMode);
            Assert.Equal(8, grid.Columns);
            PickCell[] cells = Cells(well);
            Assert.Equal(32, cells.Length);
            Assert.All(cells, c => Assert.Equal(new Size(26, 26), c.Bounds.Size));
            Assert.Equal(30, cells[1].Bounds.X - cells[0].Bounds.X, 3);
            Assert.Equal(30, cells[8].Bounds.Y - cells[0].Bounds.Y, 3);
            Assert.Equal(cells[0].Bounds.X, cells[8].Bounds.X, 3);
            Assert.Equal(4, cells.Select(c => c.Bounds.Y).Distinct().Count());
        }

        [AvaloniaFact]
        public void Grid_CellsHoldEighteenPointSwatchesAtRadiusSwatch() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);

            PickCell cell = Cells(well)[9];
            Border swatch = cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Swatch");
            Assert.True(swatch.IsVisible);
            Assert.Equal(new Size(18, 18), swatch.Bounds.Size);
            Assert.Equal(Token("Foreman.Radius.Swatch"), swatch.CornerRadius);
            Assert.Equal(Orange, Assert.IsAssignableFrom<ISolidColorBrush>(swatch.Background).Color);
            Assert.Equal(fixture.Brush("Foreman.Color.SwatchStroke").ToImmutable(), swatch.BorderBrush?.ToImmutable());
        }

        private static readonly (string Name, string Hex)[] FullPalette = [
            ("White", "FFFFFF"), ("Light gray", "D1D1D6"), ("Gray", "AEAEB2"), ("Medium gray", "8E8E93"),
            ("Dark gray", "636366"), ("Charcoal", "48484A"), ("Graphite", "2C2C2E"), ("Black", "000000"),
            ("Red", "FF3B30"), ("Orange", "FF9500"), ("Yellow", "FFCC00"), ("Green", "34C759"),
            ("Teal", "30B0C7"), ("Blue", "007AFF"), ("Indigo", "5856D6"), ("Purple", "AF52DE"),
            ("Light red", "FF9D97"), ("Light orange", "FFCA80"), ("Light yellow", "FFE680"), ("Light green", "9AE3AC"),
            ("Light teal", "98D8E3"), ("Light blue", "80BDFF"), ("Light indigo", "ACAAEB"), ("Light purple", "D7A8EF"),
            ("Dark red", "B22921"), ("Dark orange", "B26800"), ("Dark yellow", "B28F00"), ("Dark green", "248B3E"),
            ("Dark teal", "227B8B"), ("Dark blue", "0055B2"), ("Dark indigo", "3E3C96"), ("Dark purple", "7B399B"),
        ];

        [AvaloniaFact]
        public void Palette_IsTheFixedEightByFour_NamedAndOrderedPerThePage() {
            var swatches = ColorPopover.Swatches(allowsNone: false);

            Assert.Equal(FullPalette.Select(p => p.Name), swatches.Select(s => s.Name));
            Assert.Equal(FullPalette.Select(p => Color.Parse("#" + p.Hex)), swatches.Select(s => s.Color!.Value));
        }

        [AvaloniaFact]
        public void Palette_WithAllowsNone_PutsNoneFirst_AndDropsCharcoal() {
            var swatches = ColorPopover.Swatches(allowsNone: true);

            Assert.Equal(32, swatches.Count);
            Assert.Equal("None", swatches[0].Name);
            Assert.Null(swatches[0].Color);
            Assert.DoesNotContain(swatches, s => s.Name == "Charcoal");
            Assert.Equal("White", swatches[1].Name);
            Assert.Equal("Red", swatches[8].Name);
        }

        //Content colours, not chrome: the cells draw the same hex in both appearances.
        [AvaloniaFact]
        public void Palette_IsIdenticalInDarkAppearance() {
            using var fixture = new KitFixture();
            fixture.Window.RequestedThemeVariant = ThemeVariant.Dark;
            ColorWell well = Opened(fixture, Orange);

            Border swatch = Cells(well)[9].GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Swatch");
            Assert.Equal(Orange, Assert.IsAssignableFrom<ISolidColorBrush>(swatch.Background).Color);
        }

        [AvaloniaFact]
        public void Grid_WithAllowsNone_CellZeroIsTheNoneCell() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange, allowsNone: true);

            PickCell[] cells = Cells(well);
            Assert.True(cells[0].IsNone);
            Assert.Equal("None", cells[0].AccessibleName);
            Assert.DoesNotContain(cells, c => c.AccessibleName == "Charcoal");
        }

        [AvaloniaFact]
        public void HexRow_IsAPopoverColumnFormRowLabelledHex_TwelveBelowTheGrid() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);

            ColorPopover popover = Popover(well);
            FormRow row = popover.HexRow!;
            Assert.Equal("Hex", row.Label);
            Assert.Equal(FormRowLabelColumn.Popover, row.LabelColumn);
            Point fieldAt = popover.HexField!.TranslatePoint(new Point(0, 0), row)!.Value;
            Assert.Equal(86 + 12, fieldAt.X, 1);
            PickGrid grid = popover.Grid!;
            double gridBottom = grid.TranslatePoint(new Point(0, grid.Bounds.Height), popover)!.Value.Y;
            double rowTop = row.TranslatePoint(new Point(0, 0), popover)!.Value.Y;
            Assert.Equal(12, rowTop - gridBottom, 1);
        }

        [AvaloniaFact]
        public void HexField_Is84Wide_WithALeadingHashInTextTertiary_ShowingSixUppercaseDigits() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Color.Parse("#ff9500"));

            TextBox hex = Popover(well).HexField!;
            Assert.Equal(84, hex.Bounds.Width);
            Assert.Equal("FF9500", hex.Text);
            TextBlock hash = Assert.IsType<TextBlock>(hex.InnerLeftContent);
            Assert.Equal("#", hash.Text);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), hash.Foreground?.ToImmutable());
            Assert.Equal(Token("Foreman.Type.Body.FontSize"), hex.FontSize);
            Assert.Equal("Hex", AutomationProperties.GetName(hex));
        }

        private static void TypeHex(KitFixture fixture, ColorWell well, string text) {
            TextBox hex = Popover(well).HexField!;
            hex.Text = "";
            fixture.StateOf(hex).Type(text);
        }

        [AvaloniaFact]
        public void Hex_ThreeDigitsAndReturn_SetsTheColour_AndReformatsToSixUppercase() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);

            TypeHex(fixture, well, "1e9");
            fixture.StateOf(Popover(well).HexField!).KeyDown(Key.Enter);

            Assert.Equal(Color.Parse("#11EE99"), well.Color);
            Assert.Equal("11EE99", Popover(well).HexField!.Text);
            Assert.True(well.IsOpen);
        }

        [AvaloniaFact]
        public void Hex_SixDigitsWithHash_AnyCase_CommitsOnTabOut() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);

            TypeHex(fixture, well, "#1e90ff");
            fixture.StateOf(Popover(well).HexField!).KeyDown(Key.Tab);

            Assert.Equal(Color.Parse("#1E90FF"), well.Color);
            Assert.Equal("1E90FF", Popover(well).HexField!.Text);
            Assert.True(Popover(well).Grid!.IsFocused);
        }

        [AvaloniaFact]
        public void Hex_Invalid_ShowsTheRowError_AndLeavesTheColourUnchanged() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);

            TypeHex(fixture, well, "zz");
            fixture.StateOf(Popover(well).HexField!).KeyDown(Key.Enter);

            Assert.Equal("Enter a hex color like 1E90FF.", Popover(well).HexRow!.Error);
            Assert.Equal(Orange, well.Color);

            TypeHex(fixture, well, "007aff");
            fixture.StateOf(Popover(well).HexField!).KeyDown(Key.Enter);
            Assert.Null(Popover(well).HexRow!.Error);
        }

        [AvaloniaFact]
        public void Arrows_MoveSelectionWithFocus_AndPreviewEachColourLive() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);
            PickGrid grid = Popover(well).Grid!;

            fixture.StateOf(grid).KeyDown(Key.Right);

            Assert.Equal(10, grid.FocusedIndex);
            Assert.Equal(10, grid.SelectedIndex);
            Assert.Equal(Color.Parse("#FFCC00"), well.Color);
            Assert.True(well.IsOpen);

            fixture.StateOf(grid).KeyDown(Key.Down);
            Assert.Equal(Color.Parse("#FFE680"), well.Color);
        }

        [AvaloniaTheory]
        [InlineData(Key.Enter)]
        [InlineData(Key.Space)]
        public void ReturnOrSpace_OnACell_CommitsAndCloses(Key key) {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);
            PickGrid grid = Popover(well).Grid!;

            fixture.StateOf(grid).KeyDown(Key.Left);
            fixture.StateOf(grid).KeyDown(key);

            Assert.Equal(Color.Parse("#FF3B30"), well.Color);
            Assert.False(well.IsOpen);
            Assert.True(well.IsFocused);
        }

        [AvaloniaFact]
        public void Click_OnACell_CommitsAndCloses() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);

            fixture.StateOf(Cells(well)[13]).Click();

            Assert.Equal(Color.Parse("#007AFF"), well.Color);
            Assert.False(well.IsOpen);
        }

        [AvaloniaFact]
        public void Escape_RestoresTheColourFromOpenTime_ThenCloses_WithFocusBackOnTheWell() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);
            PickGrid grid = Popover(well).Grid!;
            fixture.StateOf(grid).KeyDown(Key.Right);
            fixture.StateOf(grid).KeyDown(Key.Right);
            Assert.NotEqual(Orange, well.Color);

            fixture.StateOf(grid).KeyDown(Key.Escape);

            Assert.Equal(Orange, well.Color);
            Assert.False(well.IsOpen);
            Assert.True(well.IsFocused);
        }

        [AvaloniaFact]
        public void ClickOutside_CommitsTheCurrentColour_AndCloses() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);
            fixture.StateOf(Popover(well).Grid!).KeyDown(Key.Right);

            fixture.Window.MouseDown(new Point(390, 290), MouseButton.Left, RawInputModifiers.None);
            fixture.Window.MouseUp(new Point(390, 290), MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Color.Parse("#FFCC00"), well.Color);
            Assert.False(well.IsOpen);
        }

        [AvaloniaFact]
        public void Tab_MovesBetweenTheGridAndTheHexField_AndStaysInside() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);
            ColorPopover popover = Popover(well);

            fixture.StateOf(popover.Grid!).KeyDown(Key.Tab);
            Assert.True(popover.HexField!.IsFocused);

            fixture.StateOf(popover.HexField).KeyDown(Key.Tab);
            Assert.True(popover.Grid!.IsFocused);

            fixture.StateOf(popover.Grid).KeyDown(Key.Tab, KeyModifiers.Shift);
            Assert.True(popover.HexField.IsFocused);
            Assert.Equal(Orange, well.Color);
        }

        [AvaloniaFact]
        public void None_SetsAlphaZero_AndKeepsRgb() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Color.FromArgb(102, 0x00, 0x7A, 0xFF), allowsNone: true, allowsAlpha: true);

            fixture.StateOf(Cells(well)[0]).Click();

            Assert.Equal(Color.FromArgb(0, 0x00, 0x7A, 0xFF), well.Color);
        }

        [AvaloniaFact]
        public void ChoosingACell_AtAlphaZero_RestoresTheLastOpacity_WithAllowsAlpha() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Color.FromArgb(102, 0x00, 0x7A, 0xFF), allowsNone: true, allowsAlpha: true);
            fixture.StateOf(Cells(well)[0]).Click();
            Assert.Equal(0, well.Color.A);

            well.IsOpen = true;
            AnimationClock.Settle(300);
            fixture.StateOf(Cells(well)[9]).Click();

            Assert.Equal(Color.FromArgb(102, 0xFF, 0x95, 0x00), well.Color);
        }

        [AvaloniaFact]
        public void ChoosingACell_AtAlphaZero_WithoutAllowsAlpha_IsOpaque() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Color.FromArgb(0, 0xFF, 0xFF, 0xFF), allowsNone: true);

            fixture.StateOf(Cells(well)[9]).Click();

            Assert.Equal(Color.FromArgb(255, 0xFF, 0x95, 0x00), well.Color);
        }

        [AvaloniaFact]
        public void Open_UsesTheKitPopoverMotion_AndFadesOnlyUnderReduceMotion() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);
            Border panel = Panel(well.Frame!);
            Assert.All(panel.Transitions!, t => Assert.Equal(Motion.PopoverDuration, t switch {
                DoubleTransition d => d.Duration,
                TransformOperationsTransition o => o.Duration,
                _ => TimeSpan.Zero,
            }));
            Assert.Contains(panel.Transitions!, t => t is TransformOperationsTransition);
            Assert.Equal(1, panel.RenderTransform!.Value.M11, 3);

            well.IsOpen = false;
            AnimationClock.Settle(300);
            Assert.Equal(Motion.PopoverOpenScale, panel.RenderTransform!.Value.M11, 3);
            Motion.ReduceMotion = true;
            well.IsOpen = true;
            AnimationClock.Settle(300);

            DoubleTransition fade = Assert.IsType<DoubleTransition>(Assert.Single(panel.Transitions!));
            Assert.Equal(Visual.OpacityProperty, fade.Property);
        }

        [AvaloniaFact]
        public void VoiceOver_TheGridIsARadioGroupNamedLabelColors_WithCellsNamedByPaletteName() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange, allowsNone: true);
            PickGrid grid = Popover(well).Grid!;

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(grid);
            Assert.Equal("Fill colors", peer.GetName());
            PickCell[] cells = Cells(well);
            AutomationPeer none = ControlAutomationPeer.CreatePeerForElement(cells[0]);
            Assert.Equal(AutomationControlType.RadioButton, none.GetAutomationControlType());
            Assert.StartsWith("None", none.GetName());
            Assert.StartsWith("Light blue", ControlAutomationPeer.CreatePeerForElement(cells[21]).GetName());
        }

        [AvaloniaFact]
        public void TypeToSelect_UsesThePaletteNames_AndPreviews() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);
            PickGrid grid = Popover(well).Grid!;

            fixture.Window.KeyTextInput("Te");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(12, grid.FocusedIndex);
            Assert.Equal(Color.Parse("#30B0C7"), well.Color);
        }

        [AvaloniaFact]
        public void Pumping_AnOpenPopover_SettlesToABoundedNonZeroLayout() {
            using var fixture = new KitFixture();
            ColorWell well = Opened(fixture, Orange);
            AnimationClock.Settle(300);
            int layouts = 0;
            well.Frame!.LayoutUpdated += (_, _) => layouts++;

            AnimationClock.Settle(300);

            Assert.True(layouts <= 2, $"{layouts} layout passes after settle");
            Assert.True(Panel(well.Frame).Bounds.Height > 0);
        }
    }
}
