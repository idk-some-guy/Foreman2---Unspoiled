using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
    //Guards against a literal Brushes.* or raw hex color anywhere under src/Foreman.Mac except GalleryWindow.
    public class TokenLiteralGuardTests {
        private static string RepoRoot => Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..");
        private static string AppSourceDirectory => Path.Combine(RepoRoot, "src", "Foreman.Mac");

        private static IEnumerable<string> SourceFiles(string extension) =>
            Directory.EnumerateFiles(AppSourceDirectory, $"*{extension}", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Where(path => !path.EndsWith($"GalleryWindow{extension}", StringComparison.Ordinal));

        [Fact]
        public void NoBrushesLiteral_OutsideGalleryWindow() {
            var offenders = new List<string>();

            foreach (string path in SourceFiles(".cs")) {
                foreach (Match match in Regex.Matches(File.ReadAllText(path), @"Brushes\.\w+")) {
                    if (match.Value != "Brushes.Transparent")
                        offenders.Add($"{path}: {match.Value}");
                }
            }

            Assert.Empty(offenders);
        }

        [Fact]
        public void NoHexColorLiteral_OutsideGalleryWindow() {
            var offenders = new List<string>();

            foreach (string path in SourceFiles(".cs").Concat(SourceFiles(".axaml"))) {
                foreach (Match match in Regex.Matches(File.ReadAllText(path), @"#[0-9A-Fa-f]{6,8}\b"))
                    offenders.Add($"{path}: {match.Value}");
            }

            Assert.Empty(offenders);
        }
    }
}
