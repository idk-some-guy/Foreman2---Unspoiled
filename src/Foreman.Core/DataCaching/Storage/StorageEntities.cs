using System;
using System.Collections.Generic;

namespace Foreman.DataCaching.Storage {
    //Keep these columns in lockstep with AppSettings.
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

        public double UiScale { get; set; }

        public bool InspectorPinned { get; set; }

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

        public bool ShowToolbar { get; set; } = true;
        public bool ShowWelcomeAtLaunch { get; set; } = true;
        public DateTime? OpenRecentClearedAt { get; set; }
        public double? GraphSummaryLeft { get; set; }
        public double? GraphSummaryTop { get; set; }
        public double? GraphSummaryWidth { get; set; }
        public double? GraphSummaryHeight { get; set; }
        public int GraphSummarySegment { get; set; }
        public double? ResearchLeft { get; set; }
        public double? ResearchTop { get; set; }
        public double? ResearchWidth { get; set; }
        public double? ResearchHeight { get; set; }
        public int ResearchSegment { get; set; }
        public int ResearchTechnologyFilter { get; set; }
    }

    public sealed class PresetDataRow {
        public string PresetName { get; set; } = "";
        public byte[] CompressedPJson { get; set; } = [];
        public string? CustomOverlayJson { get; set; }
        public DateTime ImportedUtc { get; set; }
    }

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

    public enum GraphVersionKind { Saved, Automatic, BeforeRestore }

    public sealed class GraphRow {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string PresetName { get; set; } = "";
        public int NodeCount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public DateTime LastOpenedAt { get; set; }
        public DateTime? SavedAt { get; set; }
        public string Data { get; set; } = "";
        public byte[]? Thumbnail { get; set; }
        public string? SourcePath { get; set; }
        public List<GraphVersionRow> Versions { get; set; } = [];
    }

    public sealed class GraphVersionRow {
        public Guid Id { get; set; }
        public Guid GraphId { get; set; }
        public DateTime CreatedAt { get; set; }
        public GraphVersionKind Kind { get; set; }
        public int NodeCount { get; set; }
        public string Data { get; set; } = "";
    }

    public sealed class SchemaVersionRow {
        public int Version { get; set; }
    }
}
