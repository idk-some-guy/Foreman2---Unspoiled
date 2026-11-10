using Foreman.DataCaching;
using System.Collections.Generic;

namespace Foreman.Models.Research {
    internal sealed class ResearchRead {
        public required SaveFileLoadOutcome Outcome { get; init; }
        public FactorioRunFailure Failure { get; init; }
        public FactorioCrashPhase CrashPhase { get; init; }
        public FactorioInstallCheck? InstallCheck { get; init; }
        public ResearchState? State { get; init; }
        public IReadOnlyDictionary<string, string> SaveMods { get; init; } = new Dictionary<string, string>();
        public FactorioGameVersion? SaveGameVersion { get; init; }

        public bool Succeeded => Outcome == SaveFileLoadOutcome.Ok && Failure == FactorioRunFailure.None && State is not null;
    }
}
