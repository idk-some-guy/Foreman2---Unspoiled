using Foreman;
using Foreman.Models;

namespace Foreman.Mac.Services {
    //Upstream only had a bool FlagDarkMode; we widen it to a tri-state to follow the OS theme by default.
    public enum ThemeMode { System, Light, Dark }

    //Names and defaults are verbatim from upstream Properties.Settings.Designer.cs.
    public sealed class AppSettings {
        public string CurrentPresetName { get; set; } = "";
        public ModuleSelector.Style DefaultModuleOption { get; set; } = ModuleSelector.Style.None;
        public int MinorGridlines { get; set; }
        public int MajorGridlines { get; set; }
        public bool AltGridlines { get; set; }
        public bool ShowHidden { get; set; }
        public bool IgnoreAssemblerStatus { get; set; }
        public bool DynamicLineWidth { get; set; }
        public bool RecipeNameOnlyFilter { get; set; }
        public int LevelOfDetail { get; set; }
        public AssemblerSelector.Style DefaultAssemblerOption { get; set; } = AssemblerSelector.Style.Worst;
        public ProductionGraph.RateUnit DefaultRateUnit { get; set; } = ProductionGraph.RateUnit.Per1Sec;
        public string LastSaveFileLocation { get; set; } = "";
        public bool ShowRecipeToolTip { get; set; } = true;
        public bool ShowUnavailable { get; set; }
        public bool LockedRecipeEditorPosition { get; set; }
        public int NodeCountForSimpleView { get; set; } = 300;
        public bool UseRecipeBWfilters { get; set; } = true;
        public bool ShowWarningArrows { get; set; } = true;
        public bool ShowErrorArrows { get; set; } = true;
        public bool AbbreviateSciPacks { get; set; } = true;
        public bool RoundAssemblerCount { get; set; }
        public bool EnableExtraProductivityForNonMiners { get; set; }
        public bool ShowDisconnectedArrows { get; set; }
        public NodeDirection DefaultNodeDirection { get; set; } = NodeDirection.Up;
        public bool FlagOUSuppliedNodes { get; set; }
        public bool ShowOUSuppliedArrows { get; set; }
        public bool IconsOnlyView { get; set; }
        public int IconsSize { get; set; } = 24;
        public bool SimplePassthroughNodes { get; set; }
        public bool ArrowsOnLinks { get; set; }
        public bool SmartNodeDirection { get; set; } = true;
        public ThemeMode FlagDarkMode { get; set; } = ThemeMode.System;
        public bool UpgradeRequired { get; set; } = true;
        public string AnnotTextFontFamily { get; set; } = "Segoe UI";
        public string AnnotTextFontSize { get; set; } = "14";
        public int AnnotTextFontStyle { get; set; } = 1;
        public int AnnotTextColorARGB { get; set; } = -16777216;
        public int AnnotTextBackColorARGB { get; set; }
        public int AnnotTextAlign { get; set; } = 1;
        public int AnnotShapeType { get; set; }
        public int AnnotShapeFillColorARGB { get; set; } = 5278975;
        public int AnnotShapeBorderColorARGB { get; set; } = -600016676;
        public int AnnotShapeBorderWidth { get; set; } = 2;

        //Advanced (Solver options) group box - no upstream Properties.Settings counterpart (reference
        //docs/panels-reference.md §5): upstream keeps these on SettingsForm.SettingsFormOptions only,
        //round-tripped through MainForm.cs's GraphViewer.Graph fields instead of Properties.Settings.Default.
        public int QualitySteps { get; set; } = 1;
        public decimal LowPriorityPower { get; set; } = 4;
        public bool PullConsumerNodes { get; set; }
        public decimal PullConsumerNodesPower { get; set; } = 1;
    }
}
