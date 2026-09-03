using System.Linq;
using System.Reflection;

namespace Foreman;

/// <summary>Application release version, from <c>Version</c> / <c>UpstreamVersion</c> in the repo-root Directory.Build.props.</summary>
internal static class AppVersion {
    /// <summary>SemVer string (e.g. 2.2.16 or 2.2.16-beta.1+build.42).</summary>
    public static string SemVer { get; } = ResolveSemVer();

    public static string Display => "v" + SemVer;

    public static string ProductName => "Foreman " + SemVer;

    /// <summary>SemVer without build metadata (e.g. the full git SHA after '+'), for UI display space.</summary>
    public static string ShortSemVer { get; } = SemVer.Split('+')[0];

    public static string ShortDisplay => "v" + ShortSemVer;

    /// <summary>The DanielKote/Foreman2 release this build ports, from the <c>UpstreamVersion</c> assembly metadata.</summary>
    public static string UpstreamVersion { get; } = ResolveUpstreamVersion();

    /// <summary>The spec's in-app label format: "v &lt;version&gt; based on &lt;upstream version&gt;".</summary>
    public static string VersionedDisplay => $"v {ShortSemVer} based on {UpstreamVersion}";

    private static string ResolveSemVer() {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
            return informational;

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        return assemblyVersion is null
            ? "0.0.0"
            : assemblyVersion.Revision > 0
            ? $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}.{assemblyVersion.Revision}"
            : $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
    }

    private static string ResolveUpstreamVersion() {
        var metadata = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "UpstreamVersion")?
            .Value;
        return string.IsNullOrEmpty(metadata) ? "unknown" : metadata;
    }
}
