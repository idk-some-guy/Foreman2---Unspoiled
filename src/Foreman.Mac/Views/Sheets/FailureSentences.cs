using Foreman.DataCaching;
using System;

namespace Foreman.Mac.Views.Sheets {
    internal static class FailureSentences {
        public const string SeeTheLog = "The details are in Foreman’s log.";

        public static string NotAFactorioFolder => OperatingSystem.IsMacOS()
            ? "This folder doesn’t contain Factorio. Choose the folder that holds factorio.app."
            : "This folder doesn’t contain Factorio. Choose the folder that holds bin/x64/factorio.";

        public static string Reason(FactorioRunFailure failure, FactorioCrashPhase phase, FactorioInstallCheck? install) => failure switch {
            FactorioRunFailure.AnotherInstanceRunning => "Factorio is already running. Quit Factorio, then try again.",
            FactorioRunFailure.ModsFolderNoAccess => "Foreman can’t write to the mods folder. Check its permissions, then try again.",
            FactorioRunFailure.ModsFolderUnreadable => "Foreman couldn’t open the mods folder. The details are in Foreman’s log.",
            FactorioRunFailure.HelperModCopyFailed => "Foreman couldn’t install its helper mod. Reinstall Foreman, then try again.",
            FactorioRunFailure.TestSaveNotCreated => "Factorio quit before creating its test save. A mod is the usual cause; see factorio-current.log.",
            FactorioRunFailure.TestSaveNotLoaded => "Factorio couldn’t load its test save. See factorio-current.log for the error.",
            FactorioRunFailure.ExportIncomplete => "Factorio couldn’t finish loading your mods. Make sure it reaches its main menu, then try again.",
            FactorioRunFailure.ExportUnreadable => "Foreman couldn’t read Factorio’s export. The details are in Foreman’s log.",
            FactorioRunFailure.ModInconsistency => "Factorio reported different mods than expected. Start Factorio to check for mod errors, then try again.",
            FactorioRunFailure.Crashed when phase != FactorioCrashPhase.None => $"Factorio crashed while {CrashPhrase(phase)}. A mod is the usual cause; see factorio-current.log.",
            FactorioRunFailure.InvalidInstall when install is not null && install.Problem != FactorioInstallProblem.None => Install(install),
            _ => SeeTheLog,
        };

        public static string Install(FactorioInstallCheck check) => check.Problem switch {
            FactorioInstallProblem.VersionUnreadable => "Foreman couldn’t read this Factorio’s version. Choose another Factorio installation.",
            FactorioInstallProblem.TooOld => $"Factorio {check.VersionText} is too old for this Foreman. Use Factorio 2.0.7 or later.",
            FactorioInstallProblem.TooNew => $"Factorio {check.VersionText} is newer than this Foreman supports. Check for a Foreman update.",
            _ => NotAFactorioFolder,
        };

        private static string CrashPhrase(FactorioCrashPhase phase) => phase switch {
            FactorioCrashPhase.CreatingTestSave => "creating the test save",
            FactorioCrashPhase.RunningExportScripts => "running the preset export scripts",
            _ => "reading the save file",
        };
    }
}
