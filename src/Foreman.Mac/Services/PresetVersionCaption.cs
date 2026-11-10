using Foreman.DataCaching;

namespace Foreman.Mac.Services {
    internal static class PresetVersionCaption {
        private const string Separator = " · ";
        private const string OutdatedWord = "Outdated";
        private const string OlderVersionPhrase = "made by an older version of Foreman";

        public static string ForList(PresetHeader? header) => header switch {
            null => "",
            { GameVersion: { } version, IsOutdated: false } => version.ToString(),
            { GameVersion: { } version } => version + Separator + OutdatedWord,
            { IsOutdated: true } => OutdatedWord,
            _ => "",
        };

        public static string ForComparator(FactorioGameVersion? version, bool outdated) {
            string text = version is { } known ? "Factorio " + known : "Unknown version";
            return outdated ? text + Separator + OutdatedWord : text;
        }

        public static string ListNameSuffix(PresetHeader? header) => header switch {
            null => "",
            { GameVersion: { } version, IsOutdated: false } => ", Factorio " + version,
            { GameVersion: { } version } => ", Factorio " + version + ", " + OlderVersionPhrase,
            { IsOutdated: true } => ", " + OlderVersionPhrase,
            _ => "",
        };

        public static string ComparatorName(string preset, FactorioGameVersion? version, bool outdated) {
            string text = preset + (version is { } known ? ", Factorio " + known : ", unknown version");
            return outdated ? text + ", " + OlderVersionPhrase : text;
        }
    }
}
