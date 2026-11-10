using Avalonia.Controls;
using Foreman.DataCaching;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    internal static class OutdatedPresetAlert {
        public static Task? Present(Window? owner, Preset preset, DataCache cache, Func<Window, string, string, Task> show) {
            if (PresetExportFormat.ShowOutdatedWarningIfNeeded(cache) is not string sentence || owner is null)
                return null;
            string headline = string.Format(CultureInfo.InvariantCulture, "“{0}” was made by an older version of Foreman.", Dialogs.MiddleTruncate(preset.Name));
            Task alert = show(owner, headline, sentence);
            Async.Fire(alert, nameof(OutdatedPresetAlert));
            return alert;
        }
    }
}
