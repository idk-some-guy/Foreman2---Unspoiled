using Foreman.DataCaching;
using Foreman.Models.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Models.Research {
    internal static class ResearchSync {
        public static Task<ResearchRead> ReadAsync(string savePath, CancellationToken token, Func<string, CancellationToken, SaveFileReader.Result>? load = null, TimeProvider? clock = null) {
            Func<string, CancellationToken, SaveFileReader.Result> loader = load ?? SaveFileReader.Load;
            TimeProvider time = clock ?? TimeProvider.System;
            return Task.Run(() => {
                if (token.IsCancellationRequested)
                    return new ResearchRead { Outcome = SaveFileLoadOutcome.Cancel };

                SaveFileReader.Result result = loader(savePath, token);

                SaveFileInfo? info = result.SaveFileInfo;
                ResearchState? state = null;
                if (result.Outcome == SaveFileLoadOutcome.Ok && result.Failure == FactorioRunFailure.None && info is not null)
                    state = ResearchStateMapper.FromSave(info, savePath, time.GetUtcNow().UtcDateTime);

                FactorioGameVersion? version = null;
                if (info is not null && info.Mods.TryGetValue("base", out string? baseVersion) && FactorioGameVersion.TryParse(baseVersion, out FactorioGameVersion parsed))
                    version = parsed;

                return new ResearchRead {
                    Outcome = result.Outcome,
                    Failure = result.Failure,
                    CrashPhase = result.CrashPhase,
                    InstallCheck = result.InstallCheck,
                    State = state,
                    SaveMods = info is null ? [] : new Dictionary<string, string>(info.Mods),
                    SaveGameVersion = version,
                };
            }, CancellationToken.None);
        }

        public static ResearchSyncReport Apply(ProductionGraph graph, ResearchState read, bool clearTypedBonuses) {
            bool firstSync = graph.Research.Source is null && graph.Research.HandEditCount == 0;
            IReadOnlyList<ResearchChange> changes = firstSync ? [] : Changes(graph.Research, read);

            int cleared = 0;
            if (clearTypedBonuses) {
                foreach (RecipeNode node in NodesWithTypedProductivity(graph)) {
                    node.ExtraProductivityBonus = 0;
                    cleared++;
                }
            }

            graph.Research = read;

            return new ResearchSyncReport {
                FirstSync = firstSync,
                Changes = changes,
                ReplacedEditCount = changes.Count(change => change.ReplacedEdit),
                BonusCount = read.Values.Count,
                RecipeCount = read.RecipeProductivity.Count,
                TechnologyCount = read.Technologies.Count,
                ClearedNodeCount = cleared,
            };
        }

        public static IReadOnlyList<RecipeNode> NodesWithTypedProductivity(ProductionGraph graph) =>
            [.. graph.Nodes.OfType<RecipeNode>().Where(node => node.ExtraProductivityBonus > 0)];

        public static IReadOnlyList<ResearchChange> Changes(ResearchState before, ResearchState after) {
            var changes = new List<ResearchChange>();
            foreach (ResearchField field in ResearchFields.All) {
                double? old = before.Get(field);
                double? current = after.Get(field);
                if (old != current)
                    changes.Add(new ResearchChange(field, null, old, current, before.IsHandEdited(field)));
            }

            foreach (string recipe in before.RecipeProductivity.Keys.Union(after.RecipeProductivity.Keys).Order(StringComparer.Ordinal)) {
                double? old = before.RecipeProductivity.TryGetValue(recipe, out double o) ? o : null;
                double? current = after.RecipeProductivity.TryGetValue(recipe, out double n) ? n : null;
                if (old != current)
                    changes.Add(new ResearchChange(null, recipe, old, current, before.IsRecipeHandAdded(recipe) || before.IsRecipeHandEdited(recipe)));
            }
            return changes;
        }
    }
}
