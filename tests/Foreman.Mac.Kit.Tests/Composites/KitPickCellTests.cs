using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    public sealed class KitPickCellTests {
        private static ControlTheme KitPickCellTheme() {
            Assert.True(Application.Current!.TryGetResource("Kit.PickCell", null, out object? theme));
            return Assert.IsType<ControlTheme>(theme);
        }

        private static PickCell Themed() => new() { Theme = KitPickCellTheme() };

        private static Border Frame(PickCell cell) =>
            cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");

        private static Border Ring(PickCell cell) =>
            cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Ring");

        private static Image Icon(PickCell cell) =>
            cell.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "PART_Icon");

        private static Viewbox PlusBox(PickCell cell) =>
            cell.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_PlusBox");

        private static Viewbox AlertBox(PickCell cell) =>
            cell.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_AlertBox");

        [AvaloniaFact]
        public void KitPickCell_IsAControlThemeTargetingPickCell() {
            using var fixture = new KitFixture();
            Assert.Equal(typeof(PickCell), KitPickCellTheme().TargetType);
        }

        [AvaloniaFact]
        public void Cell_Is26SquareAtRadius5() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(26, cell.Bounds.Width);
            Assert.Equal(26, cell.Bounds.Height);
            Application.Current!.TryGetResource("Foreman.Radius.PickCell", Application.Current.ActualThemeVariant, out object? radius);
            Assert.Equal(Assert.IsType<CornerRadius>(radius), Frame(cell).CornerRadius);
        }

        [AvaloniaFact]
        public void Selected_FillsPickCellSelected_AndTurnsTheOuterBorderTransparent() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Rect before = cell.Bounds;
            cell.IsSelected = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.PickCellSelected").ToImmutable(), Frame(cell).Background?.ToImmutable());
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(Frame(cell).BorderBrush).Color);
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Ring(cell).BorderBrush?.ToImmutable());
            Assert.Equal(new Thickness(1), Ring(cell).BorderThickness);
            Assert.Equal(before, cell.Bounds);
        }

        [AvaloniaFact]
        public void NotKeyWindow_ViaInheritedAttachedProperty_SwapsRingColor() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            cell.IsSelected = true;

            var container = new Border { Child = cell };
            container.SetValue(ListHost.IsKeyProperty, false);
            fixture.Window.Content = container;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(fixture.Brush("Foreman.Color.SelectionInactive").ToImmutable(), Ring(cell).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void SelectedAndFocused_CarriesBothTheInsetRingAndTheOuterFocusAdorner() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            cell.IsSelected = true;
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            FocusRing.SetShowAlways(cell, true);
            Dispatcher.UIThread.RunJobs();

            Assert.NotNull(AdornerLayer.GetAdorner(cell));
            Assert.Equal(fixture.Brush("Foreman.Color.Accent").ToImmutable(), Ring(cell).BorderBrush?.ToImmutable());
        }

        [AvaloniaFact]
        public void Empty_ShowsPlusGlyphInTextTertiary_AndTheEmptySlotTooltip() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            cell.IsEmpty = true;
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();
            AnimationClock.Settle();

            Assert.True(PlusBox(cell).IsVisible);
            Path glyph = Assert.IsType<Path>(PlusBox(cell).Child);
            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.Plus.12"), glyph.Data);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), glyph.Fill?.ToImmutable());
            Assert.Equal(0, Icon(cell).Opacity);
            Assert.Equal("Empty slot — press Return to choose", ToolTip.GetTip(cell));
        }

        [AvaloniaFact]
        public void Empty_WithAnEmptyItemNoun_NamesItInTheTooltip() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            cell.IsEmpty = true;
            cell.EmptyItemNoun = "module";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Empty slot — press Return to choose a module", ToolTip.GetTip(cell));
        }

        [AvaloniaFact]
        public void Filled_TooltipEqualsTheAccessibleName() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            cell.AccessibleName = "Productivity Module 3";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Productivity Module 3", ToolTip.GetTip(cell));
        }

        [AvaloniaFact]
        public void Icon_CrossFadesAtTheTabLabelCrossFadeDuration() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            var transition = Assert.Single(Icon(cell).Transitions!.OfType<DoubleTransition>(), t => t.Property == Visual.OpacityProperty);
            Assert.Equal(Motion.TabLabelCrossFadeDuration, transition.Duration);
        }

        [AvaloniaFact]
        public void Missing_ShowsAlertGlyphInWarning_AndNamesTheMissingItem() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            cell.IsMissing = true;
            cell.AccessibleName = "Deprecated Module";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Assert.True(AlertBox(cell).IsVisible);
            Path glyph = Assert.IsType<Path>(AlertBox(cell).Child);
            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.Alert.12"), glyph.Data);
            Assert.Equal(fixture.Brush("Foreman.Color.Warning").ToImmutable(), glyph.Stroke?.ToImmutable());
            Assert.Equal("Deprecated Module", ToolTip.GetTip(cell));
        }

        [AvaloniaFact]
        public void Disabled_WholeCellDims_ButTheIconIsNeverHidden() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            cell.Icon = new RenderTargetBitmap(new PixelSize(4, 4));
            fixture.Window.Content = cell;
            cell.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Opacity.Disabled", Application.Current.ActualThemeVariant, out object? disabled);
            Assert.Equal(Assert.IsType<double>(disabled), cell.Opacity);
            Assert.True(Icon(cell).IsVisible);
        }

        [AvaloniaFact]
        public void Unavailable_DimsToOpacityUnavailable_ButStaysHitTestableAndEnabled() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            cell.IsUnavailable = true;
            cell.AccessibleName = "Assembling machine 3";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Application.Current!.TryGetResource("Foreman.Opacity.Unavailable", Application.Current.ActualThemeVariant, out object? unavailable);
            Assert.Equal(Assert.IsType<double>(unavailable), cell.Opacity);
            Assert.True(cell.IsEnabled);
            Assert.True(cell.IsHitTestVisible);
            Assert.Equal("Assembling machine 3 — not available in this save", ToolTip.GetTip(cell));
        }

        [AvaloniaFact]
        public void None_ShowsTheNoneSlashGlyphInTextTertiary_OnThePlainCellFill() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            cell.IsNone = true;
            cell.AccessibleName = "No beacon";
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Viewbox noneBox = cell.GetVisualDescendants().OfType<Viewbox>().Single(v => v.Name == "PART_NoneBox");
            Assert.True(noneBox.IsVisible);
            Path glyph = Assert.IsType<Path>(noneBox.Child);
            Assert.Equal(Application.Current!.FindResource("Kit.Glyph.NoneSlash.12"), glyph.Data);
            Assert.Equal(fixture.Brush("Foreman.Color.TextTertiary").ToImmutable(), glyph.Stroke?.ToImmutable());
            Assert.Equal("No beacon", ToolTip.GetTip(cell));
            Assert.False(PlusBox(cell).IsVisible);
        }

        [AvaloniaFact]
        public void VoiceOver_InvokeRaisesInvoked() {
            using var fixture = new KitFixture();
            PickCell cell = Themed();
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            bool invoked = false;
            cell.Invoked += (_, _) => invoked = true;

            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(cell);
            IInvokeProvider? invoke = peer.GetProvider<IInvokeProvider>();
            Assert.NotNull(invoke);
            invoke!.Invoke();

            Assert.True(invoked);
        }
    }

    [Collection(KitScaleGroup.Name)]
    public sealed class KitPickCellScaleTests {
        [AvaloniaFact]
        public void CellAndIcon_TrackScaleContract_BorderAndRadiusDoNot() {
            using var fixture = new KitFixture();
            Assert.True(Application.Current!.TryGetResource("Kit.PickCell", null, out object? theme));
            var cell = new PickCell { Theme = (ControlTheme)theme! };
            fixture.Window.Content = cell;
            Dispatcher.UIThread.RunJobs();

            Border frame = cell.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Frame");
            Image icon = cell.GetVisualDescendants().OfType<Image>().Single(i => i.Name == "PART_Icon");
            CornerRadius radiusBefore = frame.CornerRadius;

            ScaleContract.Apply(1.5);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(39, cell.Bounds.Width);
            Assert.Equal(27, icon.Width);
            Assert.Equal(radiusBefore, frame.CornerRadius);
            Assert.Equal(new Thickness(1), frame.BorderThickness);
        }
    }
}
