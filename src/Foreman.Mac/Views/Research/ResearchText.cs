using Foreman.Models.Research;
using System;

namespace Foreman.Mac.Views.Research {
    internal static class ResearchText {
        private static readonly string[] SmallNumbers = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine"];

        public static string Percent(double fraction) => (fraction * 100).ToString("0.##", DisplayCulture.Format) + " %";

        public static string Count(double value) => "+" + value.ToString("0.##", DisplayCulture.Format);

        public static string Value(ResearchField field, double? value) => value switch {
            null => "none",
            { } known when ResearchFields.IsCount(field) => Count(known),
            { } known => Percent(known),
        };

        public static string FieldLabel(ResearchField field) => field switch {
            ResearchField.MiningProductivity => "Mining productivity",
            ResearchField.LabSpeed => "Lab speed",
            ResearchField.LabProductivity => "Lab productivity",
            ResearchField.InserterStackBonus => "Inserter capacity",
            ResearchField.BulkInserterCapacityBonus => "Bulk inserter capacity",
            ResearchField.BeltStackBonus => "Belt stack size",
            ResearchField.RobotSpeed => "Robot speed",
            ResearchField.RobotCargo => "Robot cargo size",
            ResearchField.RobotBattery => "Robot battery",
            ResearchField.TrainBrakingForce => "Braking force",
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        public static string? PlayTime(long? ticks) =>
            Split(ticks) is { } time ? string.Create(DisplayCulture.Format, $"{time.Hours} h {time.Minutes} min") : null;

        public static string? PlayTimeSpoken(long? ticks) =>
            Split(ticks) is { } time ? string.Create(DisplayCulture.Format, $"{time.Hours} hours {time.Minutes} minutes") : null;

        public static string SyncedWhen(DateTime utc, DateTime nowLocal) {
            DateTime local = utc.ToLocalTime();
            string time = local.ToString("H:mm", DisplayCulture.Format);
            if (local.Date == nowLocal.Date)
                return "today at " + time;
            if (local.Date == nowLocal.Date.AddDays(-1))
                return "yesterday at " + time;
            return local.ToString("MMM d", DisplayCulture.Format) + " at " + time;
        }

        public static string CountWords(int n, bool sentenceStart) {
            if (n is < 0 or >= 10)
                return n.ToString(DisplayCulture.Format);
            string word = SmallNumbers[n];
            return sentenceStart ? char.ToUpperInvariant(word[0]) + word[1..] : word;
        }

        private static (long Hours, long Minutes)? Split(long? ticks) {
            if (ticks is not { } known)
                return null;
            long seconds = known / 60;
            return (seconds / 3600, seconds % 3600 / 60);
        }
    }
}
