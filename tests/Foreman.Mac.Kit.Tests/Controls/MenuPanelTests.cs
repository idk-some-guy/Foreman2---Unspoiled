using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.Kit.Tests.support;
using System.Linq;
using Xunit;

namespace Foreman.Mac.Kit.Tests.Controls {
    public sealed class MenuPanelTests {
        private static ControlTheme KitMenuPanelTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.MenuPanel", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static MenuPanel Themed() {
            var panel = new MenuPanel { Theme = KitMenuPanelTheme() };
            panel.Items.Add(new MenuRow { Text = "Show unavailable objects", IsChecked = false });
            panel.Items.Add(new MenuRow { Text = "Show hidden objects", IsChecked = true });
            panel.Items.Add(MenuRow.Separator());
            panel.Items.Add(new MenuRow { Text = "Reset to defaults", HasCheckSlot = false, IsEnabled = false });
            return panel;
        }

        private static StackPanel ItemsPanel(MenuPanel panel) =>
            panel.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Items");

        private static Border Frame(MenuPanel panel) => (Border)ItemsPanel(panel).GetVisualParent()!;

        private static Border[] Rows(MenuPanel panel) => [.. ItemsPanel(panel).Children.OfType<Border>()];

        private static Border ItemRow(MenuPanel panel, int index) => Rows(panel).Where(r => r.Child is not null).ElementAt(index);

        private static Border Separator(MenuPanel panel) => Rows(panel).Single(r => r.Child is null);

        [AvaloniaFact]
        public void KitMenuPanel_IsAControlThemeTargetingMenuPanel() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(MenuPanel), KitMenuPanelTheme().TargetType);
        }

        [AvaloniaFact]
        public void Frame_MatchesTheSpecTokens() {
            using var fixture = new KitFixture();
            MenuPanel panel = Themed();
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Size.MenuPanelMinWidth", Application.Current.ActualThemeVariant, out object? minWidth);
            Assert.Equal(Assert.IsType<double>(minWidth), panel.MinWidth);
            Assert.Equal(208.0, panel.MinWidth);

            Assert.Equal(fixture.Brush("Foreman.Color.PopoverMaterial").ToImmutable(), panel.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.BorderStrong").ToImmutable(), panel.BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(1), panel.BorderThickness);

            Application.Current!.TryGetResource("Foreman.Radius.Control", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), panel.CornerRadius);

            Application.Current!.TryGetResource("Foreman.Shadow.Card", Application.Current.ActualThemeVariant, out object? shadow);
            Assert.Equal(Assert.IsType<BoxShadows>(shadow), Frame(panel).BoxShadow);
        }

        [AvaloniaFact]
        public void ItemRow_Is22TallWith4x0MarginAnd6x0x12x0PaddingAndRadius4() {
            using var fixture = new KitFixture();
            MenuPanel panel = Themed();
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            Border row = ItemRow(panel, 0);
            Assert.Equal(22, row.Height);
            Assert.Equal(new Thickness(4, 0), row.Margin);
            Assert.Equal(new Thickness(6, 0, 12, 0), row.Padding);
            Assert.Equal(new CornerRadius(4), row.CornerRadius);
        }

        [AvaloniaFact]
        public void UncheckedItem_StillReservesA16PointCheckColumnWithAHiddenGlyph() {
            using var fixture = new KitFixture();
            MenuPanel panel = Themed();
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            var content = (StackPanel)ItemRow(panel, 0).Child!;
            var slot = (Panel)content.Children[0];
            Assert.Equal(16, slot.Width);

            var check = slot.Children.OfType<Path>().Single();
            Assert.False(check.IsVisible);
        }

        [AvaloniaFact]
        public void CheckGlyph_UsesTheCheckmark12Geometry() {
            using var fixture = new KitFixture();
            MenuPanel panel = Themed();
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            var content = (StackPanel)ItemRow(panel, 1).Child!;
            var slot = (Panel)content.Children[0];
            var check = slot.Children.OfType<Path>().Single();

            Assert.True(check.IsVisible);
            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.Checkmark.12"), check.Data);
        }

        [AvaloniaFact]
        public void Highlighted_RowIsAccentFillWithTextOnAccentForeground() {
            using var fixture = new KitFixture();
            MenuPanel panel = Themed();
            fixture.Window.Content = panel;
            panel.HighlightedIndex = 0;
            Dispatcher.UIThread.RunJobs();

            Border row = ItemRow(panel, 0);
            var content = (StackPanel)row.Child!;
            var text = content.Children.OfType<TextBlock>().Single();

            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), row.Background?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.TextOnAccent").ToImmutable(), text.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void DisabledItem_ForegroundIsTextTertiary() {
            using var fixture = new KitFixture();
            MenuPanel panel = Themed();
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            var content = (StackPanel)ItemRow(panel, 2).Child!;
            var text = content.Children.OfType<TextBlock>().Single();

            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), text.Foreground?.ToImmutable());
        }

        [AvaloniaFact]
        public void Separator_Is1PointBorderWith8x5MarginsAndBorderFill() {
            using var fixture = new KitFixture();
            MenuPanel panel = Themed();
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            Border separator = Separator(panel);
            Assert.Equal(1, separator.Height);
            Assert.Equal(new Thickness(8, 5), separator.Margin);
            Assert.Equal(fixture.Brush("Foreman.Color.Border").ToImmutable(), separator.Background?.ToImmutable());
        }

        [AvaloniaFact]
        public void UnchangedResourceValue_DoesNotRebuildTheRows() {
            using var fixture = new KitFixture();
            MenuPanel panel = Themed();
            var host = new StackPanel();
            host.Resources["Foreman.Thickness.MenuItemMargin"] = new Thickness(4, 0);
            host.Children.Add(panel);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            Border[] before = Rows(panel);

            host.Resources["Foreman.Thickness.MenuItemMargin"] = new Thickness(4, 0);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, Rows(panel));
        }

        [AvaloniaFact]
        public void ScopedToken_MenuItemHeightComesFromTheControlsOwnResourceScope() {
            using var fixture = new KitFixture();
            MenuPanel panel = Themed();
            var host = new StackPanel();
            host.Resources["Foreman.Size.MenuItemHeight"] = 33.0;
            host.Children.Add(panel);
            fixture.Window.Content = host;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33, ItemRow(panel, 0).Height);
        }

        [AvaloniaFact]
        public void UnthemedPanel_AdoptsKitMenuPanelFromItsHostScope() {
            using var fixture = new KitFixture();
            var panel = new MenuPanel();
            panel.Items.Add(new MenuRow { Text = "Items / s" });
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            Assert.Same(KitMenuPanelTheme(), panel.Theme);
            Assert.Single(ItemsPanel(panel).Children);
            Assert.True(panel.Bounds.Width > 0 && panel.Bounds.Height > 0);
        }
    }

    public sealed class KitMenuRowIconTests {
        private static ControlTheme KitMenuPanelTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.MenuPanel", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static StackPanel ItemsPanel(MenuPanel panel) =>
            panel.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Items");

        private static Border[] Rows(MenuPanel panel) => [.. ItemsPanel(panel).Children.OfType<Border>()];

        private static Border ItemRow(MenuPanel panel, int index) => Rows(panel).Where(r => r.Child is not null).ElementAt(index);

        private static double LabelXFromPanelEdge(MenuPanel panel, TextBlock label) =>
            Avalonia.VisualExtensions.TranslatePoint(label, new Point(0, 0), ItemsPanel(panel))!.Value.X;

        [AvaloniaFact]
        public void RowWithIcon_ChildOrderIsCheckThenImageThenLabel_AndLabelXIs58() {
            using var fixture = new KitFixture();
            var icon = new RenderTargetBitmap(new PixelSize(4, 4));
            var panel = new MenuPanel { Theme = KitMenuPanelTheme() };
            panel.Items.Add(new MenuRow { Text = "Rare", IsChecked = false, Icon = icon });
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            var content = (StackPanel)ItemRow(panel, 0).Child!;
            Assert.Equal(3, content.Children.Count);
            var checkSlot = Assert.IsType<Panel>(content.Children[0]);
            var iconFrame = Assert.IsType<Border>(content.Children[1]);
            var label = Assert.IsType<TextBlock>(content.Children[2]);

            Assert.Equal(16, checkSlot.Width);
            Assert.Equal(16, iconFrame.Width);
            Assert.Equal(16, iconFrame.Height);
            Assert.Equal(new CornerRadius(3), iconFrame.CornerRadius);
            Assert.Equal(58, LabelXFromPanelEdge(panel, label), 1);
        }

        [AvaloniaFact]
        public void RowWithIconUnchecked_KeepsTheSlot_SoIconAndLabelXMatchTheCheckedRow() {
            using var fixture = new KitFixture();
            var icon = new RenderTargetBitmap(new PixelSize(4, 4));
            var panel = new MenuPanel { Theme = KitMenuPanelTheme() };
            panel.Items.Add(new MenuRow { Text = "Normal", IsChecked = true, Icon = icon });
            panel.Items.Add(new MenuRow { Text = "Rare", IsChecked = false, Icon = icon });
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            var checkedContent = (StackPanel)ItemRow(panel, 0).Child!;
            var uncheckedContent = (StackPanel)ItemRow(panel, 1).Child!;
            var checkedLabel = (TextBlock)checkedContent.Children[2];
            var uncheckedLabel = (TextBlock)uncheckedContent.Children[2];

            Assert.Equal(LabelXFromPanelEdge(panel, checkedLabel), LabelXFromPanelEdge(panel, uncheckedLabel));
            Assert.Equal(16, ((Panel)uncheckedContent.Children[0]).Width);
        }

        [AvaloniaFact]
        public void CheckedRow_GlyphSitsInTheSlot_NotTheIcon() {
            using var fixture = new KitFixture();
            var icon = new RenderTargetBitmap(new PixelSize(4, 4));
            var panel = new MenuPanel { Theme = KitMenuPanelTheme() };
            panel.Items.Add(new MenuRow { Text = "Normal", IsChecked = true, Icon = icon });
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            var content = (StackPanel)ItemRow(panel, 0).Child!;
            var checkSlot = (Panel)content.Children[0];
            var iconFrame = (Border)content.Children[1];

            Path check = Assert.Single(checkSlot.Children.OfType<Path>());
            Assert.True(check.IsVisible);
            Assert.Empty(iconFrame.GetVisualDescendants().OfType<Path>());
        }

        [AvaloniaFact]
        public void MenuWithNoIcons_LabelXIs34_NoIconChild() {
            using var fixture = new KitFixture();
            var panel = new MenuPanel { Theme = KitMenuPanelTheme() };
            panel.Items.Add(new MenuRow { Text = "Show hidden objects", IsChecked = true });
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            var content = (StackPanel)ItemRow(panel, 0).Child!;
            Assert.Equal(2, content.Children.Count);
            var label = Assert.IsType<TextBlock>(content.Children[1]);
            Assert.Equal(34, LabelXFromPanelEdge(panel, label), 1);
        }

        [AvaloniaFact]
        public void Highlighting_ChangesTheCheckmarkAndLabelBrushes_ButLeavesTheIconUntouched() {
            using var fixture = new KitFixture();
            var icon = new RenderTargetBitmap(new PixelSize(4, 4));
            var panel = new MenuPanel { Theme = KitMenuPanelTheme() };
            panel.Items.Add(new MenuRow { Text = "Rare", IsChecked = true, Icon = icon });
            fixture.Window.Content = panel;
            panel.HighlightedIndex = 0;
            Dispatcher.UIThread.RunJobs();

            var content = (StackPanel)ItemRow(panel, 0).Child!;
            var checkSlot = (Panel)content.Children[0];
            var iconFrame = (Border)content.Children[1];
            Path check = Assert.Single(checkSlot.Children.OfType<Path>());
            var label = content.Children.OfType<TextBlock>().Single();
            var image = Assert.IsType<Image>(iconFrame.Child);

            Assert.Equal(fixture.Brush("Foreman.Color.TextOnAccent").ToImmutable(), check.Stroke?.ToImmutable());
            Assert.Equal(fixture.Brush("Foreman.Color.TextOnAccent").ToImmutable(), label.Foreground?.ToImmutable());
            Assert.Same(icon, image.Source);
            Assert.Null(iconFrame.Background);
        }

        [AvaloniaFact]
        public void RowHeight_Stays22WithAnIconPresent() {
            using var fixture = new KitFixture();
            var icon = new RenderTargetBitmap(new PixelSize(4, 4));
            var panel = new MenuPanel { Theme = KitMenuPanelTheme() };
            panel.Items.Add(new MenuRow { Text = "Rare", Icon = icon });
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(22, ItemRow(panel, 0).Height);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public class MenuPanelScaleTests {
        [AvaloniaFact]
        public void RowHeight_TracksScaleContract() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.MenuPanel", null, out object? theme));
            var panel = new MenuPanel { Theme = Assert.IsType<ControlTheme>(theme) };
            panel.Items.Add(new MenuRow { Text = "Item" });
            fixture.Window.Content = panel;
            Dispatcher.UIThread.RunJobs();

            var itemsPanel = panel.GetVisualDescendants().OfType<StackPanel>().Single(s => s.Name == "PART_Items");
            Assert.Equal(22, itemsPanel.Children.OfType<Border>().Single().Height);

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(33, itemsPanel.Children.OfType<Border>().Single().Height);
        }
    }
}
