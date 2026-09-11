using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.Views.QuickAdd;
using System;
using System.Linq;
using Xunit;
using AvaloniaPoint = Avalonia.Point;

namespace Foreman.Mac.UiTests.QuickAdd {
    //Gate fix 8, item 2d / gate fix 11: the radial opens with a scale+fade in, slices ease their hover
    //fill/stroke, and closing fades the menu out before FloatingPanelHost actually detaches it.
    public sealed class RadialAnimationTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        [AvaloniaFact]
        public void ANewRadialMenu_StartsScaledDownAndTransparent() {
            var radial = new RadialMenu([RadialChoice.Add, RadialChoice.Cancel]);

            Assert.Equal(0, radial.Opacity);
            var scale = Assert.IsType<ScaleTransform>(radial.RenderTransform);
            Assert.Equal(Motion.RadialOpenScale, scale.ScaleX);
            Assert.Equal(Motion.RadialOpenScale, scale.ScaleY);
        }

        [AvaloniaFact]
        public void OnceAttached_ARadialMenu_SettlesToFullSizeAndOpaque() {
            var radial = new RadialMenu([RadialChoice.Add, RadialChoice.Cancel]);
            var window = new Avalonia.Controls.Window { Content = radial, Width = 300, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Equal(1, radial.Opacity, 2);
            var settled = Assert.IsType<ScaleTransform>(radial.RenderTransform);
            Assert.Equal(1, settled.ScaleX, 2);
        }

        [AvaloniaFact]
        public void RadialMenuStyle_DeclaresTheSpecifiedOpenTransitions() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;

            var opacity = Assert.Single(radial.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.RadialOpenDuration.TotalMilliseconds, opacity.Duration.TotalMilliseconds);
            Assert.IsType(Motion.RadialOpenEasing.GetType(), opacity.Easing);

            var scale = Assert.Single(radial.Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(Motion.RadialOpenDuration.TotalMilliseconds, scale.Duration.TotalMilliseconds);
            Assert.IsType(Motion.RadialOpenEasing.GetType(), scale.Easing);
        }

        [AvaloniaFact]
        public void RadialMenuClosingStyle_FadesOutOverMotionsRadialCloseDuration() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;

            fx.Control.FloatingPanelHost.Close();

            var opacity = Assert.Single(radial.Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.RadialCloseDuration.TotalMilliseconds, opacity.Duration.TotalMilliseconds);
        }

        [AvaloniaFact]
        public void SliceStyle_DeclaresAFillAndStrokeTransitionOverMotionsHoverDuration() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;

            Path slice = radial.GetVisualDescendants().OfType<Path>().First(p => p.Classes.Contains("foreman-radial-slice"));
            var fill = Assert.Single(slice.Transitions!.OfType<BrushTransition>(), t => t.Property == Path.FillProperty);
            Assert.Equal(Motion.HoverDuration.TotalMilliseconds, fill.Duration.TotalMilliseconds);
            var stroke = Assert.Single(slice.Transitions!.OfType<BrushTransition>(), t => t.Property == Path.StrokeProperty);
            Assert.Equal(Motion.HoverDuration.TotalMilliseconds, stroke.Duration.TotalMilliseconds);
        }

        [AvaloniaFact]
        public void Closing_LeavesThePanelClosedImmediately_ButFadesTheVisualOutBeforeDetaching() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;

            fx.Control.FloatingPanelHost.Close();

            Assert.False(fx.Control.FloatingPanelHost.IsOpen);
            Assert.Contains("closing", radial.Classes);
            Assert.Contains(radial, fx.Control.GetVisualDescendants().OfType<RadialMenu>());

            AnimationClock.Settle();

            Assert.DoesNotContain(radial, fx.Control.GetVisualDescendants().OfType<RadialMenu>());
        }

        //Cancellable: opening something new purges a still-fading radial immediately instead of leaving it
        //stuck mid-fade, or racing its own removal timer.
        [AvaloniaFact]
        public void OpeningANewPanel_ImmediatelyPurgesAStillClosingRadial() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;
            fx.Control.FloatingPanelHost.Close();
            Assert.Contains(radial, fx.Control.GetVisualDescendants().OfType<RadialMenu>());

            fx.Control.AddItemAsync(new System.Drawing.Point(0, 0));

            Assert.DoesNotContain(radial, fx.Control.GetVisualDescendants().OfType<RadialMenu>());
        }

        //A DispatcherTimer left running past its own window's lifetime eventually fires on a UI thread a
        //headless test runner may since have handed to a different, unrelated test - "the calling thread
        //cannot access this object because a different thread owns it", surfacing as THAT test's cleanup
        //failure. GraphCanvasControl.OnDetachedFromVisualTree cancels the pending transition to prevent it.
        [AvaloniaFact]
        public void DetachingTheCanvas_CancelsAPendingCloseTransition_InsteadOfLeavingItRunning() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;
            fx.Control.FloatingPanelHost.Close();
            Assert.Contains(radial, fx.Control.GetVisualDescendants().OfType<RadialMenu>());

            fx.Window.Content = null;

            Assert.DoesNotContain(radial, fx.Control.GetVisualDescendants().OfType<RadialMenu>());
        }

        //Fix round 1: RadialMenu sets its own Transitions from Motion.Transition, so ReduceMotion has to
        //reach it at cold launch - before the control ever exists, not just on a later live toggle.
        [AvaloniaFact]
        public void WithReduceMotionSetBeforeConstruction_TheRadialsOpenTransitionsAreOpacityOnly() {
            Motion.ReduceMotion = true;

            var radial = new RadialMenu([RadialChoice.Add, RadialChoice.Cancel]);

            var only = Assert.Single(radial.Transitions!);
            var opacity = Assert.IsType<DoubleTransition>(only);
            Assert.Equal(Visual.OpacityProperty, opacity.Property);
            Assert.Equal(Motion.RadialOpenDuration, opacity.Duration);
        }

        [AvaloniaFact]
        public void WithReduceMotionSetBeforeClosing_TheRadialsClosingTransitionsAreOpacityOnly() {
            using var fx = CanvasFixture.WithVanillaAndOneSupplier(out AvaloniaPoint outputTab);
            fx.DragFrom(outputTab).To(new AvaloniaPoint(400, 300)).Release();
            RadialMenu radial = fx.Control.OpenRadial!;

            Motion.ReduceMotion = true;
            fx.Control.FloatingPanelHost.Close();

            var only = Assert.Single(radial.Transitions!);
            var opacity = Assert.IsType<DoubleTransition>(only);
            Assert.Equal(Visual.OpacityProperty, opacity.Property);
            Assert.Equal(Motion.RadialCloseDuration, opacity.Duration);
        }
    }
}
