using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using System.Linq;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    //Gate fix 8, item 2a: palette rows fade+slide in on entry.
    public sealed class PaletteAnimationTests {
        [AvaloniaFact]
        public void EnteringRow_StartsAtOpacityZero_TranslatedUp() {
            var row = NewStyledRow("entering");

            Assert.Equal(0, row.Opacity);
            Assert.NotNull(row.RenderTransform);
        }

        [AvaloniaFact]
        public void SettledRow_IsFullyOpaque_WithNoTransform_AndDeclaresTheSpecifiedTransitions() {
            var row = NewStyledRow();

            Assert.Equal(1, row.Opacity);

            var opacity = Assert.Single(row.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(150, opacity.Duration.TotalMilliseconds);
            Assert.IsType<CubicEaseOut>(opacity.Easing);

            var slide = Assert.Single(row.Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(150, slide.Duration.TotalMilliseconds);
            Assert.IsType<CubicEaseOut>(slide.Easing);
        }

        //End to end: a row realized by the real palette sheds .entering and settles at full opacity, no
        //matter exactly how many dispatcher passes that takes.
        [AvaloniaFact]
        public void ARowRealizedByThePalette_EventuallySettlesAtFullOpacity_WithEnteringShed() {
            using var fx = CanvasFixture.WithVanilla();
            fx.Control.AddItemAsync(new DrawingPoint(0, 0));
            var palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = "iron";
            var results = palette.FindControl<ItemsControl>("Results")!;
            Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Control row = results.ContainerFromIndex(0)!;
            Assert.DoesNotContain("entering", row.Classes);
            Assert.Equal(1, row.Opacity, 2);
        }

        private static Button NewStyledRow(params string[] extraClasses) {
            var row = new Button { Classes = { "foreman-qa-row" } };
            foreach (string extra in extraClasses)
                row.Classes.Add(extra);
            var window = new AvaloniaWindow { Content = row, Width = 200, Height = 60 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return row;
        }
    }
}
