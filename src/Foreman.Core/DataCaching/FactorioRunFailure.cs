namespace Foreman.DataCaching {
    //Why a Factorio-driven pipeline stopped, so a caller picks its own sentence instead of parsing WarningMessage (kept for the log).
    public enum FactorioRunFailure {
        None,
        Unmapped,
        InvalidInstall,
        AnotherInstanceRunning,
        ModsFolderNoAccess,
        ModsFolderUnreadable,
        HelperModCopyFailed,
        TestSaveNotCreated,
        TestSaveNotLoaded,
        ExportIncomplete,
        ExportUnreadable,
        ModInconsistency,
        Crashed,
    }

    public enum FactorioCrashPhase { None, CreatingTestSave, RunningExportScripts, ReadingSave }
}
