using Foreman;
using Foreman.DataCaching;
using ForemanTest.support;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ForemanTest {
    [TestClass]
    public class IconPipelineTests {
        [TestMethod]
        public void LoadIconCacheYieldsNonEmptyIconsFromBundledPreset() {
            string dat = Directory.GetFiles(TestAssets.AssetsDirectory, "*.dat").First();
            var icons = IconCache.LoadIconCache(dat, NullProgress.Instance, 0, 100).GetAwaiter().GetResult();
            Assert.IsTrue(icons.Count > 100);
            var first = icons.Values.First(i => i.Icon != null).Icon!;
            Assert.IsTrue(first.Width >= 8 && first.Width <= 256);
        }

        [TestMethod]
        public async Task LoadIconCache_MissingFile_ReturnsEmptyAndLogsThePath() {
            string missingPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dat");
            ErrorLogging.ClearLog();

            var icons = await IconCache.LoadIconCache(missingPath, NullProgress.Instance, 0, 100).ConfigureAwait(false);

            Assert.AreEqual(0, icons.Count);
            string log = File.ReadAllText(ErrorLogging.LogFilePath);
            Assert.Contains(missingPath, log);
        }

        [TestMethod]
        public async Task PresetLoad_DoesNotLogIconCacheGetIconFailures() {
            if (!VanillaDataCacheFixture.PresetsAvailable)
                Assert.Inconclusive($"Preset folder not found: {VanillaDataCacheFixture.PresetsDirectory}");

            ErrorLogging.ClearLog();
            VanillaDataCacheFixture.Reset();
            await VanillaDataCacheFixture.GetLoadedAsync().ConfigureAwait(false);

            string log = File.Exists(ErrorLogging.LogFilePath) ? File.ReadAllText(ErrorLogging.LogFilePath) : "";
            Assert.DoesNotContain("IconCache.GetIcon failed", log);
        }

        //Live gate finding: DataCacheBootstrap's "Resource Extraction" chooser group icon (and every other
        //Foreman-bundled Graphics/*.png - UnknownIcon, SpoilAssembler, PlantAssembler, HeatIcon,
        //BurnerGeneratorIcon, PlayerAssembler, RocketAssembler, ElectricityIcon, NoBeacon) rendered blank
        //when launched via `dotnet run` from the project directory. Root cause: every GetIcon call site
        //passes a bare relative path (`Path.Combine("Graphics", "...")`), which `SKBitmap.Decode` resolves
        //against the process's current working directory, not the executable's own directory - the exact
        //`Application.StartupPath` vs. `Environment.CurrentDirectory` distinction the `AppPaths.
        //ExecutableDirectory` divergence (this file's neighbors, `DataCache.cs`/`PresetProcessor.cs`) already
        //exists to paper over, just not applied here. Works by accident under `dotnet test`/a published
        //build's own launch convention (CWD happens to equal the output directory there), which is why this
        //was never caught by CI.
        [TestMethod]
        public void GetIcon_RelativePath_ResolvesAgainstExecutableDirectory_NotCurrentWorkingDirectory() {
            string originalCwd = Environment.CurrentDirectory;
            string scratchCwd = Path.Combine(Path.GetTempPath(), "icon-cache-cwd-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(scratchCwd);
            try {
                Environment.CurrentDirectory = scratchCwd;

                using SKBitmap icon = IconCache.GetIcon(Path.Combine("Graphics", "UnknownIcon.png"), 32);

                bool hasNonTransparentPixel = false;
                for (int x = 0; x < icon.Width && !hasNonTransparentPixel; x++)
                    for (int y = 0; y < icon.Height && !hasNonTransparentPixel; y++)
                        if (icon.GetPixel(x, y).Alpha != 0)
                            hasNonTransparentPixel = true;

                Assert.IsTrue(hasNonTransparentPixel, "Expected the real UnknownIcon.png content, not a blank fallback bitmap.");
            } finally {
                Environment.CurrentDirectory = originalCwd;
                Directory.Delete(scratchCwd, recursive: true);
            }
        }

        [TestMethod]
        public async Task IconCache_WriteAsyncThenReadAsync_RoundTripsPixelData() {
            string dat = Directory.GetFiles(TestAssets.AssetsDirectory, "*.dat").First();
            var loaded = await IconCache.LoadIconCache(dat, NullProgress.Instance, 0, 100).ConfigureAwait(false);
            Dictionary<string, IconColorPair> sample = loaded
                .Where(kv => kv.Value.Icon is not null)
                .Take(5)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            Assert.IsTrue(sample.Count > 0);

            string tempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".foic");
            try {
                await IconCache.SaveIconCacheAsync(tempPath, sample).ConfigureAwait(false);
                var reloaded = await IconCache.LoadIconCache(tempPath, NullProgress.Instance, 0, 100).ConfigureAwait(false);

                Assert.AreEqual(sample.Count, reloaded.Count);

                string firstKey = sample.Keys.First();
                SKBitmap original = sample[firstKey].Icon!;
                SKBitmap roundTripped = reloaded[firstKey].Icon!;
                Assert.AreEqual(original.Width, roundTripped.Width);
                Assert.AreEqual(original.Height, roundTripped.Height);
                for (int x = 0; x < original.Width; x++) {
                    for (int y = 0; y < original.Height; y++)
                        Assert.AreEqual(original.GetPixel(x, y), roundTripped.GetPixel(x, y));
                }
            } finally {
                File.Delete(tempPath);
            }
        }
    }
}
