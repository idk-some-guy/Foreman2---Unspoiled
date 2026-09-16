using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitRadialRingTests : System.IDisposable {
        public void Dispose() {
            Motion.ReduceMotion = false;
            //Plain [Fact]s in this class never touch the dispatcher, so Application.Current (a leftover from
            //an earlier AvaloniaFact elsewhere in the run) is not safely readable here.
            if (Application.Current is not null && Dispatcher.UIThread.CheckAccess())
                ScaleContract.Apply(1.0);
        }

        private static ControlTheme KitRadialRingTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.RadialRing", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private sealed class RecordingCommand(System.Action onExecute) : ICommand {
            public int ExecutionCount { get; private set; }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) { ExecutionCount++; onExecute(); }
            public event System.EventHandler? CanExecuteChanged { add { } remove { } }
        }

        private static readonly string[] GlyphKeys = ["RadialCancel", "Recipe", "Item", "RadialSource", "RadialOutput", "RadialPlant"];

        private static RadialRing Themed(bool allEnabled = true) {
            var ring = new RadialRing { Theme = KitRadialRingTheme() };
            for (int i = 0; i < GlyphKeys.Length; i++)
                ring.Items.Add(new RadialItem { GlyphKey = GlyphKeys[i], Label = GlyphKeys[i], IsEnabled = allEnabled });
            return ring;
        }

        private static (Window Window, RadialRing Ring) Shown(RadialRing? ring = null) {
            RadialRing r = ring ?? Themed();
            var host = new Canvas { Width = 220, Height = 220 };
            Canvas.SetLeft(r, 0);
            Canvas.SetTop(r, 0);
            host.Children.Add(r);
            var window = new Window { Width = 220, Height = 220, Content = host };
            window.Show();
            r.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            return (window, r);
        }

        private static void MoveTo(Window window, Point ringLocal) => window.MouseMove(ringLocal, RawInputModifiers.None);

        private static void ReleaseAt(Window window, Point ringLocal) => window.MouseUp(ringLocal, MouseButton.Left, RawInputModifiers.None);

        //A wedge Path sits directly on PART_Root; an icon Path sits inside its own design-box Viewbox - both
        //carry StreamGeometry data, so the visual parent is what tells them apart.
        private static List<Path> Slices(RadialRing ring) =>
            [.. ring.GetVisualDescendants().OfType<Path>().Where(p => p.Data is StreamGeometry && p.GetVisualParent() is Canvas)];

        private static List<StackPanel> Cells(RadialRing ring) => [.. ring.GetVisualDescendants().OfType<StackPanel>()];

        private static List<Ellipse> Ellipses(RadialRing ring) => [.. ring.GetVisualDescendants().OfType<Ellipse>()];

        [AvaloniaFact]
        public void KitRadialRing_IsAControlThemeTargetingRadialRing() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(RadialRing), KitRadialRingTheme().TargetType);
        }

        [AvaloniaFact]
        public void Detach_DisposesGlyphSubscriptions_NotJustTokenSubscriptions() {
            using var fixture = new KitFixture();
            (Window window, RadialRing ring) = Shown();

            FieldInfo field = typeof(RadialRing).GetField("glyphSubs", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var glyphSubs = (List<IDisposable>)field.GetValue(ring)!;
            Assert.NotEmpty(glyphSubs);

            ((Canvas)ring.Parent!).Children.Remove(ring);
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(glyphSubs);
        }

        [AvaloniaFact]
        public void Geometry_220Across_Outer100_Hub38() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            Assert.Equal(220, ring.Bounds.Width);
            Assert.Equal(220, ring.Bounds.Height);
        }

        [AvaloniaFact]
        public void Diameter_ResolvesItsToken_AndStays220At150Percent() {
            using var fixture = new KitFixture();
            Application.Current!.TryGetResource("Foreman.Size.RadialDiameter", Application.Current.ActualThemeVariant, out object? token);
            Assert.Equal(220.0, Assert.IsType<double>(token));

            (_, RadialRing ring) = Shown();
            Assert.Equal(220, ring.Bounds.Width);
            Assert.Equal(220, ring.Bounds.Height);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(220, ring.Bounds.Width);
            Assert.Equal(220, ring.Bounds.Height);
        }

        [Fact]
        public void SixSlices_CoverAllThreeSixtyDegrees_WithNoGap() {
            Assert.Equal(6, RadialGeometry.SliceCount);
            Assert.Equal(60, RadialGeometry.SliceSpan);
            for (int i = 0; i < RadialGeometry.SliceCount; i++) {
                double thisStart = RadialGeometry.SliceStart(i);
                double nextStart = RadialGeometry.SliceStart((i + 1) % RadialGeometry.SliceCount);
                double expectedNext = (thisStart + RadialGeometry.SliceSpan) % 360;
                Assert.Equal(expectedNext, nextStart, 6);
            }
        }

        [Fact]
        public void PolarHitTest_PointOutsideRingButInsideBoundingSquare_IsNotOverASlice() {
            //A square's corner at radius ~110*sqrt2/2 ≈ 155 is well past the 100pt outer radius.
            Assert.Null(RadialGeometry.HitTest(new Point(105, 105), hubRadius: 38, outerRadius: 100));
            Assert.Null(RadialGeometry.HitTest(new Point(10, 10), hubRadius: 38, outerRadius: 100));
        }

        [AvaloniaFact]
        public void ArmedSlice_ResolvesAccentSoftFillAndAccentStroke2() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();
            ring.ArmedIndex = 2;
            Dispatcher.UIThread.RunJobs();

            var slices = Slices(ring);
            Path armed = Assert.Single(slices, s => s.StrokeThickness == 2);

            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), armed.Fill?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), armed.Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void CrossingTheHubRadius_ArmsTheWedgeItCrossedInto() {
            using var fixture = new KitFixture();
            (Window window, RadialRing ring) = Shown();

            //Centre is (110,110); due south (angle 90) at radius 60 sits inside slice index 3.
            MoveTo(window, new Point(110, 170));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, ring.ArmedIndex);
        }

        [AvaloniaFact]
        public void LeavingTheOuterRadius_KeepsTheArm() {
            using var fixture = new KitFixture();
            (Window window, RadialRing ring) = Shown();

            MoveTo(window, new Point(110, 170));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, ring.ArmedIndex);

            //Past the outer radius (100) - still due south, well outside the ring's drawn edge.
            MoveTo(window, new Point(110, 215));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, ring.ArmedIndex);
        }

        [AvaloniaFact]
        public void MovingLaterallyBetweenWedges_OutsideTheHub_ReArmsWithoutReenteringTheHub() {
            using var fixture = new KitFixture();
            (Window window, RadialRing ring) = Shown();

            //Due north (angle 270) at radius 60 sits in slice 0.
            MoveTo(window, new Point(110, 50));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, ring.ArmedIndex);

            //Same radius, angle 330 - slice 1's wedge - reached without dropping back inside the 38pt hub.
            MoveTo(window, new Point(161.96, 80));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, ring.ArmedIndex);
        }

        [AvaloniaFact]
        public void ReturningInsideTheHub_ClearsTheArm_SoReleaseCancelsInsteadOfCommitting() {
            using var fixture = new KitFixture();
            (Window window, RadialRing ring) = Shown();

            int committed = 0, dismissed = 0;
            ring.Committed += (_, _) => committed++;
            ring.Dismissed += (_, _) => dismissed++;

            //Due north (angle 270) at radius 60 sits in slice 0.
            MoveTo(window, new Point(110, 50));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, ring.ArmedIndex);

            MoveTo(window, new Point(110, 110));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(-1, ring.ArmedIndex);

            ReleaseAt(window, new Point(110, 110));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, committed);
            Assert.Equal(1, dismissed);
        }

        [AvaloniaFact]
        public void ReleaseInsideTheHub_CommitsNothing() {
            using var fixture = new KitFixture();
            (Window window, RadialRing ring) = Shown();

            int committed = 0, dismissed = 0;
            ring.Committed += (_, _) => committed++;
            ring.Dismissed += (_, _) => dismissed++;

            ReleaseAt(window, new Point(110, 110));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, committed);
            Assert.Equal(1, dismissed);
        }

        [AvaloniaFact]
        public void CrossingThenReleasing_Commits_AndExecutesTheItemsCommand() {
            using var fixture = new KitFixture();
            var executed = 0;
            RadialRing ring = Themed();
            ring.Items[3].Command = new RecordingCommand(() => executed++);
            (Window window, _) = Shown(ring);

            int? committedIndex = null;
            ring.Committed += (_, i) => committedIndex = i;

            MoveTo(window, new Point(110, 170));
            Dispatcher.UIThread.RunJobs();
            ReleaseAt(window, new Point(110, 170));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, committedIndex);
            Assert.Equal(1, executed);
        }

        [AvaloniaFact]
        public void Return_CommitsTheArmedItem() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();
            ring.ArmedIndex = 1;

            int? committedIndex = null;
            ring.Committed += (_, i) => committedIndex = i;
            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Return });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, committedIndex);
        }

        [AvaloniaFact]
        public void ArrowKeys_ArmByDirection() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Up });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, ring.ArmedIndex);

            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, ring.ArmedIndex);

            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Down });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, ring.ArmedIndex);

            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Left });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, ring.ArmedIndex);
        }

        [AvaloniaFact]
        public void NumPadDiagonals_ArmByDirection() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.NumPad9 });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, ring.ArmedIndex);

            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.NumPad3 });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, ring.ArmedIndex);

            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.NumPad1 });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(4, ring.ArmedIndex);

            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.NumPad7 });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(5, ring.ArmedIndex);
        }

        [AvaloniaFact]
        public void EscapeOrAnyOtherKey_Dismisses_AndRaisesTheSameSixItemsAsAContextMenu() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            int dismissed = 0;
            IReadOnlyList<RadialItem>? menuItems = null;
            ring.Dismissed += (_, _) => dismissed++;
            ring.ContextMenuRequested += (_, items) => menuItems = items;

            ring.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, dismissed);
            Assert.NotNull(menuItems);
            Assert.Equal(ring.Items.Select(i => i.Label), menuItems!.Select(i => i.Label));
        }

        [AvaloniaFact]
        public void CanOpen_FalseWithFewerThanTwoEnabledItems_TrueWithTwoOrMore() {
            using var fixture = new KitFixture();
            var ring = new RadialRing { Theme = KitRadialRingTheme() };
            ring.Items.Add(new RadialItem { GlyphKey = "Recipe", Label = "Only one", IsEnabled = true });
            Assert.False(ring.CanOpen);

            ring.Items.Add(new RadialItem { GlyphKey = "Item", Label = "Second", IsEnabled = true });
            Assert.True(ring.CanOpen);
        }

        [AvaloniaFact]
        public void ReduceMotion_RemovesScaleAndSweep_LeavesOneOpacityTransition() {
            using var fixture = new KitFixture();
            Motion.ReduceMotion = true;
            (_, RadialRing ring) = Shown();

            var opacityTransitions = ring.Transitions!.OfType<DoubleTransition>().Where(t => t.Property == Visual.OpacityProperty).ToList();
            Assert.Single(opacityTransitions);
            Assert.Empty(ring.Transitions!.OfType<TransformOperationsTransition>());

            //Every slice collapses to the same opacity-only fade, at zero delay - no stagger under Reduce Motion.
            foreach (Path slice in Slices(ring)) {
                Assert.All(slice.Transitions!, t => Assert.IsType<DoubleTransition>(t));
                DoubleTransition opacity = Assert.Single(slice.Transitions!.OfType<DoubleTransition>());
                Assert.Equal(Visual.OpacityProperty, opacity.Property);
                Assert.Equal(TimeSpan.Zero, opacity.Delay);
            }
        }

        [AvaloniaFact]
        public void Sweep_StaggersEachSlicesFadeInDelay_ByIndexAcrossRadialSweepDuration() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            List<Path> slices = Slices(ring);
            for (int i = 0; i < slices.Count; i++) {
                DoubleTransition opacity = Assert.Single(slices[i].Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
                TimeSpan expectedDelay = TimeSpan.FromMilliseconds(Motion.RadialSweepDuration.TotalMilliseconds / RadialGeometry.SliceCount * i);
                Assert.Equal(expectedDelay, opacity.Delay);
            }
        }

        [AvaloniaFact]
        public void Icon_UsesADesignBoxViewbox_AroundAStretchNoneMasterPath() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            foreach (Viewbox box in ring.GetVisualDescendants().OfType<Viewbox>()) {
                Assert.Equal(17, box.Width);
                Assert.Equal(17, box.Height);
                var icon = Assert.IsType<Path>(box.Child);
                Assert.Equal(Stretch.None, icon.Stretch);
                Assert.Equal(17, icon.Width);
                Assert.Equal(17, icon.Height);
                Assert.NotNull(icon.Data);
            }
        }

        [AvaloniaFact]
        public void Icon_ResolvesEachSlicesOwnGlyphKey_FromItsOwnResourceObservable() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            List<Path> icons = [.. ring.GetVisualDescendants().OfType<Viewbox>().Select(b => (Path)b.Child!)];
            Assert.Equal(GlyphKeys.Length, icons.Count);
            for (int i = 0; i < GlyphKeys.Length; i++) {
                Application.Current!.TryGetResource($"Kit.Glyph.{GlyphKeys[i]}.17", Application.Current.ActualThemeVariant, out object? expected);
                Assert.Equal(expected, icons[i].Data);
            }
        }

        [AvaloniaFact]
        public void AutomationPeer_IsAGroupNamedActions_WithSixChildPeersNamedByLabelInClockwiseOrder() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(ring);
            Assert.Equal(AutomationControlType.Group, peer.GetAutomationControlType());
            Assert.Equal("Actions", peer.GetName());

            IReadOnlyList<AutomationPeer> children = peer.GetChildren();
            Assert.Equal(GlyphKeys.Length, children.Count);
            for (int i = 0; i < GlyphKeys.Length; i++)
                Assert.Equal(GlyphKeys[i], children[i].GetName());
        }

        [AvaloniaFact]
        public void CursorDotAndHalo_TrackThePointer_AndAreTheOnlyThingThatMoves() {
            using var fixture = new KitFixture();
            (Window window, RadialRing ring) = Shown();

            var hub = Ellipses(ring).Single(e => e.Width > 20);
            (double Left, double Top) hubBefore = (Canvas.GetLeft(hub), Canvas.GetTop(hub));
            List<StackPanel> cellsBefore = Cells(ring);
            var cellPositionsBefore = cellsBefore.Select(c => (Canvas.GetLeft(c), Canvas.GetTop(c))).ToList();
            var dotBefore = (Canvas.GetLeft(Ellipses(ring).Single(e => e.Width == 8)), Canvas.GetTop(Ellipses(ring).Single(e => e.Width == 8)));

            MoveTo(window, new Point(150, 90));
            Dispatcher.UIThread.RunJobs();

            var dot = Ellipses(ring).Single(e => e.Width == 8);
            var halo = Ellipses(ring).Single(e => e.Width == 14);
            Assert.True(dot.IsVisible);
            Assert.True(halo.IsVisible);
            Assert.NotEqual(dotBefore, (Canvas.GetLeft(dot), Canvas.GetTop(dot)));

            //Concentric: the halo's top-left trails the dot's by exactly the two ellipses' shared radius delta.
            Assert.Equal(Canvas.GetLeft(dot) - 3, Canvas.GetLeft(halo));
            Assert.Equal(Canvas.GetTop(dot) - 3, Canvas.GetTop(halo));

            var hubAfter = Ellipses(ring).Single(e => e.Width > 20);
            Assert.Equal(hubBefore, (Canvas.GetLeft(hubAfter), Canvas.GetTop(hubAfter)));
            List<StackPanel> cellsAfter = Cells(ring);
            var cellPositionsAfter = cellsAfter.Select(c => (Canvas.GetLeft(c), Canvas.GetTop(c))).ToList();
            Assert.Equal(cellPositionsBefore, cellPositionsAfter);
        }

        [AvaloniaFact]
        public void Size_IdenticalAtAllFiveScaleContractSteps() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            foreach (double step in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 }) {
                ScaleContract.Apply(step);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(220, ring.Bounds.Width);
                Assert.Equal(220, ring.Bounds.Height);

                TextBlock label = ring.GetVisualDescendants().OfType<TextBlock>().First();
                Application.Current!.TryGetResource("Foreman.Type.RadialLabel.FontSize", Application.Current.ActualThemeVariant, out object? fontSizeObj);
                Assert.Equal((double)fontSizeObj!, label.FontSize);

                Viewbox iconBox = ring.GetVisualDescendants().OfType<Viewbox>().First();
                Assert.Equal(17, iconBox.Bounds.Width);
                Assert.Equal(17, iconBox.Bounds.Height);
            }
        }

        [AvaloniaFact]
        public void Pumping_OpenThenClose_SettlesToABoundedRebuildAndFinalOpacity() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            int layouts = 0;
            ring.LayoutUpdated += (_, _) => layouts++;

            AnimationClock.Settle();
            Assert.Equal(1, ring.Opacity);

            ring.IsOpen = false;
            AnimationClock.Settle();
            Assert.Equal(0, ring.Opacity);

            Assert.True(layouts <= 40, $"RadialRing laid out {layouts} times settling one open/close pair.");
        }
    }
}
