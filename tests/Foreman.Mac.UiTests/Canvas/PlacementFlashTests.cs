using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Foreman.Mac.Canvas;
using Foreman.Mac.Canvas.Elements;
using Foreman.Mac.Kit;
using Foreman.Mac.UiTests.support;
using Foreman.Mac.ViewModels;
using Foreman.Mac.Views.QuickAdd;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using DrawingPoint = System.Drawing.Point;

namespace Foreman.Mac.UiTests.Canvas {
    public sealed class PlacementFlashTests : IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static BaseNodeElement PlaceFromPalette(CanvasFixture fx, string query) {
            fx.Control.AddItemAsync(new DrawingPoint(300, 200));
            QuickAddPalette palette = fx.Control.OpenPalette!;
            ((QuickAddViewModel)palette.DataContext!).Query = query;
            Dispatcher.UIThread.RunJobs();
            List<BaseNodeElement> before = [.. fx.Viewer.NodeElements];
            CanvasKeys.KeyDownOn(palette.Search, Key.Enter);
            return fx.Viewer.NodeElements.Except(before).Single();
        }

        [AvaloniaFact]
        public void UnderReduceMotion_TheFlashKeepsItsFullLifetime() {
            Motion.ReduceMotion = true;
            using var fx = CanvasFixture.WithVanilla();
            BaseNodeElement node = PlaceFromPalette(fx, "iron plate");

            AnimationClock.Settle(250);
            Assert.Same(node, fx.Control.PlacementFlashNode);
            Assert.True(node.IsPlacementFlashing);

            AnimationClock.Settle(450);
            Assert.Null(fx.Control.PlacementFlashNode);
            Assert.False(node.IsPlacementFlashing);
        }

        [AvaloniaFact]
        public void DeletingTheNodeMidFlash_DropsItsRing() {
            using var fx = CanvasFixture.WithVanilla();
            BaseNodeElement node = PlaceFromPalette(fx, "iron plate");
            fx.Control.ClosePanels();

            fx.Viewer.SetSelection([node]);
            fx.Control.DeleteSelection();
            AnimationClock.Settle(60);

            Assert.Null(fx.Control.PlacementFlashNode);
            Assert.False(node.IsPlacementFlashing);
        }

        [Fact]
        public void FlashOpacity_RisesOverHover_HoldsTwoHundred_ThenFallsOverTheCrossFade() {
            Assert.Equal(0, GraphCanvasControl.PlacementFlashOpacityAt(TimeSpan.Zero, false), 3);
            double rising = GraphCanvasControl.PlacementFlashOpacityAt(TimeSpan.FromMilliseconds(60), false);
            Assert.InRange(rising, 0.01, 0.99);
            Assert.Equal(1, GraphCanvasControl.PlacementFlashOpacityAt(TimeSpan.FromMilliseconds(120), false), 3);
            Assert.Equal(1, GraphCanvasControl.PlacementFlashOpacityAt(TimeSpan.FromMilliseconds(320), false), 3);
            double falling = GraphCanvasControl.PlacementFlashOpacityAt(TimeSpan.FromMilliseconds(430), false);
            Assert.InRange(falling, 0.01, 0.99);
            Assert.Equal(0, GraphCanvasControl.PlacementFlashOpacityAt(TimeSpan.FromMilliseconds(540), false), 3);

            Assert.Equal(Motion.EntranceEasing.Ease(0.5), rising, 3);
            Assert.Equal(1 - Motion.LeavingEasing.Ease(0.5), falling, 3);
            Assert.Equal(Motion.CrossFadeEasing.Ease(0.5), GraphCanvasControl.PlacementFlashOpacityAt(TimeSpan.FromMilliseconds(60), true), 3);
            Assert.Equal(1 - Motion.CrossFadeEasing.Ease(0.5), GraphCanvasControl.PlacementFlashOpacityAt(TimeSpan.FromMilliseconds(430), true), 3);
        }
    }
}
