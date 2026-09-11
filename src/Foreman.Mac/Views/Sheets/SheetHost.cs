using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    public sealed class SheetHost : Panel {
        private const double ClampMargin = 12;
        private const int TopmostZIndex = 10_000;

        private static readonly Dictionary<Window, SheetHost> hostsByWindow = [];

        private sealed class StackedLayer {
            public required Border Scrim;
            public required Border Panel;
            public required Control Content;
            public Button? DefaultButton;
            public Action? Cancel;
            public InputElement? FocusToRestore;
        }

        private readonly Border scrim;
        private readonly Border panelBorder;
        private readonly Queue<TaskCompletionSource<bool>> waitingTurns = new();
        private readonly List<StackedLayer> stack = [];

        private Window? ownerWindow;
        private Control? topAnchor;
        private IReadOnlyList<Control> blockedSiblings = [];
        private Control? currentContent;
        private Control? dismissedContent;
        private Button? currentDefaultButton;
        private Action? cancelCurrent;
        private DispatcherTimer? closingTimer;
        private bool busy;

        public bool IsOpen { get; private set; }
        public Control? Content => currentContent;

        private Border ActivePanel => stack.Count > 0 ? stack[^1].Panel : panelBorder;
        private Control? ActiveContent => stack.Count > 0 ? stack[^1].Content : currentContent;
        private Button? ActiveDefaultButton => stack.Count > 0 ? stack[^1].DefaultButton : currentDefaultButton;
        private Action? ActiveCancel => stack.Count > 0 ? stack[^1].Cancel : cancelCurrent;

        private SheetHost() {
            IsVisible = false;
            ZIndex = TopmostZIndex;
            scrim = new Border { Classes = { "foreman-sheet-scrim" } };
            panelBorder = new Border { Classes = { "foreman-sheet-panel" } };
            Children.Add(scrim);
            Children.Add(panelBorder);
            AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }

        //topAnchor (MainWindow's titlebar/toolbar row) keeps the panel flush under it instead of at y=0 -
        //only read on first construction, since a window has exactly one host for its whole lifetime.
        public static SheetHost For(Window window, Control? topAnchor = null) {
            if (hostsByWindow.TryGetValue(window, out SheetHost? existing))
                return existing;

            var host = new SheetHost { ownerWindow = window, topAnchor = topAnchor };
            Attach(window, host);
            window.AddHandler(InputElement.GotFocusEvent, host.OnWindowGotFocus);
            window.Closed += (_, _) => hostsByWindow.Remove(window);
            hostsByWindow[window] = host;
            return host;
        }

        //blockedSiblings is captured before the host joins the collection so it never blocks itself
        private static void Attach(Window window, SheetHost host) {
            switch (window.Content) {
                case Grid grid:
                    Grid.SetRow(host, 0);
                    Grid.SetColumn(host, 0);
                    Grid.SetRowSpan(host, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(host, Math.Max(1, grid.ColumnDefinitions.Count));
                    host.blockedSiblings = [.. grid.Children];
                    grid.Children.Add(host);
                    break;
                case Panel panel:
                    host.blockedSiblings = [.. panel.Children];
                    panel.Children.Add(host);
                    break;
                case Control existing:
                    window.Content = null;
                    var root = new Panel();
                    root.Children.Add(existing);
                    root.Children.Add(host);
                    window.Content = root;
                    host.blockedSiblings = [existing];
                    break;
                case null:
                    window.Content = host;
                    break;
            }
        }

        public async Task<T?> ShowAsync<T>(SheetContent<T> content) {
            if (busy)
                await WaitForTurnAsync().ConfigureAwait(true);
            else
                busy = true;

            Present(content);
            try {
                return await content.Result.ConfigureAwait(true);
            } finally {
                BeginDismiss();
            }
        }

        //stacks above the open sheet: queuing would deadlock a sheet that raises its own alert
        public Task<T?> ShowStackedAsync<T>(SheetContent<T> content) =>
            IsOpen ? ShowOnTopAsync(content) : ShowAsync(content);

        private async Task<T?> ShowOnTopAsync<T>(SheetContent<T> content) {
            StackedLayer layer = PushLayer(content);
            try {
                return await content.Result.ConfigureAwait(true);
            } finally {
                BeginDismissLayer(layer);
            }
        }

        private Task<bool> WaitForTurnAsync() {
            var tcs = new TaskCompletionSource<bool>();
            waitingTurns.Enqueue(tcs);
            return tcs.Task;
        }

        private void Present<T>(SheetContent<T> content) {
            currentContent = content;
            currentDefaultButton = content.DefaultButton;
            cancelCurrent = content.Cancel;

            panelBorder.Child = content;
            panelBorder.Width = content.SheetWidth;
            panelBorder.Height = content.SheetHeight ?? double.NaN;
            PositionLayer(scrim, panelBorder);

            foreach (Control sibling in blockedSiblings)
                sibling.IsHitTestVisible = false;

            IsVisible = true;
            IsOpen = true;
            scrim.Classes.Add("entering");
            panelBorder.Classes.Add("entering");
            //Deferred: a UserControl's Content isn't realized into the visual tree until its template applies.
            Dispatcher.UIThread.Post(() => {
                scrim.Classes.Remove("entering");
                panelBorder.Classes.Remove("entering");
                FocusFirstFocusable(content);
            }, DispatcherPriority.Loaded);
        }

        private StackedLayer PushLayer<T>(SheetContent<T> content) {
            var layerScrim = new Border { Classes = { "foreman-sheet-scrim" } };
            var layerPanel = new Border { Classes = { "foreman-sheet-panel" } };
            var layer = new StackedLayer {
                Scrim = layerScrim,
                Panel = layerPanel,
                Content = content,
                DefaultButton = content.DefaultButton,
                Cancel = content.Cancel,
                FocusToRestore = FindFocused(ActivePanel),
            };
            ActivePanel.Classes.Add("dimmed");

            layerPanel.Child = content;
            layerPanel.Width = content.SheetWidth;
            layerPanel.Height = content.SheetHeight ?? double.NaN;
            PositionLayer(layerScrim, layerPanel);

            Children.Add(layerScrim);
            Children.Add(layerPanel);
            stack.Add(layer);

            layerScrim.Classes.Add("entering");
            layerPanel.Classes.Add("entering");
            Dispatcher.UIThread.Post(() => {
                layerScrim.Classes.Remove("entering");
                layerPanel.Classes.Remove("entering");
                FocusFirstFocusable(content);
            }, DispatcherPriority.Loaded);

            return layer;
        }

        //offset keeps the scrim/panel below topAnchor (MainWindow's toolbar row) instead of covering it;
        //plain windows pass no anchor, so offset is 0 and this is a no-op beyond the width/height clamp.
        private void PositionLayer(Border layerScrim, Border layerPanel) {
            double offset = topAnchor?.Bounds.Bottom ?? 0;
            if (offset > 0) {
                layerScrim.Margin = new Thickness(0, offset, 0, 0);
                layerPanel.Margin = new Thickness(0, offset, 0, 0);
            }
            if (ownerWindow is not null) {
                layerPanel.MaxWidth = Math.Max(0, ownerWindow.ClientSize.Width - (ClampMargin * 2));
                layerPanel.MaxHeight = Math.Max(0, ownerWindow.ClientSize.Height - offset - (ClampMargin * 2));
            }
        }

        private void BeginDismiss() {
            IsOpen = false;
            dismissedContent = currentContent;
            currentContent = null;
            foreach (Control sibling in blockedSiblings)
                sibling.IsHitTestVisible = true;

            scrim.Classes.Add("closing");
            panelBorder.Classes.Add("closing");

            closingTimer?.Stop();
            closingTimer = new DispatcherTimer { Interval = Motion.SheetCloseDuration };
            closingTimer.Tick += OnClosingTimerElapsed;
            closingTimer.Start();
        }

        private void OnClosingTimerElapsed(object? sender, EventArgs e) {
            closingTimer!.Stop();
            closingTimer.Tick -= OnClosingTimerElapsed;
            closingTimer = null;

            IsVisible = false;
            scrim.Classes.Remove("closing");
            panelBorder.Classes.Remove("closing");
            panelBorder.Child = null;
            currentDefaultButton = null;
            cancelCurrent = null;

            //backstop: dispose here on every dismissal path even if the sheet forgot
            if (dismissedContent is IDisposable disposable)
                disposable.Dispose();
            dismissedContent = null;

            busy = false;
            if (waitingTurns.Count > 0) {
                busy = true;
                waitingTurns.Dequeue().TrySetResult(true);
            }
        }

        private void BeginDismissLayer(StackedLayer layer) {
            stack.Remove(layer);
            layer.Scrim.Classes.Add("closing");
            layer.Panel.Classes.Add("closing");

            var timer = new DispatcherTimer { Interval = Motion.SheetCloseDuration };
            timer.Tick += (_, _) => OnLayerClosingTimerElapsed(timer, layer);
            timer.Start();
        }

        private void OnLayerClosingTimerElapsed(DispatcherTimer timer, StackedLayer layer) {
            timer.Stop();

            Children.Remove(layer.Scrim);
            Children.Remove(layer.Panel);
            layer.Panel.Child = null;

            if (layer.Content is IDisposable disposable)
                disposable.Dispose();

            ActivePanel.Classes.Remove("dimmed");
            if (layer.FocusToRestore is { } restore)
                restore.Focus();
            else if (ActiveContent is { } activeContent)
                FocusFirstFocusable(activeContent);
        }

        //Tunnel so Enter/Escape win over the focused control's own handling (TextBox Enter, Button Space/Enter).
        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (!IsOpen)
                return;

            if (e.Key == Key.Enter) {
                if (e.Source is TextBox { AcceptsReturn: true })
                    return;
                if (ActiveDefaultButton is { } button) {
                    e.Handled = true;
                    //Mirrors Button.OnClick: Command runs only if nothing handled Click first.
                    var click = new RoutedEventArgs(Button.ClickEvent);
                    button.RaiseEvent(click);
                    if (!click.Handled && button.Command is { } command && command.CanExecute(button.CommandParameter))
                        command.Execute(button.CommandParameter);
                }
            } else if (e.Key == Key.Escape) {
                e.Handled = true;
                ActiveCancel?.Invoke();
            }
        }

        //IsHitTestVisible=false only blocks pointer input, not Tab - this yanks focus back into whichever
        //layer is topmost.
        private void OnWindowGotFocus(object? sender, RoutedEventArgs e) {
            if (!IsOpen)
                return;
            Border activePanel = ActivePanel;
            if (e.Source is Visual focused && IsInside(activePanel, focused))
                return;
            if (ActiveContent is Control content)
                FocusFirstFocusable(content);
        }

        private static bool IsInside(Visual ancestor, Visual visual) {
            for (Visual? node = visual; node is not null; node = node.GetVisualParent())
                if (ReferenceEquals(node, ancestor))
                    return true;
            return false;
        }

        private static void FocusFirstFocusable(Control root) {
            InputElement? target = root.GetVisualDescendants().OfType<InputElement>()
                .FirstOrDefault(candidate => candidate.Focusable && candidate.IsEffectivelyEnabled);
            target?.Focus();
        }

        private static InputElement? FindFocused(Border panel) =>
            panel.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(candidate => candidate.IsFocused);
    }
}
