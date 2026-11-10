namespace Foreman.DataCaching {
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
