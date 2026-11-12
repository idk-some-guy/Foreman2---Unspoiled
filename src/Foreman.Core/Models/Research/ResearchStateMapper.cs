using Foreman.DataCaching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Foreman.Models.Research {
    public static class ResearchStateMapper {
        public static ResearchState FromSave(SaveFileInfo info, string savePath, DateTime syncedAtUtc) {
            var source = new ResearchSource(Path.GetFileNameWithoutExtension(savePath), savePath, info.TicksPlayed, syncedAtUtc);

            var synced = new Dictionary<ResearchField, double>();
            foreach (ResearchField field in ResearchFields.All)
                if (info.ResearchBonuses.TryGetValue(ResearchFields.ExportKey(field), out double value))
                    synced[field] = value;

            var recipes = new Dictionary<string, double>(info.RecipeProductivity);
            List<ResearchTechnology> technologies = [.. info.Technologies
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => MapTechnology(pair.Key, pair.Value, info.TechnologyLevels))];

            return new ResearchState(source, new Dictionary<ResearchField, double>(synced), synced, new Dictionary<string, double>(recipes), recipes, technologies);
        }

        private static ResearchTechnology MapTechnology(string name, bool researched, Dictionary<string, SaveTechnologyLevel> levels) {
            if (!levels.TryGetValue(name, out SaveTechnologyLevel level))
                return new ResearchTechnology(name, researched, researched ? 1 : 0, 1, 1);

            int maxLevel = level.MaxLevel >= int.MaxValue ? ResearchTechnology.Unlimited : (int)level.MaxLevel;
            int levelsDone = researched && maxLevel != ResearchTechnology.Unlimited ? maxLevel - level.MinLevel + 1 : level.Level - level.MinLevel;
            return new ResearchTechnology(name, researched, levelsDone, level.MinLevel, maxLevel);
        }
    }
}
