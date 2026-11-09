using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitToolbarRowTests {
        private static ControlTheme KitToolbarRowTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ToolbarRow", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ControlTheme KitToolbarItemTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ToolbarItem", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ToolbarItem Item(string label) =>
            new() { Theme = KitToolbarItemTheme(), Label = label, Tag = label };

        private static ToolbarRow NewRow(double? width = null) {
            var row = new ToolbarRow { Theme = KitToolbarRowTheme(), Title = "Untitled graph" };
            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.Document, Items = [Item("New"), Item("Open"), Item("Save")] });
            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.Add, Items = [Item("Recipe"), Item("Item")] });
            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.Arrange, Items = [Item("Align")] });
            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.ShareView, Items = [Item("Export"), Item("View")] });
            if (width is { } w)
                row.Width = w;
            return row;
        }

        private static Window Shown(ToolbarRow row, double windowWidth) {
            var window = new Window { Width = windowWidth, Height = 120, Content = row };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            //A style-driven property settles a pass behind its class, so the dispatcher must be pumped to a fixed point.
            AnimationClock.Settle(50);
            return window;
        }

        private static ToolbarItem[] AllItems(ToolbarRow row) =>
            [.. row.Groups.SelectMany(g => g.Items)];

        //Bounds are parent-relative, so comparing items across groups needs a common ancestor.
        private static double RootLeft(Visual v, Visual root) =>
            Avalonia.VisualExtensions.TranslatePoint(v, new Point(0, 0), root)?.X ?? double.NaN;

        private static TextBlock LabelHostOf(ToolbarItem item) =>
            item.GetVisualDescendants().OfType<TextBlock>().Single(tb => tb.Name == "PART_Label");

        private static readonly HashSet<AvaloniaProperty> SizeTransitionTargets = [
            Layoutable.WidthProperty, Layoutable.MinWidthProperty, Layoutable.HeightProperty,
            Layoutable.MarginProperty, Visual.BoundsProperty,
        ];

        private static void AssertNoSizeTransitions(Transitions? transitions) {
            if (transitions is null)
                return;
            Assert.DoesNotContain(transitions.OfType<DoubleTransition>(), dt => dt.Property is { } p && SizeTransitionTargets.Contains(p));
            Assert.DoesNotContain(transitions.OfType<ThicknessTransition>(), tt => tt.Property is { } p && SizeTransitionTargets.Contains(p));
        }

        [AvaloniaFact]
        public void KitToolbarRow_IsAControlThemeTargetingToolbarRow() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ToolbarRow), KitToolbarRowTheme().TargetType);
        }

        [AvaloniaFact]
        public void Rebuild_AppliesKitToolbarItemTheme_ToUnthemedItems() {
            using var fixture = new KitFixture();
            var row = new ToolbarRow { Theme = KitToolbarRowTheme(), Title = "Untitled graph" };
            var unthemed = new ToolbarItem { Label = "New" };
            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.Document, Items = [unthemed] });
            Shown(row, 900);

            Assert.Same(KitToolbarItemTheme(), unthemed.Theme);
            Application.Current!.TryGetResource("Foreman.Size.ToolbarItemHeight", Application.Current.ActualThemeVariant, out object? height);
            Assert.Equal(Assert.IsType<double>(height), unthemed.Bounds.Height);
            Assert.NotEqual(0, unthemed.Bounds.Width);
        }

        [AvaloniaFact]
        public void Height_ResolvesToolbarRowHeight() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            Assert.Equal(52, row.Bounds.Height);
        }

        [AvaloniaFact]
        public void RealSpacerControlsSitBetweenGroups_AndAreNotADrawnRule() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            Control[] spacers = [.. panel.Children.Where(c => c.GetType() == typeof(Control))];
            Assert.NotEmpty(spacers);
            Assert.All(spacers, s => Assert.Equal(16, s.Width));
            Assert.DoesNotContain(panel.Children, c => c is Border or Rectangle or Line);
        }

        [AvaloniaFact]
        public void TitleIsTheFirstChild_ZoomStepperIsTheLast() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            Control first = panel.Children.First(c => c.IsVisible);
            Control last = panel.Children.Last(c => c.IsVisible);

            var title = Assert.IsType<TextBlock>(first);
            Assert.Equal("Untitled graph", title.Text);
            Assert.Same(row.ZoomStepper, last);
        }

        [AvaloniaFact]
        public void SidePadding_IsSpace3TwelvePoints() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            Control title = panel.Children.First(c => c.IsVisible);
            Assert.Equal(12, title.Bounds.Left);
        }

        [AvaloniaFact]
        public void WithinGroup_ItemsAreTwoPointsApart() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            ToolbarItem[] items = AllItems(row);
            ToolbarItem newItem = items.Single(i => (string)i.Tag! == "New");
            ToolbarItem openItem = items.Single(i => (string)i.Tag! == "Open");
            Assert.Equal(2, openItem.Bounds.Left - newItem.Bounds.Right, 3);
        }

        [AvaloniaFact]
        public void BetweenGroups_SpacingIsSixteenPoints() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            ToolbarItem[] items = AllItems(row);
            ToolbarItem save = items.Single(i => (string)i.Tag! == "Save");
            ToolbarItem recipe = items.Single(i => (string)i.Tag! == "Recipe");
            double saveRight = RootLeft(save, panel) + save.Bounds.Width;
            Assert.Equal(16, RootLeft(recipe, panel) - saveRight, 3);
        }

        [AvaloniaFact]
        public void GroupOrder_MatchesTheRuledList() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(1600);
            Shown(row, 1700);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            ToolbarItem[] items = AllItems(row);
            string[] order = [.. items.OrderBy(i => RootLeft(i, panel)).Select(i => (string)i.Tag!)];
            Assert.Equal(["New", "Open", "Save", "Recipe", "Item", "Align", "Export", "View"], order);
        }

        [AvaloniaFact]
        public void SearchField_SitsBeforeZoomStepper_WithSixteenPointGap() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            Assert.Equal(16, row.ZoomStepper.Bounds.Left - row.SearchField.Bounds.Right, 3);
        }

        [AvaloniaFact]
        public void SidePaddingChange_RecomputesRequiredWidths_SoCollapseStepReactsToIt() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(1600);
            var host = new StackPanel { Children = { row } };
            var window = new Window { Width = 1700, Height = 120, Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.All(AllItems(row), i => Assert.DoesNotContain("icon-only", i.Classes));

            host.Resources["Foreman.Space.Space3"] = 600.0;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.All(AllItems(row), i => Assert.Contains("icon-only", i.Classes));
        }

        [AvaloniaFact]
        public void SearchFieldMinWidthChange_ReappliesToTheSearchField_AtTheSameStep() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(820);
            var host = new StackPanel { Children = { row } };
            var window = new Window { Width = 920, Height = 120, Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.Equal(120, row.SearchField.Width);

            host.Resources["Foreman.Metrics.SearchFieldMinWidth"] = 140.0;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.Equal(140, row.SearchField.Width);
        }

        [AvaloniaFact]
        public void ToolbarItemWidthChange_RecachesItemWidths_SoCollapseStepReactsToIt() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(1600);
            var host = new StackPanel { Children = { row } };
            var window = new Window { Width = 1700, Height = 120, Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.All(AllItems(row), i => Assert.DoesNotContain("icon-only", i.Classes));

            host.Resources["Foreman.Size.ToolbarItemWidth"] = 400.0;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.All(AllItems(row), i => Assert.Contains("icon-only", i.Classes));
        }

        [AvaloniaFact]
        public void IconOnlyWidthChange_AtFullWidth_PrimesTheCacheForALaterCollapse() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow();
            Window window = Shown(row, 1700);

            Assert.All(AllItems(row), i => Assert.DoesNotContain("icon-only", i.Classes));

            window.Resources["Foreman.Size.ToolbarItemIconOnlyWidth"] = 400.0;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.All(AllItems(row), i => Assert.DoesNotContain("icon-only", i.Classes));

            window.Width = 900;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.True(row.OverflowButton.IsVisible);
        }

        [AvaloniaFact]
        public void IconOnlyWidthChange_WhileAlreadyCollapsed_StillMeasuresTheIconOnlyStyle_AndCollapsesFurther() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(1000);
            Window window = Shown(row, 1100);

            Assert.All(AllItems(row), i => Assert.Contains("icon-only", i.Classes));
            Assert.False(row.OverflowButton.IsVisible);

            window.Resources["Foreman.Size.ToolbarItemIconOnlyWidth"] = 200.0;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(300);

            Assert.All(AllItems(row), i => Assert.Contains("icon-only", i.Classes));
            Assert.True(row.OverflowButton.IsVisible);
        }

        [AvaloniaFact]
        public void ToolbarItemWidthChange_AlsoRemeasuresGroupPanels_SoArrangeUsesFreshWidths() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(1600);
            var host = new StackPanel { Children = { row } };
            var window = new Window { Width = 1700, Height = 120, Content = host };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            ToolbarItem align = AllItems(row).Single(i => (string)i.Tag! == "Align");
            double originalRight = RootLeft(align, panel) + align.Bounds.Width;

            host.Resources["Foreman.Size.ToolbarItemWidth"] = 100.0;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            double newRight = RootLeft(align, panel) + align.Bounds.Width;
            Assert.True(newRight > originalRight + 20,
                $"Expected the group panel ahead of 'Align' to widen and push it right, but it moved from {originalRight} to {newRight}.");
        }

        [AvaloniaFact]
        public void Pumping_AfterSettling_ProducesNoFurtherLayouts() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);
            AnimationClock.Settle(300);

            int layouts = 0;
            row.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);

            Assert.Equal(0, layouts);
        }

        [AvaloniaFact]
        public void IconOnlyStep_KeepsTheItemsLabelProperty_OnlyHidesItVisually() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(1000);
            Shown(row, 1100);

            ToolbarItem newItem = AllItems(row).Single(i => (string)i.Tag! == "New");
            Assert.Contains("icon-only", newItem.Classes);
            Assert.Equal("New", newItem.Label);
        }

        [AvaloniaFact]
        public void Rebuild_AfterGroupsChangeWhileShown_KeepsExistingItemsAndOverflowsTheNewOne() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(260);
            Shown(row, 320);

            row.Groups.Add(new ToolbarGroup { Role = ToolbarGroupRole.Document, Items = [Item("Extra")] });
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            ToolbarItem[] items = AllItems(row);
            ToolbarItem extra = items.Single(i => (string)i.Tag! == "Extra");
            Assert.Contains(items, i => (string)i.Tag! == "New");
            Assert.Equal("Extra", extra.Label);
            Assert.Equal("Extra", AutomationProperties.GetName(extra));

            Assert.Equal(["Extra"], row.OverflowButton.ActionItems.Select(a => a.Text));
        }

        [AvaloniaFact]
        public void SearchField_IsLoading_ShowsItsSpinnerAfterTheDelay() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(1600);
            Shown(row, 1700);

            row.SearchField.IsLoading = true;
            AnimationClock.Settle(450);

            Foreman.Mac.Kit.Controls.Spinner spinner = row.SearchField.GetVisualDescendants().OfType<Foreman.Mac.Kit.Controls.Spinner>().Single(s => s.Name == "PART_LoadingSpinner");
            Assert.True(spinner.IsVisible);
        }

        [AvaloniaFact]
        public void OverflowMenu_RowsCarryTheItemLabelAsAutomationName() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(260);
            fixture.Window.Width = 320;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            row.OverflowButton.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(300);

            StackPanel items = row.OverflowButton.Menu.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_Items");
            string[] names = [.. items.Children.OfType<Border>().Select(b => AutomationProperties.GetName(b) ?? "")];
            Assert.Equal(["Export", "View"], names);
        }

        [AvaloniaFact]
        public void FullWidth_EveryLabelVisible_SearchAtNaturalWidth() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(1600);
            Shown(row, 1700);

            Assert.All(AllItems(row), i => Assert.DoesNotContain("icon-only", i.Classes));
            Assert.Equal(220, row.SearchField.Width);
        }

        [AvaloniaFact]
        public void NarrowWidth_DropsLabelsAndShrinksSearchField() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(820);
            Shown(row, 920);

            Assert.All(AllItems(row), i => Assert.Contains("icon-only", i.Classes));
            Assert.All(AllItems(row), i => Assert.NotNull(ToolTip.GetTip(i)));
            Assert.Equal(120, row.SearchField.Width);
            Assert.False(row.OverflowButton.IsVisible);
        }

        [AvaloniaFact]
        public void NarrowestWidth_TrailingGroupMovesIntoOverflow() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(260);
            Shown(row, 320);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            var shareViewPanel = panel.Children.OfType<StackPanel>().Single(p => AutomationProperties.GetName(p) == "Share");
            Assert.False(shareViewPanel.IsVisible);
            Assert.True(row.OverflowButton.IsVisible);
            Assert.Equal(2, row.OverflowButton.ActionItems.Count);
            Assert.Equal(["Export", "View"], row.OverflowButton.ActionItems.Select(a => a.Text));
        }

        [AvaloniaFact]
        public void TitleAndZoomStepper_NeverCollapse_AtTheNarrowestWidth() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(260);
            Shown(row, 320);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            Control title = panel.Children.First(c => c.IsVisible);
            var titleText = Assert.IsType<TextBlock>(title);
            Assert.Equal("Untitled graph", titleText.Text);
            Assert.True(row.ZoomStepper.IsVisible);
            Assert.Equal(42, row.ZoomStepper.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Readout").Bounds.Width);
        }

        [AvaloniaFact]
        public void OverflowMenu_ClickingAnActionInvokesTheOriginalItemsCommand() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(260);
            string? invoked = null;
            row.Groups[^1].Items[^1].Command = new RelayCommand(_ => invoked = "View");
            Shown(row, 320);
            Dispatcher.UIThread.RunJobs();

            MenuButtonAction action = row.OverflowButton.ActionItems.Single(a => a.Text == "View");
            action.Activate?.Invoke();

            Assert.Equal("View", invoked);
        }

        [AvaloniaFact]
        public void CollapseStep_SettlesItemBoundsWithinTheStyleAllowance_ButKeepsHoverAndLabelTransitions() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(1600);
            Window window = Shown(row, 1700);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            StackPanel[] groupPanels = [.. panel.Children.OfType<StackPanel>()];
            ToolbarItem[] items = AllItems(row);
            ToolbarItem newItem = items.Single(i => (string)i.Tag! == "New");
            Assert.DoesNotContain("icon-only", newItem.Classes);
            double labeledWidth = newItem.Bounds.Width;
            double iconOnlyWidth = panel.IconOnlyWidthOf(newItem);

            row.Width = 820;
            window.Width = 920;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("icon-only", newItem.Classes);

            var sampledWidths = new List<double>();
            newItem.LayoutUpdated += (_, _) => sampledWidths.Add(newItem.Bounds.Width);

            for (int pump = 0; pump < 10 && newItem.Bounds.Width != iconOnlyWidth; pump++)
                AnimationClock.Settle(50);

            const int styleSettleAllowance = 2;
            Assert.True(sampledWidths.Count <= styleSettleAllowance,
                $"Expected at most {styleSettleAllowance} layout passes to settle after the collapse threshold, got [{string.Join(", ", sampledWidths)}].");
            Assert.Equal(33, newItem.Bounds.Width, 1);
            Assert.Equal(iconOnlyWidth, newItem.Bounds.Width, 1);
            Assert.All(sampledWidths, w => Assert.True(
                Math.Abs(w - labeledWidth) < 0.01 || Math.Abs(w - iconOnlyWidth) < 0.01,
                $"Sampled an in-between width {w} (labeled {labeledWidth}, icon-only {iconOnlyWidth}) - collapse must jump, never animate through it."));

            Assert.All(items, i => AssertNoSizeTransitions(i.Transitions));
            Assert.All(items, i => AssertNoSizeTransitions(LabelHostOf(i).Transitions));
            Assert.All(groupPanels, g => AssertNoSizeTransitions(g.Transitions));
            Assert.Null(panel.Transitions);

            Assert.All(items, i => Assert.True(
                i.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_HoverFill").Transitions is { } t &&
                t.OfType<DoubleTransition>().Any(dt => dt.Property == Visual.OpacityProperty)));
        }

        [AvaloniaFact]
        public void Items_KeepTheirFullLabelAsAutomationName_WhenIconOnly() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(430);
            Shown(row, 500);

            ToolbarItem newItem = AllItems(row).Single(i => (string)i.Tag! == "New");
            Assert.Equal("New", AutomationProperties.GetName(newItem));
        }

        [AvaloniaFact]
        public void Groups_AreNamedByPurpose() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            StackPanel[] groupPanels = [.. panel.Children.OfType<StackPanel>()];
            Assert.Equal(["Document", "Add", "Arrange", "Share"], groupPanels.Select(AutomationProperties.GetName));
        }

        [AvaloniaFact]
        public void AutomationPeer_IsNamedToolbar() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(row);
            Assert.Equal("Toolbar", peer.GetName());
        }

        [AvaloniaFact]
        public void AutomationPeer_IsAToolbar() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(row);
            Assert.Equal(AutomationControlType.ToolBar, peer.GetAutomationControlType());
        }

        [AvaloniaFact]
        public void ZoomStepperAutomationGroup_IsReachableFromTheRowsPeerTree() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            AutomationPeer rowPeer = ControlAutomationPeer.CreatePeerForElement(row);
            AutomationPeer zoomPeer = FindDescendantPeerNamed(rowPeer, "Zoom");

            Assert.NotNull(zoomPeer);
        }

        private static AutomationPeer FindDescendantPeerNamed(AutomationPeer root, string name) {
            foreach (AutomationPeer child in root.GetChildren()) {
                if (child.GetName() == name)
                    return child;
                if (FindDescendantPeerNamed(child, name) is { } found)
                    return found;
            }
            return null!;
        }

        [AvaloniaFact]
        public void Background_ResolvesTitlebarMaterial() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            Color expected = Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Brush("Foreman.Color.TitlebarMaterial")).Color;
            Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color);
        }

        [AvaloniaFact]
        public void DisabledItem_StaysVisible_NeverHidden() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            ToolbarItem save = AllItems(row).Single(i => (string)i.Tag! == "Save");
            save.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(save.IsVisible);
            Assert.False(save.IsEnabled);
        }

        [AvaloniaFact]
        public void RovingTabStop_OnlyOneItemIsATabStop_Initially() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            ToolbarItem[] items = AllItems(row);
            Assert.Single(items, i => i.IsTabStop);
            Assert.True(items[0].IsTabStop);
        }

        [AvaloniaFact]
        public void ArrowKeys_MoveTheRovingTabStopBetweenItems() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            ToolbarItem[] items = AllItems(row);
            fixture.StateOf(items[0]).Focus();
            fixture.StateOf(items[0]).KeyDown(Key.Right);

            Assert.False(items[0].IsTabStop);
            Assert.True(items[1].IsTabStop);

            fixture.StateOf(items[1]).KeyDown(Key.Left);

            Assert.True(items[0].IsTabStop);
            Assert.False(items[1].IsTabStop);
        }

        [AvaloniaFact]
        public void ArrowKeys_ReachIntoTheOverflowButton_AtTheNarrowestWidth() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(260);
            Shown(row, 320);

            ToolbarItem last = AllItems(row).Last(i => i.Parent is StackPanel group && group.IsVisible);

            fixture.StateOf(last).Focus();
            fixture.StateOf(last).KeyDown(Key.Right);

            Assert.True(row.OverflowButton.IsTabStop);
            Assert.False(last.IsTabStop);
        }

        [AvaloniaFact]
        public void Space_ActivatesTheFocusedItem() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            string? invoked = null;
            row.Groups[0].Items[0].Command = new RelayCommand(_ => invoked = "New");
            Shown(row, 1000);

            ToolbarItem newItem = AllItems(row).Single(i => (string)i.Tag! == "New");
            fixture.StateOf(newItem).Focus();
            fixture.StateOf(newItem).KeyDown(Key.Space);
            fixture.StateOf(newItem).KeyUp(Key.Space);

            Assert.Equal("New", invoked);
        }

        [AvaloniaFact]
        public void RovingTabStop_RehomesToTheOverflowButton_WhenItsGroupFoldsIntoOverflow() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow();
            Window window = Shown(row, 1000);

            ToolbarItem[] items = AllItems(row);
            ToolbarItem view = items.Single(i => (string)i.Tag! == "View");
            fixture.StateOf(items[0]).Focus();
            for (int i = 0; i < items.Length - 1; i++)
                fixture.StateOf(items[i]).KeyDown(Key.Right);
            Assert.True(view.IsTabStop);

            row.Width = 260;
            window.Width = 320;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.False(view.IsTabStop);
            Assert.True(row.OverflowButton.IsTabStop);
            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            Assert.Single(panel.FocusTargets(), c => c.IsTabStop);
        }

        [AvaloniaFact]
        public void RovingTabStop_RehomesToAVisibleItem_WhenWideningBackOutOfOverflow() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(260);
            Window window = Shown(row, 320);

            ToolbarItem align = AllItems(row).Single(i => (string)i.Tag! == "Align");
            fixture.StateOf(align).Focus();
            fixture.StateOf(align).KeyDown(Key.Right);
            Assert.True(row.OverflowButton.IsTabStop);

            row.Width = 900;
            window.Width = 1000;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.False(row.OverflowButton.IsTabStop);
            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            Assert.Single(panel.FocusTargets(), c => c.IsTabStop);
        }

        [AvaloniaFact]
        public void RovingTabStop_SurvivesDetachAndReattach_WithoutDoubleRehoming() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Window window = Shown(row, 1000);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            ToolbarItem[] items = AllItems(row);
            ToolbarItem view = items.Single(i => (string)i.Tag! == "View");
            fixture.StateOf(items[0]).Focus();
            for (int i = 0; i < items.Length - 1; i++)
                fixture.StateOf(items[i]).KeyDown(Key.Right);
            Assert.True(view.IsTabStop);

            window.Content = null;
            Dispatcher.UIThread.RunJobs();

            panel.Measure(new Size(260, double.PositiveInfinity));
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.IsTabStop);

            window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            row.Width = 260;
            window.Width = 320;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            Assert.True(row.OverflowButton.IsTabStop);
            Assert.Single(panel.FocusTargets(), c => c.IsTabStop);
        }

        [AvaloniaFact]
        public void Tab_FromBeforeTheRowToAfterIt_PassesThroughExactlyTwoStops_TheRovingItemThenSearch() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            var before = new Button { Content = "Before" };
            var after = new Button { Content = "After" };
            var stack = new StackPanel { Children = { before, row, after } };
            var window = new Window { Width = 1000, Height = 200, Content = stack };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle(50);

            before.Focus(NavigationMethod.Tab);
            Dispatcher.UIThread.RunJobs();

            List<Control?> stops = [];
            for (int i = 0; i < 3; i++) {
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab });
                Dispatcher.UIThread.RunJobs();
                stops.Add(window.FocusManager?.GetFocusedElement() as Control);
            }

            Assert.Same(AllItems(row)[0], stops[0]);
            Assert.Same(row.SearchField, stops[1]);
            Assert.Same(after, stops[2]);
        }

        [AvaloniaFact]
        public void ArrowKeys_TraverseTheFullRovingOrder_ItemsOverflowThenZoomButtons() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(260);
            Shown(row, 320);

            List<Control> expected = [.. row.Groups.Take(row.Groups.Count - 1).SelectMany(g => g.Items), row.OverflowButton];
            Button[] stepperButtons = [.. row.ZoomStepper.GetVisualDescendants().OfType<Button>()];
            foreach (string name in new[] { "PART_Minus", "PART_Readout", "PART_Plus" })
                expected.Add(stepperButtons.Single(b => b.Name == name));

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            Assert.Equal(expected, panel.FocusTargets());
            Assert.Equal(10, expected.Count);
            Assert.Same(row.OverflowButton, expected[6]);

            fixture.StateOf(expected[0]).Focus();
            for (int i = 0; i < expected.Count - 1; i++) {
                fixture.StateOf(expected[i]).KeyDown(Key.Right);
                Assert.True(expected[i + 1].IsTabStop, $"index {i + 1} should carry the roving tab stop.");
            }
        }

        [AvaloniaFact]
        public void Title_ResolvesEmphasizedBodyTypeTokens() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            var title = Assert.IsType<TextBlock>(panel.Children.First(c => c.IsVisible));

            Application.Current!.TryGetResource("Foreman.Type.EmphasizedBody.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Type.EmphasizedBody.FontWeight", Application.Current.ActualThemeVariant, out object? weight);

            Assert.Equal(Assert.IsType<double>(size), title.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), title.FontWeight);
        }

        [AvaloniaFact]
        public void Title_StaysVerbatim_WhenTheHostSetsADirtyDocumentsTitle() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow(900);
            Shown(row, 1000);

            row.Title = "Untitled graph";
            Dispatcher.UIThread.RunJobs();

            var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
            var title = Assert.IsType<TextBlock>(panel.Children.First(c => c.IsVisible));
            Assert.Equal("Untitled graph", title.Text);
        }

        [AvaloniaFact]
        public void Pumping_ResizeThroughAllCollapseSteps_SettlesToABoundedLayoutCount() {
            using var fixture = new KitFixture();
            ToolbarRow row = NewRow();
            Window window = Shown(row, 900);

            int layouts = 0;
            row.LayoutUpdated += (_, _) => layouts++;

            foreach (double width in new[] { 900d, 500, 320, 900 }) {
                window.Width = width;
                AnimationClock.Settle();
            }

            Assert.True(layouts <= 60, $"ToolbarRow laid out {layouts} times settling four resizes.");
        }

        [Collection(KitScaleGroup.Name)]
        public sealed class ScaleTests {
            [AvaloniaFact]
            public void RowHeight_TracksScaleContract_ButGroupSpacingStaysFixed() {
                using var fixture = new KitFixture();
                ToolbarRow row = NewRow(900);
                Shown(row, 1000);

                Assert.Equal(52, row.Bounds.Height);

                ScaleContract.Apply(1.5);
                Dispatcher.UIThread.RunJobs();
                row.InvalidateMeasure();
                row.Measure(Size.Infinity);
                row.Arrange(new Rect(row.Bounds.Position, row.DesiredSize));

                Assert.Equal(78, row.Bounds.Height);

                ToolbarItem[] items = AllItems(row);
                ToolbarItem newItem = items.Single(i => (string)i.Tag! == "New");
                ToolbarItem openItem = items.Single(i => (string)i.Tag! == "Open");
                Assert.Equal(2, openItem.Bounds.Left - newItem.Bounds.Right, 3);
            }

            [AvaloniaFact]
            public void CachedItemWidths_MatchARealToolbarItemsDesiredSize_LabeledAndIconOnly() {
                using var fixture = new KitFixture();
                ToolbarRow row = NewRow(900);
                Shown(row, 1000);
                var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
                ToolbarItem recipe = AllItems(row).Single(i => (string)i.Tag! == "Recipe");

                AssertCachedWidthsMatchARealItem(panel, recipe, "Recipe");

                ScaleContract.Apply(1.5);
                Dispatcher.UIThread.RunJobs();
                row.InvalidateMeasure();
                row.Measure(Size.Infinity);
                row.Arrange(new Rect(row.Bounds.Position, row.DesiredSize));

                AssertCachedWidthsMatchARealItem(panel, recipe, "Recipe");
            }

            private static void AssertCachedWidthsMatchARealItem(ToolbarRowPanel panel, ToolbarItem rowItem, string label) {
                var probeWindow = new Window { Width = 400, Height = 200 };

                ToolbarItem labeled = Item(label);
                probeWindow.Content = labeled;
                probeWindow.Show();
                Dispatcher.UIThread.RunJobs();
                AssertWithinOnePoint(panel.LabeledWidthOf(rowItem), labeled.DesiredSize.Width, "labeled");

                ToolbarItem iconOnly = Item(label);
                iconOnly.Classes.Add("icon-only");
                probeWindow.Content = iconOnly;
                Dispatcher.UIThread.RunJobs();
                AssertWithinOnePoint(panel.IconOnlyWidthOf(rowItem), iconOnly.DesiredSize.Width, "icon-only");

                probeWindow.Close();
            }

            private static void AssertWithinOnePoint(double cached, double real, string state) =>
                Assert.True(Math.Abs(cached - real) <= 1, $"{state}: cached width {cached} vs real DesiredSize.Width {real}.");

            [AvaloniaFact]
            public void CollapseStep_RespondsToScaledItemWidths_AfterScaleContractApply() {
                using var fixture = new KitFixture();
                ToolbarRow row = NewRow(1200);
                Window window = Shown(row, 1300);

                Assert.All(AllItems(row), i => Assert.DoesNotContain("icon-only", i.Classes));

                ScaleContract.Apply(1.5);
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle(300);

                Assert.All(AllItems(row), i => Assert.Contains("icon-only", i.Classes));
                Assert.All(AllItems(row), i => Assert.Equal((string)i.Tag!, i.Label));

                window.Resources["Foreman.Size.ToolbarItemWidth"] = 100.0;
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle(50);

                Assert.All(AllItems(row), i => Assert.Contains("icon-only", i.Classes));
                Assert.All(AllItems(row), i => Assert.Equal((string)i.Tag!, i.Label));
            }

            [AvaloniaFact]
            public void ItemWidthAndFontSizeChange_AtTheirScaledValues_ExpandsOnlyAtTheScaledLabeledSum() {
                using var fixture = new KitFixture();
                ToolbarRow row = NewRow(1200);
                Window window = Shown(row, 1300);

                Assert.All(AllItems(row), i => Assert.DoesNotContain("icon-only", i.Classes));

                Application.Current!.TryGetResource("Foreman.Size.ToolbarItemWidth", Application.Current.ActualThemeVariant, out object? widthBase);
                Application.Current!.TryGetResource("Foreman.Type.Legacy.ToolbarItem.Label.FontSize", Application.Current.ActualThemeVariant, out object? fontBase);
                window.Resources["Foreman.Size.ToolbarItemWidth"] = (double)widthBase! * 1.5;
                window.Resources["Foreman.Type.Legacy.ToolbarItem.Label.FontSize"] = (double)fontBase! * 1.5;
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle(300);

                Assert.All(AllItems(row), i => Assert.Contains("icon-only", i.Classes));

                var panel = row.GetVisualDescendants().OfType<ToolbarRowPanel>().Single();
                double scaledLabeledSum = panel.RequiredWidthAtStep(0);

                row.Width = scaledLabeledSum - 20;
                window.Width = row.Width + 100;
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle(300);

                Assert.All(AllItems(row), i => Assert.Contains("icon-only", i.Classes));

                row.Width = scaledLabeledSum + 40;
                window.Width = row.Width + 100;
                Dispatcher.UIThread.RunJobs();
                AnimationClock.Settle(300);

                Assert.All(AllItems(row), i => Assert.DoesNotContain("icon-only", i.Classes));
            }
        }

        private sealed class RelayCommand(Action<object?> execute) : System.Windows.Input.ICommand {
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => execute(parameter);
            public event EventHandler? CanExecuteChanged { add { } remove { } }
        }
    }
}
