using Foreman.DataCaching;
using Foreman.Mac.Services;
using System;
using System.IO;

namespace Foreman.Mac.Views.Research {
    internal sealed class ResearchWindowHost {
        public required Func<ResearchSyncFlow> Sync { get; init; }
        public AppSettings? Settings { get; init; }
        public Func<DataCache?> Cache { get; init; } = () => null;
        public Func<string?, bool> SaveExists { get; init; } = path => path is not null && File.Exists(path);
        public TimeProvider Clock { get; init; } = TimeProvider.System;
    }
}
