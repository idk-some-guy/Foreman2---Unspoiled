using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using System.Windows.Input;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class ChipTests {
        private static ControlTheme KitChipTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Chip", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Chip Themed(string label) => new() { Theme = KitChipTheme(), Label = label };

        private static Border CloseBox(Chip chip) =>
            chip.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_CloseBox");

        private static Border CloseHoverFill(Chip chip) =>
            chip.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_CloseHoverFill");

        private static Path CloseGlyph(Chip chip) =>
            chip.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_CloseGlyph");

        private sealed class RecordingCommand : ICommand {
            public int ExecuteCount;
            public event System.EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => ExecuteCount++;
        }

        [AvaloniaFact]
        public void KitChip_IsAControlThemeTargetingChip() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Chip), KitChipTheme().TargetType);
        }

        [AvaloniaFact]
        public void Bounds_AreTwentyPointRadiusPill() {
            using var fixture = new KitFixture();
            Chip chip = Themed("Assembling machine");
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(20, chip.Height);

            Application.Current!.TryGetResource("Foreman.Radius.Pill", Application.Current.ActualThemeVariant, out object? radius);
            Border frame = chip.GetVisualDescendants().OfType<Border>().First();
            Assert.Equal(Assert.IsType<CornerRadius>(radius), frame.CornerRadius);
        }

        [AvaloniaFact]
        public void FillAndLabel_ResolveToAccentSoftAndAccentText() {
            using var fixture = new KitFixture();
            Chip chip = Themed("Assembling machine");
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            Border frame = Assert.IsType<Border>(chip.GetVisualDescendants().OfType<Border>().First());
            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), frame.Background?.ToImmutable());

            TextBlock label = chip.GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), label.Foreground?.ToImmutable());
            Assert.Equal("Assembling machine", label.Text);
        }

        [AvaloniaFact]
        public void CloseGlyph_IsChipCloseTwelveInASixteenPointBox() {
            using var fixture = new KitFixture();
            Chip chip = Themed("Assembling machine");
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(16, CloseBox(chip).Width);
            Assert.Equal(16, CloseBox(chip).Height);
            Assert.Equal(12, CloseGlyph(chip).Width);
            Assert.Equal(12, CloseGlyph(chip).Height);

            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.ChipClose.12"), CloseGlyph(chip).Data);
        }

        [AvaloniaFact]
        public void HoverOnCloseBox_ShowsDiscAndRaisesGlyphToFullOpacity() {
            using var fixture = new KitFixture();
            Chip chip = Themed("Assembling machine");
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, CloseHoverFill(chip).Opacity);
            Assert.True(CloseGlyph(chip).Opacity < 1);

            fixture.StateOf(CloseBox(chip)).Hover();
            AnimationClock.Settle();

            Assert.Equal(1, CloseHoverFill(chip).Opacity);
            Assert.Equal(1, CloseGlyph(chip).Opacity);
        }

        [AvaloniaFact]
        public void ClickOnCloseBox_RaisesRemoveCommandAndRemovedEvent() {
            using var fixture = new KitFixture();
            Chip chip = Themed("Assembling machine");
            var command = new RecordingCommand();
            chip.RemoveCommand = command;
            bool removedRaised = false;
            chip.Removed += (_, _) => removedRaised = true;
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(CloseBox(chip)).Click();

            Assert.Equal(1, command.ExecuteCount);
            Assert.True(removedRaised);
        }

        [AvaloniaTheory]
        [InlineData(Key.Space)]
        [InlineData(Key.Enter)]
        [InlineData(Key.Delete)]
        [InlineData(Key.Back)]
        public void EachRemoveKey_OnAFocusedChip_RaisesRemoveCommand(Key key) {
            using var fixture = new KitFixture();
            Chip chip = Themed("Assembling machine");
            var command = new RecordingCommand();
            chip.RemoveCommand = command;
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(chip).Focus();
            fixture.StateOf(chip).KeyDown(key);

            Assert.Equal(1, command.ExecuteCount);
        }

        [AvaloniaFact]
        public void Removal_MovesFocusToTheNextChip() {
            using var fixture = new KitFixture();
            Chip first = Themed("Assembling machine");
            Chip second = Themed("Electric furnace");
            var panel = new StackPanel { Children = { first, second } };
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(first).Focus();
            fixture.StateOf(first).KeyDown(Key.Delete);

            Assert.True(second.IsFocused);
        }

        [AvaloniaFact]
        public void Removal_WithNoNextChip_FocusesTheOwnerViaChipHostFocusFallback() {
            using var fixture = new KitFixture();
            Chip only = Themed("Assembling machine");
            var fallback = new Button();
            ChipHost.SetFocusFallback(only, fallback);
            var panel = new StackPanel { Children = { only } };
            var host = new StackPanel { Children = { fallback, panel } };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(only).Focus();
            fixture.StateOf(only).KeyDown(Key.Delete);

            Assert.True(fallback.IsFocused);
        }

        [AvaloniaFact]
        public void Removal_OfTheLastChipInAThreeChipRow_FallsBackToThePreviousChip() {
            using var fixture = new KitFixture();
            Chip first = Themed("Assembling machine");
            Chip second = Themed("Electric furnace");
            Chip third = Themed("Chemical plant");
            var fallback = new Button();
            ChipHost.SetFocusFallback(third, fallback);
            var panel = new StackPanel { Children = { first, second, third } };
            var host = new StackPanel { Children = { fallback, panel } };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(third).Focus();
            fixture.StateOf(third).KeyDown(Key.Delete);

            Assert.True(second.IsFocused);
            Assert.False(fallback.IsFocused);
        }

        [AvaloniaFact]
        public void Removal_OfTheMiddleChipInAThreeChipRow_PrefersTheNextChipOverThePrevious() {
            using var fixture = new KitFixture();
            Chip first = Themed("Assembling machine");
            Chip second = Themed("Electric furnace");
            Chip third = Themed("Chemical plant");
            var panel = new StackPanel { Children = { first, second, third } };
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(second).Focus();
            fixture.StateOf(second).KeyDown(Key.Delete);

            Assert.True(third.IsFocused);
        }

        [AvaloniaFact]
        public void Removal_WithANextChip_PrefersTheNextChipOverTheFallback() {
            using var fixture = new KitFixture();
            Chip first = Themed("Assembling machine");
            Chip second = Themed("Electric furnace");
            var fallback = new Button();
            ChipHost.SetFocusFallback(first, fallback);
            var panel = new StackPanel { Children = { first, second } };
            var host = new StackPanel { Children = { fallback, panel } };
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(first).Focus();
            fixture.StateOf(first).KeyDown(Key.Delete);

            Assert.True(second.IsFocused);
            Assert.False(fallback.IsFocused);
        }

        [AvaloniaFact]
        public void Chip_IsFocusable() {
            using var fixture = new KitFixture();
            Chip chip = Themed("Assembling machine");
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            Assert.True(chip.Focusable);
            Assert.True(chip.IsTabStop);
            Assert.DoesNotContain(chip.GetVisualDescendants(), d => d is Control { Focusable: true });
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            Chip chip = Themed("Assembling machine");
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            chip.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), chip.Opacity);
        }

        private const string LongLabel = "Compatible with Advanced Oil Processing Byproduct Intermediate";

        private static Chip HostedAt256(KitFixture fixture, string label) {
            Chip chip = Themed(label);
            fixture.Window.Content = new Border { Width = 256, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = chip };
            Dispatcher.UIThread.RunJobs();
            return chip;
        }

        private static TextBlock Label(Chip chip) => chip.GetVisualDescendants().OfType<TextBlock>().Single();

        [AvaloniaFact]
        public void LongLabel_InA256PointHost_TrimsAtTheTail_WithTheCloseBoxDockedInsideTheTrailingEdge() {
            using var fixture = new KitFixture();
            Chip chip = HostedAt256(fixture, LongLabel);

            Assert.True(chip.Bounds.Width <= 256.5, $"chip is {chip.Bounds.Width} wide");
            Assert.Equal(20, chip.Bounds.Height, 0.5);
            Rect close = new(CloseBox(chip).TranslatePoint(new Point(0, 0), chip)!.Value, CloseBox(chip).Bounds.Size);
            Assert.Equal(16, close.Width, 0.5);
            Assert.Equal(16, close.Height, 0.5);
            Assert.True(close.X >= 0 && close.Y >= 0 && close.Bottom <= chip.Bounds.Height + 0.5, $"close box at {close}");
            Assert.Equal(chip.Bounds.Width - 4, close.Right, 0.5);

            TextBlock label = Label(chip);
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal(1, label.MaxLines);
            Assert.Contains(label.TextLayout.TextLines, l => l.HasCollapsed);
            Assert.Equal(LongLabel, ToolTip.GetTip(chip));
            Assert.False(ToolTip.GetServiceEnabled(CloseBox(chip)));
            Assert.True(chip.TryFindResource(typeof(ToolTip), out object? tipTheme));
            Assert.Same(KitTheme("Kit.Tooltip"), tipTheme);
            Assert.Equal(LongLabel, ControlAutomationPeer.CreatePeerForElement(chip).GetName());
        }

        [AvaloniaFact]
        public void ShortLabel_InA256PointHost_HugsItsLabel_WithNoTrimAndNoTooltip() {
            using var fixture = new KitFixture();
            //Headless glyphs run about one em wide, so the ruled "Compatible with Iron Plate" already overflows 256 here.
            Chip chip = HostedAt256(fixture, "Iron Plate");

            Assert.True(chip.Bounds.Width < 256, $"chip is {chip.Bounds.Width} wide");
            Assert.DoesNotContain(Label(chip).TextLayout.TextLines, l => l.HasCollapsed);
            Assert.Null(ToolTip.GetTip(chip));
        }

        [AvaloniaFact]
        public void TrimmedTooltip_FollowsTheLabel_AndClearsWhenTheLabelFits() {
            using var fixture = new KitFixture();
            Chip chip = HostedAt256(fixture, LongLabel);

            chip.Label = LongLabel + " Two";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(LongLabel + " Two", ToolTip.GetTip(chip));

            chip.Label = "Iron Plate";
            Dispatcher.UIThread.RunJobs();
            Assert.Null(ToolTip.GetTip(chip));
        }

        [AvaloniaFact]
        public void AccessibleName_DefaultsToTheFullLabel_ButAnExplicitNameWins() {
            using var fixture = new KitFixture();
            Chip chip = HostedAt256(fixture, LongLabel);
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(chip);

            Assert.Equal(LongLabel, peer.GetName());
            Assert.DoesNotContain("…", peer.GetName());

            AutomationProperties.SetName(chip, "Explicit");
            Assert.Equal("Explicit", peer.GetName());
        }

        [AvaloniaFact]
        public void CloseBoxPeer_CarriesTheRemoveAccessibleName_AndInvokeRemoves() {
            using var fixture = new KitFixture();
            Chip chip = Themed("Assembling machine");
            chip.RemoveAccessibleName = "Remove Constraint";
            var command = new RecordingCommand();
            chip.RemoveCommand = command;
            bool removed = false;
            chip.Removed += (_, _) => removed = true;
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer close = Assert.Single(ControlAutomationPeer.CreatePeerForElement(chip).GetChildren(), p => p.GetName() == "Remove Constraint");
            Assert.Equal(AutomationControlType.Button, close.GetAutomationControlType());
            IInvokeProvider invoke = Assert.IsAssignableFrom<IInvokeProvider>(close.GetProvider<IInvokeProvider>());
            invoke.Invoke();

            Assert.True(removed);
            Assert.Equal(1, command.ExecuteCount);
        }

        private static object KitTheme(string key) {
            Assert.True(Application.Current!.TryGetResource(key, null, out object? theme));
            return theme!;
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class ChipScaleTests {
        private static Border CloseBox(Chip chip) =>
            chip.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_CloseBox");

        [AvaloniaFact]
        public void Height_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Chip", null, out object? theme));
            var chip = new Chip { Theme = (ControlTheme)theme!, Label = "Assembler" };
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(20, chip.Height);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(30, chip.Height);
            Assert.Equal(24, CloseBox(chip).Width);
        }

        [AvaloniaFact]
        public void CloseGlyphViewbox_TracksScaleContractWhileThePathStaysAtItsMasterSize() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Chip", null, out object? theme));
            var chip = new Chip { Theme = (ControlTheme)theme!, Label = "Assembler" };
            fixture.Window.Content = chip;
            Dispatcher.UIThread.RunJobs();

            Path glyph = chip.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_CloseGlyph");
            Viewbox box = chip.GetVisualDescendants().OfType<Viewbox>().Single();
            Assert.Equal(Stretch.None, glyph.Stretch);
            Assert.Equal(12, box.Bounds.Width);
            Assert.Equal(12, glyph.Bounds.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(18, box.Bounds.Width);
            Assert.Equal(12, glyph.Bounds.Width);
        }
    }
}
