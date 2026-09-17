using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Styling;
using Foreman.Mac.Kit.Controls;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Foreman.Mac.Kit.Composites {
    //A row (or, given Columns, a wrapped block) of PickCell slots; the grid is the sole tab stop, so arrow/Home/End/Return/Delete are all handled here.
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

        public event EventHandler<int>? ChooseRequested;
        public event EventHandler<int>? Cleared;

        //Fires on a pointer click specifically (never on keyboard/arrow selection) - the app's own equip
        //flow needs to tell "the user clicked this cell" apart from SelectedIndex simply changing.
        public event EventHandler<int>? CellClicked;

        private Grid? track;
        private readonly List<PickCell> cells = [];
        private ControlTheme? pickCellTheme;
        private Thickness gap = new(2.5);
        private bool hasKeyboardFocus;
        private readonly List<IDisposable> subs = [];

        static PickGrid() {
            FocusableProperty.OverrideDefaultValue<PickGrid>(true);
            ColumnsProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.Rebuild());
            SelectedIndexProperty.Changed.AddClassHandler<PickGrid>((g, e) => g.CoerceAndApplySelection(e));
            GroupLabelProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.ApplyAutomationName());
            NamesSlotsProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.Rebuild());
            EmptyItemNounProperty.Changed.AddClassHandler<PickGrid>((g, _) => g.Rebuild());
        }

        public PickGrid() {
            Slots = [];
            Slots.CollectionChanged += OnSlotsChanged;
            GotFocus += (_, e) => {
                if (e.NavigationMethod == NavigationMethod.Pointer)
                    return;
                hasKeyboardFocus = true;
                //Keyboard entry needs a cell to land on, or the rings draw nothing and Return/Delete aim at nothing.
                if (SelectedIndex < 0 && Slots.Count > 0)
                    SetCurrentValue(SelectedIndexProperty, 0);
                ApplySelection();
            };
            LostFocus += (_, _) => {
                hasKeyboardFocus = false;
                ApplySelection();
            };
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e) {
            base.OnApplyTemplate(e);
            track = e.NameScope.Find<Grid>("PART_Grid");
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

        protected override void OnKeyDown(KeyEventArgs e) {
            base.OnKeyDown(e);
            if (Slots.Count == 0)
                return;

            int columns = EffectiveColumns();
            switch (e.Key) {
                case Key.Left:
                    SetCurrentValue(SelectedIndexProperty, SelectedIndex < 0 ? 0 : Math.Max(SelectedIndex - 1, 0));
                    e.Handled = true;
                    break;
                case Key.Right:
                    SetCurrentValue(SelectedIndexProperty, SelectedIndex < 0 ? 0 : Math.Min(SelectedIndex + 1, Slots.Count - 1));
                    e.Handled = true;
                    break;
                case Key.Up: {
                    int target = SelectedIndex < 0 ? 0 : SelectedIndex - columns;
                    if (target >= 0)
                        SetCurrentValue(SelectedIndexProperty, target);
                    e.Handled = true;
                    break;
                }
                case Key.Down: {
                    int target = SelectedIndex < 0 ? 0 : SelectedIndex + columns;
                    if (target < Slots.Count)
                        SetCurrentValue(SelectedIndexProperty, target);
                    e.Handled = true;
                    break;
                }
                case Key.Home:
                    SetCurrentValue(SelectedIndexProperty, 0);
                    e.Handled = true;
                    break;
                case Key.End:
                    SetCurrentValue(SelectedIndexProperty, Slots.Count - 1);
                    e.Handled = true;
                    break;
                case Key.Return:
                case Key.Space:
                    if (SelectedIndex < 0 || ChooseRequested is null)
                        break;
                    ChooseRequested.Invoke(this, SelectedIndex);
                    e.Handled = true;
                    break;
                case Key.Delete:
                case Key.Back:
                    if (SelectedIndex < 0 || Cleared is null)
                        break;
                    Cleared.Invoke(this, SelectedIndex);
                    e.Handled = true;
                    break;
            }
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
            for (int i = 0; i < cells.Count; i++) {
                bool selected = i == SelectedIndex;
                cells[i].IsSelected = selected;
                FocusRing.SetShowAlways(cells[i], selected && hasKeyboardFocus);
            }
        }

        //Replacing one slot (the common solve-driven case: content changed, count didn't) updates that cell
        //in place instead of tearing down the whole track - a full Rebuild toggles IsVisible false mid-pass
        //(see below), which drops keyboard focus on whichever cell held it.
        private void OnSlotsChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (track is null)
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
            cell.IsEmpty = slot.IsEmpty;
            cell.IsMissing = slot.IsMissing;
            cell.IsEnabled = slot.IsEnabled;
            cell.AccessibleName = slot.Name;
            ApplyCellAutomation(cell, slot, index);
        }

        private void ApplyCellAutomation(PickCell cell, PickSlot slot, int index) {
            string content = slot.IsEmpty ? "Empty" : slot.Name ?? "Empty";
            string position = NamesSlots ? $"slot {index + 1} of {Slots.Count}" : $"{index + 1} of {Slots.Count}";
            AutomationProperties.SetName(cell, $"{content}, {position}");
            AutomationProperties.SetHelpText(cell, BuildCellHelpText());
        }

        private string BuildCellHelpText() {
            string help = ChooseRequested is not null ? "Return chooses this item." : "";
            if (Cleared is not null)
                help += (help.Length > 0 ? " " : "") + "Delete removes it.";
            return help;
        }

        private void Rebuild() {
            if (track is null)
                return;

            track.Children.Clear();
            track.ColumnDefinitions.Clear();
            track.RowDefinitions.Clear();
            cells.Clear();

            IsVisible = Slots.Count > 0;
            if (Slots.Count == 0) {
                ApplyAutomationName();
                return;
            }

            int columns = EffectiveColumns();
            int rows = (int)Math.Ceiling(Slots.Count / (double)columns);
            for (int c = 0; c < columns; c++)
                track.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            for (int r = 0; r < rows; r++)
                track.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int i = 0; i < Slots.Count; i++) {
                PickSlot slot = Slots[i];
                var cell = new PickCell {
                    Icon = slot.Icon,
                    IsEmpty = slot.IsEmpty,
                    IsMissing = slot.IsMissing,
                    IsEnabled = slot.IsEnabled,
                    AccessibleName = slot.Name,
                    EmptyItemNoun = EmptyItemNoun,
                    Margin = gap,
                };
                if (pickCellTheme is not null)
                    cell.Theme = pickCellTheme;

                int index = i;
                cell.PointerReleased += (_, args) => {
                    if (args.InitialPressMouseButton == MouseButton.Left) {
                        SetCurrentValue(SelectedIndexProperty, index);
                        Focus(NavigationMethod.Pointer);
                        CellClicked?.Invoke(this, index);
                    }
                };
                cell.Invoked += (_, _) => ChooseRequested?.Invoke(this, index);

                ApplyCellAutomation(cell, slot, i);

                Grid.SetRow(cell, i / columns);
                Grid.SetColumn(cell, i % columns);
                track.Children.Add(cell);
                cells.Add(cell);
            }

            ApplySelection();
            ApplyAutomationName();
        }

        private void ApplyAutomationName() {
            int count = Slots.Count;
            string unit = NamesSlots ? "slot" : "item";
            AutomationProperties.SetName(this, $"{GroupLabel ?? "Slots"}, {count} {unit}{(count == 1 ? "" : "s")}");
        }

        private sealed class PickGridAutomationPeer(PickGrid owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        }
    }
}
