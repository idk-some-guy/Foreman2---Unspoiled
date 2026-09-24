using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class ListTests {
        private static ControlTheme KitListTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.List", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ControlTheme KitListBoxItemTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.ListBoxItem", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static ListBox Themed(params string[] items) =>
            new() { Theme = KitListTheme(), ItemsSource = items };

        private static ListBoxItem[] ItemContainers(ListBox listBox) =>
            [.. listBox.GetVisualDescendants().OfType<ListBoxItem>()];

        [AvaloniaFact]
        public void KitList_IsAControlThemeTargetingListBoxWithTheRowItemTheme() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(ListBox), KitListTheme().TargetType);
            Assert.Equal(typeof(ListBoxItem), KitListBoxItemTheme().TargetType);
        }

        [AvaloniaFact]
        public void Frame_ComesFromTheListboxSpec() {
            using var fixture = new KitFixture();
            ListBox listBox = Themed("Stone furnace", "Steel furnace");
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.WellBackground").ToImmutable(), listBox.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Border").ToImmutable(), listBox.BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(1), listBox.BorderThickness);

            Application.Current!.TryGetResource("Foreman.Radius.Card", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), listBox.CornerRadius);
        }

        [AvaloniaFact]
        public void ItemContainer_UsesRowMetrics() {
            using var fixture = new KitFixture();
            ListBox listBox = Themed("Stone furnace", "Steel furnace");
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.ListRowHeight", Application.Current.ActualThemeVariant, out object? height);
            ListBoxItem item = ItemContainers(listBox)[0];
            Assert.Equal(Assert.IsType<double>(height), item.Height);
        }

        [AvaloniaFact]
        public void ItemTemplate_IsRealisedInsideEveryItemsPresenter() {
            using var fixture = new KitFixture();
            ListBox listBox = Themed("Stone furnace", "Steel furnace");
            listBox.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<string>(
                (item, _) => new Border { Name = "RowProbe", Child = new TextBlock { Text = item } });
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();

            ListBoxItem[] items = ItemContainers(listBox);
            Assert.Equal(2, items.Length);
            foreach (ListBoxItem item in items) {
                Border probe = item.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RowProbe");
                Assert.Equal(item.Content, ((TextBlock)probe.Child!).Text);
            }
        }

        [AvaloniaFact]
        public void ItemsPanel_IsTheOneTheConsumerSets_SoALongListVirtualises() {
            using var fixture = new KitFixture();
            ListBox listBox = Themed([.. Enumerable.Range(0, 200).Select(i => $"Recipe {i}")]);
            listBox.ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel());
            listBox.Height = 120;
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();

            Assert.IsType<VirtualizingStackPanel>(listBox.ItemsPanelRoot);
            int realized = ItemContainers(listBox).Length;
            Assert.InRange(realized, 1, 20);
        }

        [AvaloniaFact]
        public void Selection_ArrowKeysMoveTheCurrentItem() {
            using var fixture = new KitFixture();
            ListBox listBox = Themed("Stone furnace", "Steel furnace", "Electric furnace");
            listBox.SelectedIndex = 0;
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();
            fixture.StateOf(listBox).Focus();

            fixture.StateOf(listBox).KeyDown(Key.Down);
            Assert.Equal(1, listBox.SelectedIndex);
        }

        [AvaloniaFact]
        public void ItemContainer_HasNoMarginTheFramePaddingProvidesTheInset() {
            using var fixture = new KitFixture();
            ListBox listBox = Themed("Stone furnace", "Steel furnace");
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(default, ItemContainers(listBox)[0].Margin);

            Application.Current!.TryGetResource("Foreman.Thickness.ListFramePadding", Application.Current.ActualThemeVariant, out object? padding);
            Assert.Equal(Assert.IsType<Thickness>(padding), listBox.Padding);
        }

        [AvaloniaFact]
        public void Selected_KeyHost_UsesAccentSoftAndAccentText() {
            using var fixture = new KitFixture();
            ListBox listBox = Themed("Stone furnace", "Steel furnace");
            listBox.SelectedIndex = 0;
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();

            ListBoxItem item = ItemContainers(listBox)[0];
            Assert.Equal(fixture.Brush("Foreman.Color.AccentSoft").ToImmutable(), item.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.AccentText").ToImmutable(), item.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Selected_NotKeyHost_UsesSelectionInactiveAndText() {
            using var fixture = new KitFixture();
            ListBox listBox = Themed("Stone furnace", "Steel furnace");
            listBox.SelectedIndex = 0;
            ListHost.SetIsKey(listBox, false);
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();

            ListBoxItem item = ItemContainers(listBox)[0];
            Assert.Equal(fixture.Brush("Foreman.Color.SelectionInactive").ToImmutable(), item.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.Text").ToImmutable(), item.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Disabled_OpacityIsTheDisabledToken() {
            using var fixture = new KitFixture();
            ListBox listBox = Themed("Stone furnace");
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();

            ListBoxItem item = ItemContainers(listBox)[0];
            item.IsEnabled = false;

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), item.Opacity);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class ListScaleTests {
        [AvaloniaFact]
        public void ItemHeight_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.List", null, out object? theme));
            var listBox = new ListBox { Theme = (ControlTheme)theme!, ItemsSource = new[] { "Stone furnace" } };
            fixture.Window.Content = listBox;
            Dispatcher.UIThread.RunJobs();

            ListBoxItem item = listBox.GetVisualDescendants().OfType<ListBoxItem>().Single();
            Assert.Equal(26, item.Height);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(39, item.Height);
        }
    }
}
