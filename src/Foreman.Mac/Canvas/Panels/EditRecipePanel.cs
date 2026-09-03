using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Foreman;
using Foreman.DataCaching;
using Foreman.DataCaching.DataTypes;
using Foreman.Graph;
using Foreman.Models;
using Foreman.Models.Nodes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Foreman.Mac.Canvas.Panels {
    //Ports Controls/EditRecipePanel.cs(+.Designer.cs/.Viewport.cs) (docs/panels-reference.md §3): the recipe
    //node editor - assembler/fuel/beacon/module pickers, neighbour bonus, extra productivity, and the
    //always-visible stat readout + recipe info card. TableLayoutPanel button grids become WrapPanels of
    //IconButton (task 2's shared picker-cell control); NFButton's hover tooltip is IconButton's own
    //ToolTip.SetTip. Construction mirrors upstream's real ordering here (values -> setup cascade -> event
    //wiring LAST, per the ctor's own "set these event handlers last" comment and the absence of any
    //Designer.cs-time wiring for this panel) rather than EditFlowPanel's wire-first ordering, since the two
    //panels' upstream orderings genuinely differ.
    public sealed class EditRecipePanel : Border, IViewportFittable {
        private static readonly Color ErrorColor = Colors.DarkRed;
        private static readonly Color SelectedColor = Colors.DarkOrange;
        private static readonly Color DefaultButtonColor = IconButton.EmptyFillColor;
        private const int PickerCellSize = 40;
        private const int ModuleCellSize = 32;
        private const int BeaconValueInputWidth = 120;
        //Human nit: 120px (BeaconValueInputWidth) only clears the spinner buttons' ~68px overhead with
        //~52px left for digits - fine for beacon counts, too tight for six-digit assembler counts.
        private const int FixedAssemblerInputWidth = 170;

        //Verbatim from EditRecipePanel.Designer.cs's Color.FromArgb constants: a near-black header band
        //(AssemblerTitle/BeaconTitle/FuelTitle/A|BModulesLabel/A|BModuleOptionsLabel/RateOptionsTable) sits
        //over a mid-gray section body (AssemblerTable/BeaconTable), both nested inside the outer panel's own
        //solid-black chrome (this Border's own Background, below).
        internal static readonly Color SectionHeaderColor = Color.FromRgb(40, 40, 40);
        internal static readonly Color SectionBodyColor = Color.FromRgb(65, 65, 65);
        private static readonly IBrush SectionHeaderBrush = new SolidColorBrush(SectionHeaderColor);
        private static readonly IBrush SectionBodyBrush = new SolidColorBrush(SectionBodyColor);

        private readonly GraphViewer graphViewer;
        private readonly RecipeNodeController nodeController;
        private readonly IRecipeNodeViewModel nodeData;
        private readonly DataCache panelCache;

        private readonly List<IconButton> assemblerOptions = [];
        private readonly List<IconButton> fuelOptions = [];
        private readonly List<IconButton> assemblerModuleButtons = [];
        private readonly List<IconButton> aModuleOptionButtons = [];
        private readonly List<IconButton> beaconOptions = [];
        private readonly List<IconButton> beaconModuleButtons = [];
        private readonly List<IconButton> bModuleOptionButtons = [];

        public Action? RequestRedraw { get; set; }
        public Action? RequestReposition { get; set; }

        public RadioButton AutoAssemblersOption { get; }
        public RadioButton FixedAssemblersOption { get; }
        public NumericUpDown FixedAssemblerInput { get; }
        public CheckBox LowPriorityCheckBox { get; }
        public CheckBox KeyNodeCheckBox { get; }
        public TextBlock KeyNodeTitleLabel { get; }
        public TextBox KeyNodeTitleInput { get; }
        public QualityPicker QualitySelector { get; }

        public TextBlock AssemblerRateLabel { get; }
        public TextBlock AssemblerTitle { get; }
        public IconButton SelectedAssemblerIcon { get; }
        public WrapPanel AssemblerChoicePanel { get; }
        public IReadOnlyList<IconButton> AssemblerOptions => assemblerOptions;

        public TextBlock AssemblerEnergyLabel { get; }
        public TextBlock AssemblerEnergyPercentLabel { get; }
        public TextBlock AssemblerSpeedTitleLabel { get; }
        public TextBlock AssemblerSpeedLabel { get; }
        public TextBlock AssemblerSpeedPercentLabel { get; }
        public TextBlock AssemblerProductivityTitleLabel { get; }
        public TextBlock AssemblerProductivityPercentLabel { get; }
        public TextBlock AssemblerPollutionTitleLabel { get; }
        public TextBlock AssemblerPollutionLabel { get; }
        public TextBlock AssemblerPollutionPercentLabel { get; }
        public TextBlock AssemblerQualityTitleLabel { get; }
        public TextBlock AssemblerQualityPercentLabel { get; }
        public TextBlock GeneratorTemperatureLabel { get; }
        public TextBlock GeneratorTemperatureRangeLabel { get; }

        public TextBlock NeighboursLabel { get; }
        public NumericUpDown NeighbourInput { get; }
        public TextBlock ExtraProductivityLabel { get; }
        public NumericUpDown ExtraProductivityInput { get; }

        public TextBlock FuelTitle { get; }
        public IconButton SelectedFuelIcon { get; }
        public WrapPanel FuelOptionsPanel { get; }
        public IReadOnlyList<IconButton> FuelOptions => fuelOptions;

        public TextBlock AModulesLabel { get; }
        public TextBlock AModuleOptionsLabel { get; }
        public WrapPanel SelectedAModulesPanel { get; }
        public WrapPanel AModulesChoicePanel { get; }
        public IReadOnlyList<IconButton> AssemblerModules => assemblerModuleButtons;
        public IReadOnlyList<IconButton> AModuleOptions => aModuleOptionButtons;

        public Panel AssemblerSection { get; private set; } = null!;
        public Panel BeaconSection { get; }
        public TextBlock BeaconTitle { get; }
        public IconButton SelectedBeaconIcon { get; }
        public WrapPanel BeaconChoicePanel { get; }
        public IReadOnlyList<IconButton> BeaconOptions => beaconOptions;

        public Panel BeaconValuesPanel { get; }
        public NumericUpDown BeaconCountInput { get; }
        public NumericUpDown BeaconsPerAssemblerInput { get; }
        public NumericUpDown ConstantBeaconInput { get; }

        public Panel BeaconInfoPanel { get; }
        public TextBlock BeaconEnergyLabel { get; }
        public TextBlock BeaconModuleCountLabel { get; }
        public TextBlock BeaconEfficiencyLabel { get; }
        public TextBlock TotalBeaconsLabel { get; }
        public TextBlock TotalBeaconEnergyLabel { get; }

        public TextBlock BModulesLabel { get; }
        public TextBlock BModuleOptionsLabel { get; }
        public WrapPanel SelectedBModulesPanel { get; }
        public WrapPanel BModulesChoicePanel { get; }
        public IReadOnlyList<IconButton> BeaconModules => beaconModuleButtons;
        public IReadOnlyList<IconButton> BModuleOptions => bModuleOptionButtons;

        public ScrollViewer ScrollHost => scrollHost;

        private readonly ScrollViewer scrollHost;
        private readonly StackPanel root;
        private readonly ScrollViewer selectedAModulesBox;
        private readonly ScrollViewer selectedBModulesBox;

        public EditRecipePanel(IRecipeNodeViewModel node, GraphViewer graphViewer) {
            nodeData = node;
            if (graphViewer.Session.Editor.RequestNodeController(node.Id) is not RecipeNodeController controller)
                throw new InvalidOperationException("Recipe node has no controller.");
            nodeController = controller;
            this.graphViewer = graphViewer;
            panelCache = graphViewer.Context.DCache ?? throw new InvalidOperationException("Data cache is not loaded.");

            Background = Brushes.Black;
            Focusable = true;
            Padding = new Thickness(6);

            AutoAssemblersOption = new RadioButton { Content = "Auto", GroupName = "EditRecipeRateType", Foreground = Brushes.White };
            FixedAssemblersOption = new RadioButton { Content = "Fixed", GroupName = "EditRecipeRateType", Foreground = Brushes.White };
            FixedAssemblerInput = new NumericUpDown { Minimum = 0, Maximum = (decimal)node.MaxDesiredSetValue, Width = FixedAssemblerInputWidth, FormatString = "N4" };
            LowPriorityCheckBox = new CheckBox { Content = "Low Priority Recipe", Foreground = Brushes.White, IsChecked = nodeData.LowPriority };
            KeyNodeCheckBox = new CheckBox { Content = "Key Node", Foreground = Brushes.White, IsChecked = nodeData.KeyNode };
            KeyNodeTitleLabel = new TextBlock { Text = "Title:", Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, IsVisible = nodeData.KeyNode };
            KeyNodeTitleInput = new TextBox { Background = Brushes.LightGray, Foreground = Brushes.Black, Width = 114, MaxLength = 200, Text = nodeData.KeyNodeTitle, IsVisible = nodeData.KeyNode };
            QualitySelector = new QualityPicker();

            AssemblerRateLabel = Header("# of Assemblers:");
            AssemblerTitle = Header("Assembler:");
            SelectedAssemblerIcon = new IconButton { Width = PickerCellSize, Height = PickerCellSize };
            AssemblerChoicePanel = NewGrid();

            AssemblerEnergyLabel = Value();
            AssemblerEnergyPercentLabel = Value();
            AssemblerSpeedTitleLabel = Caption("Speed:");
            AssemblerSpeedLabel = Value();
            AssemblerSpeedPercentLabel = Value();
            AssemblerProductivityTitleLabel = Caption("Productivity:");
            AssemblerProductivityPercentLabel = Value();
            AssemblerPollutionTitleLabel = Caption("Pollution:");
            AssemblerPollutionLabel = Value();
            AssemblerPollutionPercentLabel = Value();
            AssemblerQualityTitleLabel = Caption("Quality:");
            AssemblerQualityPercentLabel = Value();
            GeneratorTemperatureLabel = Caption("Temperature Range:");
            GeneratorTemperatureRangeLabel = Value();

            //Finding B4 (same as the beacon values below): Avalonia's Fluent NumericUpDown spends ~68px on
            //its side-by-side spinner buttons alone, so upstream's narrower WinForms width left no room for
            //the digits themselves. BeaconValueInputWidth/MinWidth clears that spinner cost here too.
            NeighboursLabel = SectionLabel("Average # of neighbours:");
            NeighbourInput = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 0.05m, Width = BeaconValueInputWidth, MinWidth = BeaconValueInputWidth, FormatString = "N2" };
            ExtraProductivityLabel = SectionLabel("Extra Productivity Bonus (%):");
            ExtraProductivityInput = new NumericUpDown { Minimum = 0, Maximum = 100000, Increment = 10m, Width = BeaconValueInputWidth, MinWidth = BeaconValueInputWidth, FormatString = "N2" };

            FuelTitle = Header("Fuel:");
            SelectedFuelIcon = new IconButton { Width = PickerCellSize, Height = PickerCellSize };
            FuelOptionsPanel = NewGrid();

            AModulesLabel = SectionLabel("Modules (0/0):");
            AModulesLabel.Background = SectionHeaderBrush;
            AModuleOptionsLabel = SectionLabel("Module Options:");
            AModuleOptionsLabel.Background = SectionHeaderBrush;
            SelectedAModulesPanel = NewGrid(ModuleCellSize);
            AModulesChoicePanel = NewGrid(ModuleCellSize);
            selectedAModulesBox = FixedModuleBox(SelectedAModulesPanel);

            BeaconTitle = Header("Beacon:");
            SelectedBeaconIcon = new IconButton { Width = PickerCellSize, Height = PickerCellSize };
            BeaconChoicePanel = NewGrid();

            //Upstream's WinForms BeaconCountInput sets DecimalPlaces=2 (always "1.00"); the other two leave it
            //at the WinForms default of 0, so they're already bare integers there. "0.##" keeps genuine
            //fractional beacon counts visible while dropping BeaconCountInput's trailing ".00" on whole
            //numbers; "0" matches the other two fields' upstream integer-only rendering exactly.
            BeaconCountInput = BeaconValueInput("0.##");
            BeaconsPerAssemblerInput = BeaconValueInput("0");
            ConstantBeaconInput = BeaconValueInput("0");
            //Upstream's BeaconValuesTable stacks these three as compact labeled rows (Designer.cs: 3 rows, an
            //80px-absolute input column) rather than laying them side by side - a bare NumericUpDown next to
            //a taller sibling (the icon-list column beside it in beaconMainRow below) stretches to fill that
            //sibling's full height by default, which is what made these render as oversized boxes with their
            //spinner arrows pulled apart (review finding B3). VerticalAlignment.Top keeps this whole column
            //at its own natural height instead of matching beaconMainRow's tallest sibling.
            //Avalonia's Fluent NumericUpDown spends ~68px on its two spinner buttons alone (they sit side by
            //side, not upstream's slim stacked WinForms arrows), so upstream's 80px column left the value box
            //itself only ~10px wide - just the spinner, no visible digits (review finding B4). BeaconValueInputWidth
            //leaves real room for the text after that fixed spinner cost.
            BeaconValuesPanel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
            BeaconValuesPanel.Children.Add(LabeledRow("# Beacons:", BeaconCountInput));
            BeaconValuesPanel.Children.Add(LabeledRow("/Assembler:", BeaconsPerAssemblerInput));
            BeaconValuesPanel.Children.Add(LabeledRow("Additional:", ConstantBeaconInput));

            BeaconEnergyLabel = Value();
            BeaconModuleCountLabel = Value();
            BeaconEfficiencyLabel = Value();
            TotalBeaconsLabel = Value();
            TotalBeaconEnergyLabel = Value();
            BeaconInfoPanel = new StackPanel { Spacing = 2 };
            BeaconInfoPanel.Children.Add(InfoRow("Energy:", BeaconEnergyLabel));
            BeaconInfoPanel.Children.Add(InfoRow("Modules:", BeaconModuleCountLabel));
            BeaconInfoPanel.Children.Add(InfoRow("Efficiency:", BeaconEfficiencyLabel));
            BeaconInfoPanel.Children.Add(InfoRow("# Beacons:", TotalBeaconsLabel));
            BeaconInfoPanel.Children.Add(InfoRow("Total Energy:", TotalBeaconEnergyLabel));

            BModulesLabel = SectionLabel("Modules (0/0):");
            BModulesLabel.Background = SectionHeaderBrush;
            BModuleOptionsLabel = SectionLabel("Module Options:");
            BModuleOptionsLabel.Background = SectionHeaderBrush;
            SelectedBModulesPanel = NewGrid(ModuleCellSize);
            BModulesChoicePanel = NewGrid(ModuleCellSize);
            selectedBModulesBox = FixedModuleBox(SelectedBModulesPanel);

            //Upstream's BeaconTable places the picker, info readout and beacon-value fields side by side in
            //one row (Designer.cs row 1: BeaconChoicePanel c0, BeaconInfoTable c1, BeaconValuesTable c2), and
            //the module row below it the same way as the assembler modules row (§ BuildLayout) - restoring
            //that horizontal grouping instead of stacking every piece vertically (review finding 2).
            var beaconHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Background = SectionHeaderBrush };
            beaconHeaderRow.Children.Add(SelectedBeaconIcon);
            beaconHeaderRow.Children.Add(BeaconTitle);
            var beaconPickerColumn = new StackPanel { Spacing = 6 };
            beaconPickerColumn.Children.Add(beaconHeaderRow);
            beaconPickerColumn.Children.Add(BeaconChoicePanel);
            var beaconMainRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            beaconMainRow.Children.Add(beaconPickerColumn);
            beaconMainRow.Children.Add(BeaconInfoPanel);
            beaconMainRow.Children.Add(BeaconValuesPanel);

            var bModulesColumn = new StackPanel { Spacing = 4 };
            bModulesColumn.Children.Add(BModulesLabel);
            bModulesColumn.Children.Add(selectedBModulesBox);
            var bModuleOptionsColumn = new StackPanel { Spacing = 4 };
            bModuleOptionsColumn.Children.Add(BModuleOptionsLabel);
            bModuleOptionsColumn.Children.Add(BModulesChoicePanel);
            var bModulesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            bModulesRow.Children.Add(bModulesColumn);
            bModulesRow.Children.Add(bModuleOptionsColumn);

            BeaconSection = new StackPanel {
                Background = SectionBodyBrush,
                Spacing = 6,
                Children = { beaconMainRow, bModulesRow },
            };

            root = new StackPanel { Spacing = 6 };
            BuildLayout();
            //Restoring upstream's side-by-side sections (review finding 2) widened the panel's natural
            //content beyond what a vertical-only scroll host let through unclipped at narrower viewports -
            //matching upstream's own AutoScroll surface needs both axes now, not just the vertical one this
            //host has always handled.
            scrollHost = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            Child = scrollHost;

            FixedAssemblerInput.Maximum = (decimal)node.MaxDesiredSetValue;
            InitializeRates();
            NeighbourInput.Value = Math.Min(NeighbourInput.Maximum, (decimal)nodeData.NeighbourCount);
            ExtraProductivityInput.Value = Math.Min(ExtraProductivityInput.Maximum, (decimal)(nodeData.ExtraProductivity * 100));
            BeaconCountInput.Value = Math.Min(BeaconCountInput.Maximum, (decimal)nodeData.BeaconCount);
            BeaconsPerAssemblerInput.Value = Math.Min(BeaconsPerAssemblerInput.Maximum, (decimal)nodeData.BeaconsPerAssembler);
            ConstantBeaconInput.Value = Math.Min(ConstantBeaconInput.Maximum, (decimal)nodeData.BeaconsConst);

            List<IQuality> enabledQualities = [.. panelCache.AvailableQualities.Where(q => q.Enabled)];
            QualitySelector.SetQualities(enabledQualities);
            IQuality? defQuality = graphViewer.Graph.DefaultAssemblerQuality;
            QualitySelector.Selector.SelectedIndex = (defQuality is null || !enabledQualities.Contains(defQuality)) ? 0 : enabledQualities.IndexOf(defQuality);

            SetupAssemblerOptions();

            //Set every event handler last - after all initial values/setup are in place - mirroring
            //upstream's real construction order (the ctor's own "set these event handlers last" comment;
            //unlike EditFlowPanel, Designer.cs never pre-wires these for this panel).
            LowPriorityCheckBox.IsCheckedChanged += LowPriorityCheckBox_CheckedChanged;
            KeyNodeCheckBox.IsCheckedChanged += KeyNodeCheckBox_CheckedChanged;
            KeyNodeTitleInput.TextChanging += (_, _) => nodeController.SetKeyNodeTitle(KeyNodeTitleInput.Text ?? "");

            //Wiring only FixedAssemblersOption, matching upstream: Avalonia's shared GroupName unchecks the
            //other radio on any toggle, so this alone observes both directions (already proven by
            //EditFlowPanel's identical single-subscription pattern).
            FixedAssemblersOption.IsCheckedChanged += FixedAssemblerOption_CheckedChanged;
            FixedAssemblerInput.ValueChanged += FixedAssemblerInput_ValueChanged;
            NeighbourInput.ValueChanged += NeighbourInput_ValueChanged;
            ExtraProductivityInput.ValueChanged += ExtraProductivityInput_ValueChanged;
            BeaconCountInput.ValueChanged += BeaconInput_ValueChanged;
            BeaconsPerAssemblerInput.ValueChanged += BeaconInput_ValueChanged;
            ConstantBeaconInput.ValueChanged += BeaconInput_ValueChanged;

            QualitySelector.Selector.SelectionChanged += QualitySelector_SelectedIndexChanged;

            //Upstream's own construction-time catch-all: panel close does one final UpdateNodeValues().
            //DetachedFromVisualTree fires exactly when FloatingPanelHost.Close() removes this panel.
            DetachedFromVisualTree += (_, _) => graphViewer.Graph.UpdateNodeValues();
        }

        private void BuildLayout() {
            var rateRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            rateRow.Children.Add(AutoAssemblersOption);
            rateRow.Children.Add(FixedAssemblersOption);
            rateRow.Children.Add(FixedAssemblerInput);

            var keyNodeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            keyNodeRow.Children.Add(KeyNodeCheckBox);
            keyNodeRow.Children.Add(KeyNodeTitleLabel);
            keyNodeRow.Children.Add(KeyNodeTitleInput);

            var assemblerHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Background = SectionHeaderBrush };
            assemblerHeaderRow.Children.Add(SelectedAssemblerIcon);
            assemblerHeaderRow.Children.Add(AssemblerTitle);

            var fuelHeaderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Background = SectionHeaderBrush };
            fuelHeaderRow.Children.Add(SelectedFuelIcon);
            fuelHeaderRow.Children.Add(FuelTitle);

            var neighbourRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            neighbourRow.Children.Add(NeighboursLabel);
            neighbourRow.Children.Add(NeighbourInput);

            var extraProdRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            extraProdRow.Children.Add(ExtraProductivityLabel);
            extraProdRow.Children.Add(ExtraProductivityInput);

            //Upstream's AssemblerTable puts the picker grid (c0) beside the stat readout (c1) in the same
            //row instead of stacking them (Designer.cs: AssemblerTable.Controls.Add(AssemblerChoicePanel, 0,
            //1) / .Add(AssemblerInfoTable, 1, 1)) - restoring that horizontal grouping (review finding 2).
            var assemblerPickerColumn = new StackPanel { Spacing = 6 };
            assemblerPickerColumn.Children.Add(assemblerHeaderRow);
            assemblerPickerColumn.Children.Add(AssemblerChoicePanel);

            var assemblerInfoColumn = new StackPanel { Spacing = 2 };
            assemblerInfoColumn.Children.Add(InfoRow("Energy:", AssemblerEnergyLabel, AssemblerEnergyPercentLabel));
            assemblerInfoColumn.Children.Add(InfoRow2(AssemblerSpeedTitleLabel, AssemblerSpeedLabel, AssemblerSpeedPercentLabel));
            assemblerInfoColumn.Children.Add(InfoRow(null, AssemblerProductivityTitleLabel, AssemblerProductivityPercentLabel));
            assemblerInfoColumn.Children.Add(InfoRow2(AssemblerPollutionTitleLabel, AssemblerPollutionLabel, AssemblerPollutionPercentLabel));
            assemblerInfoColumn.Children.Add(InfoRow(null, AssemblerQualityTitleLabel, AssemblerQualityPercentLabel));
            assemblerInfoColumn.Children.Add(InfoRow2(GeneratorTemperatureLabel, GeneratorTemperatureRangeLabel, null));
            assemblerInfoColumn.Children.Add(neighbourRow);
            assemblerInfoColumn.Children.Add(extraProdRow);

            var assemblerMainRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            assemblerMainRow.Children.Add(assemblerPickerColumn);
            assemblerMainRow.Children.Add(assemblerInfoColumn);

            //Same horizontal grouping for the module row (Designer.cs row 4/5: AModulesLabel+
            //SelectedAModulesPanel in c0, AModuleOptionsLabel+AModulesChoicePanel in c1) - this is also the
            //fix for review finding 3, since the equipped-module column growing no longer pushes the
            //available-module column (and its click targets) down when it sits beside it instead of above it.
            var aModulesColumn = new StackPanel { Spacing = 4 };
            aModulesColumn.Children.Add(AModulesLabel);
            aModulesColumn.Children.Add(selectedAModulesBox);
            var aModuleOptionsColumn = new StackPanel { Spacing = 4 };
            aModuleOptionsColumn.Children.Add(AModuleOptionsLabel);
            aModuleOptionsColumn.Children.Add(AModulesChoicePanel);
            var aModulesRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            aModulesRow.Children.Add(aModulesColumn);
            aModulesRow.Children.Add(aModuleOptionsColumn);

            //Upstream's RateOptionsTable groups the rate row, key-node row, and low-priority/quality row into
            //one uniformly-dark block (Designer.cs BackColor(40,40,40), rows 0-2) rather than each floating
            //loose against the outer panel's own black - matches the top solid block in the review's upstream
            //reference screenshot (image "3").
            var rateHeaderColumn = new StackPanel { Spacing = 4 };
            rateHeaderColumn.Children.Add(AssemblerRateLabel);
            rateHeaderColumn.Children.Add(rateRow);
            var lowPriorityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            lowPriorityRow.Children.Add(LowPriorityCheckBox);
            lowPriorityRow.Children.Add(QualitySelector);
            var rateOptionsSection = new StackPanel {
                Background = SectionHeaderBrush,
                Spacing = 6,
                Children = { rateHeaderColumn, keyNodeRow, lowPriorityRow },
            };

            //Upstream's AssemblerTable is one mid-gray section (Designer.cs BackColor(65,65,65)) spanning the
            //assembler picker/stats, the fuel picker, and the assembler module pickers together - grouping
            //them the same way here (review finding B1/B2) instead of leaving them as loose root children.
            AssemblerSection = new StackPanel {
                Background = SectionBodyBrush,
                Spacing = 6,
                Children = { assemblerMainRow, fuelHeaderRow, FuelOptionsPanel, aModulesRow },
            };

            root.Children.Add(rateOptionsSection);
            root.Children.Add(AssemblerSection);
            root.Children.Add(BeaconSection);
        }

        //Upstream declares these labels' Font in points (Designer.cs "Microsoft Sans Serif", 10F[, Bold]);
        //Avalonia's FontSize is device-independent pixels at 96 DPI, so the point value needs the standard
        //96/72 conversion rather than a bare copy - a bare copy is what made this panel's headers render
        //too small against upstream (review finding 1).
        private const double UpstreamHeaderFontSize = 10.0 * 96.0 / 72.0;

        //AssemblerRateLabel/AssemblerTitle/FuelTitle/BeaconTitle: upstream's only Bold+10F labels.
        private static TextBlock Header(string text) => new() { Text = text, Foreground = Brushes.White, FontWeight = FontWeight.Bold, FontSize = UpstreamHeaderFontSize };
        //NeighboursLabel/ExtraProductivityLabel/A|BModulesLabel/A|BModuleOptionsLabel: upstream's Regular+10F labels.
        private static TextBlock SectionLabel(string text) => new() { Text = text, Foreground = Brushes.White, FontSize = UpstreamHeaderFontSize };
        //Every other title label (Speed:/Productivity:/Pollution:/Quality:/Temperature Range:): upstream sets
        //no explicit Font at all here, so this stays at the panel's ambient size, same as Value() below.
        private static TextBlock Caption(string text) => new() { Text = text, Foreground = Brushes.White };
        private static TextBlock Value() => new() { Text = "", Foreground = Brushes.White };
        private static WrapPanel NewGrid(int cellSize = PickerCellSize) => new() { ItemWidth = cellSize, ItemHeight = cellSize };

        //Upstream's SelectedAModulesPanel/SelectedBModulesPanel are a fixed-size Panel (Designer.cs
        //Size(180, 109), AutoScroll=true, BorderStyle=Fixed3D) wrapped around the growing module-icon grid -
        //equipping more modules scrolls inside that fixed box instead of growing it, which is what keeps the
        //module-options column beside it from ever moving (review finding 3: our port used to let this grid
        //grow freely, pushing the click target for "add module" down the panel on every add).
        private static ScrollViewer FixedModuleBox(Control content) => new() {
            Content = content,
            Width = 180,
            Height = 109,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };

        private static NumericUpDown BeaconValueInput(string formatString) => new() {
            Minimum = 0,
            Maximum = 1000,
            Width = BeaconValueInputWidth,
            MinWidth = BeaconValueInputWidth,
            FormatString = formatString,
            VerticalAlignment = VerticalAlignment.Center,
        };

        private static StackPanel LabeledRow(string label, Control input) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
            row.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White, Width = 70, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(input);
            return row;
        }

        private static StackPanel InfoRow(string? label, TextBlock a, TextBlock? b = null) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            if (label is not null)
                row.Children.Add(new TextBlock { Text = label, Foreground = Brushes.White });
            row.Children.Add(a);
            if (b is not null)
                row.Children.Add(b);
            return row;
        }

        private static StackPanel InfoRow2(Control a, Control b, Control? c) {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            row.Children.Add(a);
            row.Children.Add(b);
            if (c is not null)
                row.Children.Add(c);
            return row;
        }

        //------------------------------------------------------------------------------------------------------Rate / key node

        private void InitializeRates() {
            if (nodeData.RateType == RateType.Auto) {
                AutoAssemblersOption.IsChecked = true;
                FixedAssemblerInput.IsEnabled = false;
                FixedAssemblerInput.Value = Math.Min(FixedAssemblerInput.Maximum, (decimal)nodeData.ActualSetValue);
            } else {
                FixedAssemblersOption.IsChecked = true;
                FixedAssemblerInput.IsEnabled = true;
                FixedAssemblerInput.Value = Math.Min(FixedAssemblerInput.Maximum, (decimal)nodeData.DesiredSetValue);
            }
            UpdateDecimals(FixedAssemblerInput, 4);
        }

        private void SetFixedRate() {
            if (nodeData.DesiredSetValue != (double)(FixedAssemblerInput.Value ?? 0)) {
                nodeController.SetDesiredSetValue((double)(FixedAssemblerInput.Value ?? 0));
                graphViewer.Graph.UpdateNodeValues();
                UpdateAssemblerInfo();
                UpdateBeaconInfo();
                RequestRedraw?.Invoke();
            }
        }

        private void FixedAssemblerOption_CheckedChanged(object? sender, RoutedEventArgs e) {
            FixedAssemblerInput.IsEnabled = FixedAssemblersOption.IsChecked ?? false;
            RateType updatedRateType = (FixedAssemblersOption.IsChecked ?? false) ? RateType.Manual : RateType.Auto;

            if (nodeData.RateType != updatedRateType) {
                nodeController.SetRateType(updatedRateType);
                nodeController.SetDesiredSetValue((double)(FixedAssemblerInput.Value ?? 0));
                graphViewer.Graph.UpdateNodeValues();
                UpdateAssemblerInfo();
                UpdateBeaconInfo();
                RequestRedraw?.Invoke();
            }
        }

        private void FixedAssemblerInput_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
            SetFixedRate();
            UpdateDecimals(FixedAssemblerInput, 2);
        }

        private void LowPriorityCheckBox_CheckedChanged(object? sender, RoutedEventArgs e) {
            nodeController.SetPriority(LowPriorityCheckBox.IsChecked ?? false);
            graphViewer.Graph.UpdateNodeValues();
        }

        private void KeyNodeCheckBox_CheckedChanged(object? sender, RoutedEventArgs e) {
            nodeController.SetKeyNode(KeyNodeCheckBox.IsChecked ?? false);
            KeyNodeTitleLabel.IsVisible = nodeData.KeyNode;
            KeyNodeTitleInput.IsVisible = nodeData.KeyNode;
            KeyNodeTitleInput.Text = nodeData.KeyNodeTitle;
            RequestRedraw?.Invoke();
            RequestReposition?.Invoke();
        }

        //------------------------------------------------------------------------------------------------------Neighbour / extra productivity

        private void SetNeighbourBonus() {
            if (nodeData.NeighbourCount != (double)(NeighbourInput.Value ?? 0)) {
                nodeController.SetNeighbourCount((double)(NeighbourInput.Value ?? 0));
                graphViewer.Graph.UpdateNodeValues();
                UpdateAssemblerInfo();
            }
        }

        private void NeighbourInput_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
            SetNeighbourBonus();
            UpdateDecimals(NeighbourInput, 2);
        }

        private void SetExtraProductivityBonus() {
            if (nodeData.ExtraProductivity != (double)(ExtraProductivityInput.Value ?? 0) / 100) {
                nodeController.SetExtraProductivityBonus((double)(ExtraProductivityInput.Value ?? 0) / 100);
                graphViewer.Graph.UpdateNodeValues();
                UpdateAssemblerInfo();
            }
        }

        private void ExtraProductivityInput_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) => SetExtraProductivityBonus();

        //------------------------------------------------------------------------------------------------------Beacon values (RISK 1: only BeaconCountInput re-solves)

        private void SetBeaconValues(bool graphUpdateRequired) {
            double count = (double)(BeaconCountInput.Value ?? 0);
            double perAssembler = (double)(BeaconsPerAssemblerInput.Value ?? 0);
            double additional = (double)(ConstantBeaconInput.Value ?? 0);

            if (nodeData.BeaconCount != count || nodeData.BeaconsPerAssembler != perAssembler || nodeData.BeaconsConst != additional) {
                nodeController.SetBeaconCount(count);
                nodeController.SetBeaconsPerAssembler(perAssembler);
                nodeController.SetBeaconsCont(additional);

                if (graphUpdateRequired) //only graph update worthy change is the # of beacons - the others arent as important
                    graphViewer.Graph.UpdateNodeValues();

                UpdateAssemblerInfo();
                UpdateBeaconInfo();
                RequestRedraw?.Invoke();
            }
        }

        private void BeaconInput_ValueChanged(object? sender, NumericUpDownValueChangedEventArgs e) {
            var nud = (NumericUpDown)sender!;
            SetBeaconValues(sender == BeaconCountInput);
            UpdateDecimals(nud, 2);
        }

        //------------------------------------------------------------------------------------------------------Quality selector (rebuilds option lists, doesn't itself re-solve)

        private void QualitySelector_SelectedIndexChanged(object? sender, SelectionChangedEventArgs e) {
            SetupAssemblerOptions();
            SetupAssemblerModuleOptions();
            SetupBeaconOptions();
            SetupBeaconModuleOptions();

            graphViewer.Graph.DefaultAssemblerQuality = QualitySelector.SelectedQuality;
            RequestReposition?.Invoke();
        }

        //------------------------------------------------------------------------------------------------------Assembler picker

        private void SetupAssemblerOptions() {
            if (nodeData.BaseRecipe is not { Recipe: IRecipe baseRecipe })
                return;

            AssemblerChoicePanel.Children.Clear();
            assemblerOptions.Clear();
            foreach (IAssembler assembler in baseRecipe.Assemblers.Where(a => a.Enabled)) {
                IconButton button = CreatePickerButton(assembler, new AssemblerQualityPair(assembler, QualitySelector.SelectedQuality).Icon);
                button.Released += AssemblerButton_Released;
                AssemblerChoicePanel.Children.Add(button);
                assemblerOptions.Add(button);
            }

            UpdateAssembler();
        }

        private void AssemblerButton_Released(object? sender, PointerReleasedEventArgs e) {
            if (sender is not IconButton { DataObject: IAssembler newAssembler } || e.InitialPressMouseButton != MouseButton.Left)
                return;
            nodeController.SetAssembler(new AssemblerQualityPair(newAssembler, QualitySelector.SelectedQuality));
            graphViewer.Graph.UpdateNodeValues();
            UpdateAssembler();
            RequestRedraw?.Invoke();
        }

        private void UpdateAssembler() {
            foreach (IconButton button in assemblerOptions)
                if (button.DataObject is IAssembler asm)
                    button.SetFillColor((asm == nodeData.SelectedAssembler.Assembler && QualitySelector.SelectedQuality == nodeData.SelectedAssembler.Quality) ? SelectedColor
                        : (asm.IsMissing || !asm.Available) ? ErrorColor : DefaultButtonColor);

            //Quirk ported verbatim from upstream: these two only ever get turned OFF here, never back on -
            //upstream's own UpdateAssembler never re-shows them once hidden by a prior assembler switch.
            if (nodeData.SelectedAssembler.Assembler.EntityType != EntityType.Reactor) {
                NeighbourInput.IsVisible = false;
                NeighboursLabel.IsVisible = false;
            }
            if (nodeData.BaseRecipe is { Recipe: IRecipe productivityRecipe } && !productivityRecipe.HasProductivityResearch
                && nodeData.SelectedAssembler.Assembler.EntityType != EntityType.Miner && !graphViewer.Graph.EnableExtraProductivityForNonMiners) {
                ExtraProductivityInput.IsVisible = false;
                ExtraProductivityLabel.IsVisible = false;
            }

            FuelTitle.IsVisible = nodeData.SelectedAssembler.Assembler.IsBurner;
            SelectedFuelIcon.IsVisible = nodeData.SelectedAssembler.Assembler.IsBurner;
            FuelOptionsPanel.IsVisible = nodeData.SelectedAssembler.Assembler.IsBurner;
            SetupFuelOptions();

            List<IModule> moduleOptions = GetAssemblerModuleOptions();
            bool showModules = nodeData.SelectedAssembler.Assembler.ModuleSlots > 0 && moduleOptions.Count > 0;
            AModulesLabel.IsVisible = showModules;
            AModuleOptionsLabel.IsVisible = showModules;
            selectedAModulesBox.IsVisible = showModules;
            AModulesChoicePanel.IsVisible = showModules;
            SetupAssemblerModuleOptions();

            SetupBeaconOptions();
            BeaconSection.IsVisible = beaconOptions.Count != 0;

            RequestReposition?.Invoke();
        }

        //------------------------------------------------------------------------------------------------------Fuel picker

        private void SetupFuelOptions() {
            List<IItem> fuels = [.. nodeData.SelectedAssembler.Assembler.Fuels.Where(f => f.ProductionRecipes.Any(r => r.Enabled && r.Assemblers.Any(a => a.Enabled)))];

            FuelOptionsPanel.Children.Clear();
            fuelOptions.Clear();
            foreach (IItem fuel in fuels) {
                IconButton button = CreatePickerButton(fuel, fuel.Icon);
                button.Released += FuelButton_Released;
                FuelOptionsPanel.Children.Add(button);
                fuelOptions.Add(button);
            }

            UpdateFuel();
        }

        private void FuelButton_Released(object? sender, PointerReleasedEventArgs e) {
            if (sender is not IconButton { DataObject: IItem newFuel } || e.InitialPressMouseButton != MouseButton.Left)
                return;
            nodeController.SetFuel(newFuel);
            graphViewer.Graph.UpdateNodeValues();
            UpdateFuel();
            RequestRedraw?.Invoke();
        }

        private void UpdateFuel() {
            foreach (IconButton button in fuelOptions)
                if (button.DataObject is IItem item)
                    button.SetFillColor(item == nodeData.Fuel ? SelectedColor
                        : (item.IsMissing || !item.Available || !item.ProductionRecipes.Any(r => r.Available && r.Assemblers.Any(a => a.Available))) ? ErrorColor : DefaultButtonColor);

            FuelTitle.Text = string.Format(DisplayCulture.Format, "Fuel: {0}", nodeData.Fuel is null ? "-none-" : nodeData.Fuel.FriendlyName);
            if (nodeData.Fuel is IItem selectedFuel)
                SelectedFuelIcon.SetPopulated(selectedFuel, selectedFuel.Icon, DefaultButtonColor);
            else
                SelectedFuelIcon.SetEmpty();

            UpdateAssemblerInfo();
        }

        //------------------------------------------------------------------------------------------------------Assembler modules

        private List<IModule> GetAssemblerModuleOptions() =>
            nodeData.SelectedAssembler.Assembler.AllowModules && nodeData.BaseRecipe is { Recipe: IRecipe recipe }
                ? [.. recipe.AssemblerModules.Intersect(nodeData.SelectedAssembler.Assembler.Modules).Where(m => m.Enabled).OrderBy(m => m.LFriendlyName)]
                : [];

        private void SetupAssemblerModuleOptions() {
            List<IModule> moduleOptions = GetAssemblerModuleOptions();

            AModulesChoicePanel.Children.Clear();
            aModuleOptionButtons.Clear();
            foreach (IModule module in moduleOptions) {
                var pair = new ModuleQualityPair(module, QualitySelector.SelectedQuality);
                IconButton button = CreatePickerButton(module, pair.Icon);
                if (!module.Available)
                    button.SetFillColor(ErrorColor);
                button.Released += AModuleOptionButton_Released;
                AModulesChoicePanel.Children.Add(button);
                aModuleOptionButtons.Add(button);
            }

            UpdateAssemblerModules();
        }

        private void AModuleOptionButton_Released(object? sender, PointerReleasedEventArgs e) {
            if (sender is not IconButton { DataObject: IModule newModule })
                return;
            var pair = new ModuleQualityPair(newModule, QualitySelector.SelectedQuality);
            if (e.InitialPressMouseButton == MouseButton.Left)
                nodeController.AddAssemblerModule(pair);
            else if (e.InitialPressMouseButton == MouseButton.Right)
                nodeController.AddAssemblerModules(pair);
            else
                return;

            graphViewer.Graph.UpdateNodeValues();
            UpdateAssemblerModules();
            RequestRedraw?.Invoke();
        }

        private void AModuleButton_Released(object? sender, PointerReleasedEventArgs e) {
            if (sender is not IconButton button)
                return;
            int index = assemblerModuleButtons.IndexOf(button);
            if (index < 0)
                return;

            if (e.InitialPressMouseButton == MouseButton.Left)
                nodeController.RemoveAssemblerModule(index);
            else if (e.InitialPressMouseButton == MouseButton.Right)
                nodeController.RemoveAssemblerModules(nodeData.AssemblerModules[index]);
            else
                return;

            graphViewer.Graph.UpdateNodeValues();
            UpdateAssemblerModules();
            RequestRedraw?.Invoke();
        }

        private void UpdateAssemblerModules() {
            foreach (IconButton button in aModuleOptionButtons)
                button.IsEnabled = nodeData.AssemblerModules.Count < nodeData.SelectedAssembler.Assembler.ModuleSlots;

            List<IModule> moduleOptions = nodeData.BaseRecipe is { Recipe: IRecipe recipe }
                ? [.. recipe.AssemblerModules.Intersect(nodeData.SelectedAssembler.Assembler.Modules).OrderBy(m => m.LFriendlyName)]
                : [];

            SelectedAModulesPanel.Children.Clear();
            assemblerModuleButtons.Clear();
            for (int i = 0; i < nodeData.AssemblerModules.Count; i++) {
                ModuleQualityPair pair = nodeData.AssemblerModules[i];
                IconButton button = CreatePickerButton(pair.Module, pair.Icon);
                if (pair.Module.IsMissing || !pair.Module.Available || !pair.Module.Enabled || !moduleOptions.Contains(pair.Module) || i >= nodeData.SelectedAssembler.Assembler.ModuleSlots)
                    button.SetFillColor(ErrorColor);
                button.Released += AModuleButton_Released;
                SelectedAModulesPanel.Children.Add(button);
                assemblerModuleButtons.Add(button);
            }

            AModulesLabel.Text = string.Format(DisplayCulture.Format, "Modules ({0}/{1}):", nodeData.AssemblerModules.Count, nodeData.SelectedAssembler.Assembler.ModuleSlots);
            UpdateAssemblerInfo();
        }

        //------------------------------------------------------------------------------------------------------Beacon picker

        private void SetupBeaconOptions() {
            if (nodeData.BaseRecipe is not { Recipe: IRecipe beaconHostRecipe })
                return;

            List<IModule> moduleOptions = [.. beaconHostRecipe.BeaconModules];

            BeaconChoicePanel.Children.Clear();
            beaconOptions.Clear();
            if (nodeData.SelectedAssembler.Assembler.AllowBeacons) {
                foreach (IBeacon beacon in panelCache.Beacons.Values.Where(b => b.Enabled)) {
                    if (!moduleOptions.Any(m => beacon.Modules.Contains(m)))
                        continue;

                    var pair = new BeaconQualityPair(beacon, QualitySelector.SelectedQuality);
                    IconButton button = CreatePickerButton(beacon, pair.Icon);
                    button.Released += BeaconButton_Released;
                    BeaconChoicePanel.Children.Add(button);
                    beaconOptions.Add(button);
                }
            }

            UpdateBeacon();
        }

        private void BeaconButton_Released(object? sender, PointerReleasedEventArgs e) {
            if (sender is not IconButton { DataObject: IBeacon newBeacon } || e.InitialPressMouseButton != MouseButton.Left)
                return;
            var newBeaconQP = new BeaconQualityPair(newBeacon, QualitySelector.SelectedQuality);

            if (nodeData.SelectedBeacon == newBeaconQP)
                nodeController.ClearBeacon();
            else
                nodeController.SetBeacon(newBeaconQP);
            graphViewer.Graph.UpdateNodeValues();
            UpdateBeacon();
            RequestRedraw?.Invoke();
        }

        private void UpdateBeacon() {
            foreach (IconButton button in beaconOptions)
                if (button.DataObject is IBeacon bcn)
                    button.SetFillColor(nodeData.SelectedBeacon is { Beacon: IBeacon selBeacon, Quality: IQuality selQuality } && bcn == selBeacon && QualitySelector.SelectedQuality == selQuality
                        ? SelectedColor
                        : (bcn.IsMissing || !bcn.Available) ? ErrorColor : DefaultButtonColor);

            List<IModule> moduleOptions = GetBeaconModuleOptions();
            bool showModules = nodeData.SelectedBeacon is { Beacon: IBeacon beaconForModules } && beaconForModules.ModuleSlots > 0 && moduleOptions.Count > 0;

            BeaconValuesPanel.IsVisible = nodeData.SelectedBeacon;
            BeaconInfoPanel.IsVisible = nodeData.SelectedBeacon;

            BModulesLabel.IsVisible = showModules;
            BModuleOptionsLabel.IsVisible = showModules;
            selectedBModulesBox.IsVisible = showModules;
            BModulesChoicePanel.IsVisible = showModules;
            SetupBeaconModuleOptions();

            if (nodeData.SelectedBeacon)
                SetBeaconValues(graphUpdateRequired: true);

            if (nodeData.SelectedBeacon is { Beacon: IBeacon selectedBeacon, Quality: IQuality selectedBeaconQuality }) {
                BeaconTitle.Text = string.Format(DisplayCulture.Format, "Beacon: {0}", selectedBeacon.FriendlyName);
                SelectedBeaconIcon.SetPopulated(selectedBeacon, nodeData.SelectedBeacon.Icon, DefaultButtonColor);
            } else {
                BeaconTitle.Text = "Beacon: -none-";
                SelectedBeaconIcon.SetEmpty();
            }
            UpdateBeaconInfo();
        }

        //------------------------------------------------------------------------------------------------------Beacon modules

        private List<IModule> GetBeaconModuleOptions() =>
            nodeData.SelectedAssembler.Assembler.AllowBeacons && nodeData.SelectedBeacon is { Beacon: IBeacon beacon } && nodeData.BaseRecipe is { Recipe: IRecipe recipe }
                ? [.. recipe.BeaconModules.Intersect(beacon.Modules).Where(m => m.Enabled).OrderBy(m => m.LFriendlyName)]
                : [];

        private void SetupBeaconModuleOptions() {
            List<IModule> moduleOptions = GetBeaconModuleOptions();

            BModulesChoicePanel.Children.Clear();
            bModuleOptionButtons.Clear();
            foreach (IModule module in moduleOptions) {
                var pair = new ModuleQualityPair(module, QualitySelector.SelectedQuality);
                IconButton button = CreatePickerButton(module, pair.Icon);
                if (!module.Available)
                    button.SetFillColor(ErrorColor);
                button.Released += BModuleOptionButton_Released;
                BModulesChoicePanel.Children.Add(button);
                bModuleOptionButtons.Add(button);
            }

            UpdateBeaconModules();
        }

        private void BModuleOptionButton_Released(object? sender, PointerReleasedEventArgs e) {
            if (sender is not IconButton { DataObject: IModule newModule })
                return;
            var pair = new ModuleQualityPair(newModule, QualitySelector.SelectedQuality);
            if (e.InitialPressMouseButton == MouseButton.Left)
                nodeController.AddBeaconModule(pair);
            else if (e.InitialPressMouseButton == MouseButton.Right)
                nodeController.AddBeaconModules(pair);
            else
                return;

            graphViewer.Graph.UpdateNodeValues();
            UpdateBeaconModules();
            RequestRedraw?.Invoke();
        }

        private void BModuleButton_Released(object? sender, PointerReleasedEventArgs e) {
            if (sender is not IconButton button)
                return;
            int index = beaconModuleButtons.IndexOf(button);
            if (index < 0)
                return;

            if (e.InitialPressMouseButton == MouseButton.Left)
                nodeController.RemoveBeaconModule(index);
            else if (e.InitialPressMouseButton == MouseButton.Right)
                nodeController.RemoveBeaconModules(nodeData.BeaconModules[index]);
            else
                return;

            graphViewer.Graph.UpdateNodeValues();
            UpdateBeaconModules();
            RequestRedraw?.Invoke();
        }

        private void UpdateBeaconModules() {
            int moduleSlots = nodeData.SelectedBeacon is { Beacon: IBeacon beaconForSlots } ? beaconForSlots.ModuleSlots : 0;
            foreach (IconButton button in bModuleOptionButtons)
                button.IsEnabled = nodeData.BeaconModules.Count < moduleSlots;

            List<IModule> moduleOptions = GetBeaconModuleOptions();

            SelectedBModulesPanel.Children.Clear();
            beaconModuleButtons.Clear();
            for (int i = 0; i < nodeData.BeaconModules.Count; i++) {
                ModuleQualityPair pair = nodeData.BeaconModules[i];
                IconButton button = CreatePickerButton(pair.Module, pair.Icon);
                if (pair.Module.IsMissing || !pair.Module.Available || !pair.Module.Enabled || !moduleOptions.Contains(pair.Module) || i >= moduleSlots)
                    button.SetFillColor(ErrorColor);
                button.Released += BModuleButton_Released;
                SelectedBModulesPanel.Children.Add(button);
                beaconModuleButtons.Add(button);
            }

            BModulesLabel.Text = string.Format(DisplayCulture.Format, "Modules ({0}/{1}):", nodeData.BeaconModules.Count, moduleSlots);

            UpdateBeaconInfo();
            UpdateAssemblerInfo(); //for the impact of the beacon
        }

        //------------------------------------------------------------------------------------------------------Stat readout

        private void UpdateAssemblerInfo() {
            AssemblerRateLabel.Text = string.Format(DisplayCulture.Format, "# of {0}:", nodeData.SelectedAssembler.Assembler.GetEntityTypeName(true));
            AssemblerTitle.Text = string.Format(DisplayCulture.Format, "{0}: {1}", nodeData.SelectedAssembler.Assembler.GetEntityTypeName(false), nodeData.SelectedAssembler.Assembler.FriendlyName);
            SelectedAssemblerIcon.SetPopulated(nodeData.SelectedAssembler.Assembler, nodeData.SelectedAssembler.Icon, DefaultButtonColor);

            AssemblerEnergyPercentLabel.Text = nodeData.GetConsumptionMultiplier().ToString("P0", DisplayCulture.Format);
            AssemblerSpeedPercentLabel.Text = nodeData.GetSpeedMultiplier().ToString("P0", DisplayCulture.Format);
            AssemblerProductivityPercentLabel.Text = nodeData.GetProductivityMultiplier().ToString("P0", DisplayCulture.Format);
            AssemblerPollutionPercentLabel.Text = nodeData.GetPollutionMultiplier().ToString("P0", DisplayCulture.Format);
            AssemblerQualityPercentLabel.Text = nodeData.GetQualityMultiplier().ToString("P0", DisplayCulture.Format);

            bool isAssembler = nodeData.SelectedAssembler.Assembler.EntityType is EntityType.Assembler or EntityType.Miner or EntityType.OffshorePump;
            AssemblerSpeedTitleLabel.IsVisible = isAssembler;
            AssemblerSpeedLabel.IsVisible = isAssembler;
            AssemblerSpeedPercentLabel.IsVisible = isAssembler;
            AssemblerProductivityTitleLabel.IsVisible = isAssembler;
            AssemblerProductivityPercentLabel.IsVisible = isAssembler;
            AssemblerPollutionTitleLabel.IsVisible = isAssembler;
            AssemblerPollutionPercentLabel.IsVisible = isAssembler;
            AssemblerQualityTitleLabel.IsVisible = isAssembler;
            AssemblerQualityPercentLabel.IsVisible = isAssembler;

            bool isGenerator = nodeData.SelectedAssembler.Assembler.EntityType == EntityType.Generator;
            GeneratorTemperatureLabel.IsVisible = isGenerator;
            GeneratorTemperatureRangeLabel.IsVisible = isGenerator;

            string rateName = graphViewer.Graph.GetRateName();
            AssemblerSpeedLabel.Text = string.Format(DisplayCulture.Format, "{0} ({1} crafts / {2})",
                nodeData.GetAssemblerSpeed().ToString("0.##", DisplayCulture.Format),
                nodeData.GetTotalCrafts() < 1 ? nodeData.GetTotalCrafts().ToString("0.####", DisplayCulture.Format) : nodeData.GetTotalCrafts().ToString("0.#", DisplayCulture.Format),
                rateName);

            AssemblerEnergyLabel.Text = nodeData.SelectedAssembler.Assembler.IsBurner && nodeData.Fuel != null
                ? string.Format(DisplayCulture.Format, "{0} ({1} fuel / {2})", GraphicsStuff.DoubleToEnergy(nodeData.GetAssemblerEnergyConsumption(), "W"), GraphicsStuff.DoubleToString(nodeData.GetTotalAssemblerFuelConsumption()), rateName)
                : GraphicsStuff.DoubleToEnergy(nodeData.GetAssemblerEnergyConsumption(), "W");

            AssemblerPollutionLabel.Text = string.Format(DisplayCulture.Format, "{0} / min", (nodeData.GetAssemblerPollutionProduction() * 60).ToString("0.##", DisplayCulture.Format));

            if (isGenerator) {
                double minTemp = nodeData.GetGeneratorMinimumTemperature();
                double maxTemp = nodeData.GetGeneratorMaximumTemperature();
                double operationalTemp = nodeData.SelectedAssembler.Assembler.OperationTemperature;
                double effectivity = nodeData.GetGeneratorEffectivity();

                GeneratorTemperatureRangeLabel.Text = double.IsInfinity(maxTemp)
                    ? string.Format(DisplayCulture.Format, "min {0}°c  (optimal: {1}°c)", Math.Round(minTemp, 1).ToString("0.#", DisplayCulture.Format), Math.Round(operationalTemp, 1).ToString("0.#", DisplayCulture.Format))
                    : string.Format(DisplayCulture.Format, "{0}-{1}°c  (optimal: {2}°c)", Math.Round(minTemp, 1).ToString("0.#", DisplayCulture.Format), Math.Round(maxTemp, 1).ToString("0.#", DisplayCulture.Format), Math.Round(operationalTemp, 1).ToString("0.#", DisplayCulture.Format));

                AssemblerEnergyLabel.Text = GraphicsStuff.DoubleToEnergy(nodeData.GetGeneratorElectricalProduction(), "W");
                AssemblerEnergyPercentLabel.Text = effectivity.ToString("P0", DisplayCulture.Format);
            }
        }

        private void UpdateBeaconInfo() {
            if (nodeData.SelectedBeacon is { Beacon: IBeacon beacon, Quality: IQuality beaconQuality }) {
                BeaconEnergyLabel.Text = GraphicsStuff.DoubleToEnergy(nodeData.GetBeaconEnergyConsumption(), "W");
                BeaconModuleCountLabel.Text = beacon.ModuleSlots.ToString(DisplayCulture.Format);
                BeaconEfficiencyLabel.Text = beacon.GetBeaconEffectivity(beaconQuality, nodeData.BeaconCount).ToString("P0", DisplayCulture.Format);
                TotalBeaconEnergyLabel.Text = GraphicsStuff.DoubleToEnergy(nodeData.GetTotalBeaconElectricalConsumption(), "W");
            } else {
                BeaconEnergyLabel.Text = "0 J";
                BeaconModuleCountLabel.Text = "0";
                BeaconEfficiencyLabel.Text = "0%";
                TotalBeaconEnergyLabel.Text = "0 J";
            }
            TotalBeaconsLabel.Text = nodeData.GetTotalBeacons().ToString(DisplayCulture.Format);
        }

        //------------------------------------------------------------------------------------------------------Helpers

        //Module cells (option and equipped alike) get SectionBodyColor rather than every other picker's
        //DefaultButtonColor: upstream's AModulesChoiceTable/SelectedAModulesTable/BModulesChoiceTable/
        //SelectedBModulesTable never set their own BackColor, so they inherit AssemblerTable/BeaconTable's
        //dark (65,65,65) body directly. That matters uniquely here because module cells are the only picker
        //buttons that ever go IsEnabled=false (full slots) - IconButton.PaintOnto's 40%-alpha grayscale
        //filter then composites over whatever backdrop this fills, and DefaultButtonColor's brighter
        //105-gray baked a loud, uniformly bright block instead of dimming into the section body like
        //upstream (review finding: "Module Options" rendering flat gray).
        private static IconButton CreatePickerButton(IDataObjectBase obj, SKBitmap? icon) {
            var button = new IconButton { Width = obj is IModule ? ModuleCellSize : PickerCellSize, Height = obj is IModule ? ModuleCellSize : PickerCellSize };
            button.SetPopulated(obj, icon, obj is IModule ? SectionBodyColor : DefaultButtonColor);
            return button;
        }

        private static void UpdateDecimals(NumericUpDown nud, int max) {
            if (nud.Value is not decimal value)
                return;
            nud.FormatString = $"N{Math.Min(GetDecimals(value), max)}";
        }

        private static int GetDecimals(decimal d, int i = 0) {
            decimal multiplied = (decimal)((double)d * Math.Pow(10, i));
            return Math.Round(multiplied) == multiplied || i >= 6 ? i : GetDecimals(d, i + 1);
        }

        //------------------------------------------------------------------------------------------------------Nested viewport (IViewportFittable)

        //Upstream's EditPanelViewportLayout.cs is layout-only (docs/panels-reference.md §3's "Nested viewport"
        //note) - it clamps the panel's outer bounds, and upstream relies on WinForms AutoScroll on individual
        //option panels for anything past that. This port's equivalent: the whole body sits in a ScrollViewer,
        //so once EditPanelViewportLayout.Apply (FloatingPanelHost.Reposition) clamps our outer Height below
        //what the body naturally needs, content past that point scrolls instead of clipping silently.
        public void FitToViewport(double maxWidth, double maxHeight) {
            scrollHost.MaxWidth = Math.Max(1, maxWidth);
            scrollHost.MaxHeight = Math.Max(1, maxHeight);
        }

        //------------------------------------------------------------------------------------------------------Offscreen render (task deliverable PNG)

        //Resets Width/Height to auto first (matching EditPanelViewportLayout.MeasureNaturalSize/IRChooserPanel.
        //FitToViewport): FloatingPanelHost.Reposition pins both to fixed pixel values sized for whatever
        //viewport last hosted this panel live, and a render at any other size otherwise measures/arranges
        //against those stale fixed values instead of the size actually requested here.
        public void RenderOffscreen(SKCanvas canvas, int width, int height) {
            Width = double.NaN;
            Height = double.NaN;
            Measure(new Size(width, height));
            Arrange(new Rect(0, 0, width, height));

            using var backgroundPaint = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, width, height, backgroundPaint);

            PaintVisual(canvas, this, 0, 0);
        }

        private static void PaintVisual(SKCanvas canvas, Control control, float parentAbsX, float parentAbsY) {
            if (!control.IsVisible)
                return;

            Rect bounds = control.Bounds;
            float x = parentAbsX + (float)bounds.X;
            float y = parentAbsY + (float)bounds.Y;
            float w = (float)bounds.Width;
            float h = (float)bounds.Height;

            switch (control) {
                case Border border:
                    FillRect(canvas, border.Background, x, y, w, h);
                    break;
                //Section chrome (rate-options/assembler/beacon bodies, and each header band nested inside
                //them) paints via Panel.Background rather than a Border, since these are plain StackPanels -
                //fall through to the child walk below instead of returning, same as the Border case.
                case Panel panel:
                    FillRect(canvas, panel.Background, x, y, w, h);
                    break;
                case IconButton iconButton:
                    iconButton.PaintOnto(canvas, new SKRect(x, y, x + w, y + h));
                    return;
                case RecipePanel recipePanel:
                    canvas.Save();
                    canvas.Translate(x, y);
                    recipePanel.PaintOnto(canvas);
                    canvas.Restore();
                    return;
                case TextBox textBox:
                    FillRect(canvas, textBox.Background, x, y, w, h);
                    DrawText(canvas, textBox.Text, textBox.Foreground, x + 2, y, h);
                    return;
                case CheckBox checkBox:
                    DrawCheckbox(canvas, checkBox.Content as string, checkBox.IsChecked == true, checkBox.Foreground, x, y, h);
                    return;
                case RadioButton radioButton:
                    DrawCheckbox(canvas, radioButton.Content as string, radioButton.IsChecked == true, radioButton.Foreground, x, y, h);
                    return;
                case NumericUpDown numericUpDown:
                    FillRect(canvas, Brushes.DimGray, x, y, w, h);
                    DrawText(canvas, numericUpDown.Value?.ToString(numericUpDown.FormatString, CultureInfo.InvariantCulture) ?? "", Brushes.White, x + 2, y, h);
                    return;
                case ComboBox comboBox:
                    FillRect(canvas, Brushes.DimGray, x, y, w, h);
                    DrawText(canvas, comboBox.SelectedItem as string, Brushes.White, x + 2, y, h);
                    return;
                case TextBlock textBlock:
                    FillRect(canvas, textBlock.Background, x, y, w, h);
                    DrawText(canvas, textBlock.Text, textBlock.Foreground, x, y, h);
                    return;
            }

            foreach (Visual child in control.GetVisualChildren())
                if (child is Control childControl)
                    PaintVisual(canvas, childControl, x, y);
        }

        private static void FillRect(SKCanvas canvas, IBrush? brush, float x, float y, float w, float h) {
            if (brush is not ISolidColorBrush solid || w <= 0 || h <= 0)
                return;
            using var paint = new SKPaint { Color = ToSkColor(solid.Color), Style = SKPaintStyle.Fill };
            canvas.DrawRect(x, y, w, h, paint);
        }

        private static readonly SKFont ChromeFont = new() { Size = 11 };

        private static void DrawText(SKCanvas canvas, string? text, IBrush? brush, float x, float rowY, float rowHeight) {
            if (string.IsNullOrEmpty(text))
                return;
            SKColor color = brush is ISolidColorBrush solid ? ToSkColor(solid.Color) : SKColors.White;
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            SKFontMetrics metrics = ChromeFont.Metrics;
            float baseline = rowY + (rowHeight / 2) - ((metrics.Ascent + metrics.Descent) / 2);
            canvas.DrawText(text, x, baseline, ChromeFont, paint);
        }

        private static void DrawCheckbox(SKCanvas canvas, string? label, bool isChecked, IBrush? brush, float x, float y, float h) {
            const float boxSize = 12f;
            float boxY = y + (h - boxSize) / 2;
            using (var boxPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false })
                canvas.DrawRect(x, boxY, boxSize, boxSize, boxPaint);
            if (isChecked) {
                using var fillPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill };
                canvas.DrawRect(x + 2, boxY + 2, boxSize - 4, boxSize - 4, fillPaint);
            }
            DrawText(canvas, label, brush, x + boxSize + 4, y, h);
        }

        private static SKColor ToSkColor(Color c) => new(c.R, c.G, c.B, c.A);
    }
}
