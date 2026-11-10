using Foreman.DataCaching.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ForemanTest {
    [TestClass]
    public class GraphVersionThinningTests {
        private static readonly DateTime Now = new(2026, 10, 28, 12, 30, 0, DateTimeKind.Utc);

        private static GraphVersionRow Version(DateTime createdAt, GraphVersionKind kind = GraphVersionKind.Automatic) =>
            new() { Id = Guid.NewGuid(), CreatedAt = createdAt, Kind = kind };

        [TestMethod]
        public void HourlyFor24h_DailyFor30d_ThenDropped_SavedKept() {
            GraphVersionRow hourOldA = Version(Now.AddMinutes(-10));
            GraphVersionRow hourOldB = Version(Now.AddMinutes(-20));
            GraphVersionRow previousHour = Version(Now.AddMinutes(-80));
            GraphVersionRow dayOldA = Version(Now.AddDays(-3).AddHours(-5));
            GraphVersionRow dayOldB = Version(Now.AddDays(-3).AddHours(-6));
            GraphVersionRow tooOld = Version(Now.AddDays(-31));
            GraphVersionRow savedOld = Version(Now.AddDays(-90), GraphVersionKind.Saved);
            GraphVersionRow restoreOld = Version(Now.AddDays(-90), GraphVersionKind.BeforeRestore);

            IReadOnlyList<GraphVersionRow> kept = GraphVersionThinning.ToKeep(
                [hourOldB, previousHour, hourOldA, dayOldB, dayOldA, tooOld, savedOld, restoreOld], Now);

            CollectionAssert.AreEquivalent(
                new[] { hourOldA, previousHour, dayOldA, savedOld, restoreOld }, kept.ToArray());
            CollectionAssert.AreEqual(
                kept.Select(v => v.CreatedAt).OrderByDescending(d => d).ToArray(), kept.Select(v => v.CreatedAt).ToArray());
        }
    }
}
