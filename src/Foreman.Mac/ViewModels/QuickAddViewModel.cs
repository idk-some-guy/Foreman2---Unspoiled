using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Foreman.DataCaching.DataTypes;
using Foreman.Mac.Canvas;
using Foreman.Mac.Services;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Foreman.Mac.ViewModels {
    public sealed partial class QuickAddRow : ObservableObject {
        public QuickAddRow(AddCandidate candidate, string name, string kindLabel, string qualitySuffix, IImage? icon) {
            Candidate = candidate;
            Name = name;
            KindLabel = kindLabel;
            QualitySuffix = qualitySuffix;
            Icon = icon;
        }

        public AddCandidate Candidate { get; }
        public string Name { get; }
        public string KindLabel { get; }
        public string QualitySuffix { get; }
        public IImage? Icon { get; }
        public string Sub => KindLabel + QualitySuffix;

        [ObservableProperty]
        private bool isHighlighted;
    }

    public sealed partial class QuickAddViewModel : ObservableObject {
        private readonly AddCatalog catalog;
        private readonly AddRequest request;
        private readonly string qualitySuffix;
        private readonly Func<SKBitmap, IImage?> bakeIcon;
        private readonly ObservableCollection<QuickAddRow> rows = [];

        public QuickAddViewModel(AddCatalog catalog, AddRequest request, IQuality? quality, Func<SKBitmap, IImage?> bakeIcon) {
            this.catalog = catalog;
            this.request = request;
            this.bakeIcon = bakeIcon;
            qualitySuffix = catalog.ShowsQuality && quality is not null ? $" · {quality.FriendlyName}" : "";
            Rows = new ReadOnlyObservableCollection<QuickAddRow>(rows);
            query = request.SeedText;
            Refresh();
        }

        [ObservableProperty]
        private string query = "";

        [ObservableProperty]
        private int highlightedIndex = -1;

        public ReadOnlyObservableCollection<QuickAddRow> Rows { get; }
        public bool HasRows => rows.Count > 0;

        //FloatingPanelHost re-measures the palette on this (via QuickAddPalette's IPanelContentResize) -
        //Refresh() below is the only place the row count actually changes.
        public event EventHandler? RowsChanged;

        public string Watermark => request.Scope switch {
            AddScope.Items => "Search items…",
            AddScope.Recipes => "Search recipes…",
            _ => "Search…",
        };

        partial void OnQueryChanged(string value) => Refresh();
        partial void OnHighlightedIndexChanged(int value) => SyncHighlight();

        public void MoveHighlight(int delta) {
            if (rows.Count == 0) { HighlightedIndex = -1; return; }
            HighlightedIndex = ((HighlightedIndex + delta) % rows.Count + rows.Count) % rows.Count;
        }

        public AddCandidate? Commit() => HighlightedIndex >= 0 && HighlightedIndex < rows.Count ? rows[HighlightedIndex].Candidate : null;

        private void Refresh() {
            var desired = catalog.Search(Query, request.Scope, request.Link, limit: 200)
                .Select(c => new QuickAddRow(c, c.Name, c.KindLabel, c.Kind == AddKind.Recipe ? qualitySuffix : "", bakeIcon(c.Entry.Icon)))
                .ToList();
            SyncRows(desired);
            HighlightedIndex = rows.Count > 0 ? 0 : -1;
            SyncHighlight();
            OnPropertyChanged(nameof(HasRows));
            RowsChanged?.Invoke(this, EventArgs.Empty);
        }

        //Gate fix 11: a row that survives the query change keeps its own instance (Move, not
        //Remove+Add), so its container survives too and only genuinely new rows animate in.
        private void SyncRows(List<QuickAddRow> desired) {
            var desiredCandidates = new HashSet<AddCandidate>(desired.Select(r => r.Candidate));
            for (int i = rows.Count - 1; i >= 0; i--)
                if (!desiredCandidates.Contains(rows[i].Candidate))
                    rows.RemoveAt(i);

            for (int i = 0; i < desired.Count; i++) {
                int currentIndex = IndexOfCandidate(desired[i].Candidate);
                if (currentIndex == -1)
                    rows.Insert(i, desired[i]);
                else if (currentIndex != i)
                    rows.Move(currentIndex, i);
            }
        }

        private int IndexOfCandidate(AddCandidate candidate) {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Candidate.Equals(candidate))
                    return i;
            return -1;
        }

        //Also called directly from Refresh() - a same-value HighlightedIndex skips the change notification, which would otherwise leave fresh rows unmarked.
        private void SyncHighlight() {
            for (int i = 0; i < rows.Count; i++)
                rows[i].IsHighlighted = i == HighlightedIndex;
        }
    }
}
