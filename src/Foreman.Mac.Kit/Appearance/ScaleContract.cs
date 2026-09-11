using Avalonia;

namespace Foreman.Mac.Kit.Appearance {
    //A20 contract: multiplies sizing tokens by the UiScale step, re-pushed into Application.Resources.
    public static class ScaleContract {
        private static readonly string[] MultipliedKeys = [
            "Foreman.Type.LargeTitle.FontSize",
            "Foreman.Type.SectionHeader.FontSize",
            "Foreman.Type.Body.FontSize",
            "Foreman.Type.EmphasizedBody.FontSize",
            "Foreman.Type.Callout.FontSize",
            "Foreman.Type.Caption.FontSize",
            "Foreman.Type.WindowTitle.FontSize",
            "Foreman.Type.InspectorTitle.FontSize",
            "Foreman.Type.InspectorSubtitle.FontSize",
            "Foreman.Type.Browser.Subhead.FontSize",
            "Foreman.Type.SmallBody.FontSize",
            "Foreman.Type.AboutName.FontSize",
            "Foreman.Type.AboutTagline.FontSize",
            "Foreman.Type.AboutVersion.FontSize",
            "Foreman.Type.Browser.Label.FontSize",
            "Foreman.Type.Browser.Row.FontSize",
            "Foreman.Type.Kbd.FontSize",
            "Foreman.Type.Palette.Hint.FontSize",
            "Foreman.Type.Palette.Row.FontSize",
            "Foreman.Type.Palette.Sub.FontSize",
            "Foreman.Type.SearchField.FontSize",
            "Foreman.Type.ToolbarLabel.FontSize",
            "Foreman.Type.Canvas.FontSize",
            "Foreman.Size.InspectorWidth",
            "Foreman.Size.InspectorWidthMin",
            "Foreman.Size.InspectorWidthMax",
            "Foreman.Size.ControlHeightRegular",
            "Foreman.Size.ControlHeightLarge",
            "Foreman.Size.PickCell",
            "Foreman.Size.PaletteWidth",
            "Foreman.Size.PaletteRowIcon",
            "Foreman.Size.BrowserSidebar",
            "Foreman.Size.BrowserSidebarMax",
            "Foreman.Size.BrowserCell",
            "Foreman.Size.ChooserCell",
            "Foreman.Size.BrowserCellPitch",
            "Foreman.Size.BrowserCellRowHeight",
            "Foreman.Size.BrowserCellLabelHeight",
            "Foreman.Size.FormLabelColumn",
            "Foreman.Size.ToolbarItem",
            "Foreman.Size.NodePadding",
        ];

        //Remove-then-resolve (AppearanceHub.ApplyFocusRing's idiom) keeps repeated calls from compounding.
        public static void Apply(double step) {
            var app = Application.Current!;
            var resources = app.Resources;
            foreach (string key in MultipliedKeys) {
                resources.Remove(key);
                if (app.TryGetResource(key, app.ActualThemeVariant, out object? baseValue) && baseValue is double value)
                    resources[key] = value * step;
            }
        }
    }
}
