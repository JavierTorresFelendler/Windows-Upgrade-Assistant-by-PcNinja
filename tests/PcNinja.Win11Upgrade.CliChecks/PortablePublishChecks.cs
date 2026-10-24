using System.Security.Principal;
using PcNinja.Win11Upgrade.Core;

internal static class PortablePublishChecks
{
    internal static void Run(string builtHost, AppExecutableInfo info)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        { Console.WriteLine("SKIP: Elevated same-folder publish/reuse/launch-lock fixture; no actual helper or executable is run."); return; }
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PcNinja-AppUpdate-Fixture-" + Guid.NewGuid().ToString("N")));
        if (Path.GetDirectoryName(parent) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())))
            throw new Exception("Unsafe temporary publisher fixture.");
        Directory.CreateDirectory(parent);
        try
        {
            var asset = AppUpdateIdentity.AssetNameFor(info.Version);
            var tag = "v" + info.Version;
            var release = new AppUpdateManifest(1, AppUpdateIdentity.Product, AppUpdateIdentity.Repository,
                AppUpdateIdentity.ChannelFor(info.Version), info.Version, tag, asset,
                $"https://github.com/{AppUpdateIdentity.Repository}/releases/download/{tag}/{asset}", info.SizeBytes,
                info.Sha256, $"https://github.com/{AppUpdateIdentity.Repository}/releases/tag/{tag}");
            var original = Path.Combine(parent, "original-retained.exe");
            File.Copy(builtHost, original);
            string target;
            using (var workspace = AppUpdateWorkspace.CreateFixture(parent, Guid.NewGuid().ToString("N"), info.SizeBytes, info.SizeBytes))
            {
                File.Copy(builtHost, workspace.CandidatePath);
                target = workspace.TargetPath(release);
                using (workspace.PublishAndHold(release, info.SignerThumbprint))
                {
                    Require(File.Exists(original) && File.Exists(target) && Path.GetDirectoryName(target) == parent, "New version moved away from its source directory.");
                    Reject(() => File.WriteAllText(target, "must not change verified bytes"));
                    Reject(() => File.Move(target, target + ".renamed"));
                    Reject(() => Directory.Move(parent, parent + ".renamed"));
                }
            }
            using (var reused = AppUpdateWorkspace.CreateFixture(parent, Guid.NewGuid().ToString("N"), info.SizeBytes, info.SizeBytes))
            {
                reused.CopyExistingCandidate(release, info.SignerThumbprint);
                using (reused.PublishAndHold(release, info.SignerThumbprint)) { }
                reused.CleanupUncommitted();
                Require(File.Exists(original) && File.Exists(target) && Directory.GetFiles(parent, "*.exe").Length == 2,
                    "Reuse removed the original or created a collision/numbered executable.");
            }
            // Change this fixture's expected release digest, not a customer's file.
            using (var refused = AppUpdateWorkspace.CreateFixture(parent, Guid.NewGuid().ToString("N"), info.SizeBytes, info.SizeBytes))
            {
                Reject(() => refused.CopyExistingCandidate(release with { Sha256 = new string('0', 64) }, info.SignerThumbprint));
                Require(AppUpdateWorkspace.InspectExecutable(target).Sha256 == info.Sha256, "Mismatch handling overwrote the existing version.");
            }
            Console.WriteLine("PASS: Real elevated same-folder versioned publish, existing-file reuse, no overwrite/rename/Copy N, original preservation and no-write/no-delete launch guard. Temporary test-host bytes only; no helper/executable/Setup launched.");
        }
        finally
        {
            var resolved = Path.GetFullPath(parent);
            if (Path.GetDirectoryName(resolved) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) ||
                !Path.GetFileName(resolved).StartsWith("PcNinja-AppUpdate-Fixture-", StringComparison.Ordinal))
                throw new Exception("Unsafe publisher cleanup path.");
            Directory.Delete(resolved, recursive: true);
        }
    }
    private static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return; }
        throw new Exception("Unsafe portable publish operation was accepted.");
    }
}
