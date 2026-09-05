using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Views.Inspector;
using System.ComponentModel;
using System.Linq;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Foreman.Mac.UiTests.Inspector {
    //InspectorGroup in isolation (Task 6): the title row is a real Button, so a click or Enter/Space
    //while focused both toggle IsExpanded for free - this pins that wiring plus the disclosure glyph.
    public class InspectorGroupTests {
        private static (AvaloniaWindow Host, InspectorGroup Group) NewGroup(bool isExpanded = true) {
            var group = new InspectorGroup { Title = "Beacon", IsExpanded = isExpanded, GroupContent = new TextBlock { Text = "content" } };
            var host = new AvaloniaWindow { Content = group, Width = 300, Height = 300 };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            return (host, group);
        }

        private static Button HeaderOf(InspectorGroup group) =>
            group.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "GroupHeader");

        private static ContentPresenter BodyOf(InspectorGroup group) =>
            group.GetVisualDescendants().OfType<ContentPresenter>().Single(p => Equals(p.Content, group.GroupContent));

        private static void Click(AvaloniaWindow window, Button button) {
            Avalonia.Point center = new(button.Bounds.Width / 2, button.Bounds.Height / 2);
            Avalonia.Point screen = Avalonia.VisualExtensions.TranslatePoint(button, center, window) ?? center;
            window.MouseDown(screen, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(screen, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        [AvaloniaFact]
        public void TitleClick_TogglesIsExpanded_AndContentVisibility() {
            (AvaloniaWindow host, InspectorGroup group) = NewGroup(isExpanded: true);
            Assert.True(BodyOf(group).IsVisible);

            Button header = HeaderOf(group);
            Assert.True(header.Bounds.Width > 0 && header.Bounds.Height > 0, $"header bounds: {header.Bounds}");

            Click(host, header);

            Assert.False(group.IsExpanded);
            Assert.False(BodyOf(group).IsVisible);

            Click(host, HeaderOf(group));

            Assert.True(group.IsExpanded);
            Assert.True(BodyOf(group).IsVisible);
        }

        [AvaloniaFact]
        public void TitleClick_TogglesDisclosureGlyph() {
            (_, InspectorGroup group) = NewGroup(isExpanded: true);
            TextBlock glyph = HeaderOf(group).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "DisclosureGlyph");
            string expandedGlyph = glyph.Text!;

            group.IsExpanded = false;
            Assert.NotEqual(expandedGlyph, glyph.Text);

            group.IsExpanded = true;
            Assert.Equal(expandedGlyph, glyph.Text);
        }

        [AvaloniaFact]
        public void EnterKey_WhileHeaderFocused_TogglesIsExpanded() {
            (_, InspectorGroup group) = NewGroup(isExpanded: true);
            Button header = HeaderOf(group);
            header.Focus();

            header.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            Dispatcher.UIThread.RunJobs();

            Assert.False(group.IsExpanded);
        }

        private sealed class BoolSource : INotifyPropertyChanged {
            private bool flag;
            public event PropertyChangedEventHandler? PropertyChanged;
            public bool Flag {
                get => flag;
                set { flag = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Flag))); }
            }
        }

        //The header click must use SetCurrentValue - a plain setter clears whatever binding is on
        //IsExpandedProperty, so a later source-driven change would never reach the control again.
        [AvaloniaFact]
        public void TitleClick_DoesNotSeverALiveIsExpandedBinding() {
            (AvaloniaWindow host, InspectorGroup group) = NewGroup(isExpanded: false);
            var source = new BoolSource();
            group.Bind(InspectorGroup.IsExpandedProperty, new Binding(nameof(BoolSource.Flag)) { Source = source });
            Dispatcher.UIThread.RunJobs();
            Assert.False(group.IsExpanded);

            Click(host, HeaderOf(group));
            Assert.True(group.IsExpanded);

            source.Flag = false;
            Dispatcher.UIThread.RunJobs();
            Assert.False(group.IsExpanded);

            source.Flag = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(group.IsExpanded);
        }
    }
}
