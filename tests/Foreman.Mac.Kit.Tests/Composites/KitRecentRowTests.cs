using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Composites {
    public sealed class KitRecentRowTests {
        private static ControlTheme KitRecentRowTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.RecentRow", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static RecentRow NewRow() => new() {
            Theme = KitRecentRowTheme(),
            Label = "Blue science 90/min",
            PresetLabel = "Space Age",
            NodeCount = 214,
            RelativeTime = "2 hours ago",
            AbsoluteTime = "Tuesday, September 15, 2026 at 3:04 PM",
            Icon = Brushes.SteelBlue,
            MenuItems = { Item("Open"), Item("Delete Graph…") },
        };

        private static Window Shown(Control content, double width = 340, double height = 80) {
            var window = new Window { Width = width, Height = height, Content = content };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            return window;
        }

        [AvaloniaFact]
        public void KitRecentRow_IsAControlThemeTargetingRecentRow() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(RecentRow), KitRecentRowTheme().TargetType);
        }

        [AvaloniaFact]
        public void RootBackground_FollowsTheControlsOwnBackgroundProperty() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.Background = Brushes.SteelBlue;
            Shown(row);

            Border frame = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            Assert.Equal(Brushes.SteelBlue.ToImmutable(), frame.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void Height_ResolvesRecentRowHeight() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            Assert.Equal(46, row.Bounds.Height);
        }

        [AvaloniaFact]
        public void Frame_ResolvesControlRadius_AndTheContentGridUsesTheSpecPaddingAndGap() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            Border frame = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(radius, frame.CornerRadius);

            Grid content = row.GetVisualDescendants().OfType<Grid>().Single();
            Assert.Equal(new Thickness(16, 0), content.Margin);

            Border icon = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Icon");
            Assert.Equal(new Thickness(0, 0, 12, 0), icon.Margin);
        }

        [AvaloniaFact]
        public void Icon_ToName_RenderedGapIsTwelvePoints() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            Border icon = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Icon");
            TextBlock name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Name");
            Point iconRight = icon.TranslatePoint(new Point(icon.Bounds.Width, 0), row) ?? default;
            Point nameLeft = name.TranslatePoint(new Point(0, 0), row) ?? default;

            Assert.Equal(12, nameLeft.X - iconRight.X, 3);
        }

        [AvaloniaFact]
        public void Icon_ResolvesRecentRowIconSize_AtRadius7() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            Border icon = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Icon");
            Assert.Equal(30, icon.Bounds.Width);
            Assert.Equal(30, icon.Bounds.Height);
            Assert.Equal(new CornerRadius(7), icon.CornerRadius);
        }

        [AvaloniaFact]
        public void Name_UsesEmphasizedBodyTypography() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            TextBlock name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Name");
            Application.Current!.TryGetResource("Foreman.Type.EmphasizedBody.FontSize", Application.Current.ActualThemeVariant, out object? size);
            Application.Current!.TryGetResource("Foreman.Type.EmphasizedBody.FontWeight", Application.Current.ActualThemeVariant, out object? weight);
            Assert.Equal(Assert.IsType<double>(size), name.FontSize);
            Assert.Equal(Assert.IsType<FontWeight>(weight), name.FontWeight);
            Assert.Equal("Blue science 90/min", name.Text);
        }

        [AvaloniaFact]
        public void Meta_ReadsPresetDotNodeCount_AsThreeCaptionTertiaryRuns() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            TextBlock preset = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Preset");
            TextBlock nodeCount = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_NodeCount");
            Application.Current!.TryGetResource("Foreman.Type.Caption.FontSize", Application.Current.ActualThemeVariant, out object? captionSize);
            IBrush tertiary = fixture.Brush("Foreman.Color.TextTertiary");

            Assert.Equal("Space Age", preset.Text);
            Assert.Equal("214 nodes", nodeCount.Text);
            Assert.Equal(Assert.IsType<double>(captionSize), preset.FontSize);
            Assert.Equal(Assert.IsType<double>(captionSize), nodeCount.FontSize);
            Assert.Equal(tertiary.ToImmutable(), preset.Foreground?.ToImmutable());
            Assert.Equal(tertiary.ToImmutable(), nodeCount.Foreground?.ToImmutable());

            StackPanel meta = row.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_Meta");
            Application.Current!.TryGetResource("Foreman.Space.Space15", Application.Current.ActualThemeVariant, out object? space15);
            Assert.Equal(Assert.IsType<double>(space15), meta.Spacing);
            TextBlock[] runs = [.. meta.Children.OfType<TextBlock>().Where(r => r.IsVisible)];
            Assert.Equal(3, runs.Length);
            Assert.Same(preset, runs[0]);
            Assert.Equal("·", runs[1].Text);
            Assert.Equal(Assert.IsType<double>(captionSize), runs[1].FontSize);
            Assert.Equal(tertiary.ToImmutable(), runs[1].Foreground?.ToImmutable());
            Assert.Same(nodeCount, runs[2]);
        }

        [AvaloniaFact]
        public void Name_ToMeta_RenderedGapIsOnePoint() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            TextBlock name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Name");
            StackPanel meta = row.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "PART_Meta");
            Point nameBottom = name.TranslatePoint(new Point(0, name.Bounds.Height), row) ?? default;
            Point metaTop = meta.TranslatePoint(new Point(0, 0), row) ?? default;

            Assert.Equal(1, metaTop.Y - nameBottom.Y, 3);
        }

        [AvaloniaFact]
        public void NodeCount_SingularIsUnpluralized() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.NodeCount = 1;
            Shown(row);

            TextBlock nodeCount = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_NodeCount");
            Assert.Equal("1 node", nodeCount.Text);
        }

        [AvaloniaFact]
        public void Time_IsPinnedToTheTrailingEdge_AndNeverTruncates() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            TextBlock time = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Time");
            Assert.Equal("2 hours ago", time.Text);
            Assert.Equal(TextTrimming.None, time.TextTrimming);
            Assert.Equal(2, Grid.GetColumn(time));
        }

        [AvaloniaFact]
        public void Time_TooltipCarriesTheAbsoluteDate() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            TextBlock time = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Time");
            Assert.Equal("Tuesday, September 15, 2026 at 3:04 PM", ToolTip.GetTip(time));
        }

        [AvaloniaFact]
        public void SelectedAndKey_ShowsAccentFill_AndAccentText() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.IsSelected = true;
            Shown(row);
            AnimationClock.Settle();

            Border selectionFill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionFill");
            Border fill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
            TextBlock name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Name");

            Assert.Equal(1, selectionFill.Opacity);
            Assert.Equal(0, fill.Opacity);
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), name.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void SelectedButNotKey_ShowsSelectionInactiveFill_WithPlainText() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.IsSelected = true;
            row.IsKey = false;
            Shown(row);
            AnimationClock.Settle();

            Border selectionFill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionFill");
            Border fill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
            TextBlock name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Name");

            Assert.Equal(0, selectionFill.Opacity);
            Assert.Equal(1, fill.Opacity);
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), name.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Hover_IsSuppressedOnASelectedRow() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.IsSelected = true;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            fixture.StateOf(row).Hover();
            AnimationClock.Settle();

            Border selectionFill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_SelectionFill");
            Border fill = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Fill");
            Assert.Equal(1, selectionFill.Opacity);
            Assert.Equal(0, fill.Opacity);
        }

        [AvaloniaFact]
        public void Unavailable_NameTertiaryItalic_ThumbnailAtOpacityUnavailable_MetaIsOnlyTheWarningSentence() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.NodeCount = 1204;
            row.UnavailableMeta = "Preset “Space Age” isn't installed";
            row.IsUnavailable = true;
            Shown(row);

            Assert.Equal(1, row.Opacity);
            TextBlock name = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Name");
            Assert.Equal(FontStyle.Italic, name.FontStyle);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), name.Foreground?.ToImmutable());
            Border icon = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Icon");
            Application.Current!.TryGetResource("Foreman.Opacity.Unavailable", Application.Current.ActualThemeVariant, out object? unavailableOpacity);
            Assert.Equal(Assert.IsType<double>(unavailableOpacity), icon.Opacity);
            Assert.Equal(0.55, icon.Opacity);

            TextBlock meta = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_UnavailableMeta");
            Assert.True(meta.IsVisible);
            Assert.Equal("Preset “Space Age” isn't installed", meta.Text);
            Assert.Equal(fixture.Brush("Foreman.Color.Warning").ToImmutable(), meta.Foreground?.ToImmutable());
            Assert.False(row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_NodeCount").IsVisible);
            Assert.False(row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Preset").IsVisible);
            Assert.False(row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_MetaDot").IsVisible);
        }

        [AvaloniaFact]
        public void Available_MetaHidesTheUnavailableSentence_AndNameKeepsRegularStyle() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.UnavailableMeta = "Preset “Space Age” isn't installed";
            Shown(row);

            Assert.False(row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_UnavailableMeta").IsVisible);
            Assert.True(row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_NodeCount").IsVisible);
            Assert.Equal(FontStyle.Normal, row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "PART_Name").FontStyle);
            Assert.Equal(1, row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Icon").Opacity);
        }

        [AvaloniaFact]
        public void Unavailable_RowStaysSelectableAndFocusable() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.IsUnavailable = true;
            Shown(row);

            Assert.True(row.Focusable);
            Assert.True(row.IsEnabled);
            row.IsSelected = true;
            Assert.True(row.IsSelected);
        }

        private static MenuRow Item(string text, System.Action? activate = null) => new() { Text = text, HasCheckSlot = false, Activate = activate ?? (() => { }) };

        [AvaloniaFact]
        public void Menu_IsHostSupplied_NoFixedItems() {
            using var fixture = new KitFixture();
            var row = new RecentRow { Theme = KitRecentRowTheme() };
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(row.Menu.Items);
            Assert.Empty(row.MenuItems);

            row.IsUnavailable = true;
            Assert.Empty(row.Menu.Items);

            int activated = 0;
            row.MenuItems.Add(Item("Open"));
            row.MenuItems.Add(MenuRow.Separator());
            row.MenuItems.Add(Item("Delete Graph…", () => activated++));
            Assert.Empty(row.Menu.Items);

            row.Open();
            Dispatcher.UIThread.RunJobs();
            string[] texts = [.. row.Menu.Items.Select(i => i.IsSeparator ? "—" : i.Text)];
            Assert.Equal(["Open", "—", "Delete Graph…"], texts);
            row.Menu.Items[2].Activate!();
            Assert.Equal(1, activated);
        }

        [AvaloniaFact]
        public void Menu_OpensWithTheHostItemsCopiedInFresh_EachTime() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            row.Open();
            row.IsMenuOpen = false;
            row.MenuItems.Add(Item("Rename…"));
            row.Open();

            Assert.Equal(["Open", "Delete Graph…", "Rename…"], row.Menu.Items.Select(i => i.Text));
        }

        [AvaloniaFact]
        public void Return_RaisesOpenRequested_EvenWhenUnavailable() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.IsUnavailable = true;
            int raised = 0;
            row.OpenRequested += (_, _) => raised++;
            Shown(row);

            fixture.StateOf(row).KeyDown(Key.Return);

            Assert.Equal(1, raised);
        }

        [AvaloniaFact]
        public void CommandDownArrow_AlsoRaisesOpenRequested() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            int raised = 0;
            row.OpenRequested += (_, _) => raised++;
            Shown(row);

            fixture.StateOf(row).KeyDown(Key.Down, KeyModifiers.Meta);

            Assert.Equal(1, raised);
        }

        [AvaloniaFact]
        public void HelpText_MirrorsTheAbsoluteDate_AndEveryHostMenuItem() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.MenuItems.Add(Item("Rename…"));
            row.MenuItems.Add(MenuRow.Separator());
            Shown(row);

            string? help = AutomationProperties.GetHelpText(row);
            Assert.Contains("Tuesday, September 15, 2026 at 3:04 PM", help);
            foreach (string text in new[] { "Open", "Rename…", "Delete Graph…" })
                Assert.Contains(text, help);
            Assert.Contains("Shift F10", help);
        }

        [AvaloniaFact]
        public void HelpText_FollowsTheHostItems_AsTheyChange() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);
            Assert.DoesNotContain("Choose Preset…", AutomationProperties.GetHelpText(row));

            row.MenuItems.Add(Item("Choose Preset…"));

            Assert.Contains("Choose Preset…", AutomationProperties.GetHelpText(row));
        }

        [AvaloniaFact]
        public void RightClick_OpensTheContextMenu() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Point center = Avalonia.VisualExtensions.TranslatePoint(row, new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), fixture.Window) ?? default;
            fixture.Window.MouseDown(center, MouseButton.Right, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();

            Assert.True(row.IsMenuOpen);
            Popup popup = row.GetVisualDescendants().OfType<Popup>().Single();
            Assert.Equal(PlacementMode.Pointer, popup.Placement);
        }

        [AvaloniaFact]
        public void ShiftF10_OpensTheContextMenu() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(row).KeyDown(Key.F10, KeyModifiers.Shift);

            Assert.True(row.IsMenuOpen);
        }

        [AvaloniaFact]
        public void ShiftF10_AnchorsTheMenuToTheRow_NotTheStalePointer() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(row).KeyDown(Key.F10, KeyModifiers.Shift);

            Popup popup = row.GetVisualDescendants().OfType<Popup>().Single();
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
        }

        [AvaloniaFact]
        public void MenuKey_OpensTheContextMenu_AnchoredToTheRow() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(row).KeyDown(Key.Apps);

            Assert.True(row.IsMenuOpen);
            Assert.Equal(0, row.Menu.HighlightedIndex);
            Popup popup = row.GetVisualDescendants().OfType<Popup>().Single();
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
        }

        [AvaloniaFact]
        public void MenuPlacement_IsResolvedPerInvocation_KeyboardThenPointerThenProgrammatic() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            Popup popup = row.GetVisualDescendants().OfType<Popup>().Single();

            fixture.StateOf(row).KeyDown(Key.F10, KeyModifiers.Shift);
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
            fixture.StateOf(row).KeyDown(Key.Escape);
            Assert.False(row.IsMenuOpen);

            Point center = Avalonia.VisualExtensions.TranslatePoint(row, new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), fixture.Window) ?? default;
            fixture.Window.MouseDown(center, MouseButton.Right, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PlacementMode.Pointer, popup.Placement);
            row.IsMenuOpen = false;
            Dispatcher.UIThread.RunJobs();

            row.Open();
            Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsMenuOpen);
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
            Assert.Equal(0, row.Menu.HighlightedIndex);
        }

        [AvaloniaFact]
        public void SettingIsMenuOpenDirectly_AfterAPointerOpen_DoesNotInheritTheStalePointerPlacement() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Point center = Avalonia.VisualExtensions.TranslatePoint(row, new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), fixture.Window) ?? default;
            fixture.Window.MouseDown(center, MouseButton.Right, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(row.IsMenuOpen);
            row.IsMenuOpen = false;
            Dispatcher.UIThread.RunJobs();

            row.IsMenuOpen = true;
            Dispatcher.UIThread.RunJobs();

            Popup popup = row.GetVisualDescendants().OfType<Popup>().Single();
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
            Assert.Equal(0, row.Menu.HighlightedIndex);
        }

        [AvaloniaFact]
        public void IsMenuOpen_SetBeforeTheTemplateApplies_OpensAtThePendingPlacement() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.IsMenuOpen = true;
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            Popup popup = row.GetVisualDescendants().OfType<Popup>().Single();
            Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, popup.Placement);
            Assert.Equal(0, row.Menu.HighlightedIndex);
        }

        [AvaloniaFact]
        public void Escape_ClosesAnOpenContextMenu() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(row).KeyDown(Key.F10, KeyModifiers.Shift);
            Assert.True(row.IsMenuOpen);

            fixture.StateOf(row).KeyDown(Key.Escape);

            Assert.False(row.IsMenuOpen);
        }

        [AvaloniaFact]
        public void Backspace_RaisesRemoveRequested_WithTheRowsItem_ExactlyOnce() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            var item = new object();
            row.DataContext = item;
            int raised = 0;
            object? carried = null;
            row.RemoveRequested += (_, e) => { raised++; carried = e.Item; };
            Shown(row);

            fixture.StateOf(row).KeyDown(Key.Back);

            Assert.Equal(1, raised);
            Assert.Same(item, carried);
        }

        [AvaloniaFact]
        public void Backspace_RaisesRemoveRequested_WithoutAnyMenuItems() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            int raised = 0;
            row.RemoveRequested += (_, _) => raised++;
            Shown(row);

            fixture.StateOf(row).KeyDown(Key.Back);

            Assert.Equal(1, raised);
        }

        [AvaloniaFact]
        public void Space_DoesNothing_AndLeavesTheEventUnhandled() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            int openRequested = 0;
            row.OpenRequested += (_, _) => openRequested++;
            int removeRequested = 0;
            row.RemoveRequested += (_, _) => removeRequested++;
            Shown(row);

            var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space };
            row.RaiseEvent(e);

            Assert.Equal(0, openRequested);
            Assert.False(row.IsMenuOpen);
            Assert.Equal(0, removeRequested);
            Assert.False(e.Handled);
        }

        [AvaloniaFact]
        public void AutomationName_IsOnePhrase_WithNamePresetNodeCountAndDate() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);

            string? name = AutomationProperties.GetName(row);
            Assert.Contains("Blue science 90/min", name);
            Assert.Contains("Space Age", name);
            Assert.Contains("214 nodes", name);
            Assert.Contains("Tuesday, September 15, 2026 at 3:04 PM", name);
        }

        [AvaloniaFact]
        public void AutomationName_Unavailable_ReadsNormallyThenPresetNotInstalled() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            row.UnavailableMeta = "Preset “Space Age” isn't installed";
            row.IsUnavailable = true;
            Shown(row);

            Assert.Equal("Blue science 90/min, Space Age, 214 nodes, Tuesday, September 15, 2026 at 3:04 PM, preset not installed", AutomationProperties.GetName(row));
        }

        [AvaloniaFact]
        public void Delete_DoesNotRaiseRemoveRequested() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            int raised = 0;
            row.RemoveRequested += (_, _) => raised++;
            Shown(row);

            fixture.StateOf(row).KeyDown(Key.Delete);

            Assert.Equal(0, raised);
        }

        [AvaloniaFact]
        public void HelpText_NamesBackspaceOnlyWhenTheHostListensForRemoval_AndAppendsTheHostHints() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            Shown(row);
            Assert.DoesNotContain("Backspace", AutomationProperties.GetHelpText(row));

            row.RemoveRequested += (_, _) => { };
            row.HelpHints = "Delete Graph: Command Backspace.";
            row.MenuItems.Add(Item("Rename…"));

            string? help = AutomationProperties.GetHelpText(row);
            Assert.Contains("Remove: Backspace.", help);
            Assert.Contains("Delete Graph: Command Backspace.", help);
        }

        [AvaloniaFact]
        public void Pumping_ContextMenu_SteadyStateAfterOpen_RaisesNoFurtherLayout() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();

            fixture.StateOf(row).KeyDown(Key.F10, KeyModifiers.Shift);
            AnimationClock.Settle();
            Assert.True(row.Menu.Bounds.Width > 0 && row.Menu.Bounds.Height > 0);

            int layouts = 0;
            row.LayoutUpdated += (_, _) => layouts++;
            row.Menu.LayoutUpdated += (_, _) => layouts++;
            AnimationClock.Settle(300);

            Assert.Equal(0, layouts);
        }

        [AvaloniaFact]
        public void Pumping_ContextMenu_RepeatedOpenCloseCycles_StaysBounded() {
            using var fixture = new KitFixture();
            RecentRow row = NewRow();
            fixture.Window.Content = row;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            int layouts = 0;
            row.LayoutUpdated += (_, _) => layouts++;
            row.Menu.LayoutUpdated += (_, _) => layouts++;

            for (int cycle = 0; cycle < 3; cycle++) {
                fixture.StateOf(row).KeyDown(Key.F10, KeyModifiers.Shift);
                AnimationClock.Settle();
                Assert.True(row.IsMenuOpen);
                fixture.StateOf(row).KeyDown(Key.Escape);
                AnimationClock.Settle();
                Assert.False(row.IsMenuOpen);
            }

            Assert.True(layouts <= 120, $"RecentRow context menu laid out {layouts} times across three open/close cycles.");

            layouts = 0;
            AnimationClock.Settle(300);
            Assert.Equal(0, layouts);
        }

        [Collection(KitScaleGroup.Name)]
        public sealed class ScaleTests {
            [AvaloniaFact]
            public void RowHeightAndIcon_TrackScaleContract() {
                using var fixture = new KitFixture();
                RecentRow row = NewRow();
                Shown(row);
                ScaleContract.Apply(1.5);
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(69, row.Bounds.Height);
                Border icon = row.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Icon");
                Assert.Equal(45, icon.Bounds.Width);
                Assert.Equal(45, icon.Bounds.Height);
            }
        }
    }
}
