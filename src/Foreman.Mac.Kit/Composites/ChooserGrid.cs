using Avalonia;
using Avalonia.Animation;
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
using System.Collections.Specialized;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    //The add-browser grid (addendum §04); a cell never receives real keyboard focus, so all navigation lives here.
    public class ChooserGrid : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(ChooserGrid);

        private const double RowPitch = 96; //Size.ChooserCell 88 + Space.Space2 8

        public static readonly StyledProperty<string?> QueryProperty =
            AvaloniaProperty.Register<ChooserGrid, string?>(nameof(Query));

        public static readonly StyledProperty<ChooserItem?> SelectedItemProperty =
            AvaloniaProperty.Register<ChooserGrid, ChooserItem?>(nameof(SelectedItem));

        public static readonly StyledProperty<object?> StateContentProperty =
            AvaloniaProperty.Register<ChooserGrid, object?>(nameof(StateContent));

        public AvaloniaList<ChooserSection> Sections { get; } = [];

        public string? Query {
            get => GetValue(QueryProperty);
            set => SetValue(QueryProperty, value);
        }

        public ChooserItem? SelectedItem {
            get => GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public object? StateContent {
            get => GetValue(StateContentProperty);
            set => SetValue(StateContentProperty, value);
        }

        public event EventHandler<ChooserItem>? Activated;

        private ScrollViewer? scroll;
        private StackPanel? track;
        private ContentPresenter? statePresenter;
        private ControlTheme? cellTheme;
        private ControlTheme? headingTheme;
        private double headingSpacing = 6;
        private readonly List<ChooserCell> cells = [];
        private readonly List<ChooserItem> items = [];
        private readonly List<int> sectionOfCell = [];
        private readonly List<(ChooserSection Section, NotifyCollectionChangedEventHandler Handler)> itemSubs = [];
        private readonly List<IDisposable> subs = [];
        private IDisposable? scrollSub;
        private bool hasKeyboardFocus;
        private string typeBuffer = "";
        private long lastTypeTicks;

        static ChooserGrid() {
            FocusableProperty.OverrideDefaultValue<ChooserGrid>(true);
            SelectedItemProperty.Changed.AddClassHandler<ChooserGrid>((g, _) => g.ApplySelection());
            StateContentProperty.Changed.AddClassHandler<ChooserGrid>((g, _) => g.ApplyStateContent());
            QueryProperty.Changed.AddClassHandler<ChooserGrid>((g, _) => g.Rebuild());
        }

        public ChooserGrid() {
            Sections.CollectionChanged += OnSectionsChanged;
            GotFocus += (_, e) => {
                if (e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional) {
                    hasKeyboardFocus = true;
                    ApplySelection();
                }
            };
            LostFocus += (_, _) => {
                hasKeyboardFocus = false;
                ApplySelection();
            };
            ThemeAware.Attach(this, RefreshMotionState);
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            scroll = e.NameScope.Find<ScrollViewer>("PART_Scroll");
            track = e.NameScope.Find<StackPanel>("PART_Track");
            statePresenter = e.NameScope.Find<ContentPresenter>("PART_State");
            HookScroll();
            Rebuild();
            ApplyStateContent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
            HookScroll();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
            scrollSub?.Dispose();
            scrollSub = null;
        }

        //Held outside `subs` since the template is only ever applied once - re-attach is the only chance this gets re-hooked.
        private void HookScroll() {
            scrollSub?.Dispose();
            scrollSub = scroll?.GetObservable(ScrollViewer.OffsetProperty)
                .Subscribe(new Observer<Vector>(_ => Dispatcher.UIThread.Post(UpdateStickyHeadings, DispatcherPriority.Loaded)));
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new ChooserGridAutomationPeer(this);

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (e.Handled || cells.Count == 0)
                return;

            int index = CurrentIndex();
            bool noSelection = index < 0;
            switch (e.Key) {
                case Key.Left:
                    MoveTo(noSelection ? 0 : Math.Max(index - 1, 0));
                    e.Handled = true;
                    break;
                case Key.Right:
                    MoveTo(noSelection ? 0 : Math.Min(index + 1, cells.Count - 1));
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (noSelection) MoveTo(0); else MoveVertical(index, -1);
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (noSelection) MoveTo(0); else MoveVertical(index, 1);
                    e.Handled = true;
                    break;
                case Key.Home:
                    MoveTo(0);
                    e.Handled = true;
                    break;
                case Key.End:
                    MoveTo(cells.Count - 1);
                    e.Handled = true;
                    break;
                case Key.PageUp:
                    MoveByPage(-1);
                    e.Handled = true;
                    break;
                case Key.PageDown:
                    MoveByPage(1);
                    e.Handled = true;
                    break;
                case Key.Return:
                    if (index >= 0 && index < items.Count)
                        Activated?.Invoke(this, items[index]);
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnTextInput(TextInputEventArgs e) {
            base.OnTextInput(e);
            if (string.IsNullOrEmpty(e.Text) || cells.Count == 0)
                return;

            long now = Environment.TickCount64;
            typeBuffer = now - lastTypeTicks > 1000 ? e.Text : typeBuffer + e.Text;
            lastTypeTicks = now;

            int current = CurrentIndex();
            bool noSelection = current < 0;
            int start = noSelection ? 0 : current;
            int firstOffset = noSelection ? 0 : 1;

            List<int> sameSection = [.. Enumerable.Range(0, cells.Count).Where(i => sectionOfCell[i] == sectionOfCell[start])];
            int posInSection = sameSection.IndexOf(start);
            for (int offset = firstOffset; offset <= sameSection.Count; offset++) {
                int candidate = sameSection[(posInSection + offset) % sameSection.Count];
                if (Matches(candidate)) {
                    MoveTo(candidate);
                    e.Handled = true;
                    return;
                }
            }
            for (int offset = firstOffset; offset <= cells.Count; offset++) {
                int candidate = (start + offset) % cells.Count;
                if (Matches(candidate)) {
                    MoveTo(candidate);
                    e.Handled = true;
                    return;
                }
            }
        }

        private bool Matches(int index) =>
            items[index].Name is { } name && name.StartsWith(typeBuffer, StringComparison.OrdinalIgnoreCase);

        private int CurrentIndex() =>
            SelectedItem is null ? -1 : items.IndexOf(SelectedItem);

        private void MoveTo(int index) {
            if (index < 0 || index >= items.Count)
                return;
            SetCurrentValue(SelectedItemProperty, items[index]);
        }

        private void MoveByPage(int direction) {
            if (CurrentIndex() < 0) {
                MoveTo(0);
                return;
            }
            double viewport = scroll?.Viewport.Height ?? 0;
            int rows = Math.Max(1, (int)(viewport / RowPitch));
            for (int i = 0; i < rows; i++) {
                int before = CurrentIndex();
                MoveVertical(before, direction);
                if (CurrentIndex() == before)
                    break;
            }
        }

        //Rows come from real rendered Bounds, not a tracked column count, so any laid-out width works.
        private void MoveVertical(int index, int direction) {
            ChooserCell current = cells[index];
            int section = sectionOfCell[index];
            double x = current.Bounds.X, y = current.Bounds.Y;

            List<ChooserCell> sameSectionCells = CellsInSection(section);
            List<ChooserCell> candidates = [.. direction > 0
                ? sameSectionCells.Where(c => c.Bounds.Y > y + 1)
                : sameSectionCells.Where(c => c.Bounds.Y < y - 1)];

            if (candidates.Count > 0) {
                double targetY = direction > 0 ? candidates.Min(c => c.Bounds.Y) : candidates.Max(c => c.Bounds.Y);
                ChooserCell target = candidates.Where(c => Math.Abs(c.Bounds.Y - targetY) <= 1)
                    .OrderBy(c => Math.Abs(c.Bounds.X - x)).First();
                MoveTo(cells.IndexOf(target));
                return;
            }

            int nextSection = section + direction;
            while (nextSection >= 0 && nextSection < Sections.Count && CellsInSection(nextSection).Count == 0)
                nextSection += direction;
            if (nextSection < 0 || nextSection >= Sections.Count)
                return;

            List<ChooserCell> nextSectionCells = CellsInSection(nextSection);
            double edgeY = direction > 0 ? nextSectionCells.Min(c => c.Bounds.Y) : nextSectionCells.Max(c => c.Bounds.Y);
            ChooserCell landing = nextSectionCells.Where(c => Math.Abs(c.Bounds.Y - edgeY) <= 1)
                .OrderBy(c => Math.Abs(c.Bounds.X - x)).First();
            MoveTo(cells.IndexOf(landing));
        }

        private List<ChooserCell> CellsInSection(int section) {
            List<ChooserCell> result = [];
            for (int i = 0; i < cells.Count; i++)
                if (sectionOfCell[i] == section)
                    result.Add(cells[i]);
            return result;
        }

        private void OnSectionsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            foreach ((ChooserSection oldSection, NotifyCollectionChangedEventHandler oldHandler) in itemSubs)
                oldSection.Items.CollectionChanged -= oldHandler;
            itemSubs.Clear();

            foreach (ChooserSection section in Sections) {
                NotifyCollectionChangedEventHandler handler = (_, _) => Rebuild();
                section.Items.CollectionChanged += handler;
                itemSubs.Add((section, handler));
            }
            Rebuild();
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Kit.ChooserCell").Subscribe(new ResourceSinks.Sink<ControlTheme?>(v => { cellTheme = v; Rebuild(); }, null)));
            subs.Add(this.GetResourceObservable("Kit.ChooserHeading").Subscribe(new ResourceSinks.Sink<ControlTheme?>(v => { headingTheme = v; Rebuild(); }, null)));
            subs.Add(this.GetResourceObservable("Foreman.Space.Space15").Subscribe(new ResourceSinks.Sink<double>(v => { headingSpacing = v; Rebuild(); }, 6)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private void Rebuild() {
            if (track is null)
                return;

            List<ChooserCell> oldCells = [.. cells];
            List<ChooserItem> oldItems = [.. items];
            var newItemSet = new HashSet<ChooserItem>(Sections.SelectMany(s => s.Items));

            cells.Clear();
            items.Clear();
            sectionOfCell.Clear();
            track.Children.Clear();

            bool reduceMotion = Motion.ReduceMotion;
            bool firstHeading = true;
            for (int s = 0; s < Sections.Count; s++) {
                ChooserSection section = Sections[s];
                if (section.Items.Count == 0)
                    continue;

                track.Children.Add(BuildHeading(section, firstHeading));
                firstHeading = false;

                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                for (int i = 0; i < section.Items.Count; i++) {
                    ChooserItem item = section.Items[i];
                    ChooserCell cell = BuildCell(item, cells.Count, reduceMotion);
                    AutomationProperties.SetName(cell, $"{item.Name}, {i + 1} of {section.Items.Count}, {SectionName(section)}");
                    wrap.Children.Add(cell);
                    cells.Add(cell);
                    items.Add(item);
                    sectionOfCell.Add(s);
                }
                AutomationProperties.SetName(wrap, HeadingText(section));
                track.Children.Add(wrap);
            }

            if (!reduceMotion)
                for (int i = 0; i < oldCells.Count; i++)
                    if (!newItemSet.Contains(oldItems[i]))
                        FadeOutAndRemove(oldCells[i]);

            ApplySelection();
            ApplyAutomationSummary();
            Dispatcher.UIThread.Post(UpdateStickyHeadings, DispatcherPriority.Loaded);
        }

        private ChooserCell BuildCell(ChooserItem item, int flatIndex, bool reduceMotion) {
            var cell = new ChooserCell {
                Icon = item.Icon,
                Label = item.Name,
                FullName = item.Name,
                IsEnabled = item.IsEnabled,
                IsUnavailable = !item.IsAvailable,
                IsIconMissing = item.IsIconMissing,
                Margin = new Thickness(0, 0, 8, 8),
            };
            if (cellTheme is not null)
                cell.Theme = cellTheme;

            cell.PointerReleased += (_, args) => {
                if (args.InitialPressMouseButton == MouseButton.Left) {
                    SetCurrentValue(SelectedItemProperty, item);
                    Focus(NavigationMethod.Pointer);
                }
            };
            cell.Invoked += (_, _) => Activated?.Invoke(this, item);

            if (reduceMotion) {
                cell.Transitions = [];
            } else {
                cell.Classes.Add("entering");
                cell.Transitions = Motion.Transition(TransitionKind.Fade, Motion.EntranceDuration, Motion.EntranceEasing, Motion.StaggerDelay(flatIndex));
                cell.Loaded += OnCellLoaded;
            }
            return cell;
        }

        private static void OnCellLoaded(object? sender, RoutedEventArgs e) {
            var cell = (ChooserCell)sender!;
            cell.Loaded -= OnCellLoaded;
            Dispatcher.UIThread.Post(() => cell.Classes.Remove("entering"), DispatcherPriority.Loaded);
        }

        //A leaving cell gets one last no-heading wrap of its own so it keeps rendering while it fades.
        private void FadeOutAndRemove(ChooserCell cell) {
            if (track is null)
                return;
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, IsHitTestVisible = false };
            CompositeMotion.FadeOutAndRemove(cell, wrap, track);
        }

        private void RefreshMotionState() {
            if (Motion.ReduceMotion) {
                foreach (ChooserCell cell in cells) {
                    cell.Transitions = [];
                    cell.Opacity = 1;
                    cell.Classes.Remove("entering");
                }
            } else {
                for (int i = 0; i < cells.Count; i++)
                    cells[i].Transitions = Motion.Transition(TransitionKind.Fade, Motion.EntranceDuration, Motion.EntranceEasing, Motion.StaggerDelay(i));
            }
        }

        private StackPanel BuildHeading(ChooserSection section, bool isFirst) {
            var heading = new StackPanel {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, isFirst ? 0 : 16, 0, 8),
                Spacing = headingSpacing,
                Focusable = false,
            };
            heading.Classes.Add("chooser-heading");
            AutomationProperties.SetName(heading, HeadingText(section));

            if (!string.IsNullOrEmpty(Query)) {
                heading.Children.Add(HeadingRun(section.Group ?? ""));
                heading.Children.Add(HeadingRun("·"));
            }
            heading.Children.Add(HeadingRun(section.Subgroup ?? ""));
            return heading;
        }

        private TextBlock HeadingRun(string text) {
            var run = new TextBlock { Text = text, Focusable = false };
            if (headingTheme is not null)
                run.Theme = headingTheme;
            return run;
        }

        private string HeadingText(ChooserSection section) =>
            string.IsNullOrEmpty(Query) ? section.Subgroup ?? "" : $"{section.Group} · {section.Subgroup}";

        private static string SectionName(ChooserSection section) =>
            string.IsNullOrEmpty(section.Group) ? section.Subgroup ?? "" : $"{section.Group} {section.Subgroup}";

        private void ApplySelection() {
            int selectedIndex = SelectedItem is null ? -1 : items.IndexOf(SelectedItem);
            for (int i = 0; i < cells.Count; i++) {
                bool selected = i == selectedIndex;
                cells[i].IsSelected = selected;
                FocusRing.SetShowAlways(cells[i], selected && hasKeyboardFocus);
            }
        }

        private void ApplyStateContent() {
            bool hasState = StateContent is not null;
            if (scroll is not null)
                scroll.IsVisible = !hasState;
            if (statePresenter is not null) {
                statePresenter.Content = StateContent;
                statePresenter.IsVisible = hasState;
            }
        }

        private void ApplyAutomationSummary() =>
            AutomationProperties.SetName(this, $"{items.Count} result{(items.Count == 1 ? "" : "s")}");

        //Sticky headings: pin each to the viewport top via RenderTransform (never Margin, to avoid a layout feedback loop) until the next heading pushes it back up.
        private void UpdateStickyHeadings() {
            if (track is null || scroll is null)
                return;

            List<StackPanel> headings = [.. track.Children.OfType<StackPanel>().Where(p => p.Classes.Contains("chooser-heading"))];
            if (headings.Count == 0)
                return;

            foreach (StackPanel heading in headings)
                heading.RenderTransform = null;

            double[] naturalY = new double[headings.Count];
            for (int i = 0; i < headings.Count; i++)
                naturalY[i] = (headings[i].TranslatePoint(new Point(0, 0), scroll) ?? default).Y;

            for (int i = 0; i < headings.Count; i++) {
                double pushLimit = i + 1 < headings.Count ? naturalY[i + 1] - headings[i].Bounds.Height : double.PositiveInfinity;
                double targetY = naturalY[i] >= 0 ? naturalY[i] : Math.Min(0, pushLimit);
                headings[i].RenderTransform = new TranslateTransform(0, targetY - naturalY[i]);
            }
        }

        private sealed class Observer<T>(Action<T> onNext) : IObserver<T> {
            public void OnNext(T value) => onNext(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        private sealed class ChooserGridAutomationPeer(ChooserGrid owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        }
    }
}
