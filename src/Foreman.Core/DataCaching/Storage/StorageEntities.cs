using System;

namespace Foreman.DataCaching.Storage {
    //One typed column per AppSettings property (Foreman.Mac/Services/AppSettings.cs).
    //Foreman.Core can't reference Foreman.Mac, so columns mirror the property SHAPES: enums as int,
    //decimals as decimal. Keep this in lockstep with AppSettings when either one changes.
    public sealed class SettingsRow {
        public int Id { get; set; }

        public string CurrentPresetName { get; set; } = "";
        public int DefaultModuleOption { get; set; }
        public int MinorGridlines { get; set; }
        public int MajorGridlines { get; set; }
        public bool AltGridlines { get; set; }
        public bool ShowHidden { get; set; }
        public bool IgnoreAssemblerStatus { get; set; }
        public bool DynamicLineWidth { get; set; }
        public int LevelOfDetail { get; set; }
        public int DefaultAssemblerOption { get; set; }
        public int DefaultRateUnit { get; set; }
        public string LastSaveFileLocation { get; set; } = "";
        public bool ShowUnavailable { get; set; }
        public int NodeCountForSimpleView { get; set; }
        public bool UseRecipeBWfilters { get; set; }
        public bool ShowWarningArrows { get; set; }
        public bool ShowErrorArrows { get; set; }
        public bool AbbreviateSciPacks { get; set; }
        public bool RoundAssemblerCount { get; set; }
        public bool EnableExtraProductivityForNonMiners { get; set; }
        public bool ShowDisconnectedArrows { get; set; }
        public int DefaultNodeDirection { get; set; }
        public bool FlagOUSuppliedNodes { get; set; }
        public bool ShowOUSuppliedArrows { get; set; }
        public bool IconsOnlyView { get; set; }
        public int IconsSize { get; set; }
        public bool SimplePassthroughNodes { get; set; }
        public bool ArrowsOnLinks { get; set; }
        public bool SmartNodeDirection { get; set; }
        public int FlagDarkMode { get; set; }
        public bool UpgradeRequired { get; set; }
        public string AnnotTextFontFamily { get; set; } = "";
        public string AnnotTextFontSize { get; set; } = "";
        public int AnnotTextFontStyle { get; set; }
        public int AnnotTextColorARGB { get; set; }
        public int AnnotTextBackColorARGB { get; set; }
        public int AnnotTextAlign { get; set; }
        public int AnnotShapeType { get; set; }
        public int AnnotShapeFillColorARGB { get; set; }
        public int AnnotShapeBorderColorARGB { get; set; }
        public int AnnotShapeBorderWidth { get; set; }
        public int QualitySteps { get; set; }
        public decimal LowPriorityPower { get; set; }
        public bool PullConsumerNodes { get; set; }
        public decimal PullConsumerNodesPower { get; set; }

        //Added schema version 2 - see ForemanDbContext.Open's v1->v2 ALTER TABLE step.
        public double UiScale { get; set; }

        //Added schema version 3 - see ForemanDbContext.Open's v2->v3 ALTER TABLE step.
        public bool InspectorPinned { get; set; }

        //Added schema version 4 with ShowRecipeToolTip and RecipeNameOnlyFilter dropped.
        public double InspectorWidth { get; set; }
        public double BrowserWidth { get; set; }
        public double BrowserHeight { get; set; }
        public string BrowserItemsGroup { get; set; } = "";
        public string BrowserRecipesGroup { get; set; } = "";
        public double? SettingsEnabledObjectsWidth { get; set; }
        public double? SettingsEnabledObjectsHeight { get; set; }
        public double? SettingsAdvancedWidth { get; set; }
        public double? SettingsAdvancedHeight { get; set; }
        public int DefaultLinkRouting { get; set; }
    }

    //A single imported preset, compressed with zstd; CustomOverlayJson holds the presetCustom
    //overlay when the user has one layered on top.
    public sealed class PresetDataRow {
        public string PresetName { get; set; } = "";
        public byte[] CompressedPJson { get; set; } = [];
        public string? CustomOverlayJson { get; set; }
        public DateTime ImportedUtc { get; set; }
    }

    //Content-addressed icon storage; PresetIconRow rows point in by Sha256 so identical icons
    //across presets share one blob.
    public sealed class IconBlob {
        public string Sha256 { get; set; } = "";
        public byte[] PngBytes { get; set; } = [];
    }

    public sealed class PresetIconRow {
        public string PresetName { get; set; } = "";
        public string IconName { get; set; } = "";
        public int Tint { get; set; }
        public string Sha256 { get; set; } = "";
    }

    public sealed class SchemaVersionRow {
        public int Version { get; set; }
    }
}
