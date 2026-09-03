using System;

namespace Foreman.DataCaching.Storage {
    //One typed column per AppSettings property (Foreman.Mac/Services/AppSettings.cs, 47 currently).
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
        public bool RecipeNameOnlyFilter { get; set; }
        public int LevelOfDetail { get; set; }
        public int DefaultAssemblerOption { get; set; }
        public int DefaultRateUnit { get; set; }
        public string LastSaveFileLocation { get; set; } = "";
        public bool ShowRecipeToolTip { get; set; }
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
