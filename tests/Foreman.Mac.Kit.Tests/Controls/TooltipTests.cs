using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class TooltipTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitTooltipTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Tooltip", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ToolTip Themed(string content) => new() { Theme = KitTooltipTheme(), Content = content };

        [AvaloniaFact]
        public void KitTooltip_IsAControlThemeTargetingToolTip() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ToolTip), KitTooltipTheme().TargetType);
        }

        [AvaloniaFact]
        public void Background_ResolvesToTooltipMaterial() {
            using var fixture = new KitFixture();
            ToolTip tip = Themed("Find node — ⌘F");
            fixture.Window.Content = tip;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.TooltipMaterial").ToImmutable(), tip.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void ReduceTransparency_SwapsBackgroundToOpaqueFallback() {
            using var fixture = new KitFixture();
            var hub = new AppearanceHub((Application)Application.Current!, fixture.Appearance);
            fixture.Appearance.ReduceTransparency = true;
            fixture.Appearance.RaiseChanged();

            Assert.True(Application.Current!.Resources.ContainsKey("Foreman.Color.TooltipMaterial"));

            ToolTip tip = Themed("Overproduction");
            fixture.Window.Content = tip;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.TooltipMaterialOpaque").ToImmutable(), tip.Background?.ToImmutable());

            fixture.Appearance.ReduceTransparency = false;
            fixture.Appearance.RaiseChanged();

            Assert.False(Application.Current!.Resources.ContainsKey("Foreman.Color.TooltipMaterial"));

            hub.Dispose();
        }

        [AvaloniaFact]
        public void Metrics_ComeFromTokens() {
            using var fixture = new KitFixture();
            ToolTip tip = Themed("Find node — ⌘F");
            fixture.Window.Content = tip;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), tip.CornerRadius);

            Application.Current!.TryGetResource("Foreman.Thickness.TooltipPadding", Application.Current.ActualThemeVariant, out object? padding);
            Assert.Equal(Assert.IsType<Thickness>(padding), tip.Padding);

            Application.Current!.TryGetResource("Foreman.Type.Caption.FontSize", Application.Current.ActualThemeVariant, out object? fontSize);
            Assert.Equal(Assert.IsType<double>(fontSize), tip.FontSize);

            Assert.Equal(260, tip.MaxWidth);
        }

        [AvaloniaFact]
        public void ContentPresenter_WrapsAndCapsAtThreeLines() {
            using var fixture = new KitFixture();
            ToolTip tip = Themed("Find node — ⌘F");
            fixture.Window.Content = tip;
            Dispatcher.UIThread.RunJobs();

            ContentPresenter presenter = tip.GetVisualDescendants().OfType<ContentPresenter>().Single();
            Assert.Equal(TextWrapping.Wrap, presenter.TextWrapping);
            Assert.Equal(3, presenter.MaxLines);
            Assert.Equal(TextTrimming.CharacterEllipsis, presenter.TextTrimming);
        }

        [AvaloniaFact]
        public void NoTransitionsAreAttached_InEitherMotionMode() {
            using var fixture = new KitFixture();
            ToolTip tip = Themed("Find node — ⌘F");
            fixture.Window.Content = tip;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(tip.Transitions);

            Motion.ReduceMotion = true;
            ToolTip reduced = Themed("Find node — ⌘F");
            fixture.Window.Content = reduced;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(reduced.Transitions);
        }

        [AvaloniaFact]
        public void TooltipTiming_SetsShowDelayAndBetweenShowDelayFromMotion() {
            using var fixture = new KitFixture();
            var host = new Border();
            fixture.Window.Content = host;

            TooltipTiming.SetIsEnabled(host, true);

            Assert.Equal((int)Motion.TooltipDelay.TotalMilliseconds, ToolTip.GetShowDelay(host));
            Assert.Equal((int)Motion.TooltipWarmWindow.TotalMilliseconds, ToolTip.GetBetweenShowDelay(host));
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class TooltipScaleTests {
        [AvaloniaFact]
        public void FontSize_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Tooltip", null, out object? theme));
            var tip = new ToolTip { Theme = (ControlTheme)theme!, Content = "Find node — ⌘F" };
            fixture.Window.Content = tip;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(11, tip.FontSize);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(16.5, tip.FontSize);
        }
    }
}
