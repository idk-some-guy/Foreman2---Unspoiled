using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class RevertButtonTests {
        private static RevertButton Themed() {
            Assert.True(Application.Current!.TryGetResource("Kit.RevertButton", null, out object? theme));
            return new RevertButton { Theme = Assert.IsType<ControlTheme>(theme) };
        }

        [AvaloniaFact]
        public void RevertButton_Size_AndReservedSlot() {
            using var fixture = new KitFixture();
            RevertButton button = Themed();
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new Size(20, 20), button.Bounds.Size);
            Assert.Equal(new CornerRadius(5), button.CornerRadius);
            Assert.Equal(new Thickness(0), button.BorderThickness);

            button.IsReserved = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new Size(20, 20), button.Bounds.Size);
            Assert.True(button.IsVisible);
            Assert.Equal(0, button.Opacity);
            Assert.False(button.IsHitTestVisible);
            Assert.False(button.Focusable);
        }

        [AvaloniaFact]
        public void RevertButton_Reserved_IsHiddenFromAutomationButKeepsItsSlot() {
            using var fixture = new KitFixture();
            RevertButton button = Themed();
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(button);
            Assert.True(peer.IsControlElement());

            button.IsReserved = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(peer.IsControlElement());
            Assert.Equal(new Size(20, 20), button.Bounds.Size);

            button.IsReserved = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(peer.IsControlElement());
        }

        [AvaloniaFact]
        public void RevertButton_GlyphIsTheRevertGlyphInTextSecondary() {
            using var fixture = new KitFixture();
            RevertButton button = Themed();
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            Path glyph = button.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Glyph");
            Assert.True(Application.Current!.TryGetResource("Kit.Glyph.Revert.12", null, out object? geometry));
            Assert.Same(geometry, glyph.Data);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), glyph.Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void RevertButton_Hover_FillsWithControlBackgroundHover() {
            using var fixture = new KitFixture();
            RevertButton button = Themed();
            fixture.Window.Content = button;
            Dispatcher.UIThread.RunJobs();

            Border fill = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HoverFill");
            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), fill.Background?.ToImmutable());
        }
    }
}
