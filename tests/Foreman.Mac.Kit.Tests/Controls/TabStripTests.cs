using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Tests.support;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using TabStrip = Foreman.Mac.Kit.Controls.TabStrip;
using TabStripItem = Foreman.Mac.Kit.Controls.TabStripItem;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class TabStripTests : System.IDisposable {
        public void Dispose() => Motion.ReduceMotion = false;

        private static ControlTheme KitTabStripTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.TabStrip", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static Geometry SampleGlyph() {
            Application.Current!.TryGetResource("Kit.Glyph.Filter.14", null, out object? geometry);
            return Assert.IsAssignableFrom<Geometry>(geometry);
        }

        private static TabStrip ToolbarTabs(params string[] labels) {
            var strip = new TabStrip { Theme = KitTabStripTheme() };
            strip.Classes.Add("toolbar-tabs");
            foreach (string label in labels)
                strip.Items.Add(new TabStripItem { Text = label, Glyph = SampleGlyph() });
            return strip;
        }

        private static TabStrip UnderlineTabs(params string[] labels) {
            var strip = new TabStrip { Theme = KitTabStripTheme() };
            strip.Classes.Add("underline");
            foreach (string label in labels)
                strip.Items.Add(new TabStripItem { Text = label });
            return strip;
        }

        private static StackPanel Track(TabStrip strip) =>
            strip.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Track");

        private static Border[] Items(TabStrip strip) =>
            [.. Track(strip).Children.OfType<Border>()];

        private static Border Fill(Border item) =>
            item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");

        private static Border SelectionFill(Border item) =>
            item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionFill");

        private static Border Underline(TabStrip strip) =>
            strip.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Underline");

        [AvaloniaFact]
        public void KitTabStrip_IsAControlThemeTargetingTabStrip() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(TabStrip), KitTabStripTheme().TargetType);
        }

        [AvaloniaFact]
        public void ToolbarTabs_ItemRadiusIsControlRadiusNotSeven() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects");
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Items(strip)[0].CornerRadius);
            Assert.NotEqual(new CornerRadius(7), Items(strip)[0].CornerRadius);
        }

        [AvaloniaFact]
        public void ToolbarTabs_GlyphAndLabelUseTheirOwnSizeStep() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General");
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            Viewbox box = Items(strip)[0].GetVisualDescendants().OfType<Viewbox>().Single();
            TextBlock label = Items(strip)[0].GetVisualDescendants().OfType<TextBlock>().Single();

            Application.Current!.TryGetResource("Foreman.Size.Glyph18", Application.Current.ActualThemeVariant, out object? glyphSize);
            Application.Current!.TryGetResource("Foreman.Type.TabLabel.FontSize", Application.Current.ActualThemeVariant, out object? labelSize);
            Assert.Equal(Assert.IsType<double>(glyphSize), box.Bounds.Width);
            Assert.Equal(Assert.IsType<double>(labelSize), label.FontSize);
        }

        [AvaloniaFact]
        public void ToolbarTabs_Glyph_IsStrokedInTheLabelColourInsideAnEighteenPointMasterBox() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects");
            strip.SelectedIndex = 0;
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            Path selected = Items(strip)[0].GetVisualDescendants().OfType<Path>().Single();
            Path unselected = Items(strip)[1].GetVisualDescendants().OfType<Path>().Single();

            Assert.Null(selected.Fill);
            Assert.Equal(1.5, selected.StrokeThickness);
            Assert.Equal(PenLineCap.Round, selected.StrokeLineCap);
            Assert.Equal(PenLineJoin.Round, selected.StrokeJoin);
            Assert.Equal(Stretch.None, selected.Stretch);
            Assert.Equal(new Size(18, 18), selected.Bounds.Size);
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), selected.Stroke?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), unselected.Stroke?.ToImmutable());
        }

        [AvaloniaFact]
        public void ToolbarTabs_GlyphFill_IsFilledInTheLabelColour_UnderTheStroke_AndFollowsSelection() {
            using var fixture = new KitFixture();
            Application.Current!.TryGetResource("Kit.Glyph.Beacon.18", null, out object? stroke);
            Application.Current!.TryGetResource("Kit.Glyph.Beacon.Fill.18", null, out object? fill);
            var strip = new TabStrip { Theme = KitTabStripTheme() };
            strip.Classes.Add("toolbar-tabs");
            strip.Items.Add(new TabStripItem { Text = "Beacons", Glyph = (Geometry)stroke!, GlyphFill = (Geometry)fill! });
            strip.Items.Add(new TabStripItem { Text = "Modules", Glyph = SampleGlyph() });
            strip.SelectedIndex = 0;
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            Path[] paths = [.. Items(strip)[0].GetVisualDescendants().OfType<Path>()];
            Assert.Equal(2, paths.Length);
            Path fillPath = paths.Single(p => ReferenceEquals(p.Data, fill));
            Path strokePath = paths.Single(p => ReferenceEquals(p.Data, stroke));
            Assert.Null(fillPath.Stroke);
            Assert.Equal(new Size(18, 18), fillPath.Bounds.Size);
            Assert.Equal(Stretch.None, fillPath.Stretch);
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), fillPath.Fill?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), strokePath.Stroke?.ToImmutable());

            strip.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), fillPath.Fill?.ToImmutable());
            Assert.Single(Items(strip)[1].GetVisualDescendants().OfType<Path>());
        }

        [AvaloniaFact]
        public void ToolbarTabs_EachItemCarriesItsModelsToolTip() {
            using var fixture = new KitFixture();
            var strip = new TabStrip { Theme = KitTabStripTheme() };
            strip.Classes.Add("toolbar-tabs");
            strip.Items.Add(new TabStripItem { Text = "General", Glyph = SampleGlyph(), ToolTip = "General — ⌘1" });
            strip.Items.Add(new TabStripItem { Text = "Objects", Glyph = SampleGlyph() });
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("General — ⌘1", ToolTip.GetTip(Items(strip)[0]));
            Assert.Null(ToolTip.GetTip(Items(strip)[1]));
        }

        [AvaloniaFact]
        public void ToolbarTabs_Selected_FillIsAccentSoftAndForegroundIsAccentText() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects");
            strip.SelectedIndex = 0;
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), SelectionFill(Items(strip)[0]).Background?.ToImmutable());
            Assert.Equal(1, SelectionFill(Items(strip)[0]).Opacity);
            Assert.Equal(0, Fill(Items(strip)[0]).Opacity);

            TextBlock label = Items(strip)[0].GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), label.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void ToolbarTabs_HoverToSelected_CrossFadesFillLayers() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects");
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            fixture.StateOf(Items(strip)[1]).Hover();
            AnimationClock.Settle();
            Assert.Equal(1, Fill(Items(strip)[1]).Opacity);
            Assert.Equal(0, SelectionFill(Items(strip)[1]).Opacity);

            strip.SelectedIndex = 1;
            AnimationClock.Settle();

            Assert.Equal(0, Fill(Items(strip)[1]).Opacity);
            Assert.Equal(1, SelectionFill(Items(strip)[1]).Opacity);
        }

        [AvaloniaFact]
        public void ToolbarTabs_Unselected_IsTextSecondary() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects");
            strip.SelectedIndex = 0;
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            TextBlock label = Items(strip)[1].GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), label.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void ToolbarTabs_Hover_UnselectedItemBrightensToText() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects");
            strip.SelectedIndex = 0;
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(Items(strip)[1]).Hover();

            TextBlock label = Items(strip)[1].GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), label.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void ToolbarTabs_SelectedItem_HasNoHoverVisual() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects");
            strip.SelectedIndex = 0;
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(Items(strip)[0]).Hover();

            TextBlock label = Items(strip)[0].GetVisualDescendants().OfType<TextBlock>().Single();
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), label.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void ToolbarTabs_ClickSelectsTheItem() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects");
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            fixture.StateOf(Items(strip)[1]).Click();

            Assert.Equal(1, strip.SelectedIndex);
        }

        [AvaloniaFact]
        public void Underline_SelectedIsTextAt600UnselectedIsTextSecondaryAt400() {
            using var fixture = new KitFixture();
            TabStrip strip = UnderlineTabs("Buildings", "Items & fluids");
            strip.SelectedIndex = 0;
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            TextBlock selectedLabel = Items(strip)[0].GetVisualDescendants().OfType<TextBlock>().Single();
            TextBlock otherLabel = Items(strip)[1].GetVisualDescendants().OfType<TextBlock>().Single();

            Application.Current!.TryGetResource("Foreman.Type.SectionHeader.FontWeight", Application.Current.ActualThemeVariant, out object? selectedWeight);
            Application.Current!.TryGetResource("Foreman.Type.Callout.FontWeight", Application.Current.ActualThemeVariant, out object? normalWeight);

            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), selectedLabel.Foreground?.ToImmutable());
            Assert.Equal(Assert.IsType<FontWeight>(selectedWeight), selectedLabel.FontWeight);
            Assert.Equal(fixture.Brush("Foreman.Color.TextSecondary").ToImmutable(), otherLabel.Foreground?.ToImmutable());
            Assert.Equal(Assert.IsType<FontWeight>(normalWeight), otherLabel.FontWeight);
        }

        [AvaloniaFact]
        public void Underline_NoBackgroundBrushIsSetOnAnyItem() {
            using var fixture = new KitFixture();
            TabStrip strip = UnderlineTabs("Buildings", "Items & fluids");
            strip.SelectedIndex = 0;
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            foreach (Border item in Items(strip))
                Assert.True(item.Background is null || (item.Background is ISolidColorBrush b && b.Color.A == 0));
        }

        [AvaloniaFact]
        public void Underline_UnderlineBarSlidesToTheSelectedItemOverSegmentedSelectionDuration() {
            using var fixture = new KitFixture();
            TabStrip strip = UnderlineTabs("Buildings", "Items & fluids", "Key nodes");
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            strip.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            var transform = Assert.IsType<TransformOperations>(Underline(strip).RenderTransform);
            Assert.Equal(Items(strip)[1].Bounds.X, transform.Value.M31, 1);
            Assert.Equal(Items(strip)[1].Bounds.Width, Underline(strip).Width, 1);
        }

        [AvaloniaFact]
        public void Underline_Transitions_MoveAtSegmentedSelectionDuration() {
            using var fixture = new KitFixture();
            TabStrip strip = UnderlineTabs("Buildings", "Items & fluids");
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            var transition = Assert.Single(Underline(strip).Transitions!.OfType<TransformOperationsTransition>());
            Assert.Equal(Visual.RenderTransformProperty, transition.Property);
            Assert.Equal(Motion.SegmentedSelectionDuration, transition.Duration);
        }

        [AvaloniaFact]
        public void ReduceMotion_UnderlineHasNoRenderTransformTransition() {
            using var fixture = new KitFixture();
            fixture.Appearance.ReduceMotion = true;
            _ = new AppearanceHub(Application.Current!, fixture.Appearance);
            TabStrip strip = UnderlineTabs("Buildings", "Items & fluids");
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            Assert.DoesNotContain(Underline(strip).Transitions!, t => t is TransformOperationsTransition);
        }

        [AvaloniaFact]
        public void Keyboard_LeftRightMoveSelectionWithWraparound() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects", "Presets");
            fixture.Window.Content = strip;
            fixture.StateOf(strip).Focus();

            fixture.StateOf(strip).KeyDown(Key.Right);
            Assert.Equal(1, strip.SelectedIndex);

            fixture.StateOf(strip).KeyDown(Key.Left);
            fixture.StateOf(strip).KeyDown(Key.Left);
            Assert.Equal(2, strip.SelectedIndex);
        }

        [AvaloniaFact]
        public void Keyboard_HomeAndEndJumpToFirstAndLast() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects", "Presets");
            strip.SelectedIndex = 1;
            fixture.Window.Content = strip;
            fixture.StateOf(strip).Focus();

            fixture.StateOf(strip).KeyDown(Key.End);
            Assert.Equal(2, strip.SelectedIndex);

            fixture.StateOf(strip).KeyDown(Key.Home);
            Assert.Equal(0, strip.SelectedIndex);
        }

        [AvaloniaFact]
        public void Keyboard_SkipsDisabledItems() {
            using var fixture = new KitFixture();
            var strip = new TabStrip { Theme = KitTabStripTheme() };
            strip.Classes.Add("toolbar-tabs");
            strip.Items.Add(new TabStripItem { Text = "General", Glyph = SampleGlyph() });
            strip.Items.Add(new TabStripItem { Text = "Objects", Glyph = SampleGlyph(), IsEnabled = false });
            strip.Items.Add(new TabStripItem { Text = "Presets", Glyph = SampleGlyph() });
            fixture.Window.Content = strip;
            fixture.StateOf(strip).Focus();

            fixture.StateOf(strip).KeyDown(Key.Right);

            Assert.Equal(2, strip.SelectedIndex);
        }

        [AvaloniaFact]
        public void KeyboardFocus_RingIsBoundToTheSelectedItem() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects");
            strip.SelectedIndex = 1;
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(strip).Focus();

            Assert.IsType<Rectangle>(AdornerLayer.GetAdorner(Items(strip)[1]));
            Assert.Null(AdornerLayer.GetAdorner(Items(strip)[0]));
        }

        [AvaloniaFact]
        public void SelectedIndex_CoercesIntoTheItemsRange() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs("General", "Objects", "Presets");
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            strip.SelectedIndex = 9;

            Assert.Equal(2, strip.SelectedIndex);
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledTokenPerItem() {
            using var fixture = new KitFixture();
            var strip = new TabStrip { Theme = KitTabStripTheme() };
            strip.Classes.Add("toolbar-tabs");
            strip.Items.Add(new TabStripItem { Text = "General", Glyph = SampleGlyph(), IsEnabled = false });
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), Items(strip)[0].Opacity);
        }

        [AvaloniaFact]
        public void Peer_IsANamedTabListOfTabItems_ReportingSelectionPositionAndHelp() {
            using var fixture = new KitFixture();
            TabStrip strip = ToolbarTabs();
            strip.Items.Add(new TabStripItem { Text = "Presets", Glyph = SampleGlyph(), ToolTip = "Presets — ⌘1" });
            strip.Items.Add(new TabStripItem { Text = "General", Glyph = SampleGlyph(), ToolTip = "General — ⌘2" });
            strip.Items.Add(new TabStripItem { Text = "Advanced", Glyph = SampleGlyph(), ToolTip = "Advanced — ⌘3", IsEnabled = false });
            AutomationProperties.SetName(strip, "Settings panes");
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(strip);
            Assert.Equal(AutomationControlType.Tab, peer.GetAutomationControlType());
            Assert.Equal("Settings panes", peer.GetName());
            IReadOnlyList<AutomationPeer> tabs = peer.GetChildren();
            Assert.Equal(["Presets", "General", "Advanced"], tabs.Select(t => t.GetName()));
            Assert.All(tabs, t => Assert.Equal(AutomationControlType.TabItem, t.GetAutomationControlType()));
            Assert.Equal(["Presets — ⌘1", "General — ⌘2", "Advanced — ⌘3"], tabs.Select(t => t.GetHelpText()));
            Assert.Equal([1, 2, 3], Items(strip).Select(AutomationProperties.GetPositionInSet));
            Assert.All(Items(strip), i => Assert.Equal(3, AutomationProperties.GetSizeOfSet(i)));
            Assert.Equal([true, true, false], tabs.Select(t => t.IsEnabled()));
            Assert.Equal([true, false, false], tabs.Select(t => t.GetProvider<ISelectionItemProvider>()!.IsSelected));
            Assert.Same(tabs[0], Assert.Single(peer.GetProvider<ISelectionProvider>()!.GetSelection()));

            tabs[1].GetProvider<ISelectionItemProvider>()!.Select();
            Assert.Equal(1, strip.SelectedIndex);
            tabs[2].GetProvider<ISelectionItemProvider>()!.Select();
            Assert.Equal(1, strip.SelectedIndex);

            strip.SelectedIndex = 0;
            Assert.Equal([true, false, false], peer.GetChildren().Select(t => t.GetProvider<ISelectionItemProvider>()!.IsSelected));

            var raised = new List<AutomationPropertyChangedEventArgs>[] { [], [], [] };
            for (int i = 0; i < tabs.Count; i++) {
                int index = i;
                tabs[i].PropertyChanged += (_, e) => raised[index].Add(e);
            }
            strip.SelectedIndex = 1;
            AutomationPropertyChangedEventArgs deselected = Assert.Single(raised[0]);
            Assert.Same(SelectionItemPatternIdentifiers.IsSelectedProperty, deselected.Property);
            Assert.Equal((true, false), ((bool?)deselected.OldValue, (bool?)deselected.NewValue));
            AutomationPropertyChangedEventArgs selected = Assert.Single(raised[1]);
            Assert.Same(SelectionItemPatternIdentifiers.IsSelectedProperty, selected.Property);
            Assert.Equal((false, true), ((bool?)selected.OldValue, (bool?)selected.NewValue));
            Assert.Empty(raised[2]);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class TabStripScaleTests {
        [AvaloniaFact]
        public void ItemHeightAndGlyph_TrackScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.TabStrip", null, out object? theme));
            Application.Current!.TryGetResource("Kit.Glyph.Filter.14", null, out object? geometry);
            var strip = new TabStrip { Theme = (ControlTheme)theme! };
            strip.Classes.Add("toolbar-tabs");
            strip.Items.Add(new TabStripItem { Text = "General", Glyph = (Geometry)geometry! });
            fixture.Window.Content = strip;
            Dispatcher.UIThread.RunJobs();

            Border ItemNow() => strip.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Track")
                .Children.OfType<Border>().Single();

            Assert.Equal(46, ItemNow().Height);
            Assert.Equal(18, ItemNow().GetVisualDescendants().OfType<Viewbox>().Single().Bounds.Width);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(69, ItemNow().Height);
            Assert.Equal(27, ItemNow().GetVisualDescendants().OfType<Viewbox>().Single().Bounds.Width);
            Assert.Equal(18, ItemNow().GetVisualDescendants().OfType<Path>().Single().Width);
        }
    }
}
