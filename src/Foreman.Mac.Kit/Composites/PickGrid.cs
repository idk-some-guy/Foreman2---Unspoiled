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
    public class PickGrid : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(PickGrid);

        public static readonly StyledProperty<int> ColumnsProperty =
            AvaloniaProperty.Register<PickGrid, int>(nameof(Columns));

        public static readonly StyledProperty<int> SelectedIndexProperty =
            AvaloniaProperty.Register<PickGrid, int>(nameof(SelectedIndex), -1);

        public static readonly StyledProperty<int> FocusedIndexProperty =
            AvaloniaProperty.Register<PickGrid, int>(nameof(FocusedIndex), -1);

        public static readonly StyledProperty<string?> GroupLabelProperty =
            AvaloniaProperty.Register<PickGrid, string?>(nameof(GroupLabel), "Slots");

        public static readonly StyledProperty<bool> NamesSlotsProperty =
            AvaloniaProperty.Register<PickGrid, bool>(nameof(NamesSlots), true);

        public static readonly StyledProperty<string?> EmptyItemNounProperty =
            AvaloniaProperty.Register<PickGrid, string?>(nameof(EmptyItemNoun));

        public static readonly StyledProperty<bool> IsChoiceModeProperty =
            AvaloniaProperty.Register<PickGrid, bool>(nameof(IsChoiceMode));

        public static readonly StyledProperty<int?> TargetSlotProperty =
            AvaloniaProperty.Register<PickGrid, int?>(nameof(TargetSlot));

        public static readonly StyledProperty<bool> TargetIsReplaceProperty =
            AvaloniaProperty.Register<PickGrid, bool>(nameof(TargetIsReplace));

        public static readonly DirectProperty<PickGrid, string> ArmingNoteProperty =
            AvaloniaProperty.RegisterDirect<PickGrid, string>(nameof(ArmingNote), g => g.armingNote);

        public AvaloniaList<PickSlot> Slots { get; }

        public int Columns {
            get => GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        public int SelectedIndex {
            get => GetValue(SelectedIndexProperty);
            set => SetValue(SelectedIndexProperty, value);
        }

        public int FocusedIndex {
            get => GetValue(FocusedIndexProperty);
            set => SetValue(FocusedIndexProperty, value);
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

        public int? TargetSlot {
            get => GetValue(TargetSlotProperty);
            set => SetValue(TargetSlotProperty, value);
        }

        public bool TargetIsReplace {
            get => GetValue(TargetIsReplaceProperty);
            set => SetValue(TargetIsReplaceProperty, value);
        }

        public string ArmingNote {
            get => armingNote;
            private set => SetAndRaise(ArmingNoteProperty, ref armingNote, value);
        }

        public event EventHandler<int>? ChooseRequested;
        public event EventHandler<int>? Cleared;

        public event EventHandler<int>? CellClicked;

        public void ArmAfterClear(int index) {
            hostArmedAfterClear = true;
            SetCurrentValue(SelectedIndexProperty, index >= 0 && index < Slots.Count ? index : -1);
        }

        private Grid? track;
        private WrapPanel? wrap;
        private readonly List<PickCell> cells = [];
        private ControlTheme? pickCellTheme;
        private Thickness gap = new(2.5);
        private bool hasKeyboardFocus;
        private bool hostArmedAfterClear;
        private string armingNote = "";
        private string typeBuffer = "";
        private long lastTypeTicks;
        private readonly List<IDisposable> subs = [];

        static PickGrid() {
            FocusableProperty.OverrideDefaultValue<PickGrid>(true);
            ColumnsProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.Rebuild());
            SelectedIndexProperty.Changed.AddClassHandler<PickGrid>((g, e) => g.CoerceAndApplySelection(e));
            FocusedIndexProperty.Changed.AddClassHandler<PickGrid>((g, e) => g.CoerceAndApplyFocus(e));
            GroupLabelProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.ApplyAutomationName());
            NamesSlotsProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.Rebuild());
            EmptyItemNounProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.Rebuild());
            IsChoiceModeProperty.Changed.AddClassHandler<PickGrid>((g, _) => {
                g.PseudoClasses.Set(":choice", g.IsChoiceMode);
                g.Rebuild();
            });
            TargetSlotProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.RefreshOptionCellState());
            TargetIsReplaceProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.RefreshOptionCellState());
        }

        public PickGrid() {
            Slots = [];
            Slots.CollectionChanged += OnSlotsChanged;
            GotFocus += (_, e) => {
                if (e.NavigationMethod == NavigationMethod.Pointer)
                    return;
                hasKeyboardFocus = true;
                if (IsChoiceMode) {
                    SetCurrentValue(FocusedIndexProperty, SelectedIndex >= 0 ? SelectedIndex : (Slots.Count > 0 ? 0 : -1));
                } else {
                    int candidate = FocusedIndex >= 0 && FocusedIndex < Slots.Count ? FocusedIndex : (Slots.Count > 0 ? 0 : -1);
                    SetCurrentValue(FocusedIndexProperty, candidate);
                }
                ApplySelection();
            };
            LostFocus += (_, _) => {
                hasKeyboardFocus = false;
                if (IsChoiceMode)
                    SetCurrentValue(FocusedIndexProperty, -1);
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

        private int NavIndex => FocusedIndex;

        private void SetNavIndex(int index) => SetCurrentValue(FocusedIndexProperty, index);

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
                    HandleCommitKey(e, nav);
                    break;
                case Key.Delete:
                case Key.Back:
                    if (IsChoiceMode || nav < 0 || Cleared is null)
                        break;
                    hostArmedAfterClear = false;
                    Cleared.Invoke(this, nav);
                    if (NamesSlots && !hostArmedAfterClear)
                        SetCurrentValue(SelectedIndexProperty, nav);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (IsChoiceMode || !NamesSlots)
                        break;
                    if (SelectedIndex >= 0) {
                        SetCurrentValue(SelectedIndexProperty, -1);
                    } else {
                        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null, NavigationMethod.Unspecified);
                    }
                    e.Handled = true;
                    break;
            }
        }

        private void HandleCommitKey(KeyEventArgs e, int nav) {
            if (IsChoiceMode) {
                if (nav < 0 || ChooseRequested is null)
                    return;
                ChooseRequested.Invoke(this, nav);
                AnnounceCommit(nav);
                e.Handled = true;
                return;
            }
            if (nav < 0)
                return;
            if (NamesSlots) {
                if (e.Key == Key.Space) {
                    SetCurrentValue(SelectedIndexProperty, SelectedIndex == nav ? -1 : nav);
                } else {
                    SetCurrentValue(SelectedIndexProperty, nav);
                    ChooseRequested?.Invoke(this, nav);
                }
            } else {
                CommitOptionCell(nav);
            }
            e.Handled = true;
        }

        private void CommitOptionCell(int index) {
            if (TargetSlot == 0)
                return;
            SetCurrentValue(SelectedIndexProperty, index);
            SetCurrentValue(FocusedIndexProperty, index);
            CellClicked?.Invoke(this, index);
        }

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
                return;

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

        private void CoerceAndApplyFocus(AvaloniaPropertyChangedEventArgs e) {
            int requested = (int)e.NewValue!;
            int clamped = Slots.Count == 0 ? -1 : Math.Clamp(requested, -1, Slots.Count - 1);
            if (clamped != requested) {
                SetCurrentValue(FocusedIndexProperty, clamped);
                return;
            }
            ApplySelection();
        }

        private void ApplySelection() {
            for (int i = 0; i < cells.Count; i++) {
                cells[i].IsSelected = i == SelectedIndex;
                FocusRing.SetShowAlways(cells[i], hasKeyboardFocus && i == FocusedIndex);
            }
            UpdateArmingNote();
        }

        private void UpdateArmingNote() {
            string previous = armingNote;
            if (IsChoiceMode || !NamesSlots || Slots.Count == 0) {
                ArmingNote = "";
            } else {
                string noun = string.IsNullOrEmpty(EmptyItemNoun) ? "item" : EmptyItemNoun;
                int slotCount = Slots.Count;
                int filled = Slots.Count(s => !s.IsEmpty);
                if (SelectedIndex >= 0)
                    ArmingNote = $"Replacing slot {SelectedIndex + 1}. Press Escape to cancel.";
                else if (filled == 0)
                    ArmingNote = $"No {noun}s. The next {noun} fills slot 1.";
                else if (filled >= slotCount)
                    ArmingNote = $"All {slotCount} slots full. Select a slot to replace.";
                else
                    ArmingNote = $"{filled} of {slotCount} slots filled. The next {noun} fills slot {FirstEmptyIndex() + 1}.";
            }
            if (!IsChoiceMode && NamesSlots)
                AutomationProperties.SetHelpText(this, armingNote);
            if (armingNote != previous)
                AnnounceArmingNote();
        }

        private int FirstEmptyIndex() {
            for (int i = 0; i < Slots.Count; i++)
                if (Slots[i].IsEmpty)
                    return i;
            return -1;
        }

        private void AnnounceArmingNote() {
            AutomationPeer peer = ControlAutomationPeer.CreatePeerForElement(this);
            peer.RaisePropertyChangedEvent(AutomationElementIdentifiers.HelpTextProperty, null, armingNote);
        }

        private void RefreshOptionCellState() {
            if (IsChoiceMode || NamesSlots)
                return;
            for (int i = 0; i < cells.Count && i < Slots.Count; i++)
                ApplyCellAutomation(cells[i], Slots[i], i);
        }

        private string BuildOptionCellHelpText() => TargetSlot switch {
            null => BuildCellHelpText(),
            0 => PickCell.FullAndUnarmedMessage,
            int slot => TargetIsReplace ? $"Replaces slot {slot}" : $"Fills slot {slot}",
        };

        //A full Rebuild toggles IsVisible false mid-pass and drops keyboard focus, so one replaced slot updates its cell in place.
        private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
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
            cell.Swatch = slot.Swatch;
            cell.IsEnabled = slot.IsEnabled;
            cell.AccessibleName = slot.Name;
            ApplyCellAutomation(cell, slot, index);
            UpdateArmingNote();
        }

        private void ApplyCellAutomation(PickCell cell, PickSlot slot, int index) {
            string content = slot.IsEmpty && !IsChoiceMode ? "Empty" : slot.Name ?? "Empty";
            string position = !IsChoiceMode && NamesSlots ? $"slot {index + 1} of {Slots.Count}" : $"{index + 1} of {Slots.Count}";
            AutomationProperties.SetName(cell, $"{content}, {position}");
            AutomationProperties.SetHelpText(cell, IsChoiceMode ? BuildChoiceCellHelpText(slot)
                : !NamesSlots ? BuildOptionCellHelpText()
                : BuildCellHelpText());
            if (!IsChoiceMode && !NamesSlots)
                cell.IsFullAndUnarmed = TargetSlot == 0;
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
            SetCurrentValue(FocusedIndexProperty, -1);

            IsVisible = Slots.Count > 0;
            if (track is not null)
                track.IsVisible = !IsChoiceMode && Slots.Count > 0;
            if (wrap is not null)
                wrap.IsVisible = IsChoiceMode && Slots.Count > 0;

            if (Slots.Count == 0) {
                if (SelectedIndex >= 0)
                    SetCurrentValue(SelectedIndexProperty, -1);
                UpdateArmingNote();
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
                    Swatch = slot.Swatch,
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
                    if (IsChoiceMode) {
                        SetCurrentValue(SelectedIndexProperty, index);
                        SetCurrentValue(FocusedIndexProperty, index);
                        Focus(NavigationMethod.Pointer);
                        CellClicked?.Invoke(this, index);
                        ChooseRequested?.Invoke(this, index);
                        AnnounceCommit(index);
                    } else if (NamesSlots) {
                        SetCurrentValue(SelectedIndexProperty, SelectedIndex == index ? -1 : index);
                        SetCurrentValue(FocusedIndexProperty, index);
                        Focus(NavigationMethod.Pointer);
                        CellClicked?.Invoke(this, index);
                    } else {
                        Focus(NavigationMethod.Pointer);
                        CommitOptionCell(index);
                    }
                };
                cell.Invoked += (_, _) => {
                    if (IsChoiceMode) {
                        ChooseRequested?.Invoke(this, index);
                    } else if (NamesSlots) {
                        SetCurrentValue(SelectedIndexProperty, index);
                        ChooseRequested?.Invoke(this, index);
                    } else {
                        CommitOptionCell(index);
                    }
                };

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

            if (SelectedIndex >= Slots.Count)
                SetCurrentValue(SelectedIndexProperty, Slots.Count - 1);
            else
                ApplySelection();
            ApplyAutomationName();
        }

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

        private sealed class PickGridAutomationPeer(PickGrid owner) : ControlAutomationPeer(owner), ISelectionProvider, IValueProvider {
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

            bool IValueProvider.IsReadOnly => true;
            string? IValueProvider.Value => Grid.ArmingNote;
            void IValueProvider.SetValue(string? value) { }
        }
    }
}
