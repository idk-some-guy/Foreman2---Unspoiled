using Foreman;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace ForemanTest {
    [TestClass]
    public class AppVersionTests {
        [TestMethod]
        public void ShortSemVer_DropsBuildMetadataAfterPlus() {
            Assert.IsFalse(AppVersion.ShortSemVer.Contains('+', StringComparison.Ordinal));
            Assert.IsTrue(AppVersion.SemVer.StartsWith(AppVersion.ShortSemVer, StringComparison.Ordinal));
        }

        [TestMethod]
        public void ShortDisplay_IsVPrefixedShortSemVer() {
            Assert.AreEqual("v" + AppVersion.ShortSemVer, AppVersion.ShortDisplay);
        }

        [TestMethod]
        public void ShortSemVer_MatchesDirectoryBuildPropsVersion() {
            Assert.AreEqual("1.0.0", AppVersion.ShortSemVer);
        }

        [TestMethod]
        public void UpstreamVersion_IsNotEmpty() {
            Assert.IsFalse(string.IsNullOrWhiteSpace(AppVersion.UpstreamVersion));
        }

        [TestMethod]
        public void VersionedDisplay_MatchesSpecFormat() {
            Assert.AreEqual("v 1.0.0 based on 2.4.0", AppVersion.VersionedDisplay);
        }
    }
}
