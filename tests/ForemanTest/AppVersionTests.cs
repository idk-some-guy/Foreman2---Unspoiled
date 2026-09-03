using Foreman;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

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
            var informational = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;
            Assert.IsFalse(string.IsNullOrEmpty(informational), "assembly has no informational version");
            var expected = informational!.Split('+')[0];

            StringAssert.Matches(expected, new Regex(@"^\d+\.\d+\.\d+$"));
            Assert.AreEqual(expected, AppVersion.ShortSemVer);
        }

        [TestMethod]
        public void UpstreamVersion_IsNotEmpty() {
            Assert.IsFalse(string.IsNullOrWhiteSpace(AppVersion.UpstreamVersion));
        }

        [TestMethod]
        public void VersionedDisplay_MatchesSpecFormat() {
            var upstream = typeof(AppVersion).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "UpstreamVersion")?.Value;
            Assert.IsFalse(string.IsNullOrWhiteSpace(upstream), "assembly has no UpstreamVersion metadata");
            StringAssert.Matches(upstream!, new Regex(@"^\d+\.\d+\.\d+$"));

            Assert.AreEqual($"v {AppVersion.ShortSemVer} based on {upstream}", AppVersion.VersionedDisplay);
        }
    }
}
