using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Foreman.Mac.Kit;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Composites;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Foreman.Mac.Views.Sheets {
    //Each sheet is one Kit.SheetFrame layer (an AlertSheet brings its own Kit.AlertFrame); stacked layers sit above the base one.
    public sealed class SheetHost : Panel {
        private const double ClampMargin = 12;
        private const int TopmostZIndex = 10_000;
        private const string TryAgainLabel = "Try Again";

        private static readonly Dictionary<Window, SheetHost> hostsByWindow = [];

        private sealed class Layer {
            public required Control Root;
            public required Control Content;
            public required Action<bool> SetOpen;
            public required Action Cancel;
            public ButtonRow? Buttons;
            public Button? LegacyDefault;
            public SheetBody? Body;
            public bool Dismissed;
            public Button? ActivityDefault;
            public InputElement? LastFocused;
            public bool PriorDefaultEnabled;
            public object? PriorDefaultContent;
        }

        private readonly Queue<TaskCompletionSource<bool>> waitingTurns = new();
        private readonly List<Layer> layers = [];

        private Control? topAnchor;
        private IReadOnlyList<Control> blockedSiblings = [];
        private Layer? baseLayer;
        private DispatcherTimer? closingTimer;
        private bool busy;

        public bool IsOpen { get; private set; }
        public Control? Content => baseLayer?.Content;

        private Layer? TopLayer => layers.LastOrDefault(l => !l.Dismissed);

        //An alert's ⌘. lives on its button row; a sheet frame's own handler closes the frame's open popover before the sheet.
        internal InputElement? FrontmostCancelTarget => !IsOpen || TopLayer is not { } top ? null
            : top.Root is SheetFrame frame ? frame
            : (InputElement?)top.Buttons ?? top.Root;

        private SheetHost() {
            IsVisible = false;
            ZIndex = TopmostZIndex;
            AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);

            //The host outlives any one sheet, so it is the one stable subscription forwarding theme refreshes.
            ThemeAware.Attach(this, () => {
                foreach (Layer layer in layers)
                    (layer.Content as IThemeAware)?.RefreshTheme();
            });
        }

        //topAnchor (MainWindow's titlebar/toolbar row) keeps the panel flush under it instead of at y=0 -
        //only read on first construction, since a window has exactly one host for its whole lifetime.
        public static SheetHost For(Window window, Control? topAnchor = null) {
            if (hostsByWindow.TryGetValue(window, out SheetHost? existing))
                return existing;

            var host = new SheetHost { topAnchor = topAnchor };
            Attach(window, host);
            window.AddHandler(InputElement.GotFocusEvent, host.OnWindowGotFocus);
            window.Closed += (_, _) => hostsByWindow.Remove(window);
            hostsByWindow[window] = host;
            return host;
        }

        //blockedSiblings is captured before the host joins the collection so it never blocks itself; the toolbar row stays live (v2 §04).
        //A layout panel (DockPanel, StackPanel) would lay the host out as one more child, so only a plain Panel takes it as an overlay.
        private static void Attach(Window window, SheetHost host) {
            switch (window.Content) {
                case Grid grid:
                    Grid.SetRow(host, 0);
                    Grid.SetColumn(host, 0);
                    Grid.SetRowSpan(host, Math.Max(1, grid.RowDefinitions.Count));
                    Grid.SetColumnSpan(host, Math.Max(1, grid.ColumnDefinitions.Count));
                    host.blockedSiblings = [.. grid.Children.Where(c => !ReferenceEquals(c, host.topAnchor))];
                    grid.Children.Add(host);
                    break;
                case Panel panel when panel.GetType() == typeof(Panel):
                    host.blockedSiblings = [.. panel.Children.Where(c => !ReferenceEquals(c, host.topAnchor))];
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

            baseLayer = PushLayer(content);
            foreach (Control sibling in blockedSiblings)
                sibling.IsHitTestVisible = false;
            IsVisible = true;
            IsOpen = true;
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
            Layer layer = PushLayer(content);
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

        private Layer PushLayer<T>(SheetContent<T> content) {
            double topInset = topAnchor?.Bounds.Bottom ?? 0;
            Layer layer = content is AlertSheet alert ? AlertLayer(alert, topInset) : FrameLayer(content, topInset);
            content.ActivityChanged += OnActivityChanged;
            layers.Add(layer);
            Children.Add(layer.Root);
            ApplyActivity(layer, content.Activity);

            //Opened once templates are realized, so the kit's open transition runs instead of snapping to :open.
            Dispatcher.UIThread.Post(() => {
                if (!layer.Dismissed)
                    layer.SetOpen(true);
            }, DispatcherPriority.Loaded);
            return layer;
        }

        private static Layer AlertLayer(AlertSheet alert, double topInset) {
            AlertFrame frame = alert.FrameControl;
            frame.TopInset = topInset;
            var layer = new Layer {
                Root = alert,
                Content = alert,
                SetOpen = open => frame.IsOpen = open,
                Cancel = alert.Cancel,
                Buttons = alert.Buttons,
            };
            frame.Cancelled += (_, _) => layer.Cancel();
            return layer;
        }

        private static Layer FrameLayer<T>(SheetContent<T> content, double topInset) {
            var body = new SheetBody(content, content.SheetHeight);
            var frame = new SheetFrame {
                Theme = KitTheme("Kit.SheetFrame"),
                Title = content.Title,
                WidthClass = content.WidthClass,
                TopInset = topInset,
                Buttons = content.Buttons,
                Content = body,
            };
            //A width outside the named three (Graph Summary's 780 until it becomes a window) scopes the class token to this frame.
            if (content.SheetWidth > 0)
                frame.Resources[WidthToken(content.WidthClass)] = content.SheetWidth;

            var layer = new Layer {
                Root = frame,
                Content = content,
                SetOpen = open => frame.IsOpen = open,
                Cancel = content.Cancel,
                Buttons = content.Buttons,
                LegacyDefault = content.Buttons is null ? content.DefaultButton : null,
                Body = body,
            };
            //A row Cancel button that completes the sheet on its own click has already run by the time the frame reports Escape.
            frame.Cancelled += (_, _) => {
                if (!content.Result.IsCompleted)
                    layer.Cancel();
            };
            return layer;
        }

        private static string WidthToken(SheetWidthClass widthClass) => widthClass switch {
            SheetWidthClass.Alert => "Foreman.Metrics.SheetWidthAlert",
            SheetWidthClass.Wide => "Foreman.Metrics.SheetWidthWide",
            _ => "Foreman.Metrics.SheetWidthStandard",
        };

        private static ControlTheme KitTheme(string key) {
            Application.Current!.TryGetResource(key, null, out object? theme);
            return theme as ControlTheme ?? throw new InvalidOperationException($"SheetHost: kit theme '{key}' not found.");
        }

        private void OnActivityChanged(object? sender, EventArgs e) {
            Layer? layer = layers.FirstOrDefault(l => ReferenceEquals(l.Content, sender));
            if (layer is not null && !layer.Dismissed && sender is ISheetActivitySource source)
                ApplyActivity(layer, source.Activity);
        }

        private static void ApplyActivity(Layer layer, SheetActivity? activity) {
            if (layer.Body is null)
                return;
            if (layer.ActivityDefault is { } prior) {
                prior.IsEnabled = layer.PriorDefaultEnabled;
                prior.Content = layer.PriorDefaultContent;
                layer.ActivityDefault = null;
            }
            if (activity is null) {
                layer.Body.Strip = null;
                return;
            }

            Button? def = layer.Buttons?.ResolveDefaultButton() ?? layer.LegacyDefault;
            if (def is not null) {
                layer.ActivityDefault = def;
                layer.PriorDefaultEnabled = def.IsEnabled;
                layer.PriorDefaultContent = def.Content;
                if (activity.Kind == SheetActivityKind.InProgress)
                    def.IsEnabled = false;
                else
                    def.Content = TryAgainLabel;
            }

            //One strip per operation, updated in place, so it never shrinks or loses its bar mid-operation.
            if (layer.Body.Strip is not StatePanel panel) {
                panel = new StatePanel {
                    Theme = KitTheme("Kit.StatePanel"),
                    ForceCompact = true,
                    ExternalButtons = (Control?)layer.Buttons ?? layer.LegacyDefault,
                };
                layer.Body.Strip = panel;
            }
            panel.Shape = activity.Kind == SheetActivityKind.InProgress ? StatePanelShape.Loading : StatePanelShape.Error;
            panel.Sentence = activity.Sentence;
            panel.Progress = activity.Progress;
        }

        //Mirrors Button.OnClick: Command runs only if nothing handled Click first.
        private static void Click(Button button) {
            var click = new RoutedEventArgs(Button.ClickEvent);
            button.RaiseEvent(click);
            if (!click.Handled && button.Command is { } command && command.CanExecute(button.CommandParameter))
                command.Execute(button.CommandParameter);
        }

        private void BeginDismiss() {
            IsOpen = false;
            foreach (Control sibling in blockedSiblings)
                sibling.IsHitTestVisible = true;

            Layer? closing = baseLayer;
            baseLayer = null;
            if (closing is not null)
                Retire(closing);

            closingTimer?.Stop();
            closingTimer = new DispatcherTimer { Interval = Motion.SheetCloseDuration };
            closingTimer.Tick += (_, _) => OnClosingTimerElapsed(closing);
            closingTimer.Start();
        }

        private void OnClosingTimerElapsed(Layer? closed) {
            closingTimer!.Stop();
            closingTimer = null;

            if (closed is not null)
                Remove(closed);
            if (layers.Count == 0)
                IsVisible = false;

            busy = false;
            if (waitingTurns.Count > 0) {
                busy = true;
                waitingTurns.Dequeue().TrySetResult(true);
            }
        }

        private void BeginDismissLayer(Layer layer) {
            Retire(layer);
            var timer = new DispatcherTimer { Interval = Motion.SheetCloseDuration };
            timer.Tick += (_, _) => {
                timer.Stop();
                Remove(layer);
            };
            timer.Start();
        }

        //Marked dismissed before closing so the focus the kit hands back lands on the layer now on top.
        private void Retire(Layer layer) {
            layer.Dismissed = true;
            if (layer.Content is ISheetActivitySource source)
                source.ActivityChanged -= OnActivityChanged;
            layer.SetOpen(false);
        }

        private void Remove(Layer layer) {
            layers.Remove(layer);
            Children.Remove(layer.Root);
            if (layer.Body is not null)
                layer.Body.Release();

            //backstop: dispose here on every dismissal path even if the sheet forgot
            if (layer.Content is IDisposable disposable)
                disposable.Dispose();
        }

        //Legacy sheets keep their default button in the body; the kit frame only knows a Kit.ButtonRow.
        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (!IsOpen || e.Key != Key.Enter || TopLayer is not { Buttons: null, LegacyDefault: { } button })
                return;
            if (e.Source is TextBox { AcceptsReturn: true })
                return;
            e.Handled = true;
            if (button.IsEffectivelyEnabled)
                Click(button);
        }

        //IsHitTestVisible=false only blocks pointer input; this yanks focus back to where it was in the topmost layer.
        private void OnWindowGotFocus(object? sender, RoutedEventArgs e) {
            if (!IsOpen || TopLayer is not { } top)
                return;
            if (e.Source is Visual focused && IsInside(top.Root, focused)) {
                top.LastFocused = focused as InputElement;
                return;
            }
            if (top.LastFocused is { Focusable: true, IsEffectivelyEnabled: true, IsEffectivelyVisible: true } last && IsInside(top.Root, last) && last.Focus())
                return;
            FocusFirstFocusable(top.Content);
        }

        private static bool IsInside(Visual ancestor, Visual visual) {
            for (Visual? node = visual; node is not null; node = node.GetVisualParent())
                if (ReferenceEquals(node, ancestor))
                    return true;
            return false;
        }

        private static void FocusFirstFocusable(Control root) {
            InputElement? target = root.GetVisualDescendants().OfType<InputElement>()
                .FirstOrDefault(candidate => candidate.Focusable && candidate.IsEffectivelyEnabled && candidate.IsEffectivelyVisible);
            target?.Focus();
        }

        //The kit clamps width only; the unconstrained probe measure gives the frame's chrome, which a constrained one would clip.
        protected override Size MeasureOverride(Size availableSize) {
            double width = 0, height = 0;
            foreach (Control child in Children) {
                if (!double.IsInfinity(availableSize.Height) && layers.FirstOrDefault(l => ReferenceEquals(l.Root, child))?.Body is { } body) {
                    child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
                    double chrome = child.DesiredSize.Height - body.DesiredSize.Height;
                    if (body.SetLimit(availableSize.Height - (ClampMargin * 2) - chrome))
                        for (Visual? node = body; node is Layoutable layoutable && !ReferenceEquals(node, this); node = node.GetVisualParent())
                            layoutable.InvalidateMeasure();
                }
                child.Measure(availableSize);
                width = Math.Max(width, child.DesiredSize.Width);
                height = Math.Max(height, child.DesiredSize.Height);
            }
            return new Size(width, height);
        }

        //The frame's Content: the sheet's body, then an optional state strip above the button row.
        private sealed class SheetBody : Panel {
            public static readonly StyledProperty<double> StripGapProperty =
                AvaloniaProperty.Register<SheetBody, double>(nameof(StripGap), 16);

            static SheetBody() => AffectsMeasure<SheetBody>(StripGapProperty);

            private readonly Control body;
            private readonly double? fixedHeight;
            private Control? strip;
            private double limit = double.PositiveInfinity;

            public SheetBody(Control body, double? fixedHeight) {
                this.body = body;
                this.fixedHeight = fixedHeight;
                ClipToBounds = true;
                Children.Add(body);
                this.Bind(StripGapProperty, this.GetResourceObservable("Foreman.Space.Space4"));
            }

            public double StripGap => GetValue(StripGapProperty);

            public Control? Strip {
                get => strip;
                set {
                    if (strip is not null)
                        Children.Remove(strip);
                    strip = value;
                    if (strip is not null)
                        Children.Add(strip);
                    InvalidateMeasure();
                }
            }

            public bool SetLimit(double value) {
                value = Math.Max(0, value);
                if (Math.Abs(value - limit) < 0.5)
                    return false;
                limit = value;
                return true;
            }

            //Detaches the sheet so a caller can re-host the same content instance later.
            public void Release() => Children.Clear();

            private double StripHeight => strip is null ? 0 : strip.DesiredSize.Height + StripGap;

            protected override Size MeasureOverride(Size availableSize) {
                strip?.Measure(new Size(availableSize.Width, double.PositiveInfinity));
                double bodyCap = Math.Max(0, limit - StripHeight);
                body.Measure(new Size(availableSize.Width, fixedHeight ?? double.PositiveInfinity));
                double natural = fixedHeight ?? body.DesiredSize.Height;
                double bodyHeight = Math.Min(natural, bodyCap);
                if (bodyHeight < natural)
                    body.Measure(new Size(availableSize.Width, bodyHeight));
                double width = Math.Max(body.DesiredSize.Width, strip?.DesiredSize.Width ?? 0);
                return new Size(width, bodyHeight + StripHeight);
            }

            protected override Size ArrangeOverride(Size finalSize) {
                double bodyHeight = Math.Max(0, finalSize.Height - StripHeight);
                body.Arrange(new Rect(0, 0, finalSize.Width, bodyHeight));
                strip?.Arrange(new Rect(0, bodyHeight + StripGap, finalSize.Width, strip.DesiredSize.Height));
                return finalSize;
            }
        }
    }
}
