using System;
using System.Windows.Input;

namespace Foreman.Mac {
    //A command that renders disabled until its upstream behavior is ported (phases 3-6).
    public sealed class ShellCommand : ICommand {
        private readonly Action execute;

        public ShellCommand(Action execute, bool isImplemented) {
            this.execute = execute;
            IsImplemented = isImplemented;
        }

        public bool IsImplemented { get; }

        //IsImplemented is fixed for a command's lifetime, so CanExecute never changes after construction.
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => IsImplemented;

        public void Execute(object? parameter) => execute();
    }

    //One command per upstream MainForm toolbar button (MainForm.Designer.cs MenuButtonsTable/GridLinesGroupBox/ProductionGroupBox).
    public sealed class ShellCommands {
        public ShellCommand New { get; }
        public ShellCommand Load { get; }
        public ShellCommand Save { get; }
        public ShellCommand SaveAs { get; }
        public ShellCommand Import { get; }
        public ShellCommand ExportImage { get; }
        public ShellCommand AddItem { get; }
        public ShellCommand AddRecipe { get; }
        public ShellCommand Autoconnect { get; }
        public ShellCommand Settings { get; }
        public ShellCommand GraphSummary { get; }
        public ShellCommand Help { get; } = new(() => { }, isImplemented: false);
        public ShellCommand AlignSelection { get; }

        public ShellCommands(Action onNew, Action onLoad, Action onSave, Action onSaveAs, Action onImport, Action onExportImage, Action onAddItem, Action onAddRecipe, Action onAutoconnect, Action onAlignSelection, Action onSettings, Action onGraphSummary) {
            New = new ShellCommand(onNew, isImplemented: true);
            Load = new ShellCommand(onLoad, isImplemented: true);
            Save = new ShellCommand(onSave, isImplemented: true);
            SaveAs = new ShellCommand(onSaveAs, isImplemented: true);
            Import = new ShellCommand(onImport, isImplemented: true);
            ExportImage = new ShellCommand(onExportImage, isImplemented: true);
            AddItem = new ShellCommand(onAddItem, isImplemented: true);
            AddRecipe = new ShellCommand(onAddRecipe, isImplemented: true);
            Autoconnect = new ShellCommand(onAutoconnect, isImplemented: true);
            AlignSelection = new ShellCommand(onAlignSelection, isImplemented: true);
            Settings = new ShellCommand(onSettings, isImplemented: true);
            GraphSummary = new ShellCommand(onGraphSummary, isImplemented: true);
        }
    }
}
