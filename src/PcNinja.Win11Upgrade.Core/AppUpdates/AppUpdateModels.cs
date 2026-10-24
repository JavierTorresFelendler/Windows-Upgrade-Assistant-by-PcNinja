using System.Reflection;

namespace PcNinja.Win11Upgrade.Core;

public static class AppUpdateIdentity
{
    public const string Repository = "JavierTorresFelendler/Windows-Upgrade-Assistant-by-PcNinja";
    public const string Product = "Windows Upgrade Assistant By PcNinja";
    public const string AssetName = "Windows-Upgrade-Assistant-By-PcNinja.exe";
    public const string ManifestName = "update-manifest.json";
    public const long MaximumBytes = 100_000_000;
    public static string BuildVersion => typeof(AppUpdateIdentity).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? throw new InvalidOperationException("Application build identity is unavailable.");
    public static string Version => BuildVersion.Split('+')[0];
    public static string Channel => ChannelFor(Version);
    public static string ChannelFor(string version) => ReleaseVersion.Parse(version).IsBeta ? "beta" : "stable";
    public static string AssetNameFor(string version)
    {
        var parsed = ReleaseVersion.Parse(version);
        var display = $"{parsed.Major}.{parsed.Minor}" + (parsed.Patch == 0 ? "" : $".{parsed.Patch}");
        if (!parsed.IsStable)
        {
            var parts = parsed.Prerelease.Split('.');
            if (parts.Length != 2 || parts[0] is not ("beta" or "rc") ||
                !int.TryParse(parts[1], out var number) || number <= 0 || number > 65534)
                throw new FormatException("Only numbered Beta/RC or stable asset identities are supported.");
            display += parts[0] == "beta" ? $"-Beta{number}" : $"-RC{number}";
        }
        return $"{Path.GetFileNameWithoutExtension(AssetName)}-V{display}.exe";
    }
}

public sealed record AppUpdateManifest(int SchemaVersion, string Product, string Repository, string Channel,
    string Version, string Tag, string AssetName, string AssetUrl, long SizeBytes, string Sha256, string ReleaseNotesUrl,
    string Importance = "normal", string Message = "A new version of Windows Upgrade Assistant By PcNinja is available.");

public enum AppUpdateCheckState { Available, UpToDate, NoStableRelease, Unavailable, NoBetaRelease }

public sealed record AppUpdateCheckResult(AppUpdateCheckState State, string Message, AppUpdateManifest? Release = null);

public sealed record AppUpdateProgress(long BytesReceived, long TotalBytes);
