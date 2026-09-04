using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
    //Task 8 (contract #toolbar, gate fix 2): the 11 toolbar glyphs live in Themes/ToolbarIcons.axaml,
    //merged into Application.Resources (not StyleInclude'd) so MainWindow.axaml's StaticResource lookups
    //can reach them - proven here through the real App resources, not a hand-built dictionary.
    public class ToolbarIconTests {
        private static readonly string[] IconKeys = [
            "Foreman.Icon.New", "Foreman.Icon.Load", "Foreman.Icon.Save", "Foreman.Icon.AddItem",
            "Foreman.Icon.AddRecipe", "Foreman.Icon.Autoconnect", "Foreman.Icon.AlignSelected",
            "Foreman.Icon.ExportImage", "Foreman.Icon.GraphSummary", "Foreman.Icon.ViewOptions",
            "Foreman.Icon.Settings",
        ];

        [AvaloniaFact]
        public void AllElevenToolbarIconKeys_ResolveThroughAppResources() {
            foreach (string key in IconKeys) {
                Assert.True(Application.Current!.TryGetResource(key, null, out object? value), key);
                Assert.IsAssignableFrom<Geometry>(value);
            }
        }

        [AvaloniaFact]
        public void ViewOptionsFillKey_ResolvesThroughAppResources() {
            Assert.True(Application.Current!.TryGetResource("Foreman.Icon.ViewOptions.Fill", null, out object? value));
            Assert.IsAssignableFrom<Geometry>(value);
        }
    }
}
