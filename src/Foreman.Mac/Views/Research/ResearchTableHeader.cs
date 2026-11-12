using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;

namespace Foreman.Mac.Views.Research {
    internal sealed class ResearchTableHeader(Grid row, string namePrefix, string[] labels, HorizontalAlignment[] alignments, int columnOffset, Action sorted) {
        public int SortColumn { get; private set; }
        public bool SortAscending { get; private set; } = true;

        public void SortBy(int column) {
            if (SortColumn == column)
                SortAscending = !SortAscending;
            else {
                SortColumn = column;
                SortAscending = column == 0;
            }
            sorted();
        }

        public void Build(Control owner, ColumnDefinitions columns, double columnGap) {
            row.Children.Clear();
            row.ColumnDefinitions = columns;
            row.ColumnSpacing = columnGap;
            row.Margin = ResearchTableKit.RowInset(owner);
            for (int i = 0; i < labels.Length; i++) {
                int column = i;
                bool isSorted = SortColumn == i;
                string arrow = isSorted ? (SortAscending ? " ▴" : " ▾") : "";
                var label = new TextBlock {
                    Theme = ResearchTableKit.KitTheme("Kit.Text"), Classes = { "caption" }, Text = labels[i] + arrow, FontWeight = FontWeight.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = alignments[i],
                };
                if (!isSorted)
                    label.Classes.Add("secondary");
                var cell = new Border { Background = Brushes.Transparent, Child = label, Focusable = false, Name = namePrefix + i };
                AutomationProperties.SetName(cell, isSorted ? $"{labels[i]}, sorted {(SortAscending ? "ascending" : "descending")}" : labels[i]);
                cell.PointerPressed += (_, e) => {
                    if (e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) {
                        SortBy(column);
                        e.Handled = true;
                    }
                };
                Grid.SetColumn(cell, i + columnOffset);
                row.Children.Add(cell);
            }
        }
    }
}
