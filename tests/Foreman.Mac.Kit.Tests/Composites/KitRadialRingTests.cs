using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
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
using System.Windows.Input;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitRadialRingTests : System.IDisposable {
        public void Dispose() {
            Motion.ReduceMotion = false;
            //Plain [Fact]s never touch the dispatcher, so Application.Current is not safely readable here.
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

        private static RadialRing FourOutcome() {
            var ring = new RadialRing { Theme = KitRadialRingTheme() };
            AutomationProperties.SetName(ring, "Link options");
            ring.Items.Add(new RadialItem { GlyphKey = "RadialAdd", Label = "Add…" });
            ring.Items.Add(new RadialItem { GlyphKey = "RadialOutput", Label = "Output" });
            ring.Items.Add(new RadialItem { GlyphKey = "Passthrough", Label = "Passthrough" });
            ring.Items.Add(RadialItem.Empty);
            ring.Items.Add(RadialItem.Empty);
            ring.Items.Add(new RadialItem { GlyphKey = "RadialCancel", Label = "Cancel" });
            return ring;
        }

        private static Point RingPoint(double radius, double angleDeg) {
            double rad = angleDeg * Math.PI / 180;
            return new Point(110 + (radius * Math.Cos(rad)), 110 + (radius * Math.Sin(rad)));
        }

        private static KeyEventArgs PressKey(Control target, Key key, KeyModifiers modifiers = KeyModifiers.None) {
            var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers };
            target.RaiseEvent(args);
            Dispatcher.UIThread.RunJobs();
            return args;
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

            MoveTo(window, new Point(110, 215));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(3, ring.ArmedIndex);
        }

        [AvaloniaFact]
        public void MovingLaterallyBetweenWedges_OutsideTheHub_ReArmsWithoutReenteringTheHub() {
            using var fixture = new KitFixture();
            (Window window, RadialRing ring) = Shown();

            MoveTo(window, new Point(110, 50));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, ring.ArmedIndex);

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
        public void Escape_Dismisses_WithoutRequestingTheContextMenu() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown();

            int dismissed = 0;
            IReadOnlyList<RadialItem>? menuItems = null;
            ring.Dismissed += (_, _) => dismissed++;
            ring.ContextMenuRequested += (_, items) => menuItems = items;

            PressKey(ring, Key.Escape);

            Assert.Equal(1, dismissed);
            Assert.Null(menuItems);
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
            (_, RadialRing ring) = Shown(FourOutcome());

            var opacityTransitions = ring.Transitions!.OfType<DoubleTransition>().Where(t => t.Property == Visual.OpacityProperty).ToList();
            Assert.Single(opacityTransitions);
            Assert.Empty(ring.Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(6, Slices(ring).Count);

            foreach (Path slice in Slices(ring)) {
                Assert.All(slice.Transitions!, t => Assert.IsType<DoubleTransition>(t));
                DoubleTransition opacity = Assert.Single(slice.Transitions!.OfType<DoubleTransition>());
                Assert.Equal(Visual.OpacityProperty, opacity.Property);
                Assert.Equal(TimeSpan.Zero, opacity.Delay);
            }
        }

        [AvaloniaFact]
        public void Sweep_StaggersEachSlicesFadeInDelay_ByIndexAcrossRadialSweepDuration_EmptyWedgesIncluded() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown(FourOutcome());

            List<Path> slices = Slices(ring);
            Assert.Equal(6, slices.Count);
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
        public void FourOutcomeRing_Is220AtEveryScaleStep_WithSixSixtyDegreeWedgesCentredClockwiseFromTop() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown(FourOutcome());

            foreach (double step in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 }) {
                ScaleContract.Apply(step);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(220, ring.Bounds.Width);
                Assert.Equal(220, ring.Bounds.Height);
            }

            List<Path> slices = Slices(ring);
            Assert.Equal(6, slices.Count);
            for (int i = 0; i < 6; i++) {
                double centre = 270 + (60 * i);
                Assert.True(slices[i].Data!.FillContains(RingPoint(69, centre)), $"wedge {i} misses its centre {centre}");
                Assert.True(slices[i].Data!.FillContains(RingPoint(69, centre - 29)), $"wedge {i} is narrower than 60 degrees");
                Assert.True(slices[i].Data!.FillContains(RingPoint(69, centre + 29)), $"wedge {i} is narrower than 60 degrees");
                Assert.False(slices[i].Data!.FillContains(RingPoint(69, centre + 31)), $"wedge {i} is wider than 60 degrees");
            }
        }

        [AvaloniaFact]
        public void EmptySlots_KeepTheFilledWedgesFillAndHairlineStroke_WithNoGlyphLabelOrVisibleCell() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown(FourOutcome());

            List<Path> slices = Slices(ring);
            List<StackPanel> cells = Cells(ring);
            foreach (int i in new[] { 3, 4 }) {
                Assert.Equal(slices[0].Fill?.ToImmutable(), slices[i].Fill?.ToImmutable());
                Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), slices[i].Stroke?.ToImmutable());
                Assert.Equal(1, slices[i].StrokeThickness);
                Assert.False(cells[i].IsVisible);
                Path icon = (Path)cells[i].GetVisualDescendants().OfType<Viewbox>().Single().Child!;
                Assert.Null(icon.Data);
                Assert.True(string.IsNullOrEmpty(cells[i].GetVisualDescendants().OfType<TextBlock>().Single().Text));
            }
        }

        [AvaloniaFact]
        public void FilledCells_SitOnTheSixtyNinePointLabelRadius_AtTheirFixedSlots() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown(FourOutcome());

            List<StackPanel> cells = Cells(ring);
            (int Slot, double X, double Y)[] expected = [(0, 110, 41), (1, 169.76, 75.5), (2, 169.76, 144.5), (5, 50.24, 75.5)];
            foreach ((int slot, double x, double y) in expected) {
                StackPanel cell = cells[slot];
                Assert.Equal(x, Canvas.GetLeft(cell) + (cell.Width / 2), 0.5);
                Assert.Equal(y, Canvas.GetTop(cell) + (cell.Height / 2), 0.5);
            }
        }

        [AvaloniaFact]
        public void PointingIntoAnEmptyWedge_Disarms_AndReleasingThereDismissesWithoutCommitting() {
            using var fixture = new KitFixture();
            var executed = 0;
            RadialRing ring = FourOutcome();
            ring.Items[2].Command = new RecordingCommand(() => executed++);
            (Window window, _) = Shown(ring);
            int committed = 0, dismissed = 0;
            ring.Committed += (_, _) => committed++;
            ring.Dismissed += (_, _) => dismissed++;

            MoveTo(window, RingPoint(60, 90));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(-1, ring.ArmedIndex);

            MoveTo(window, RingPoint(60, 30));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, ring.ArmedIndex);

            MoveTo(window, RingPoint(60, 90));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(-1, ring.ArmedIndex);

            ReleaseAt(window, RingPoint(60, 90));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, dismissed);
            Assert.Equal(0, committed);
            Assert.Equal(0, executed);
        }

        [AvaloniaFact]
        public void DirectionKeys_ArmFilledSlots_AndAKeyAimedAtAnEmptyWedgeLeavesTheArm() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown(FourOutcome());

            PressKey(ring, Key.Up);
            Assert.Equal(0, ring.ArmedIndex);
            PressKey(ring, Key.Left);
            Assert.Equal(5, ring.ArmedIndex);
            PressKey(ring, Key.Right);
            Assert.Equal(2, ring.ArmedIndex);
            PressKey(ring, Key.Down);
            Assert.Equal(2, ring.ArmedIndex);
        }

        [AvaloniaFact]
        public void APrintableKey_RequestsTheContextMenu_WithOnlyTheFilledItemsInSlotOrder() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown(FourOutcome());
            IReadOnlyList<RadialItem>? menuItems = null;
            ring.ContextMenuRequested += (_, items) => menuItems = items;

            PressKey(ring, Key.A);

            Assert.NotNull(menuItems);
            Assert.Equal(["Add…", "Output", "Passthrough", "Cancel"], menuItems!.Select(i => i.Label));
        }

        [AvaloniaFact]
        public void AutomationPeer_IsAMenuNamedByTheHost_WithAMenuItemPerFilledWedgeInClockwiseOrder() {
            using var fixture = new KitFixture();
            (_, RadialRing ring) = Shown(FourOutcome());

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(ring);
            Assert.Equal(AutomationControlType.Menu, peer.GetAutomationControlType());
            Assert.Equal("Link options", peer.GetName());

            IReadOnlyList<AutomationPeer> children = peer.GetChildren();
            Assert.Equal(["Add…", "Output", "Passthrough", "Cancel"], children.Select(c => c.GetName()));
            Assert.All(children, c => Assert.Equal(AutomationControlType.MenuItem, c.GetAutomationControlType()));
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

        [AvaloniaFact]
        public void TabBareModifiersAndCommandChords_LeaveTheRingOpenFocusedAndArmed_WhilePrintableKeysRequestTheFallback() {
            using var fixture = new KitFixture();
            foreach (KeyModifiers modifiers in new[] { KeyModifiers.None, KeyModifiers.Shift }) {
                (Window window, RadialRing ring) = Shown(FourOutcome());
                ((Canvas)ring.Parent!).Children.Insert(0, new Button { Content = "Beside" });
                ring.Focus();
                ring.ArmedIndex = 2;

                KeyEventArgs args = PressKey(ring, Key.Tab, modifiers);

                Assert.True(ring.IsFocused, modifiers.ToString());
                Assert.True(args.Handled, modifiers.ToString());
                Assert.True(ring.IsOpen);
                Assert.Equal(2, ring.ArmedIndex);
                window.Close();
            }

            var ignored = new (Key Key, KeyModifiers Modifiers)[] {
                (Key.Tab, KeyModifiers.None), (Key.LeftShift, KeyModifiers.None), (Key.RightShift, KeyModifiers.None), (Key.LWin, KeyModifiers.None),
                (Key.LeftCtrl, KeyModifiers.None), (Key.LeftAlt, KeyModifiers.None),
                (Key.A, KeyModifiers.Meta), (Key.A, KeyModifiers.Control), (Key.Space, KeyModifiers.Meta),
            };
            foreach ((Key key, KeyModifiers modifiers) in ignored) {
                (Window window, RadialRing ring) = Shown(FourOutcome());
                ring.ArmedIndex = 2;
                int dismissed = 0, requested = 0;
                ring.Dismissed += (_, _) => dismissed++;
                ring.ContextMenuRequested += (_, _) => requested++;

                PressKey(ring, key, modifiers);

                Assert.True(ring.IsOpen, $"{modifiers}+{key}");
                Assert.Equal(2, ring.ArmedIndex);
                Assert.Equal(0, dismissed);
                Assert.Equal(0, requested);
                window.Close();
            }

            var printable = new (Key Key, KeyModifiers Modifiers)[] {
                (Key.A, KeyModifiers.None), (Key.Space, KeyModifiers.None), (Key.A, KeyModifiers.Shift), (Key.A, KeyModifiers.Alt),
            };
            foreach ((Key key, KeyModifiers modifiers) in printable) {
                (Window window, RadialRing ring) = Shown(FourOutcome());
                int dismissed = 0, requested = 0;
                ring.Dismissed += (_, _) => dismissed++;
                ring.ContextMenuRequested += (_, _) => requested++;

                PressKey(ring, key, modifiers);

                Assert.Equal(1, dismissed);
                Assert.Equal(1, requested);
                window.Close();
            }
        }

        private static void TabTwelveTimesFrom(Control start, RadialRing ring) {
            start.Focus();
            TopLevel top = TopLevel.GetTopLevel(start)!;
            for (int i = 0; i < 12; i++) {
                PressKey((Control)top.FocusManager!.GetFocusedElement()!, Key.Tab);
                Assert.NotSame(ring, top.FocusManager.GetFocusedElement());
            }
        }

        private static bool HasLinkOptionsMenu(AutomationPeer peer) =>
            (peer.GetAutomationControlType() == AutomationControlType.Menu && peer.GetName() == "Link options")
            || peer.GetChildren().Any(HasLinkOptionsMenu);

        [AvaloniaFact]
        public void ClosedRing_FadesOutBeforeHiding_ThenLeavesTheTabOrderAndTheTree_AndReopensVisibleAndFocused() {
            using var fixture = new KitFixture();
            Assert.False(new RadialRing { Theme = KitRadialRingTheme() }.Focusable);

            (Window window, RadialRing ring) = Shown(FourOutcome());
            var host = (Canvas)ring.Parent!;
            var before = new Button { Content = "Before" };
            host.Children.Insert(0, before);
            AnimationClock.Settle();
            Assert.True(ring.IsFocused);

            ring.IsOpen = false;
            Dispatcher.UIThread.RunJobs();
            Assert.True(ring.IsVisible);
            TabTwelveTimesFrom(before, ring);
            Assert.True(ring.IsVisible);
            Assert.False(ring.Focusable);
            Assert.False(KeyboardNavigation.GetIsTabStop(ring));

            AnimationClock.Settle((int)Motion.RadialCloseDuration.TotalMilliseconds + 150);
            Assert.False(ring.IsVisible);
            TabTwelveTimesFrom(before, ring);
            Assert.False(HasLinkOptionsMenu(ControlAutomationPeer.CreatePeerForElement(host)));

            ring.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(ring.IsVisible);
            Assert.True(ring.IsFocused);
            Assert.True(HasLinkOptionsMenu(ControlAutomationPeer.CreatePeerForElement(host)));
            window.Close();
        }

        private static double ScaleOf(RadialRing ring) => Assert.IsAssignableFrom<ITransform>(ring.RenderTransform).Value.M11;

        [AvaloniaFact]
        public void ClosedAndOpeningRing_SitsAtRadialOpenScale_FromMotion_AndNeverScalesUnderReduceMotion() {
            using var fixture = new KitFixture();
            Assert.DoesNotContain(KitRadialRingTheme().Setters.OfType<Setter>(), s => s.Property == Visual.RenderTransformProperty);

            RadialRing ring = Themed();
            var window = new Window { Width = 220, Height = 220, Content = new Canvas { Children = { ring } } };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Motion.RadialOpenScale, ScaleOf(ring), 3);

            ring.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Motion.RadialOpenScale, ScaleOf(ring), 3);
            AnimationClock.Settle();
            Assert.Equal(1, ScaleOf(ring), 3);

            ring.IsOpen = false;
            AnimationClock.Settle();
            Assert.Equal(Motion.RadialOpenScale, ScaleOf(ring), 3);
            window.Close();

            Motion.ReduceMotion = true;
            (Window reduced, RadialRing still) = Shown();
            Assert.Equal(1, ScaleOf(still), 3);
            still.IsOpen = false;
            AnimationClock.Settle();
            Assert.Equal(1, ScaleOf(still), 3);
            reduced.Close();
        }
    }
}
