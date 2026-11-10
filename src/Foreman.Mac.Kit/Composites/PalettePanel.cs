using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Foreman.Mac.Kit.Appearance;
using Foreman.Mac.Kit.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Foreman.Mac.Kit.Composites {
    public sealed record PaletteResult(string Name, string Kind, IBrush? Icon);

    public sealed class PaletteFooterHint {
        public string? KeyText { get; set; }
        public string? KeyAccessibleName { get; set; }
        public string? Description { get; set; }
    }

    public class PalettePanel : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(PalettePanel);

        private const double NoResultsMaxHeight = 180;

        public static readonly StyledProperty<PaletteResult?> SelectedItemProperty =
            AvaloniaProperty.Register<PalettePanel, PaletteResult?>(nameof(SelectedItem));

        public static readonly StyledProperty<string?> NoResultsHeadlineProperty =
            AvaloniaProperty.Register<PalettePanel, string?>(nameof(NoResultsHeadline));

        public static readonly StyledProperty<string?> NoResultsSentenceProperty =
            AvaloniaProperty.Register<PalettePanel, string?>(nameof(NoResultsSentence));

        public static readonly StyledProperty<ButtonRow?> NoResultsButtonsProperty =
            AvaloniaProperty.Register<PalettePanel, ButtonRow?>(nameof(NoResultsButtons));

        public static readonly StyledProperty<Control?> EmptyContentProperty =
            AvaloniaProperty.Register<PalettePanel, Control?>(nameof(EmptyContent));

        public static readonly StyledProperty<Control?> HeaderAccessoryProperty =
            AvaloniaProperty.Register<PalettePanel, Control?>(nameof(HeaderAccessory));

        public string? NoResultsHeadline { get => GetValue(NoResultsHeadlineProperty); set => SetValue(NoResultsHeadlineProperty, value); }
        public string? NoResultsSentence { get => GetValue(NoResultsSentenceProperty); set => SetValue(NoResultsSentenceProperty, value); }
        public ButtonRow? NoResultsButtons { get => GetValue(NoResultsButtonsProperty); set => SetValue(NoResultsButtonsProperty, value); }
        public Control? EmptyContent { get => GetValue(EmptyContentProperty); set => SetValue(EmptyContentProperty, value); }
        public Control? HeaderAccessory { get => GetValue(HeaderAccessoryProperty); set => SetValue(HeaderAccessoryProperty, value); }

        public AvaloniaList<PaletteResult> Results { get; } = [];
        public AvaloniaList<PaletteFooterHint> FooterHints { get; } = [];

        public PaletteResult? SelectedItem {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public SearchField Search { get; } = new();

        public event EventHandler<PaletteResult>? Committed;
        public event EventHandler<PaletteResult>? CommittedAsSource;
        public event EventHandler? CloseRequested;

        private readonly StatePanel noResultsPanel = new() { Shape = StatePanelShape.NoResults };

        private Panel? header;
        private ContentPresenter? accessoryPresenter;
        private StackPanel? track;
        private ContentPresenter? statePresenter;
        private StackPanel? footer;
        private ControlTheme? rowTheme;
        private readonly List<PaletteRow> rows = [];
        private List<PaletteResult>? currentItems;
        private bool rebuildScheduled;
        private bool isKey = true;
        private CancellationTokenSource? reflowCancellation;

        static PalettePanel() {
            SelectedItemProperty.Changed.AddClassHandler<PalettePanel>((p, _) => p.ApplySelection());
            NoResultsHeadlineProperty.Changed.AddClassHandler<PalettePanel>((p, _) => p.noResultsPanel.Headline = p.NoResultsHeadline);
            NoResultsSentenceProperty.Changed.AddClassHandler<PalettePanel>((p, _) => p.noResultsPanel.Sentence = p.NoResultsSentence);
            NoResultsButtonsProperty.Changed.AddClassHandler<PalettePanel>((p, _) => p.ApplyNoResultsButtons());
            EmptyContentProperty.Changed.AddClassHandler<PalettePanel>((p, _) => p.ApplyStateVisibility());
            HeaderAccessoryProperty.Changed.AddClassHandler<PalettePanel>((p, _) => p.ApplyHeaderAccessory());
        }

        public PalettePanel() {
            Search.Classes.Add("flush");

            Results.CollectionChanged += (_, _) => ScheduleRebuild();
            FooterHints.CollectionChanged += (_, _) => RebuildFooter();
            Search.GetObservable(TextBox.TextProperty).Subscribe(new TextObserver(_ => ApplyStateVisibility()));
            this.GetObservable(ListHost.IsKeyProperty).Subscribe(new BoolObserver(v => { isKey = v; ApplyIsKeyToRows(); }));

            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            ThemeAware.Attach(this, RefreshMotionState);
            AutomationProperties.SetName(this, "Results");
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            if (currentItems is not null && !Results.SequenceEqual(currentItems))
                ScheduleRebuild();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            rebuildScheduled = false;
            CompositeMotion.ClearExplicitHeight(this, ref reflowCancellation);
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            header = e.NameScope.Find<Panel>("PART_Header");
            accessoryPresenter = e.NameScope.Find<ContentPresenter>("PART_HeaderAccessory");
            track = e.NameScope.Find<StackPanel>("PART_Track");
            statePresenter = e.NameScope.Find<ContentPresenter>("PART_State");
            footer = e.NameScope.Find<StackPanel>("PART_Footer");

            ApplyTheme("Kit.SearchField", Search);
            ApplyTheme("Kit.StatePanel", noResultsPanel);
            if (this.TryFindResource("Kit.PaletteRow", null, out object? theme) && theme is ControlTheme paletteRowTheme)
                rowTheme = paletteRowTheme;

            if (header is not null && !header.Children.Contains(Search))
                header.Children.Insert(0, Search);
            if (statePresenter is not null)
                statePresenter.MaxHeight = NoResultsMaxHeight;

            ApplyNoResultsButtons();
            ApplyHeaderAccessory();
            RebuildFooter();
            Rebuild();
        }

        private void ScheduleRebuild() {
            if (rebuildScheduled)
                return;
            rebuildScheduled = true;
            Dispatcher.UIThread.Post(() => {
                rebuildScheduled = false;
                if (VisualRoot is null)
                    return;
                Rebuild();
            }, DispatcherPriority.Loaded);
        }

        private void ApplyTheme(string key, StyledElement target) {
            if (target.Theme is null && this.TryFindResource(key, null, out object? theme) && theme is ControlTheme controlTheme)
                target.Theme = controlTheme;
        }

        private void RebuildFooter() {
            if (footer is null)
                return;
            footer.Children.Clear();
            foreach (PaletteFooterHint hint in FooterHints) {
                var cap = new KeyCap { Text = hint.KeyText, AccessibleName = hint.KeyAccessibleName };
                ApplyTheme("Kit.KeyCap", cap);
                var description = new TextBlock { Text = hint.Description, VerticalAlignment = VerticalAlignment.Center };
                ApplyTheme("Kit.Text", description);
                description.Classes.Add("caption");
                description.Classes.Add("tertiary");
                footer.Children.Add(new StackPanel {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children = { cap, description },
                });
            }
        }

        private void Rebuild() {
            if (track is null)
                return;

            List<PaletteResult> newItems = [.. Results];
            if (currentItems is not null && newItems.SequenceEqual(currentItems))
                return;

            List<PaletteRow> oldRows = [.. rows];
            List<PaletteResult> oldItems = currentItems ?? [];
            var newSet = new HashSet<PaletteResult>(newItems);

            rows.Clear();
            track.Children.Clear();

            bool reduceMotion = Motion.ReduceMotion;
            for (int i = 0; i < newItems.Count; i++) {
                PaletteRow row = BuildRow(newItems[i], i, newItems.Count, reduceMotion);
                track.Children.Add(row);
                rows.Add(row);
            }

            if (!reduceMotion)
                for (int i = 0; i < oldRows.Count; i++)
                    if (!newSet.Contains(oldItems[i]))
                        FadeOutAndRemove(oldRows[i]);

            currentItems = newItems;
            SetCurrentValue(SelectedItemProperty, newItems.Count > 0 ? newItems[0] : null);
            ApplySelection();
            ApplyStateVisibility();
            ApplyAutomationSummary();
            BeginHeightReflow();
        }

        private PaletteRow BuildRow(PaletteResult item, int index, int total, bool reduceMotion) {
            var row = new PaletteRow { Label = item.Name, Kind = item.Kind, Icon = item.Icon, IsKey = isKey };
            if (rowTheme is not null)
                row.Theme = rowTheme;
            AutomationProperties.SetName(row, $"{item.Name}, {item.Kind}, {index + 1} of {total}");
            row.Activated += (_, _) => {
                SetCurrentValue(SelectedItemProperty, item);
                if (row.OptionHeld)
                    CommitAsSource();
                else
                    Commit();
            };

            if (reduceMotion) {
                row.Transitions = [];
            } else {
                row.Classes.Add("entering");
                row.Transitions = Motion.Transition(TransitionKind.Fade, Motion.EntranceDuration, Motion.EntranceEasing, Motion.StaggerDelay(index));
                row.Loaded += OnRowLoaded;
            }
            return row;
        }

        private static void OnRowLoaded(object? sender, RoutedEventArgs e) {
            var row = (PaletteRow)sender!;
            row.Loaded -= OnRowLoaded;
            Dispatcher.UIThread.Post(() => row.Classes.Remove("entering"), DispatcherPriority.Loaded);
        }

        private void FadeOutAndRemove(PaletteRow row) {
            if (track is null)
                return;
            var wrap = new StackPanel { IsHitTestVisible = false };
            CompositeMotion.FadeOutAndRemove(row, wrap, track);
        }

        private void ApplyIsKeyToRows() {
            foreach (PaletteRow row in rows)
                row.IsKey = isKey;
        }

        private void ApplySelection() {
            int selectedIndex = SelectedItem is null || currentItems is null ? -1 : currentItems.IndexOf(SelectedItem);
            for (int i = 0; i < rows.Count; i++)
                rows[i].IsSelected = i == selectedIndex;
        }

        private void MoveSelection(int delta) {
            if (currentItems is not { Count: > 0 } items)
                return;
            int index = SelectedItem is null ? -1 : items.IndexOf(SelectedItem);
            int next = index < 0 ? 0 : (((index + delta) % items.Count) + items.Count) % items.Count;
            SetCurrentValue(SelectedItemProperty, items[next]);
        }

        private void Commit() {
            if (SelectedItem is { } item)
                Committed?.Invoke(this, item);
        }

        private void CommitAsSource() {
            if (SelectedItem is { } item)
                CommittedAsSource?.Invoke(this, item);
        }

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            switch (e.Key) {
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Return when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                    CommitAsSource();
                    e.Handled = true;
                    break;
                case Key.Return:
                    Commit();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    CloseRequested?.Invoke(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
        }

        private void ApplyStateVisibility() {
            bool noResults = (currentItems?.Count ?? 0) == 0;
            if (track is not null)
                track.IsVisible = !noResults;
            if (statePresenter is null)
                return;
            Control? state = !noResults ? null
                : string.IsNullOrEmpty(Search.Text) && EmptyContent is { } empty ? empty
                : NoResultsButtons is not null ? noResultsPanel
                : null;
            if (!ReferenceEquals(statePresenter.Content, state))
                statePresenter.Content = state;
            statePresenter.IsVisible = state is not null;
        }

        //StatePanel refuses to build without a button, so the no-results panel only joins the tree once the host gives it one.
        private void ApplyNoResultsButtons() {
            if (NoResultsButtons is null) {
                if (ReferenceEquals(statePresenter?.Content, noResultsPanel))
                    statePresenter!.Content = null;
                noResultsPanel.Buttons = null;
            } else {
                ApplyTheme("Kit.ButtonRow", NoResultsButtons);
                foreach (Control item in NoResultsButtons.Items.OfType<Button>())
                    ApplyTheme("Kit.Button", item);
                noResultsPanel.Buttons = NoResultsButtons;
            }
            ApplyStateVisibility();
        }

        private void ApplyHeaderAccessory() {
            if (accessoryPresenter is null)
                return;
            accessoryPresenter.Content = HeaderAccessory;
            accessoryPresenter.IsVisible = HeaderAccessory is not null;
        }

        private void ApplyAutomationSummary() {
            int count = currentItems?.Count ?? 0;
            AutomationProperties.SetName(this, $"{count} result{(count == 1 ? "" : "s")}");
        }

        private void RefreshMotionState() {
            if (Motion.ReduceMotion) {
                foreach (PaletteRow row in rows) {
                    row.Transitions = [];
                    row.Opacity = 1;
                    row.Classes.Remove("entering");
                }
            } else {
                for (int i = 0; i < rows.Count; i++)
                    rows[i].Transitions = Motion.Transition(TransitionKind.Fade, Motion.EntranceDuration, Motion.EntranceEasing, Motion.StaggerDelay(i));
            }
        }

        private void BeginHeightReflow() {
            CompositeMotion.ReflowHeight(this, () => {
                //Measure keeps the constraint it is given as the one layout re-measures with, so reuse the host's width, not Bounds.
                double width = LayoutInformation.GetPreviousMeasureConstraint(this)?.Width ?? Bounds.Width;
                Measure(new Size(width, double.PositiveInfinity));
                return DesiredSize.Height;
            }, () => CompositeMotion.ClearExplicitHeight(this, ref reflowCancellation), ref reflowCancellation);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new PalettePanelAutomationPeer(this);

        private sealed class PalettePanelAutomationPeer(PalettePanel owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        }

        private sealed class TextObserver(Action<string?> onNext) : IObserver<string?> {
            public void OnNext(string? value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        private sealed class BoolObserver(Action<bool> onNext) : IObserver<bool> {
            public void OnNext(bool value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }
}
