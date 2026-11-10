using Foreman.DataCaching.DataTypes;
using System;

namespace Foreman.Models {
    public static class SpoilQuality {
        public static IQuality Resolve(IItem item, IQuality input) {
            IQuality result = input;
            for (int steps = Math.Abs(item.SpoilQualityChange); steps > 0; steps--) {
                IQuality? next = item.SpoilQualityChange > 0 ? result.NextQuality : result.PrevQuality;
                if (next is null)
                    break;
                result = next;
            }
            if (item.SpoilQualityMin is IQuality min && result.Level < min.Level)
                result = min;
            if (item.SpoilQualityMax is IQuality max && result.Level > max.Level)
                result = max;
            return result;
        }

        public static IQuality? InputFor(IItem item, IQuality output) {
            if (Resolve(item, output) == output)
                return output;
            IQuality lowest = output;
            while (lowest.PrevQuality is IQuality prev)
                lowest = prev;
            for (IQuality? candidate = lowest; candidate is not null; candidate = candidate.NextQuality)
                if (Resolve(item, candidate) == output)
                    return candidate;
            return null;
        }
    }
}
