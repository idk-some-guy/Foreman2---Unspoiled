using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using Foreman.Mac.Kit.Controls;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Foreman.Mac.Kit.Composites {
    //A row (or, given Columns, a wrapped block) of PickCell slots; the grid is the sole tab stop, so arrow/Home/End/Return/Delete are all handled here.
    //Choice mode (.choice, 2026-09-17 building-picker ruling) repurposes the same cells as a radio group: a
    //wrapping track instead of a fixed-column Grid, a focus cursor kept apart from the armed SelectedIndex,
    //and pointer/Return committing instead of merely selecting.
    public class PickGrid : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(PickGrid);

        public static readonly StyledProperty<int> ColumnsProperty =
            AvaloniaProperty.Register<PickGrid, int>(nameof(Columns));

        //-1 is the grid's own "nothing selected" state, the default until the user picks a cell.
        public static readonly StyledProperty<int> SelectedIndexProperty =
            AvaloniaProperty.Register<PickGrid, int>(nameof(SelectedIndex), -1);

        public static readonly StyledProperty<string?> GroupLabelProperty =
            AvaloniaProperty.Register<PickGrid, string?>(nameof(GroupLabel), "Slots");

        //Equipped grids hold fixed positions ("slot"); a catalog/option grid holds none - the host says which by setting this false.
        public static readonly StyledProperty<bool> NamesSlotsProperty =
            AvaloniaProperty.Register<PickGrid, bool>(nameof(NamesSlots), true);

        //The empty-cell tooltip's noun, propagated to every PickCell - the kit names none by default.
        public static readonly StyledProperty<string?> EmptyItemNounProperty =
            AvaloniaProperty.Register<PickGrid, string?>(nameof(EmptyItemNoun));

        //A radio group of icon cells (2026-09-17 building-picker ruling): exactly one cell selected, no
        //empty cells, Delete does nothing, arrows move a focus cursor and Space/Return commits it.
        public static readonly StyledProperty<bool> IsChoiceModeProperty =
            AvaloniaProperty.Register<PickGrid, bool>(nameof(IsChoiceMode));

        public AvaloniaList<PickSlot> Slots { get; }

        //0 = natural width, one row sized to the slot count (addendum §03: "the grid's natural width is its cell count").
        public int Columns {
            get => GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        public int SelectedIndex {
            get => GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public string? GroupLabel {
            get => GetValue(GroupLabelProperty);
            set => SetValue(GroupLabelProperty, value);
        }

        public bool NamesSlots {
            get => GetValue(NamesSlotsProperty);
            set => SetValue(NamesSlotsProperty, value);
        }

        public string? EmptyItemNoun {
            get => GetValue(EmptyItemNounProperty);
            set => SetValue(EmptyItemNounProperty, value);
        }

        public bool IsChoiceMode {
            get => GetValue(IsChoiceModeProperty);
            set => SetValue(IsChoiceModeProperty, value);
        }

        public event EventHandler<int>? ChooseRequested;
        public event EventHandler<int>? Cleared;

        //Fires on a pointer click specifically (never on keyboard/arrow selection) - the app's own equip
        //flow needs to tell "the user clicked this cell" apart from SelectedIndex simply changing.
        public event EventHandler<int>? CellClicked;

        private Grid? track;
        private WrapPanel? wrap;
        private readonly List<PickCell> cells = [];
        private ControlTheme? pickCellTheme;
        private Thickness gap = new(2.5);
        private bool hasKeyboardFocus;
        //Choice mode only: the cell arrows move, kept apart from SelectedIndex (the armed/committed value) -
        //arrows never re-solve the graph, only Space/Return does. -1 means "follow SelectedIndex".
        private int focusIndex = -1;
        private string typeBuffer = "";
        private long lastTypeTicks;
        private readonly List<IDisposable> subs = [];

        static PickGrid() {
            FocusableProperty.OverrideDefaultValue<PickGrid>(true);
            ColumnsProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.Rebuild());
            SelectedIndexProperty.Changed.AddClassHandler<PickGrid>((g, e) => g.CoerceAndApplySelection(e));
            GroupLabelProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.ApplyAutomationName());
            NamesSlotsProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.Rebuild());
            EmptyItemNounProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.Rebuild());
            IsChoiceModeProperty.Changed.AddClassHandler<PickGrid>((g, _) => {
                g.PseudoClasses.Set(":choice", g.IsChoiceMode);
                g.Rebuild();
            });
        }

        public PickGrid() {
            Slots = [];
            Slots.CollectionChanged += OnSlotsChanged;
            GotFocus += (_, e) => {
                if (e.NavigationMethod == NavigationMethod.Pointer)
                    return;
                hasKeyboardFocus = true;
                if (IsChoiceMode) {
                    focusIndex = SelectedIndex >= 0 ? SelectedIndex : (Slots.Count > 0 ? 0 : -1);
                } else if (SelectedIndex < 0 && Slots.Count > 0) {
                    //Keyboard entry needs a cell to land on, or the rings draw nothing and Return/Delete aim at nothing.
                    SetCurrentValue(SelectedIndexProperty, 0);
                }
                ApplySelection();
            };
            LostFocus += (_, _) => {
                hasKeyboardFocus = false;
                focusIndex = -1;
                ApplySelection();
            };
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            track = e.NameScope.Find<Grid>("PART_Grid");
            wrap = e.NameScope.Find<WrapPanel>("PART_Wrap");
            Rebuild();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnAttachedToVisualTree(e);
            Subscribe();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            Unsubscribe();
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new PickGridAutomationPeer(this);

        //The armed value while navigating - SelectedIndex normally, but in choice mode arrows move this
        //instead so a re-solve only happens on commit (Space/Return), never on a plain traversal.
        private int NavIndex => IsChoiceMode ? (focusIndex >= 0 ? focusIndex : SelectedIndex) : SelectedIndex;

        private void SetNavIndex(int index) {
            if (IsChoiceMode) {
                focusIndex = index;
                ApplySelection();
            } else {
                SetCurrentValue(SelectedIndexProperty, index);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (Slots.Count == 0)
                return;

            int columns = EffectiveColumns();
            int nav = NavIndex;
            switch (e.Key) {
                case Key.Left:
                    SetNavIndex(nav < 0 ? 0 : Math.Max(nav - 1, 0));
                    e.Handled = true;
                    break;
                case Key.Right:
                    SetNavIndex(nav < 0 ? 0 : Math.Min(nav + 1, Slots.Count - 1));
                    e.Handled = true;
                    break;
                case Key.Up:
                    if (IsChoiceMode)
                        MoveVerticalChoice(nav, -1);
                    else if (nav < 0)
                        SetNavIndex(0);
                    else if (nav - columns >= 0)
                        SetNavIndex(nav - columns);
                    e.Handled = true;
                    break;
                case Key.Down:
                    if (IsChoiceMode)
                        MoveVerticalChoice(nav, 1);
                    else if (nav < 0)
                        SetNavIndex(0);
                    else if (nav + columns < Slots.Count)
                        SetNavIndex(nav + columns);
                    e.Handled = true;
                    break;
                case Key.Home:
                    SetNavIndex(0);
                    e.Handled = true;
                    break;
                case Key.End:
                    SetNavIndex(Slots.Count - 1);
                    e.Handled = true;
                    break;
                case Key.Return:
                case Key.Space:
                    if (nav < 0 || ChooseRequested is null)
                        break;
                    ChooseRequested.Invoke(this, nav);
                    AnnounceCommit(nav);
                    e.Handled = true;
                    break;
                case Key.Delete:
                case Key.Back:
                    //Choice mode: nothing to empty, so Delete/Backspace do nothing (2026-09-17 ruling).
                    if (IsChoiceMode || SelectedIndex < 0 || Cleared is null)
                        break;
                    Cleared.Invoke(this, SelectedIndex);
                    e.Handled = true;
                    break;
            }
        }

        //Type-to-select by entity name (choice mode only, 2026-09-17 ruling): moves focus, never commits.
        protected override void OnTextInput(TextInputEventArgs e) {
            base.OnTextInput(e);
            if (!IsChoiceMode || string.IsNullOrEmpty(e.Text) || Slots.Count == 0)
                return;

            long now = Environment.TickCount64;
            typeBuffer = now - lastTypeTicks > 1000 ? e.Text : typeBuffer + e.Text;
            lastTypeTicks = now;

            int start = Math.Max(NavIndex, 0);
            for (int offset = 0; offset <= Slots.Count; offset++) {
                int candidate = (start + offset) % Slots.Count;
                if (Slots[candidate].Name is { } name && name.StartsWith(typeBuffer, StringComparison.OrdinalIgnoreCase)) {
                    SetNavIndex(candidate);
                    e.Handled = true;
                    return;
                }
            }
        }

        //Rows come from real rendered Bounds (the choice track wraps freely), not a tracked column count.
        private void MoveVerticalChoice(int index, int direction) {
            if (index < 0 || cells.Count == 0) {
                SetNavIndex(0);
                return;
            }

            PickCell current = cells[index];
            double x = current.Bounds.X, y = current.Bounds.Y;
            List<int> candidates = [.. Enumerable.Range(0, cells.Count)
                .Where(i => direction > 0 ? cells[i].Bounds.Y > y + 1 : cells[i].Bounds.Y < y - 1)];
            if (candidates.Count == 0)
                return; //clamps at the grid's top/bottom edge

            double targetY = direction > 0 ? candidates.Min(i => cells[i].Bounds.Y) : candidates.Max(i => cells[i].Bounds.Y);
            int target = candidates.Where(i => Math.Abs(cells[i].Bounds.Y - targetY) <= 1)
                .OrderBy(i => Math.Abs(cells[i].Bounds.X - x)).First();
            SetNavIndex(target);
        }

        private void Subscribe() {
            Unsubscribe();
            subs.Add(this.GetResourceObservable("Kit.PickCell").Subscribe(new ResourceSinks.Sink<ControlTheme?>(v => { pickCellTheme = v; Rebuild(); }, null)));
            subs.Add(this.GetResourceObservable("Foreman.Thickness.PickCellGap").Subscribe(new ResourceSinks.ThicknessSink(v => { gap = v; Rebuild(); }, gap)));
        }

        private void Unsubscribe() {
            foreach (IDisposable sub in subs)
                sub.Dispose();
            subs.Clear();
        }

        private int EffectiveColumns() => Columns > 0 ? Math.Min(Columns, Math.Max(Slots.Count, 1)) : Math.Max(Slots.Count, 1);

        private void CoerceAndApplySelection(AvaloniaPropertyChangedEventArgs e) {
            int requested = (int)e.NewValue!;
            int clamped = Slots.Count == 0 ? -1 : Math.Clamp(requested, -1, Slots.Count - 1);
            if (clamped != requested) {
                SetCurrentValue(SelectedIndexProperty, clamped);
                return;
            }
            ApplySelection();
        }

        private void ApplySelection() {
            int focus = IsChoiceMode ? (focusIndex >= 0 ? focusIndex : SelectedIndex) : SelectedIndex;
            for (int i = 0; i < cells.Count; i++) {
                bool selected = i == SelectedIndex;
                cells[i].IsSelected = selected;
                FocusRing.SetShowAlways(cells[i], hasKeyboardFocus && i == focus);
            }
        }

        //Replacing one slot (the common solve-driven case: content changed, count didn't) updates that cell
        //in place instead of tearing down the whole track - a full Rebuild toggles IsVisible false mid-pass
        //(see below), which drops keyboard focus on whichever cell held it.
        private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            //Ahead of the untemplated return: an invisible subtree is never measured, so a grid that waited
            //for slots would never template and never reach the Rebuild that derives this.
            IsVisible = Slots.Count > 0;
            if (track is null && wrap is null)
                return;
            if (e.Action == NotifyCollectionChangedAction.Replace
                && e.NewItems?.Count == 1 && e.OldItems?.Count == 1
                && e.NewStartingIndex < cells.Count) {
                UpdateCell(e.NewStartingIndex, (PickSlot)e.NewItems[0]!);
                return;
            }
            Rebuild();
        }

        private void UpdateCell(int index, PickSlot slot) {
            PickCell cell = cells[index];
            cell.Icon = slot.Icon;
            cell.IsEmpty = IsChoiceMode ? false : slot.IsEmpty;
            cell.IsMissing = slot.IsMissing;
            cell.IsUnavailable = slot.IsUnavailable;
            cell.IsNone = slot.IsNone;
            cell.IsEnabled = slot.IsEnabled;
            cell.AccessibleName = slot.Name;
            ApplyCellAutomation(cell, slot, index);
        }

        private void ApplyCellAutomation(PickCell cell, PickSlot slot, int index) {
            string content = slot.IsEmpty && !IsChoiceMode ? "Empty" : slot.Name ?? "Empty";
            //A choice grid is never a set of slots by definition, regardless of NamesSlots' default.
            string position = !IsChoiceMode && NamesSlots ? $"slot {index + 1} of {Slots.Count}" : $"{index + 1} of {Slots.Count}";
            AutomationProperties.SetName(cell, $"{content}, {position}");
            AutomationProperties.SetHelpText(cell, IsChoiceMode ? BuildChoiceCellHelpText(slot) : BuildCellHelpText());
        }

        private string BuildCellHelpText() {
            string help = ChooseRequested is not null ? "Return chooses this item." : "";
            if (Cleared is not null)
                help += (help.Length > 0 ? " " : "") + "Delete removes it.";
            return help;
        }

        private static string BuildChoiceCellHelpText(PickSlot slot) =>
            slot.IsUnavailable && !string.IsNullOrEmpty(slot.Name) ? $"{slot.Name} — not available in this save" : "";

        private void Rebuild() {
            Panel? container = IsChoiceMode ? wrap : track;
            if (container is null)
                return;

            track?.Children.Clear();
            track?.ColumnDefinitions.Clear();
            track?.RowDefinitions.Clear();
            wrap?.Children.Clear();
            cells.Clear();
            focusIndex = -1;

            IsVisible = Slots.Count > 0;
            if (track is not null)
                track.IsVisible = !IsChoiceMode && Slots.Count > 0;
            if (wrap is not null)
                wrap.IsVisible = IsChoiceMode && Slots.Count > 0;

            if (Slots.Count == 0) {
                if (SelectedIndex >= 0)
                    SetCurrentValue(SelectedIndexProperty, -1);
                ApplyAutomationName();
                return;
            }

            int columns = EffectiveColumns();
            int rows = (int)Math.Ceiling(Slots.Count / (double)columns);
            if (!IsChoiceMode && track is not null) {
                for (int c = 0; c < columns; c++)
                    track.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                for (int r = 0; r < rows; r++)
                    track.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            }

            for (int i = 0; i < Slots.Count; i++) {
                PickSlot slot = Slots[i];
                var cell = new PickCell {
                    Icon = slot.Icon,
                    IsEmpty = IsChoiceMode ? false : slot.IsEmpty,
                    IsMissing = slot.IsMissing,
                    IsUnavailable = slot.IsUnavailable,
                    IsNone = slot.IsNone,
                    IsEnabled = slot.IsEnabled,
                    AccessibleName = slot.Name,
                    EmptyItemNoun = EmptyItemNoun,
                    IsChoiceItem = IsChoiceMode,
                    Margin = gap,
                };
                if (pickCellTheme is not null)
                    cell.Theme = pickCellTheme;

                int index = i;
                cell.PointerReleased += (_, args) => {
                    if (args.InitialPressMouseButton != MouseButton.Left)
                        return;
                    SetCurrentValue(SelectedIndexProperty, index);
                    Focus(NavigationMethod.Pointer);
                    CellClicked?.Invoke(this, index);
                    if (IsChoiceMode) {
                        focusIndex = index;
                        ChooseRequested?.Invoke(this, index);
                        AnnounceCommit(index);
                    }
                };
                cell.Invoked += (_, _) => ChooseRequested?.Invoke(this, index);

                ApplyCellAutomation(cell, slot, i);

                if (IsChoiceMode) {
                    wrap!.Children.Add(cell);
                } else {
                    Grid.SetRow(cell, i / columns);
                    Grid.SetColumn(cell, i % columns);
                    track!.Children.Add(cell);
                }
                cells.Add(cell);
            }

            //A Slots shrink can leave SelectedIndex pointing past the new end - clamp here too, not only on
            //an explicit SelectedIndex write, or a stale index defeats the keyboard-entry guard.
            if (SelectedIndex >= Slots.Count)
                SetCurrentValue(SelectedIndexProperty, Slots.Count - 1);
            else
                ApplySelection();
            ApplyAutomationName();
        }

        //Committing announces the new value (choice mode only, 2026-09-17 ruling) - an arrow key only moves
        //focus and never reaches here, so VoiceOver speaks a change exactly when the graph re-solves.
        private void AnnounceCommit(int index) {
            if (!IsChoiceMode || index < 0 || index >= Slots.Count)
                return;
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(this);
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, null, Slots[index].Name ?? "");
        }

        private void ApplyAutomationName() {
            if (IsChoiceMode) {
                AutomationProperties.SetName(this, GroupLabel ?? "");
                AutomationProperties.SetHelpText(this, SelectedIndex >= 0 && SelectedIndex < Slots.Count ? Slots[SelectedIndex].Name ?? "" : "");
                return;
            }
            int count = Slots.Count;
            string unit = NamesSlots ? "slot" : "item";
            AutomationProperties.SetName(this, $"{GroupLabel ?? "Slots"}, {count} {unit}{(count == 1 ? "" : "s")}");
        }

        private sealed class PickGridAutomationPeer(PickGrid owner) : ControlAutomationPeer(owner), ISelectionProvider {
            private PickGrid Grid => (PickGrid)Owner;

            protected override AutomationControlType GetAutomationControlTypeCore() =>
                Grid.IsChoiceMode ? AutomationControlType.List : AutomationControlType.Group;

            bool ISelectionProvider.CanSelectMultiple => false;
            bool ISelectionProvider.IsSelectionRequired => Grid.IsChoiceMode;

            IReadOnlyList<AutomationPeer> ISelectionProvider.GetSelection() {
                if (!Grid.IsChoiceMode || Grid.SelectedIndex < 0 || Grid.SelectedIndex >= Grid.cells.Count)
                    return [];
                return [ControlAutomationPeer.CreatePeerForElement(Grid.cells[Grid.SelectedIndex])];
            }
        }
    }
}
