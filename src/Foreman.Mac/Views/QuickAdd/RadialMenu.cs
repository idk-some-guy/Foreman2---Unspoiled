using Avalonia.Automation;
using Foreman.Mac.Kit.Composites;
using Foreman.Mac.Kit.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.Mac.Views.QuickAdd {
    public enum RadialChoice { Add, Source, Output, Passthrough, Spoil, Unspoil, Plant, Unplant, Cancel }

    public sealed class RadialMenu {
        private const int SlotCount = 6;

        public RadialRing Ring { get; }

        public IReadOnlyList<RadialChoice?> Slots { get; }

        public IReadOnlyList<RadialChoice> Slices { get; }

        public event EventHandler<RadialChoice>? Chosen;
        public event EventHandler? Dismissed;
        public event EventHandler? MenuRequested;

        public RadialMenu(IReadOnlyList<RadialChoice> choices) {
            var slots = new RadialChoice?[SlotCount];
            foreach (RadialChoice choice in choices)
                slots[SlotOf(choice)] = choice;
            Slots = slots;
            Slices = [.. slots.OfType<RadialChoice>()];

            Ring = new RadialRing();
            AutomationProperties.SetName(Ring, "Link options");
            foreach (RadialChoice? slot in slots)
                Ring.Items.Add(slot is RadialChoice filled ? new RadialItem { GlyphKey = GlyphOf(filled), Label = LabelOf(filled) } : RadialItem.Empty);

            Ring.Committed += (_, index) => {
                if (slots[index] is RadialChoice choice)
                    Chosen?.Invoke(this, choice);
            };
            Ring.Dismissed += (_, _) => Dismissed?.Invoke(this, EventArgs.Empty);
            Ring.ContextMenuRequested += (_, _) => MenuRequested?.Invoke(this, EventArgs.Empty);
        }

        public void CommitArmed() {
            if (Ring.ArmedIndex is >= 0 and < SlotCount && Slots[Ring.ArmedIndex] is RadialChoice choice)
                Chosen?.Invoke(this, choice);
            else
                Dismissed?.Invoke(this, EventArgs.Empty);
        }

        public static int SlotOf(RadialChoice choice) => choice switch {
            RadialChoice.Add => 0,
            RadialChoice.Source or RadialChoice.Output => 1,
            RadialChoice.Passthrough => 2,
            RadialChoice.Spoil or RadialChoice.Unspoil => 3,
            RadialChoice.Plant or RadialChoice.Unplant => 4,
            _ => 5,
        };

        public static string LabelOf(RadialChoice choice) => choice switch {
            RadialChoice.Add => "Add…",
            RadialChoice.Source => "Source",
            RadialChoice.Output => "Output",
            RadialChoice.Passthrough => "Passthrough",
            RadialChoice.Spoil => "Spoil",
            RadialChoice.Unspoil => "Unspoil",
            RadialChoice.Plant => "Plant",
            RadialChoice.Unplant => "Unplant",
            _ => "Cancel",
        };

        private static string GlyphOf(RadialChoice choice) => choice switch {
            RadialChoice.Add => "RadialAdd",
            RadialChoice.Source => "RadialSource",
            RadialChoice.Output => "RadialOutput",
            RadialChoice.Passthrough => "Passthrough",
            RadialChoice.Spoil => "RadialSpoil",
            RadialChoice.Unspoil => "RadialUnspoil",
            RadialChoice.Plant => "RadialPlant",
            RadialChoice.Unplant => "RadialUnplant",
            _ => "RadialCancel",
        };

        public IReadOnlyList<MenuRow> FallbackRows(Action<RadialChoice> choose) {
            int[][] groups = [[0, 1, 2], [3, 4], [5]];
            var rows = new List<MenuRow>();
            foreach (int[] group in groups) {
                RadialChoice[] filled = [.. group.Select(i => Slots[i]).OfType<RadialChoice>()];
                if (filled.Length == 0)
                    continue;
                if (rows.Count > 0)
                    rows.Add(MenuRow.Separator());
                foreach (RadialChoice choice in filled)
                    rows.Add(new MenuRow { Text = LabelOf(choice), HasCheckSlot = false, Activate = () => choose(choice) });
            }
            return rows;
        }
    }
}
