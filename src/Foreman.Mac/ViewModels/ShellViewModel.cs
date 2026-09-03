using CommunityToolkit.Mvvm.Input;
using System;

namespace Foreman.Mac.ViewModels {
    //One command per upstream MainForm toolbar button (MainForm.Designer.cs MenuButtonsTable/GridLinesGroupBox/ProductionGroupBox).
    public sealed class ShellViewModel {
        public IRelayCommand NewCommand { get; }
        public IRelayCommand LoadCommand { get; }
        public IRelayCommand SaveCommand { get; }
        public IRelayCommand SaveAsCommand { get; }
        public IRelayCommand ImportCommand { get; }
        public IRelayCommand ExportImageCommand { get; }
        public IRelayCommand AddItemCommand { get; }
        public IRelayCommand AddRecipeCommand { get; }
        public IRelayCommand AutoconnectCommand { get; }
        public IRelayCommand SettingsCommand { get; }
        public IRelayCommand GraphSummaryCommand { get; }
        public IRelayCommand HelpCommand { get; } = new RelayCommand(() => { }, () => false);
        public IRelayCommand AlignSelectionCommand { get; }

        public ShellViewModel(Action onNew, Action onLoad, Action onSave, Action onSaveAs, Action onImport, Action onExportImage, Action onAddItem, Action onAddRecipe, Action onAutoconnect, Action onAlignSelection, Action onSettings, Action onGraphSummary) {
            NewCommand = new RelayCommand(onNew);
            LoadCommand = new RelayCommand(onLoad);
            SaveCommand = new RelayCommand(onSave);
            SaveAsCommand = new RelayCommand(onSaveAs);
            ImportCommand = new RelayCommand(onImport);
            ExportImageCommand = new RelayCommand(onExportImage);
            AddItemCommand = new RelayCommand(onAddItem);
            AddRecipeCommand = new RelayCommand(onAddRecipe);
            AutoconnectCommand = new RelayCommand(onAutoconnect);
            AlignSelectionCommand = new RelayCommand(onAlignSelection);
            SettingsCommand = new RelayCommand(onSettings);
            GraphSummaryCommand = new RelayCommand(onGraphSummary);
        }
    }
}
