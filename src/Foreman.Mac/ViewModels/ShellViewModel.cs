using CommunityToolkit.Mvvm.Input;
using System;

namespace Foreman.Mac.ViewModels {
    public sealed class ShellViewModel {
        public IRelayCommand NewCommand { get; }
        public IRelayCommand LoadCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand ImportCommand { get; }
        public IRelayCommand ExportImageCommand { get; }
        public IRelayCommand AddItemCommand { get; }
        public IRelayCommand AddRecipeCommand { get; }
        public IRelayCommand AutoconnectCommand { get; }
        public IRelayCommand SettingsCommand { get; }
        public IRelayCommand GraphSummaryCommand { get; }
        public IRelayCommand AlignSelectionCommand { get; }
        public IRelayCommand SyncResearchCommand { get; }
        public IRelayCommand SyncResearchFromAnotherSaveCommand { get; }
        public IRelayCommand ResearchWindowCommand { get; }

        public ShellViewModel(Action onNew, Action onLoad, Action onSave, Action onImport, Action onExportImage, Action onAddItem, Action onAddRecipe, Action onAutoconnect, Action onAlignSelection, Action onSettings, Action onGraphSummary, Action onSyncResearch, Action onSyncResearchFromAnotherSave, Func<bool>? canExecuteAlignSelection = null, Func<bool>? canExecuteSyncResearch = null, Action? onResearchWindow = null, Func<bool>? canExecuteResearchWindow = null) {
            NewCommand = new RelayCommand(onNew);
            LoadCommand = new RelayCommand(onLoad);
            SaveCommand = new RelayCommand(onSave);
            ImportCommand = new RelayCommand(onImport);
            ExportImageCommand = new RelayCommand(onExportImage);
            AddItemCommand = new RelayCommand(onAddItem);
            AddRecipeCommand = new RelayCommand(onAddRecipe);
            AutoconnectCommand = new RelayCommand(onAutoconnect);
            AlignSelectionCommand = new RelayCommand(onAlignSelection, canExecuteAlignSelection ?? (() => true));
            SettingsCommand = new RelayCommand(onSettings);
            GraphSummaryCommand = new RelayCommand(onGraphSummary);
            SyncResearchCommand = new RelayCommand(onSyncResearch, canExecuteSyncResearch ?? (() => true));
            SyncResearchFromAnotherSaveCommand = new RelayCommand(onSyncResearchFromAnotherSave, canExecuteSyncResearch ?? (() => true));
            ResearchWindowCommand = new RelayCommand(onResearchWindow ?? (() => { }), canExecuteResearchWindow ?? (() => true));
        }
    }
}
