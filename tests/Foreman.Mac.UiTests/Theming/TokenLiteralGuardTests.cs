using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
    //Guards against a literal Brushes.* or raw hex color anywhere under src/Foreman.Mac; Canvas/ waits for its own token pass.
    public class TokenLiteralGuardTests {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        private static string AppSourceDirectory => Path.Combine(RepoRoot, "src", "Foreman.Mac");

        //Paths are made relative first: the test's own bin folder sits under the repo, so an absolute path always contains "bin".
        private static bool IsUnder(string path, string directory) =>
            Path.GetRelativePath(AppSourceDirectory, path).Split(Path.DirectorySeparatorChar).Contains(directory);

        private static IEnumerable<string> SourceFiles(string extension, bool includeCanvas = false) =>
            Directory.EnumerateFiles(AppSourceDirectory, $"*{extension}", SearchOption.AllDirectories)
                .Where(path => !IsUnder(path, "obj") && !IsUnder(path, "bin"))
                .Where(path => includeCanvas || !IsUnder(path, "Canvas"));

        //An unresolved repo root would pass both scans over zero files.
        private static string[] CoveredCsFiles() {
            string[] files = [.. SourceFiles(".cs")];
            Assert.Contains(files, path => path.EndsWith(Path.Combine("Views", "GalleryWindow.cs"), StringComparison.Ordinal));
            Assert.DoesNotContain(files, path => IsUnder(path, "Canvas"));
            Assert.True(files.Length >= 60, $"expected at least 60 app cs files outside Canvas, found {files.Length}");
            return files;
        }

        [Fact]
        public void NoBrushesLiteral_Anywhere() {
            var offenders = new List<string>();

            foreach (string path in CoveredCsFiles()) {
                foreach (Match match in Regex.Matches(File.ReadAllText(path), @"\bBrushes\.\w+")) {
                    if (match.Value != "Brushes.Transparent")
                        offenders.Add($"{path}: {match.Value}");
                }
            }

            Assert.Empty(offenders);
        }

        [Fact]
        public void NoHexColorLiteral_Anywhere() {
            var offenders = new List<string>();

            foreach (string path in CoveredCsFiles().Concat(SourceFiles(".axaml"))) {
                foreach (Match match in Regex.Matches(File.ReadAllText(path), @"#[0-9A-Fa-f]{6,8}\b"))
                    offenders.Add($"{path}: {match.Value}");
            }

            Assert.Empty(offenders);
        }

        //A style class fails silently once its selector is gone, so no legacy class name may survive anywhere, Canvas included.
        [Fact]
        public void NoLegacyStyleClass_Anywhere() {
            var offenders = new List<string>();

            foreach (string path in SourceFiles(".cs", includeCanvas: true).Concat(SourceFiles(".axaml", includeCanvas: true))) {
                foreach (Match match in Regex.Matches(File.ReadAllText(path), @"foreman-[a-z-]+"))
                    offenders.Add($"{path}: {match.Value}");
            }

            Assert.Empty(offenders);
        }
    }
}
