using Foreman;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ForemanTest {
    [TestClass]
    public class FactorioGameVersionTests {
        [TestMethod]
        public void TryParse_BannerWithBuildSuffix_ReadsThreeParts() {
            Assert.IsTrue(FactorioGameVersion.TryParse("2.1.24 (build 84000, mac-arm64)", out FactorioGameVersion version));

            Assert.AreEqual(new FactorioGameVersion(2, 1, 24), version);
        }

        [TestMethod]
        public void TryParse_LeadingZeros_AreIgnored() {
            Assert.IsTrue(FactorioGameVersion.TryParse("2.01.007", out FactorioGameVersion version));

            Assert.AreEqual(new FactorioGameVersion(2, 1, 7), version);
        }

        [TestMethod]
        public void TryParse_TwoParts_Fails() {
            Assert.IsFalse(FactorioGameVersion.TryParse("2.1", out _));
        }

        [TestMethod]
        public void TryParse_NullOrText_Fails() {
            Assert.IsFalse(FactorioGameVersion.TryParse(null, out _));
            Assert.IsFalse(FactorioGameVersion.TryParse("not a version", out _));
        }

        [TestMethod]
        public void ToString_IsThreeDottedParts() {
            Assert.AreEqual("2.1.24", new FactorioGameVersion(2, 1, 24).ToString());
        }

        [TestMethod]
        public void CompareTo_OrdersByMajorThenMinorThenBuild() {
            Assert.IsLessThan(0, new FactorioGameVersion(2, 0, 76).CompareTo(new FactorioGameVersion(2, 1, 0)));
            Assert.IsGreaterThan(0, new FactorioGameVersion(2, 1, 24).CompareTo(new FactorioGameVersion(2, 1, 7)));
            Assert.AreEqual(0, new FactorioGameVersion(2, 0, 7).CompareTo(new FactorioGameVersion(2, 0, 7)));
        }

        [TestMethod]
        public void LineFor_2_0_76_IsTheTwoZeroLine() {
            SupportedGameLine? line = SupportedGameVersions.LineFor(new FactorioGameVersion(2, 0, 76));

            Assert.IsNotNull(line);
            Assert.AreEqual(2, line.Major);
            Assert.AreEqual(0, line.Minor);
            Assert.AreEqual("foremanexport_2.0.0", line.ExportModFolder);
            Assert.AreEqual("foremansavereader_2.0.0", line.SaveReaderModFolder);
        }

        [TestMethod]
        public void LineFor_2_0_6_IsNull() {
            Assert.IsNull(SupportedGameVersions.LineFor(new FactorioGameVersion(2, 0, 6)));
        }

        [TestMethod]
        public void Classify_2_0_6_IsTooOld() {
            Assert.AreEqual(FactorioInstallProblem.TooOld, SupportedGameVersions.Classify(new FactorioGameVersion(2, 0, 6)));
        }

        [TestMethod]
        public void Classify_2_0_7_IsNone() {
            Assert.AreEqual(FactorioInstallProblem.None, SupportedGameVersions.Classify(new FactorioGameVersion(2, 0, 7)));
        }

        [TestMethod]
        public void Classify_2_1_24_IsTooNew() {
            Assert.AreEqual(FactorioInstallProblem.TooNew, SupportedGameVersions.Classify(new FactorioGameVersion(2, 1, 24)));
        }

        [TestMethod]
        public void Classify_1_1_110_IsTooOld() {
            Assert.AreEqual(FactorioInstallProblem.TooOld, SupportedGameVersions.Classify(new FactorioGameVersion(1, 1, 110)));
        }

        [TestMethod]
        public void Classify_3_0_0_IsTooNew() {
            Assert.AreEqual(FactorioInstallProblem.TooNew, SupportedGameVersions.Classify(new FactorioGameVersion(3, 0, 0)));
        }

        [TestMethod]
        public void MinimumFor_2_0_3_Is2_0_7() {
            Assert.AreEqual(new FactorioGameVersion(2, 0, 7), SupportedGameVersions.MinimumFor(new FactorioGameVersion(2, 0, 3)));
        }

        [TestMethod]
        public void MinimumFor_1_1_110_IsTheFirstLinesMinimum() {
            Assert.AreEqual(new FactorioGameVersion(2, 0, 7), SupportedGameVersions.MinimumFor(new FactorioGameVersion(1, 1, 110)));
        }
    }
}
