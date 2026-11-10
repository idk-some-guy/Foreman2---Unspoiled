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
    public sealed class QuickAddRow {
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
    }

    public sealed partial class QuickAddViewModel : ObservableObject {
        public const string BrowseAllLabel = "Browse All…";
        public const string RemoveConstraintLabel = "Remove Constraint";
        public const string EmptyHeadline = "Search to add a node";
        public const string EmptySentence = "Type a few letters of a recipe or an item, then press Return to place it.";
        private const int QuotedQueryLimit = 24;

        private readonly AddCatalog catalog;
        private readonly string qualitySuffix;
        private readonly string lockedQualitySuffix;
        private readonly Func<SKBitmap, IImage?> bakeIcon;
        private readonly ObservableCollection<QuickAddRow> rows = [];
        private AddRequest request;

        public QuickAddViewModel(AddCatalog catalog, AddRequest request, IQuality? quality, Func<SKBitmap, IImage?> bakeIcon) {
            this.catalog = catalog;
            this.request = request;
            this.bakeIcon = bakeIcon;
            qualitySuffix = catalog.ShowsQuality && quality is not null ? $" · {quality.FriendlyName}" : "";
            lockedQualitySuffix = catalog.ShowsQuality ? $" · {catalog.LowestQuality.FriendlyName} only" : "";
            Rows = new ReadOnlyObservableCollection<QuickAddRow>(rows);
            query = request.SeedText;
            Refresh();
        }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(NoResultsHeadline), nameof(NoResultsSentence))]
        private string query = "";

        public ReadOnlyObservableCollection<QuickAddRow> Rows { get; }

        public event EventHandler? RowsChanged;
        public event EventHandler? ConstraintChanged;

        public AddLinkContext? Link => request.Link;
        private string? LinkItemName => request.Link?.Item.Item?.FriendlyName;

        private string? LinkVerb => request.Link?.NodeType switch {
            NewNodeType.Supplier => "make",
            NewNodeType.Consumer => "take",
            _ => null,
        };

        public string? ConstraintText => LinkItemName is string name ? $"Compatible with {name}" : null;

        public string Watermark => LinkVerb is string verb && LinkItemName is string name ? $"Search recipes that {verb} {name}"
            : request.Scope switch {
                AddScope.Items => "Search items…",
                AddScope.Recipes => "Search recipes…",
                _ => "Search…",
            };

        public string NoResultsHeadline => LinkVerb is string verb && LinkItemName is string name && Query.Length == 0
            ? $"No recipes {verb} {name}"
            : $"No matches for “{Quoted(Query)}”";

        public string NoResultsSentence => LinkVerb is string verb && LinkItemName is string name
            ? Query.Length == 0 ? $"No recipes {verb} {name}" : $"Only recipes that {verb} {name} are listed while a link is being dragged."
            : "Nothing in this preset matches the search.";

        private static string Quoted(string text) => text.Length > QuotedQueryLimit ? text[..QuotedQueryLimit] + "…" : text;

        partial void OnQueryChanged(string value) => Refresh();

        public void RemoveConstraint() {
            if (request.Link is null)
                return;
            request = request with { Link = null };
            OnPropertyChanged(nameof(Link));
            OnPropertyChanged(nameof(ConstraintText));
            OnPropertyChanged(nameof(Watermark));
            OnPropertyChanged(nameof(NoResultsHeadline));
            OnPropertyChanged(nameof(NoResultsSentence));
            ConstraintChanged?.Invoke(this, EventArgs.Empty);
            Refresh();
        }

        private void Refresh() {
            var desired = catalog.Search(Query, request.Scope, request.Link, limit: 200)
                .Select(c => new QuickAddRow(c, c.Name, c.KindLabel, c.Kind != AddKind.Recipe ? "" : c.Recipe is { CanSetQuality: false } ? lockedQualitySuffix : qualitySuffix, bakeIcon(c.Entry.Icon)))
                .ToList();
            SyncRows(desired);
            RowsChanged?.Invoke(this, EventArgs.Empty);
        }

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
    }
}
