using Foreman.DataCaching;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    internal static class PresetHeaders {
        private sealed record Entry(DateTime Stamp, Task<PresetHeader?> Read);

        private static readonly ConcurrentDictionary<string, Entry> Cache = new();

        public static bool TryGet(Preset preset, out PresetHeader? header) {
            string path = PathOf(preset);
            if (Cache.TryGetValue(path, out Entry? entry) && entry.Stamp == StampOf(path) && entry.Read.IsCompletedSuccessfully) {
                header = entry.Read.Result;
                return true;
            }
            header = null;
            return false;
        }

        public static Task<PresetHeader?> GetAsync(Preset preset) {
            string path = PathOf(preset);
            DateTime stamp = StampOf(path);
            return Cache.AddOrUpdate(path,
                _ => new Entry(stamp, Task.Run(() => PresetProcessor.ReadPresetHeader(preset))),
                (_, existing) => existing.Stamp == stamp ? existing : new Entry(stamp, Task.Run(() => PresetProcessor.ReadPresetHeader(preset)))).Read;
        }

        private static string PathOf(Preset preset) => PresetProcessor.GetPresetPath(preset.Name, ".pjson");

        private static DateTime StampOf(string path) => File.GetLastWriteTimeUtc(path);
    }
}
