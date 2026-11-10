using System.Collections.Generic;

namespace Foreman.Models.Research {
    internal sealed class ResearchSyncReport {
        public required bool FirstSync { get; init; }
        public required IReadOnlyList<ResearchChange> Changes { get; init; }
        public required int ReplacedEditCount { get; init; }
        public required int BonusCount { get; init; }
        public required int RecipeCount { get; init; }
        public required int TechnologyCount { get; init; }
        public required int ClearedNodeCount { get; init; }
    }
}
