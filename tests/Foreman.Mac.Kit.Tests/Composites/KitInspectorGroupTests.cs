using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
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
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitInspectorGroupTests {
        private static ControlTheme KitInspectorGroupTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.InspectorGroup", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static InspectorGroup Themed(string header, object? content = null, bool isExpanded = true) =>
            new() { Theme = KitInspectorGroupTheme(), Header = header, IsExpanded = isExpanded, Content = content ?? new TextBlock { Text = "body" } };

        private static Border HeaderRow(InspectorGroup group) =>
            group.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HeaderRow");

        private static Path Triangle(InspectorGroup group) =>
            group.GetVisualDescendants().OfType<Path>().Single(p => p.Name == "PART_Triangle");

        private static ContentPresenter ContentHost(InspectorGroup group) =>
            group.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentHost");

        private static Rectangle? RingOn(Control control) => AdornerLayer.GetAdorner(control) as Rectangle;

        [AvaloniaFact]
        public void KitInspectorGroup_IsAControlThemeTargetingInspectorGroup() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(InspectorGroup), KitInspectorGroupTheme().TargetType);
        }

        [AvaloniaFact]
        public void HeaderRow_HeightIsControlHeightRegular() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Recipe");
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(22, HeaderRow(group).Height);
        }

        [AvaloniaFact]
        public void Triangle_UsesTheDisclosureTwelvePointGlyph() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Recipe");
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Kit.Glyph.Disclosure.12", null, out object? geometry);
            Assert.Equal(geometry, Triangle(group).Data);
        }

        [AvaloniaFact]
        public void FocusRing_BoundsEqualTheWholeHeaderRow_NotJustTheTriangle() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Recipe");
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Border header = HeaderRow(group);
            fixture.StateOf(header).Focus();

            Rectangle ring = Assert.IsType<Rectangle>(RingOn(header));
            Assert.Equal(new Size(header.Bounds.Width + 7, header.Bounds.Height + 7), ring.Bounds.Size);
            Assert.NotEqual(new Size(12 + 7, 12 + 7), ring.Bounds.Size);
        }

        [AvaloniaFact]
        public void ConstructedCollapsed_NeverRealizesItsBody() {
            using var fixture = new KitFixture();
            var marker = new TextBlock { Text = "body" };
            InspectorGroup group = Themed("Module slots", marker, isExpanded: false);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            ContentPresenter host = ContentHost(group);
            Assert.False(host.IsVisible);
            Assert.Null(host.Content);
            Assert.DoesNotContain(marker, group.GetVisualDescendants());
        }

        [AvaloniaFact]
        public void ConstructedExpanded_RealizesItsBodyImmediately() {
            using var fixture = new KitFixture();
            var marker = new TextBlock { Text = "body" };
            InspectorGroup group = Themed("Recipe", marker, isExpanded: true);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            ContentPresenter host = ContentHost(group);
            Assert.True(host.IsVisible);
            Assert.Same(marker, host.Content);
        }

        [AvaloniaFact]
        public void ExpandingAfterConstructedCollapsed_RealizesTheBodyThenKeepsItOnRecollapse() {
            using var fixture = new KitFixture();
            var marker = new TextBlock { Text = "body" };
            InspectorGroup group = Themed("Module slots", marker, isExpanded: false);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            group.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            ContentPresenter host = ContentHost(group);
            Assert.True(host.IsVisible);
            Assert.Same(marker, host.Content);

            group.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(host.IsVisible);
            Assert.Same(marker, host.Content);
        }

        [AvaloniaFact]
        public void GapToNextGroup_IsTwelvePoints_AndZeroWhenCollapsed() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.FormGroup", null, out object? formTheme));
            FormGroup Body() {
                var group = new FormGroup { Theme = (ControlTheme)formTheme! };
                group.Items.Add(new TextBlock { Text = "row" });
                return group;
            }

            FormGroup firstBody = Body();
            InspectorGroup first = Themed("Recipe", firstBody);
            InspectorGroup second = Themed("Modules", Body());
            var stack = new StackPanel { Children = { first, second } };
            fixture.Window.Content = stack;
            Dispatcher.UIThread.RunJobs();

            double bodyBottom = firstBody.TranslatePoint(new Point(0, firstBody.Bounds.Height), stack)!.Value.Y;
            Assert.Equal(12, second.Bounds.Y - bodyBottom, 2);

            first.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, second.Bounds.Y - first.Bounds.Bottom, 2);
        }

        [AvaloniaFact]
        public void ReplacingContent_ResetsOutgoingFormGroupTrailingGap_BeforeApplyingZeroToIncoming() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.FormGroup", null, out object? formTheme));
            FormGroup Body() {
                var group = new FormGroup { Theme = (ControlTheme)formTheme! };
                group.Items.Add(new TextBlock { Text = "row" });
                return group;
            }

            FormGroup first = Body();
            InspectorGroup group = Themed("Recipe", first);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            FormGroup second = Body();
            group.Content = second;
            Dispatcher.UIThread.RunJobs();

            Assert.Null(first.TrailingGap);
            Assert.Equal(0, second.TrailingGap);
        }

        [AvaloniaFact]
        public void HeaderRow_PaddingIsFourPointLeadingOnly() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Recipe");
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Border header = HeaderRow(group);
            Assert.Equal(new Thickness(4, 0, 0, 0), header.Padding);

            StackPanel row = header.GetVisualChildren().OfType<StackPanel>().Single();
            Point origin = row.TranslatePoint(new Point(0, 0), header) ?? default;
            Assert.Equal(4, origin.X, 2);
        }

        [AvaloniaFact]
        public void AutomationPeer_ExposesExpandCollapseState() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Recipe", isExpanded: true);
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(group);
            var provider = Assert.IsAssignableFrom<IExpandCollapseProvider>(peer);
            Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);

            provider.Collapse();
            Assert.False(group.IsExpanded);
            Assert.Equal(ExpandCollapseState.Collapsed, provider.ExpandCollapseState);

            provider.Expand();
            Assert.True(group.IsExpanded);
            Assert.Equal(ExpandCollapseState.Expanded, provider.ExpandCollapseState);
        }

        [AvaloniaFact]
        public void NestingAnInspectorGroupInsideAnother_Throws() {
            using var fixture = new KitFixture();
            InspectorGroup inner = Themed("Inner");
            InspectorGroup outer = Themed("Outer", inner, isExpanded: true);

            Assert.Throws<InvalidOperationException>(() => {
                fixture.Window.Content = outer;
                Dispatcher.UIThread.RunJobs();
            });
        }

        [AvaloniaFact]
        public void Space_TogglesExpansion() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Recipe");
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Border header = HeaderRow(group);
            fixture.StateOf(header).Focus();
            fixture.StateOf(header).KeyDown(Key.Space);
            Assert.False(group.IsExpanded);

            fixture.StateOf(header).KeyDown(Key.Space);
            Assert.True(group.IsExpanded);
        }

        [AvaloniaFact]
        public void Left_Collapses_Right_Expands() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Recipe");
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Border header = HeaderRow(group);
            fixture.StateOf(header).Focus();
            fixture.StateOf(header).KeyDown(Key.Left);
            Assert.False(group.IsExpanded);

            fixture.StateOf(header).KeyDown(Key.Right);
            Assert.True(group.IsExpanded);
        }

        [AvaloniaFact]
        public void UpDown_MoveFocusBetweenSiblingGroupHeaders() {
            using var fixture = new KitFixture();
            InspectorGroup first = Themed("Recipe");
            InspectorGroup second = Themed("Modules");
            InspectorGroup third = Themed("Beacons");
            var stack = new StackPanel { Children = { first, second, third } };
            fixture.Window.Content = stack;
            Dispatcher.UIThread.RunJobs();

            Border secondHeader = HeaderRow(second);
            fixture.StateOf(secondHeader).Focus();

            fixture.StateOf(secondHeader).KeyDown(Key.Down);
            Assert.True(HeaderRow(third).IsFocused);

            fixture.StateOf(HeaderRow(third)).KeyDown(Key.Up);
            Assert.True(secondHeader.IsFocused);
        }

        [AvaloniaFact]
        public void ClickingHeaderRow_TogglesExpansion() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Recipe");
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(HeaderRow(group)).Click();
            Assert.False(group.IsExpanded);

            fixture.StateOf(HeaderRow(group)).Click();
            Assert.True(group.IsExpanded);
        }

        [AvaloniaFact]
        public void HoverFill_UsesControlBackgroundHover() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Recipe");
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Border header = HeaderRow(group);
            fixture.StateOf(header).Hover();

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.ControlBackgroundHover")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color);
        }

        [AvaloniaFact]
        public void Title_LetterSpacingIsZero_AndCasingIsUnchanged() {
            using var fixture = new KitFixture();
            InspectorGroup group = Themed("Module slots");
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            var text = group.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_HeaderText");
            Assert.Equal(0, text.LetterSpacing);
            Assert.Equal("Module slots", text.Content);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class KitInspectorGroupScaleTests {
        private static Border HeaderRow(InspectorGroup group) =>
            group.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HeaderRow");

        [AvaloniaFact]
        public void HeaderHeight_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.InspectorGroup", null, out object? theme));
            var group = new InspectorGroup { Theme = (ControlTheme)theme!, Header = "Recipe", Content = new TextBlock { Text = "body" } };
            fixture.Window.Content = group;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(22, HeaderRow(group).Height);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33, HeaderRow(group).Height);
        }
    }
}
