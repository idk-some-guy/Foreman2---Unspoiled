using System;

namespace Foreman.Models.Research {
    public sealed record ResearchSource(string SaveName, string SavePath, long? TicksPlayed, DateTime SyncedAtUtc);
}
