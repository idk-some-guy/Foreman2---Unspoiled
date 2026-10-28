using System;
using System.Collections.Generic;
using System.Linq;

namespace Foreman.DataCaching.Storage {
    public static class GraphVersionThinning {
        private static readonly TimeSpan HourlyWindow = TimeSpan.FromHours(24);
        private static readonly TimeSpan DailyWindow = TimeSpan.FromDays(30);

        public static IReadOnlyList<GraphVersionRow> ToKeep(IEnumerable<GraphVersionRow> versions, DateTime now) {
            GraphVersionRow[] all = [.. versions.OrderByDescending(v => v.CreatedAt)];
            var kept = new List<GraphVersionRow>();
            var seen = new HashSet<(bool Hourly, DateTime Bucket)>();
            foreach (GraphVersionRow version in all) {
                if (version.Kind != GraphVersionKind.Automatic) {
                    kept.Add(version);
                    continue;
                }
                TimeSpan age = now - version.CreatedAt;
                if (age >= DailyWindow)
                    continue;
                (bool, DateTime) key = age < HourlyWindow
                    ? (true, new DateTime(version.CreatedAt.Year, version.CreatedAt.Month, version.CreatedAt.Day, version.CreatedAt.Hour, 0, 0))
                    : (false, version.CreatedAt.Date);
                if (seen.Add(key))
                    kept.Add(version);
            }
            return kept;
        }
    }
}
