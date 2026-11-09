using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Foreman {
    public readonly record struct FactorioGameVersion(int Major, int Minor, int Build) : IComparable<FactorioGameVersion> {
        private static readonly Regex Parts = new(@"^\s*(\d+)\.(\d+)\.(\d+)", RegexOptions.CultureInvariant);

        public static bool TryParse(string? text, out FactorioGameVersion version) {
            version = default;
            if (text is null)
                return false;
            Match match = Parts.Match(text);
            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major)
                || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
                || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int build))
                return false;
            version = new FactorioGameVersion(major, minor, build);
            return true;
        }

        public int CompareTo(FactorioGameVersion other) {
            int byMajor = Major.CompareTo(other.Major);
            if (byMajor != 0)
                return byMajor;
            int byMinor = Minor.CompareTo(other.Minor);
            return byMinor != 0 ? byMinor : Build.CompareTo(other.Build);
        }

        public static bool operator <(FactorioGameVersion left, FactorioGameVersion right) => left.CompareTo(right) < 0;
        public static bool operator <=(FactorioGameVersion left, FactorioGameVersion right) => left.CompareTo(right) <= 0;
        public static bool operator >(FactorioGameVersion left, FactorioGameVersion right) => left.CompareTo(right) > 0;
        public static bool operator >=(FactorioGameVersion left, FactorioGameVersion right) => left.CompareTo(right) >= 0;

        public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Build}");
    }

    public sealed record SupportedGameLine(int Major, int Minor, int MinimumBuild, string ExportModFolder, string SaveReaderModFolder) {
        public FactorioGameVersion Minimum => new(Major, Minor, MinimumBuild);
    }

    public static class SupportedGameVersions {
        public static IReadOnlyList<SupportedGameLine> Lines { get; } = [
            new SupportedGameLine(2, 0, 7, "foremanexport_2.0.0", "foremansavereader_2.0.0"),
            new SupportedGameLine(2, 1, 23, "foremanexport_2.1.0", "foremansavereader_2.1.0"),
        ];

        public static SupportedGameLine? LineFor(FactorioGameVersion version) =>
            Lines.FirstOrDefault(line => line.Major == version.Major && line.Minor == version.Minor && version.Build >= line.MinimumBuild);

        public static FactorioInstallProblem Classify(FactorioGameVersion version) {
            if (LineFor(version) is not null)
                return FactorioInstallProblem.None;
            bool sameLine = Lines.Any(line => line.Major == version.Major && line.Minor == version.Minor);
            return sameLine || version < Lines[0].Minimum ? FactorioInstallProblem.TooOld : FactorioInstallProblem.TooNew;
        }

        public static FactorioGameVersion MinimumFor(FactorioGameVersion version) =>
            (Lines.FirstOrDefault(line => line.Major == version.Major && line.Minor == version.Minor) ?? Lines[0]).Minimum;
    }
}
