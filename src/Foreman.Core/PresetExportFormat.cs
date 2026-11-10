using Foreman.DataCaching;
using System.Globalization;

namespace Foreman {
    /// <summary>
    /// Version of the foremanexport JSON schema. Bump <see cref="CurrentVersion"/> when export layout changes;
    /// keep in sync with <c>FOREMAN_EXPORT_VERSION</c> in foremanexport instrument-control.lua.
    /// </summary>
    public static class PresetExportFormat {
        public const string VersionPropertyName = "foreman_export_version";

        /// <summary>Latest export schema version understood by this Foreman build.</summary>
        public const int CurrentVersion = 1;

        public static int ReadVersion(DataCache? dc) => dc?.Version ?? 0;

        public static bool IsOutdated(DataCache? dc) => dc is null || dc.Version < CurrentVersion;

        public const string OutdatedSentence = "To get the latest fixes, import it again from Factorio in Settings > Presets.";

        public static string? ShowOutdatedWarningIfNeeded(DataCache? dc) {
            if (!IsOutdated(dc))
                return null;

            ErrorLogging.LogLine(string.Format(CultureInfo.InvariantCulture,
                "Preset \"{0}\" was made by an older version of Foreman. {1}", dc?.PresetName, OutdatedSentence));
            return OutdatedSentence;
        }
    }
}
