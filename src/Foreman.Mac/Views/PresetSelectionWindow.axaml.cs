using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Foreman;
using Foreman.DataCaching;
using System.Collections.Generic;

namespace Foreman.Mac.Views {
    //Ports Forms/PresetSelectionForm.cs(+.Designer.cs) (reference docs/io-reference.md §8): the ranked
    //picker MainWindow.ResolveChosenPresetAsync shows when no installed preset matches the loaded save's
    //own preset exactly. Avalonia's ListBox has no WinForms ListView column support, so the 4-column layout
    //(Preset/Mods/Items/Recipes) is a shared Grid.ColumnDefinitions string reused by the header row and the
    //per-item DataTemplate rather than real ColumnHeaders.
    public partial class PresetSelectionWindow : Window {
        internal sealed class PresetRow(Preset preset, string modsText, string itemsText, string recipesText, string tooltip) {
            public Preset Preset { get; } = preset;
            public string Name => Preset.Name;
            public string ModsText { get; } = modsText;
            public string ItemsText { get; } = itemsText;
            public string RecipesText { get; } = recipesText;
            public string Tooltip { get; } = tooltip;
        }

        private readonly ListBox presetListView;
        private readonly Button confirmationButton;
        private readonly Button cancellingButton;

        public Preset? ChosenPreset { get; private set; }

        public PresetSelectionWindow() : this([]) {
        }

        //Ports the constructor's sort + per-package row build (PresetSelectionForm.cs:14-53): List<T>.Sort
        //calls PresetErrorPackage.CompareTo, so the closest-matching preset lands first.
        public PresetSelectionWindow(List<PresetErrorPackage> presetErrors) {
            InitializeComponent();
            presetErrors.Sort();

            presetListView = this.FindControl<ListBox>("PresetSelectionListView")!;
            confirmationButton = this.FindControl<Button>("ConfirmationButton")!;
            cancellingButton = this.FindControl<Button>("CancellingButton")!;

            presetListView.ItemsSource = BuildRows(presetErrors);
            presetListView.DoubleTapped += (_, _) => Commit();
            confirmationButton.Click += (_, _) => Commit();
            cancellingButton.Click += (_, _) => Close(false);
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        //Ports ConfirmationButton_Click/PresetSelectionListView_MouseDoubleClick (PresetSelectionForm.cs:57-76):
        //both commit the selected row the same way.
        private void Commit() {
            if (presetListView.SelectedItem is not PresetRow row)
                return;
            ChosenPreset = row.Preset;
            Close(true);
        }

        //Ports the compatibility[]/ToolTipText block (PresetSelectionForm.cs:19-53) verbatim, including its
        //"%00" percent format (multiplies by 100 and prints the '%' before the digits - upstream's own
        //quirk, kept as-is) and the exact tooltip breakdown strings.
        internal static List<PresetRow> BuildRows(List<PresetErrorPackage> presetErrors) {
            var rows = new List<PresetRow>();
            foreach (PresetErrorPackage p in presetErrors) {
                float modsCompatibility = (float)(p.RequiredMods.Count - p.MissingMods.Count - p.WrongVersionMods.Count - p.AddedMods.Count) / p.RequiredMods.Count;
                float itemsCompatibility = (float)(p.RequiredItems.Count - p.MissingItems.Count) / p.RequiredItems.Count;
                float recipesCompatibility = (float)(p.RequiredRecipes.Count - p.MissingRecipes.Count - p.IncorrectRecipes.Count) / p.RequiredRecipes.Count;

                string tooltip =
                    "Mods:\n" +
                    string.Format(DisplayCulture.Format, "     ({0}) Correct\n", p.RequiredMods.Count - p.MissingMods.Count - p.WrongVersionMods.Count) +
                    string.Format(DisplayCulture.Format, "     ({0}) Missing\n", p.MissingMods.Count) +
                    string.Format(DisplayCulture.Format, "     ({0}) Extra\n", p.AddedMods.Count) +
                    string.Format(DisplayCulture.Format, "     ({0}) Wrong Version\n", p.WrongVersionMods.Count) +
                    "Items:\n" +
                    string.Format(DisplayCulture.Format, "     ({0}) Correct\n", p.RequiredItems.Count - p.MissingItems.Count) +
                    string.Format(DisplayCulture.Format, "     ({0}) Missing\n", p.MissingItems.Count) +
                    "Recipes:\n" +
                    string.Format(DisplayCulture.Format, "     ({0}) Correct\n", p.RequiredRecipes.Count - p.MissingRecipes.Count - p.IncorrectRecipes.Count) +
                    string.Format(DisplayCulture.Format, "     ({0}) Missing\n", p.MissingRecipes.Count) +
                    string.Format(DisplayCulture.Format, "     ({0}) Incorrect", p.IncorrectRecipes.Count);

                rows.Add(new PresetRow(p.Preset,
                    modsCompatibility.ToString("%00", DisplayCulture.Format),
                    itemsCompatibility.ToString("%00", DisplayCulture.Format),
                    recipesCompatibility.ToString("%00", DisplayCulture.Format),
                    tooltip));
            }
            return rows;
        }

        //Test-only seams (nothing outside Foreman.Mac.UiTests reads these) - see ShapePropertiesWindow's
        //equivalent comment for the established convention this follows.
        internal ListBox PresetSelectionListViewControl => presetListView;
        internal Button ConfirmationButtonControl => confirmationButton;
        internal Button CancellingButtonControl => cancellingButton;

        internal void SimulateSelectRow(Preset preset) =>
            presetListView.SelectedItem = ((List<PresetRow>)presetListView.ItemsSource!).Find(r => r.Preset == preset);

        internal void SimulateConfirmClick() => Commit();
        internal void SimulateDoubleClickRow(Preset preset) {
            SimulateSelectRow(preset);
            Commit();
        }
        internal void SimulateCancelClick() => Close(false);
    }
}
