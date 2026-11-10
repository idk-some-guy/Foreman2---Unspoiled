using Avalonia.Controls;
using Foreman.DataCaching;
using Foreman.DataCaching.Storage;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Foreman.Mac.Services {
    internal sealed class WelcomeHost {
        public required GraphLibraryService Library { get; init; }
        public required AppSettings Settings { get; init; }
        public Action SaveSettings { get; init; } = static () => { };
        public Func<IReadOnlyList<Preset>> InstalledPresets { get; init; } = static () => [];
        public Func<Preset, IProgress<KeyValuePair<int, string>>, CancellationToken, Task<bool>> LoadPreset { get; init; } = static (_, _, _) => Task.FromResult(true);
        public Func<Task<bool>> NewGraph { get; init; } = static () => Task.FromResult(false);
        public Func<string, Task<bool>> OpenPath { get; init; } = static _ => Task.FromResult(false);
        public Func<Task<bool>> OpenFile { get; init; } = static () => Task.FromResult(false);
        public Func<GraphRow, Preset?, Task<bool>> OpenGraph { get; init; } = static (_, _) => Task.FromResult(false);
        public Func<GraphRow, Task> ExportGraph { get; init; } = static _ => Task.CompletedTask;
        public Func<GraphRow, Task<List<PresetErrorPackage>>> RatePresets { get; init; } = static _ => Task.FromResult(new List<PresetErrorPackage>());
        public Func<Action<Guid>, Action, NativeMenu> OpenRecentMenu { get; init; } = static (_, _) => [];
        public Action ClearOpenRecent { get; init; } = static () => { };
        public Preset? InitialPreset { get; init; }
        public Func<bool> HasOpenGraph { get; init; } = static () => false;
    }
}
