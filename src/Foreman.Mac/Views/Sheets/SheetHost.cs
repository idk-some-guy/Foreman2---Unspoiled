using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Themes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    //One SheetHost per window (For caches it); a second ShowAsync queues FIFO behind whichever sheet is open.
    public sealed class SheetHost : Panel {
        private const double ClampMargin = 12;

        private static readonly Dictionary<Window, SheetHost> hostsByWindow = [];

        private readonly Border scrim;
        private readonly Border panelBorder;
        private readonly Queue<TaskCompletionSource<bool>> waitingTurns = new();

        private Window? ownerWindow;
        private Control? siblingContent;
        private Control? currentContent;
        private Control? dismissedContent;
        private Button? currentDefaultButton;
        private Action? cancelCurrent;
        private DispatcherTimer? closingTimer;
        private bool busy;

        public bool IsOpen { get; private set; }
        public Control? Content => currentContent;

        private SheetHost() {
            IsVisible = false;
            scrim = new Border { Classes = { "foreman-sheet-scrim" } };
            panelBorder = new Border { Classes = { "foreman-sheet-panel" } };
            Children.Add(scrim);
            Children.Add(panelBorder);
            AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }

        public static SheetHost For(Window window) {
            if (hostsByWindow.TryGetValue(window, out SheetHost? existing))
                return existing;

            var host = new SheetHost { ownerWindow = window };
            Attach(window, host);
            window.AddHandler(InputElement.GotFocusEvent, host.OnWindowGotFocus);
            window.Closed += (_, _) => hostsByWindow.Remove(window);
            hostsByWindow[window] = host;
            return host;
        }

        //A Grid root gets the host as a spanning child (reaches the titlebar row too); any other Panel just
        //takes it as a last child; anything else gets wrapped.
        private static void Attach(Window window, SheetHost host) {
            switch (window.Content) {
                case Grid grid:
                    Grid.SetRow(host, 0);
                    Grid.SetColumn(host, 0);
                    Grid.SetRowSpan(host, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(host, Math.Max(1, grid.ColumnDefinitions.Count));
                    grid.Children.Add(host);
                    host.siblingContent = grid;
                    break;
                case Panel panel:
                    panel.Children.Add(host);
                    host.siblingContent = panel;
                    break;
                case Control existing:
                    window.Content = null;
                    var root = new Panel();
                    root.Children.Add(existing);
                    root.Children.Add(host);
                    window.Content = root;
                    host.siblingContent = existing;
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

            if (ownerWindow is not null) {
                panelBorder.MaxWidth = Math.Max(0, ownerWindow.ClientSize.Width - (ClampMargin * 2));
                panelBorder.MaxHeight = Math.Max(0, ownerWindow.ClientSize.Height - (ClampMargin * 2));
            }

            if (siblingContent is not null)
                siblingContent.IsHitTestVisible = false;

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

        private void BeginDismiss() {
            IsOpen = false;
            dismissedContent = currentContent;
            currentContent = null;
            if (siblingContent is not null)
                siblingContent.IsHitTestVisible = true;

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

        //Tunnel so Enter/Escape win over the focused control's own handling (TextBox Enter, Button Space/Enter).
        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (!IsOpen)
                return;

            if (e.Key == Key.Enter) {
                if (e.Source is TextBox { AcceptsReturn: true })
                    return;
                if (currentDefaultButton is { } button) {
                    e.Handled = true;
                    //Mirrors Button.OnClick: Command runs only if nothing handled Click first.
                    var click = new RoutedEventArgs(Button.ClickEvent);
                    button.RaiseEvent(click);
                    if (!click.Handled && button.Command is { } command && command.CanExecute(button.CommandParameter))
                        command.Execute(button.CommandParameter);
                }
            } else if (e.Key == Key.Escape) {
                e.Handled = true;
                cancelCurrent?.Invoke();
            }
        }

        //IsHitTestVisible=false only blocks pointer input, not Tab - this yanks focus back into the sheet.
        private void OnWindowGotFocus(object? sender, RoutedEventArgs e) {
            if (!IsOpen)
                return;
            if (e.Source is Visual focused && IsInside(panelBorder, focused))
                return;
            if (panelBorder.Child is Control content)
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
    }
}
