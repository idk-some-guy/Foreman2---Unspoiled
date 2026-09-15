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

namespace Foreman.Mac.Kit.Composites {
    //A row (or, given Columns, a wrapped block) of PickCell slots. The grid is the sole tab stop - cells
    //never receive real focus - so arrow/Home/End/Return/Delete are all handled here and the currently
    //"current" slot is both the inset-ring selection and, only while the grid itself holds keyboard focus,
    //the outer FocusRing target (addendum §13 delta 1: the two marks are independent and can compose).
    public class PickGrid : TemplatedControl {
        protected override Type StyleKeyOverride => typeof(PickGrid);

        public static readonly StyledProperty<int> ColumnsProperty =
            AvaloniaProperty.Register<PickGrid, int>(nameof(Columns));

        public static readonly StyledProperty<int> SelectedIndexProperty =
            AvaloniaProperty.Register<PickGrid, int>(nameof(SelectedIndex));

        public static readonly StyledProperty<string?> GroupLabelProperty =
            AvaloniaProperty.Register<PickGrid, string?>(nameof(GroupLabel), "Slots");

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

        public event EventHandler<int>? ChooseRequested;
        public event EventHandler<int>? Cleared;

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
        }

        public PickGrid() {
            Slots = [];
            Slots.CollectionChanged += (_, _) => Rebuild();
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
                    SetCurrentValue(SelectedIndexProperty, Math.Max(SelectedIndex - 1, 0));
                    e.Handled = true;
                    break;
                case Key.Right:
                    SetCurrentValue(SelectedIndexProperty, Math.Min(SelectedIndex + 1, Slots.Count - 1));
                    e.Handled = true;
                    break;
                case Key.Up: {
                    int target = SelectedIndex - columns;
                    if (target >= 0)
                        SetCurrentValue(SelectedIndexProperty, target);
                    e.Handled = true;
                    break;
                }
                case Key.Down: {
                    int target = SelectedIndex + columns;
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
                    ChooseRequested?.Invoke(this, SelectedIndex);
                    e.Handled = true;
                    break;
                case Key.Delete:
                case Key.Back:
                    Cleared?.Invoke(this, SelectedIndex);
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
            int clamped = Slots.Count == 0 ? 0 : Math.Clamp((int)e.NewValue!, 0, Slots.Count - 1);
            if (clamped != (int)e.NewValue!) {
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
                    Margin = gap,
                };
                if (pickCellTheme is not null)
                    cell.Theme = pickCellTheme;

                int index = i;
                cell.PointerReleased += (_, args) => {
                    if (args.InitialPressMouseButton == MouseButton.Left) {
                        SetCurrentValue(SelectedIndexProperty, index);
                        Focus(NavigationMethod.Pointer);
                    }
                };
                cell.Invoked += (_, _) => ChooseRequested?.Invoke(this, index);

                string content = slot.IsEmpty ? "Empty" : slot.Name ?? "Empty";
                AutomationProperties.SetName(cell, $"{content}, slot {i + 1} of {Slots.Count}");
                AutomationProperties.SetHelpText(cell, "Return chooses a module. Delete removes it.");

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
            AutomationProperties.SetName(this, $"{GroupLabel ?? "Slots"}, {count} slot{(count == 1 ? "" : "s")}");
        }

        private sealed class PickGridAutomationPeer(PickGrid owner) : ControlAutomationPeer(owner) {
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        }
    }
}
