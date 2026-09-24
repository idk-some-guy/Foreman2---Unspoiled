using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Provider;
using Avalonia.Automation.Peers;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class SegmentedTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitSegmentedTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Segmented", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        internal static Segmented Themed(params string[] items) {
            var segmented = new Segmented { Theme = KitSegmentedTheme() };
            foreach (string item in items)
                segmented.Items.Add(item);
            return segmented;
        }

        internal static Border[] Segments(Segmented segmented) =>
            [.. segmented.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("segment"))];

        internal static TextBlock[] Labels(Segmented segmented) =>
            [.. segmented.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("segLabel"))];

        internal static Border Selection(Segmented segmented) =>
            segmented.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Selection");

        internal static Grid Track(Segmented segmented) =>
            segmented.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "PART_Track");

        //Every label sits inside its own cell, and after settling on each index PART_Selection's real (transformed) rect matches that cell's rect.
        internal static void AssertLayoutMatchesCells(Segmented segmented) {
            Dispatcher.UIThread.RunJobs();

            Border[] cells = Segments(segmented);
            TextBlock[] labels = Labels(segmented);

            for (int i = 0; i < cells.Length; i++) {
                Border cell = cells[i];
                TextBlock label = labels[i];

                Assert.True(label.Bounds.X >= -0.5 && label.Bounds.Y >= -0.5, $"label {i} starts outside its cell: {label.Bounds}");
                Assert.True(label.Bounds.Right <= cell.Bounds.Width + 0.5, $"label {i} right edge {label.Bounds.Right} exceeds cell width {cell.Bounds.Width}");
                Assert.True(label.Bounds.Bottom <= cell.Bounds.Height + 0.5, $"label {i} bottom edge {label.Bounds.Bottom} exceeds cell height {cell.Bounds.Height}");
                Assert.True(label.DesiredSize.Width <= cell.Bounds.Width + 0.5, $"label {i} desired width {label.DesiredSize.Width} clips cell width {cell.Bounds.Width}");
                Assert.True(label.DesiredSize.Height <= cell.Bounds.Height + 0.5, $"label {i} desired height {label.DesiredSize.Height} clips cell height {cell.Bounds.Height}");
            }

            for (int i = 0; i < cells.Length; i++) {
                segmented.SelectedIndex = i;
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle();

                Border cell = cells[i];
                Border selection = Selection(segmented);
                double dx = selection.RenderTransform is TransformOperations ops ? ops.Value.M31 : 0;

                Assert.Equal(cell.Bounds.X, selection.Bounds.X + dx, 0);
                Assert.Equal(cell.Bounds.Width, selection.Bounds.Width, 0);
                Assert.Equal(cell.Bounds.Height, selection.Bounds.Height, 0);
            }
        }

        [AvaloniaFact]
        public void KitSegmented_IsAControlThemeTargetingSegmented() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Segmented), KitSegmentedTheme().TargetType);
        }

        [AvaloniaFact]
        public void Sizes_ComeFromTokens() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ControlHeightRegular", Application.Current.ActualThemeVariant, out object? height);
            Assert.Equal(Assert.IsType<double>(height), segmented.Height);

            Border segment = Segments(segmented)[0];
            Application.Current!.TryGetResource("Foreman.Radius.SegmentItem", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), segment.CornerRadius);
        }

        [AvaloniaFact]
        public void Selection_ActiveSegmentFillsControlBackgroundAndTextForeground() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            segmented.SelectedIndex = 1;
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            TextBlock[] labels = Labels(segmented);

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackground").ToImmutable(), Selection(segmented).Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), labels[1].Foreground?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), labels[0].Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Hover_AllLabelsLightenToText() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(segmented).Hover();

            foreach (TextBlock label in Labels(segmented))
                Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), label.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Pressed_ActiveSegmentDarkensToControlBackgroundPressed() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            Border segment0 = Segments(segmented)[0];
            fixture.StateOf(segment0).Hover();
            fixture.StateOf(segment0).Press();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundPressed").ToImmutable(), Selection(segmented).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Selection_OverlayIsNotHitTestVisible() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            Assert.False(Selection(segmented).IsHitTestVisible);
        }

        [AvaloniaFact]
        public void PointerClick_OnTheEmptyAreaOfAColumn_SelectsThatSegment() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            segmented.Width = 200;
            segmented.SelectedIndex = 1;
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            Border segment = Segments(segmented)[0];
            Point edge = Avalonia.VisualExtensions.TranslatePoint(segment, new Point(4, segment.Bounds.Height / 2), fixture.Window)!.Value;

            fixture.Window.MouseDown(edge, MouseButton.Left, RawInputModifiers.None);
            fixture.Window.MouseUp(edge, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, segmented.SelectedIndex);
        }

        [AvaloniaFact]
        public void Press_DoesNotSelect_ReleaseOverTheSameSegmentSelects() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            segmented.Width = 200;
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            Border segment = Segments(segmented)[1];
            Point edge = Avalonia.VisualExtensions.TranslatePoint(segment, new Point(4, segment.Bounds.Height / 2), fixture.Window)!.Value;

            fixture.Window.MouseDown(edge, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, segmented.SelectedIndex);

            fixture.Window.MouseUp(edge, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, segmented.SelectedIndex);
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            fixture.Window.Content = segmented;

            segmented.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), segmented.Opacity);
        }

        [AvaloniaFact]
        public void ArrowKeys_MoveSelectionWithWraparound() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down", "Auto");
            fixture.Window.Content = segmented;
            fixture.StateOf(segmented).Focus();

            fixture.StateOf(segmented).KeyDown(Key.Right);
            Assert.Equal(1, segmented.SelectedIndex);

            fixture.StateOf(segmented).KeyDown(Key.Left);
            fixture.StateOf(segmented).KeyDown(Key.Left);
            Assert.Equal(2, segmented.SelectedIndex);
        }

        [AvaloniaFact]
        public void Space_LeavesSelectionUnchangedAndHandlesTheKey() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            segmented.SelectedIndex = 1;
            fixture.Window.Content = segmented;
            fixture.StateOf(segmented).Focus();

            var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space };
            segmented.RaiseEvent(args);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, segmented.SelectedIndex);
            Assert.True(args.Handled);
        }

        [AvaloniaFact]
        public void Selection_MovesImmediatelyRegardlessOfReduceMotion() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            Segmented segmented = Themed("Up", "Down");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            segmented.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackground").ToImmutable(), Selection(segmented).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void KeyboardFocus_ShowsAFocusRingAtTheControlRadius() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            fixture.Window.Content = segmented;

            fixture.StateOf(segmented).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(segmented));

            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Application.Current!.TryGetResource("Foreman.FocusRing.Offset", Application.Current.ActualThemeVariant, out object? offset);
            double expected = ((CornerRadius)radius!).TopLeft + (double)offset!;
            Assert.Equal(expected, ring.RadiusX);
        }

        [AvaloniaFact]
        public void Mini_UsesTheDrawnFontSizeAndWeight() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            segmented.Classes.Add("mini");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Type.SegmentMini.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Type.SegmentMini.FontWeight", Application.Current.ActualThemeVariant, out object? weight);
            TextBlock label = Labels(segmented)[0];
            Assert.Equal(Assert.IsType<double>(size), label.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), label.FontWeight);
        }

        [AvaloniaFact]
        public void Transitions_MoveTheSelectionAtSegmentedSelectionDuration() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            var transition = Assert.Single(Selection(segmented).Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(Visual.RenderTransformProperty, transition.Property);
            Assert.Equal(Motion.SegmentedSelectionDuration, transition.Duration);
        }

        [AvaloniaFact]
        public void ReduceMotion_SelectionHasNoRenderTransformTransition() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            Segmented segmented = Themed("Up", "Down");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(Selection(segmented).Transitions!, t => t is TransformOperationsTransition);
        }

        [AvaloniaFact]
        public void Selection_TranslatesToTheSelectedSegment() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down", "Auto");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            double expectedX = Segments(segmented)[0].Bounds.Width;

            segmented.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            var transform = Assert.IsType<TransformOperations>(Selection(segmented).RenderTransform);
            Assert.Equal(expectedX, transform.Value.M31, 1);
        }

        [AvaloniaFact]
        public void LayoutPassDuringTheSlide_DoesNotCancelTheAnimation() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down", "Auto");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            double expectedX = Segments(segmented)[0].Bounds.Width;

            segmented.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            Labels(segmented)[0].InvalidateMeasure();
            fixture.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            var midFlight = Assert.IsType<TransformOperations>(Selection(segmented).RenderTransform);
            Assert.NotEqual(expectedX, midFlight.Value.M31, 1);

            AnimationClock.Settle();

            var settled = Assert.IsType<TransformOperations>(Selection(segmented).RenderTransform);
            Assert.Equal(expectedX, settled.Value.M31, 1);
        }

        [AvaloniaFact]
        public void Reattach_WithUnchangedResources_DoesNotRebuildTheSegments() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            var host = new StackPanel();
            host.Children.Add(segmented);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            Border[] before = Segments(segmented);

            host.Children.Remove(segmented);
            Dispatcher.UIThread.RunJobs();
            host.Children.Add(segmented);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, Segments(segmented));
        }

        [AvaloniaFact]
        public void RetemplateTwice_ThenOneLayoutPass_RepositionsSelectionOnlyOnce() {
            using var fixture = new KitFixture();
            ControlTheme theme = KitSegmentedTheme();
            Segmented segmented = new() { Theme = theme, Width = 140 };
            segmented.Items.Add("Up");
            segmented.Items.Add("Down");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            segmented.Theme = null;
            Dispatcher.UIThread.RunJobs();
            segmented.Theme = theme;
            Dispatcher.UIThread.RunJobs();

            int baseline = segmented.SelectionRepositions;

            segmented.Width += 40;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(baseline + 1, segmented.SelectionRepositions);

            fixture.Window.Content = null;
            Dispatcher.UIThread.RunJobs();

            segmented.Width += 40;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(baseline + 1, segmented.SelectionRepositions);
        }

        [AvaloniaFact]
        public void Layout_EqualLabels_CellsTrackSelectionAllAlign() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down", "Auto");
            fixture.Window.Content = segmented;

            AssertLayoutMatchesCells(segmented);
        }

        [AvaloniaFact]
        public void Layout_UnequalLabels_CellsTrackSelectionAllAlign() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Downwards", "A");
            fixture.Window.Content = segmented;

            AssertLayoutMatchesCells(segmented);
        }

        [AvaloniaFact]
        public void Layout_Mini_EqualLabels_CellsTrackSelectionAllAlign() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down", "Auto");
            segmented.Classes.Add("mini");
            fixture.Window.Content = segmented;

            AssertLayoutMatchesCells(segmented);
        }

        [AvaloniaFact]
        public void Layout_Mini_UnequalLabels_CellsTrackSelectionAllAlign() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Downwards", "A");
            segmented.Classes.Add("mini");
            fixture.Window.Content = segmented;

            AssertLayoutMatchesCells(segmented);
        }

        [AvaloniaFact]
        public void Layout_UnconstrainedWidth_TrackWidthMatchesSumOfCellsDesiredWidths() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Downwards", "A");
            var host = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            host.Children.Add(segmented);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            double expected = Segments(segmented).Sum(cell => cell.DesiredSize.Width);
            Assert.Equal(expected, Track(segmented).Bounds.Width, 1);
        }

        [AvaloniaFact]
        public void HoverAndPress_DoNotReSizeColumns_MiniToggleDoes() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Downwards", "A");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            int baseline = segmented.ColumnSizings;

            fixture.StateOf(segmented).Hover();
            fixture.StateOf(segmented).Press();
            segmented.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(baseline, segmented.ColumnSizings);

            segmented.Classes.Add("mini");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(baseline + 1, segmented.ColumnSizings);
        }

        [AvaloniaFact]
        public void ScopedToken_SegmentItemPaddingComesFromTheControlsOwnResourceScope() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Up", "Down");
            var host = new StackPanel();
            host.Resources["Foreman.Thickness.SegmentItemPadding"] = new Thickness(15, 4.5);
            host.Children.Add(segmented);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new Thickness(15, 4.5), Segments(segmented)[0].Padding);
        }

        [AvaloniaFact]
        public void AutomationPeer_IsARadioGroup_WhoseChildrenAreRadioButtonsWithPosition() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Rectangle", "Ellipse");
            Avalonia.Automation.AutomationProperties.SetName(segmented, "Shape");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(segmented);
            Assert.Equal(AutomationControlType.List, peer.GetAutomationControlType());
            Assert.Equal("radio group", peer.GetLocalizedControlType());
            Assert.Equal("Shape", peer.GetName());
            AutomationPeer[] children = [.. peer.GetChildren()];
            Assert.Equal(2, children.Length);
            Assert.All(children, c => Assert.Equal(AutomationControlType.RadioButton, c.GetAutomationControlType()));
            Assert.Equal(["Rectangle, 1 of 2", "Ellipse, 2 of 2"], children.Select(c => c.GetName()));
            Assert.True(children[0].GetProvider<ISelectionItemProvider>()!.IsSelected);
            Assert.False(children[1].GetProvider<ISelectionItemProvider>()!.IsSelected);
            Assert.Same(children[0], Assert.Single(peer.GetProvider<ISelectionProvider>()!.GetSelection()));

            children[1].GetProvider<ISelectionItemProvider>()!.Select();
            Assert.Equal(1, segmented.SelectedIndex);
            Assert.True(children[1].GetProvider<ISelectionItemProvider>()!.IsSelected);
        }

        [AvaloniaFact]
        public void AccessibleFocus_AfterTab_IsTheGroup_ReadingTheSelectedSegmentAsItsValue() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Rectangle", "Ellipse");
            Avalonia.Automation.AutomationProperties.SetName(segmented, "Shape");

            AutomationPeer? focus = AccessibleFocus.TabOnto(fixture.Window, segmented);

            Assert.True(segmented.IsFocused);
            Assert.NotNull(focus);
            Assert.Equal(("radio group", "Shape"), (focus.GetLocalizedControlType(), focus.GetName()));
            Assert.Equal("Rectangle, 1 of 2", focus.GetProvider<IValueProvider>()!.Value);
        }

        [AvaloniaFact]
        public void AutomationPeer_MovingTheSelection_RaisesSelectionAndValueChanges() {
            using var fixture = new KitFixture();
            Segmented segmented = Themed("Rectangle", "Ellipse");
            Avalonia.Automation.AutomationProperties.SetName(segmented, "Shape");
            AutomationPeer focus = AccessibleFocus.TabOnto(fixture.Window, segmented)!;
            var events = AccessibleFocus.Record(focus);

            fixture.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, segmented.SelectedIndex);
            Assert.Single(events, e => e.Property == SelectionPatternIdentifiers.SelectionProperty);
            AutomationPropertyChangedEventArgs value = Assert.Single(events, e => e.Property == ValuePatternIdentifiers.ValueProperty);
            Assert.Equal(("Rectangle, 1 of 2", "Ellipse, 2 of 2"), ((string)value.OldValue!, (string)value.NewValue!));
            Assert.Equal("Ellipse, 2 of 2", focus.GetProvider<IValueProvider>()!.Value);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class SegmentedScaleTests {
        [AvaloniaFact]
        public void Height_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Segmented", null, out object? theme));
            var segmented = new Segmented { Theme = Assert.IsType<ControlTheme>(theme) };
            segmented.Items.Add("Up");
            segmented.Items.Add("Down");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(22, segmented.Height);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33, segmented.Height);
        }

        [AvaloniaFact]
        public void Layout_150Percent_EqualLabels_CellsTrackSelectionAllAlign() {
            using var fixture = new KitFixture();
            Segmented segmented = SegmentedTests.Themed("Up", "Down", "Auto");
            segmented.Classes.Add("mini");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();
            Application.Current!.TryGetResource("Foreman.Type.SegmentMini.FontSize", Application.Current.ActualThemeVariant, out object? miniFontSize);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal((double)miniFontSize! * 1.5, SegmentedTests.Labels(segmented)[0].FontSize, 1);
            SegmentedTests.AssertLayoutMatchesCells(segmented);
        }

        [AvaloniaFact]
        public void Layout_150Percent_UnequalLabels_CellsTrackSelectionAllAlign() {
            using var fixture = new KitFixture();
            Segmented segmented = SegmentedTests.Themed("Up", "Downwards", "A");
            segmented.Classes.Add("mini");
            fixture.Window.Content = segmented;
            Dispatcher.UIThread.RunJobs();
            Application.Current!.TryGetResource("Foreman.Type.SegmentMini.FontSize", Application.Current.ActualThemeVariant, out object? miniFontSize);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal((double)miniFontSize! * 1.5, SegmentedTests.Labels(segmented)[0].FontSize, 1);
            SegmentedTests.AssertLayoutMatchesCells(segmented);
        }
    }
}
