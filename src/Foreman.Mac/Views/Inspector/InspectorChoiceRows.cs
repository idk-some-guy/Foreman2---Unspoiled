using Avalonia.Controls;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using Foreman.Mac.ViewModels;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using KitPickGrid = Foreman.Mac.Kit.Composites.PickGrid;

namespace Foreman.Mac.Views.Inspector {
    internal static class InspectorChoiceRows {
        internal static void UpdateOptionsAvailability(KitPickGrid equipped, KitPickGrid options) {
            int firstEmpty = FirstEmptySlotIndex(equipped);
            options.TargetIsReplace = equipped.SelectedIndex >= 0;
            options.TargetSlot = equipped.Slots.Count == 0 ? null
                : equipped.SelectedIndex >= 0 ? equipped.SelectedIndex + 1
                : firstEmpty >= 0 ? firstEmpty + 1
                : 0; //full and unarmed: no target
        }

        internal static void RetargetArmAfterDelete(KitPickGrid grid) => grid.ArmAfterClear(FirstEmptySlotIndex(grid));

        internal static int FirstEmptySlotIndex(KitPickGrid grid) {
            for (int i = 0; i < grid.Slots.Count; i++)
                if (grid.Slots[i].IsEmpty)
                    return i;
            return -1;
        }

        internal static void UpdateChoiceRow(Control owner, FormRow row, KitPickGrid grid, StackPanel valueLine, Image valueLineIcon, TextBlock valueLineText,
                PopUp fallback, IReadOnlyList<PickGridItem> options, PickGridItem? current, string emptyWarning, bool includeNone,
                string? statsSuffix = null, string? oneCandidateNote = null, string? noneName = null, bool orphanWithoutOptions = false) {
            Debug.Assert(!includeNone || noneName is not null, "A row with a None cell needs its label.");
            noneName ??= "";
            bool currentOrphaned = current is not null && (options.Count > 0 || orphanWithoutOptions) && !options.Any(o => o.IsSelected);
            int total = options.Count + (includeNone ? 1 : 0) + (currentOrphaned ? 1 : 0);
            row.Error = null;
            row.ErrorIsWarning = false;
            row.NoteIsWarning = false;
            row.NoteSuffix = null;
            row.NoteIsValue = true;

            if (total == 0) {
                grid.Slots.Clear();
                fallback.IsVisible = false;
                valueLine.IsVisible = true;
                valueLine.Opacity = UnavailableOpacity(owner);
                valueLineIcon.Source = current?.Icon;
                valueLineText.Text = current?.Name ?? "";
                row.Note = null;
                row.Error = emptyWarning;
                row.ErrorIsWarning = true;
                return;
            }

            if (total == 1) {
                grid.Slots.Clear();
                fallback.IsVisible = false;
                valueLine.IsVisible = true;
                valueLine.Opacity = 1;
                bool onlyNone = includeNone && options.Count == 0;
                valueLineIcon.Source = onlyNone ? null : options[0].Icon;
                valueLineText.Text = onlyNone ? noneName : options[0].Name;
                row.Note = oneCandidateNote;
                row.NoteSuffix = oneCandidateNote is not null && !string.IsNullOrEmpty(statsSuffix) ? statsSuffix : null;
                return;
            }

            valueLine.IsVisible = false;

            if (total > MaxChoiceCells(owner)) {
                grid.Slots.Clear();
                fallback.IsVisible = true;
                fallback.Icon = current?.Icon;
                fallback.Items.Clear();
                fallback.ItemIcons.Clear();
                if (includeNone) {
                    fallback.Items.Add(noneName);
                    fallback.ItemIcons.Add(null);
                }
                foreach (PickGridItem item in options) {
                    fallback.Items.Add(item.Name);
                    fallback.ItemIcons.Add(item.Icon);
                }
                fallback.SelectedItem = current?.Name ?? (includeNone ? noneName : null);
                row.NoteIsValue = false;
                row.Note = statsSuffix;
                return;
            }

            fallback.IsVisible = false;

            var desired = new List<PickSlot>(total);
            if (includeNone)
                desired.Add(new PickSlot { Name = noneName, IsNone = true });
            foreach (PickGridItem item in options)
                desired.Add(new PickSlot { Icon = item.Icon, Name = item.Name, IsMissing = item.IsMissing, IsUnavailable = !item.IsAvailable && !item.IsMissing });
            if (currentOrphaned)
                desired.Add(new PickSlot { Icon = current!.Icon, Name = current.Name, IsMissing = true });
            SyncChoiceSlots(grid, desired);

            int selected = includeNone && current is null ? 0 : -1;
            for (int i = 0; i < options.Count; i++)
                if (options[i].IsSelected)
                    selected = i + (includeNone ? 1 : 0);
            if (currentOrphaned)
                selected = desired.Count - 1;
            grid.SelectedIndex = selected;
            row.Note = selected >= 0 && selected < desired.Count ? desired[selected].Name : null;
            row.NoteIsWarning = currentOrphaned;
            row.NoteSuffix = !currentOrphaned && !string.IsNullOrEmpty(statsSuffix) ? statsSuffix : null;
        }

        internal static int MaxChoiceCells(Control owner) {
            owner.TryFindResource("Foreman.Metrics.PickChoiceMax", null, out object? value);
            return value is double d ? (int)d : 9;
        }

        internal static double UnavailableOpacity(Control owner) {
            owner.TryFindResource("Foreman.Opacity.Unavailable", owner.ActualThemeVariant, out object? value);
            return value is double d ? d : 0.55;
        }

        internal static void SyncSlots(KitPickGrid grid, IReadOnlyList<PickGridItem> items, int slotCount) {
            int total = Math.Max(slotCount, items.Count);
            for (int i = 0; i < total; i++) {
                PickSlot desired = i < items.Count ? BuildSlot(items[i]) : new PickSlot { IsEmpty = true };
                if (i < grid.Slots.Count) {
                    if (!SlotsEqual(grid.Slots[i], desired))
                        grid.Slots[i] = desired;
                } else {
                    grid.Slots.Add(desired);
                }
            }
            while (grid.Slots.Count > total)
                grid.Slots.RemoveAt(grid.Slots.Count - 1);
        }

        internal static void SyncChoiceSlots(KitPickGrid grid, List<PickSlot> desired) {
            for (int i = 0; i < desired.Count; i++) {
                if (i < grid.Slots.Count) {
                    if (!SlotsEqual(grid.Slots[i], desired[i]))
                        grid.Slots[i] = desired[i];
                } else {
                    grid.Slots.Add(desired[i]);
                }
            }
            while (grid.Slots.Count > desired.Count)
                grid.Slots.RemoveAt(grid.Slots.Count - 1);
        }

        internal static PickSlot BuildSlot(PickGridItem item) =>
            new() { Icon = item.Icon, Name = item.Name, IsMissing = !item.IsAvailable };

        internal static bool SlotsEqual(PickSlot a, PickSlot b) =>
            a.Icon == b.Icon && a.Name == b.Name && a.IsEmpty == b.IsEmpty && a.IsMissing == b.IsMissing
            && a.IsEnabled == b.IsEnabled && a.IsUnavailable == b.IsUnavailable && a.IsNone == b.IsNone;
    }
}
