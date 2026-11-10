using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    public sealed class DisclosureTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitDisclosureTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.Disclosure", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Disclosure Themed(object header, object content) =>
            new() { Theme = KitDisclosureTheme(), Header = header, Content = content };

        private static Border HeaderRow(Disclosure d) =>
            d.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HeaderRow");

        private static Path Triangle(Disclosure d) =>
            d.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Triangle");

        private static ContentPresenter ContentHost(Disclosure d) =>
            d.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentHost");

        [AvaloniaFact]
        public void KitDisclosure_IsAControlThemeTargetingDisclosure() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(Disclosure), KitDisclosureTheme().TargetType);
        }

        [AvaloniaFact]
        public void Glyph_IsTheDisclosureTwelveMaster() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Kit.Glyph.Disclosure.12", null, out object? geometry);
            Assert.Equal(geometry, Triangle(disclosure).Data);
        }

        [AvaloniaFact]
        public void Glyph_RenderedGeometryMatchesItsUnscaledMaster() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            var master = (Geometry)Application.Current!.FindResource("Kit.Glyph.Disclosure.12")!;
            Path triangle = Triangle(disclosure);
            Assert.Equal(master.Bounds.Width, triangle.RenderedGeometry!.Bounds.Width, 2);
            Assert.Equal(master.Bounds.Height, triangle.RenderedGeometry.Bounds.Height, 2);
        }

        [AvaloniaFact]
        public void Glyph_DiffersFromThePopUpChevronInBounds() {
            using var fixture = new KitFixture();
            Application.Current!.TryGetResource("Kit.Glyph.Disclosure.12", null, out object? disclosureGeometry);
            Application.Current!.TryGetResource("Kit.Glyph.PopUpChevrons.16", null, out object? chevronGeometry);

            Rect disclosureBounds = Assert.IsAssignableFrom<Geometry>(disclosureGeometry).Bounds;
            Rect chevronBounds = Assert.IsAssignableFrom<Geometry>(chevronGeometry).Bounds;

            Assert.NotEqual(disclosureBounds, chevronBounds);
        }

        [AvaloniaFact]
        public void HeaderRow_IsTheOnlyFocusableElement() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            Assert.False(disclosure.Focusable);
            Assert.True(HeaderRow(disclosure).Focusable);
            Assert.DoesNotContain(disclosure.GetVisualDescendants().OfType<Control>(),
                c => c != HeaderRow(disclosure) && c.Focusable);
        }

        [AvaloniaFact]
        public void HeaderRow_IsAtLeastTheRegularControlHeight() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ControlHeightRegular", Application.Current.ActualThemeVariant, out object? height);
            Assert.Equal(Assert.IsType<double>(height), HeaderRow(disclosure).MinHeight);
        }

        [AvaloniaFact]
        public void ClickingHeaderRow_TogglesIsExpanded() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content") ;
            disclosure.IsExpanded = false;
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(HeaderRow(disclosure)).Click();
            Assert.True(disclosure.IsExpanded);

            fixture.StateOf(HeaderRow(disclosure)).Click();
            Assert.False(disclosure.IsExpanded);
        }

        [AvaloniaFact]
        public void ClickingContent_DoesNotToggleIsExpanded() {
            using var fixture = new KitFixture();
            var contentButton = new Button { Content = "Inner" };
            Disclosure disclosure = Themed("Module slots", contentButton);
            disclosure.IsExpanded = true;
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(contentButton).Click();

            Assert.True(disclosure.IsExpanded);
        }

        [AvaloniaFact]
        public void Content_VisibilityTracksIsExpanded() {
            using var fixture = new KitFixture();
            var content = new TextBlock { Text = "content" };
            Disclosure disclosure = Themed("Module slots", content);
            disclosure.IsExpanded = false;
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            Assert.False(ContentHost(disclosure).IsVisible);

            disclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(ContentHost(disclosure).IsVisible);
        }

        [AvaloniaFact]
        public void Rotation_ZeroCollapsedNinetyExpanded() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            disclosure.IsExpanded = false;
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            var collapsed = Assert.IsType<TransformOperations>(Triangle(disclosure).RenderTransform);
            Assert.Equal(1, collapsed.Value.M11, 3);
            Assert.Equal(0, collapsed.Value.M12, 3);

            disclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            var expanded = Assert.IsType<TransformOperations>(Triangle(disclosure).RenderTransform);
            Assert.Equal(0, expanded.Value.M11, 3);
            Assert.Equal(1, expanded.Value.M12, 3);
        }

        [AvaloniaFact]
        public void SpaceAndReturn_ToggleIsExpandedWhenHeaderFocused() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            disclosure.IsExpanded = false;
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            Border header = HeaderRow(disclosure);
            fixture.StateOf(header).KeyDown(Key.Space);
            Assert.True(disclosure.IsExpanded);

            fixture.StateOf(header).KeyDown(Key.Enter);
            Assert.False(disclosure.IsExpanded);
        }

        [AvaloniaFact]
        public void LeftCollapses_RightExpands() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            disclosure.IsExpanded = true;
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            Border header = HeaderRow(disclosure);
            fixture.StateOf(header).KeyDown(Key.Left);
            Assert.False(disclosure.IsExpanded);

            fixture.StateOf(header).KeyDown(Key.Right);
            Assert.True(disclosure.IsExpanded);
        }

        [AvaloniaFact]
        public void Hover_HeaderRowBackgroundIsControlBackgroundHover() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(HeaderRow(disclosure)).Hover();

            Assert.Equal(fixture.Brush("Foreman.Color.ControlBackgroundHover").ToImmutable(), HeaderRow(disclosure).Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            fixture.Window.Content = disclosure;

            disclosure.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), disclosure.Opacity);
        }

        [AvaloniaFact]
        public void Transitions_RotateAtDisclosureDuration() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            var transform = Assert.Single(Triangle(disclosure).Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(Visual.RenderTransformProperty, transform.Property);
            Assert.Equal(Motion.DisclosureDuration, transform.Duration);
        }

        [AvaloniaFact]
        public void ReduceMotion_OnlyOneOpacityTransitionRemains() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            Disclosure disclosure = Themed("Module slots", "content");
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            var only = Assert.Single(Triangle(disclosure).Transitions!);
            var opacity = Assert.IsType<DoubleTransition>(only);
            Assert.Equal(Visual.OpacityProperty, opacity.Property);
        }

        [AvaloniaFact]
        public void RenderTransformOrigin_ParsesToCenterAndRotatedBoundsStayInside() {
            using var fixture = new KitFixture();
            Disclosure disclosure = Themed("Module slots", "content");
            disclosure.IsExpanded = false;
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            Path triangle = Triangle(disclosure);
            Assert.Equal(RelativePoint.Center, triangle.RenderTransformOrigin);

            disclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Border headerRow = HeaderRow(disclosure);
            Matrix matrix = triangle.TransformToVisual(headerRow) ?? Matrix.Identity;
            Rect transformedBounds = new Rect(triangle.Bounds.Size).TransformToAABB(matrix);
            Rect headerRowBounds = new Rect(headerRow.Bounds.Size).Inflate(0.5);

            Assert.True(headerRowBounds.Contains(transformedBounds),
                $"triangle's transformed bounds {transformedBounds} escaped the header row's bounds {headerRowBounds}");
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class DisclosureScaleTests {
        [AvaloniaFact]
        public void TriangleViewbox_TracksScaleContractWhileThePathStaysAtItsMasterSize() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.Disclosure", null, out object? theme));
            var disclosure = new Disclosure { Theme = (ControlTheme)theme!, Header = "Module slots", Content = "content" };
            fixture.Window.Content = disclosure;
            Dispatcher.UIThread.RunJobs();

            Path triangle = disclosure.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Triangle");
            Viewbox box = disclosure.GetVisualDescendants().OfType<Viewbox>().Single();
            Assert.Equal(Stretch.None, triangle.Stretch);
            Assert.Equal(12, box.Bounds.Width);
            Assert.Equal(12, triangle.Bounds.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(18, box.Bounds.Width);
            Assert.Equal(12, triangle.Bounds.Width);
        }
    }
}
