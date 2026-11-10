using Foreman.DataCaching.DataTypes;
using System;
using System.Collections.Generic;

namespace Foreman.Models {
    public static class QualityRoll {
        public static IReadOnlyList<(IQuality Quality, double Chance)> Distribute(IQuality from, double qualityEffect, uint maxSteps) {
            var reached = new List<(IQuality Quality, double Chance)>();
            IQuality current = from;
            double chance = qualityEffect;
            for (uint step = 1; step < maxSteps && current.NextQuality is IQuality next; step++) {
                chance *= step == 1 ? current.NextProbability : current.ChainProbability;
                if (chance == 0 || !next.Enabled || !next.Available)
                    break;
                reached.Add((next, Math.Min(chance, 1.0)));
                current = next;
            }
            return reached;
        }
    }
}
