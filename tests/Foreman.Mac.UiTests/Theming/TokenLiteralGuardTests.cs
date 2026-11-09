using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Foreman.Mac.UiTests.Theming {
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

        [Fact]
        public void NoMotionLiteral_InAxaml() {
            string kitDirectory = Path.Combine(RepoRoot, "src", "Foreman.Mac.Kit");
            string[] files = [.. SourceFiles(".axaml", includeCanvas: true), .. Directory.EnumerateFiles(kitDirectory, "*.axaml", SearchOption.AllDirectories)
                .Where(path => !Path.GetRelativePath(kitDirectory, path).Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))];
            Assert.Contains(files, path => path.EndsWith(Path.Combine("Controls", "Spinner.axaml"), StringComparison.Ordinal));
            Assert.Contains(files, path => path.EndsWith("App.axaml", StringComparison.Ordinal));

            var offenders = new List<string>();
            foreach (string path in files) {
                foreach (Match match in Regex.Matches(File.ReadAllText(path), @"\b(?:Easing|Duration)=""[^{""][^""]*"""))
                    offenders.Add($"{path}: {match.Value}");
            }

            Assert.Empty(offenders);
        }

        [Fact]
        public void NoColorLiteral_UnderCanvas() {
            string canvas = Path.Combine(AppSourceDirectory, "Canvas");
            string[] files = [.. Directory.EnumerateFiles(canvas, "*.cs", SearchOption.AllDirectories).Where(path => !IsUnder(path, "obj") && !IsUnder(path, "bin"))];
            Assert.Contains(files, path => path.EndsWith("GridManager.cs", StringComparison.Ordinal));
            Assert.True(files.Length >= 30, $"expected at least 30 canvas cs files, found {files.Length}");
            var offenders = new List<string>();

            foreach (string path in files) {
                string relative = Path.GetRelativePath(AppSourceDirectory, path);
                foreach (Match match in Regex.Matches(File.ReadAllText(path), @"new SKColor\(\s*(?:0x)?\d|\bSKColors\.\w+|#[0-9A-Fa-f]{6,8}\b|\bColor\.From(?:Rgb|Argb)\("))
                    offenders.Add($"{relative}: {match.Value}");
            }

            Assert.Empty(offenders);
        }
    }
}
