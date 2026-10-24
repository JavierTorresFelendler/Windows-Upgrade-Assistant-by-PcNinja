using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PcNinja.Win11Upgrade.Core;

internal static class AppUpdateFileChecks
{
    internal static void Run()
    {
        CheckIdentifiersAndSpace();
        CheckAclPolicy();
        CheckCandidateIdentity();
        CheckRequests();
        CheckInvalidExecutables();
        CheckNativeFixture();
        Console.WriteLine("PASS: App-update portable root/nonce/space, protected ACL allowlist, bounded private JSON, candidate version/hash/size/signer continuity and invalid PE/product refusal. No Program Files writes, executable launch or network used.");
    }

    private static void CheckIdentifiersAndSpace()
    {
        var nonce = Guid.NewGuid().ToString("N");
        Require(AppUpdateWorkspace.NormalizeOperationId(nonce.ToUpperInvariant()) == nonce, "Canonical operation identity changed.");
        foreach (var invalid in new[] { "", "..", "../outside", Guid.NewGuid().ToString(), nonce + " ", new string('g', 32), new string('0', 32), nonce + "\\child", "C:\\target" })
            Reject(() => AppUpdateWorkspace.NormalizeOperationId(invalid));
        foreach (var invalid in new[] { -1L, 0L, AppUpdateIdentity.MaximumBytes + 1, long.MaxValue })
        {
            Reject(() => AppUpdateWorkspace.RequiredFreeBytes(invalid, 1));
            Reject(() => AppUpdateWorkspace.RequiredFreeBytes(1, invalid));
            // Invalid sizes must fail before public Create can inspect/create any directory.
            Reject(() => AppUpdateWorkspace.Create(Path.GetTempPath(), nonce, invalid, 1));
        }
        Require(AppUpdateWorkspace.RequiredFreeBytes(1, 2) == 3 + 64L * 1024 * 1024, "Updater space reserve was lost or confused with ISO workspace.");
        Require(AppUpdateWorkspace.StateDirectoryName == ".pcninja-updates", "Portable staging namespace changed.");
        Reject(() => AppUpdateWorkspace.CreateFixture(Path.GetTempPath(), nonce, 1, 1));
    }

    private static void CheckAclPolicy()
    {
        var privateAcl = new RawSecurityDescriptor(AppUpdateWorkspace.PrivateDirectorySddl);
        var publicAcl = new RawSecurityDescriptor(AppUpdateWorkspace.PublicDirectorySddl);
        AppUpdateWorkspace.ValidateControlledDescriptor(privateAcl, true, true);
        AppUpdateWorkspace.ValidateControlledDescriptor(privateAcl, false, true);
        AppUpdateWorkspace.ValidateControlledDescriptor(publicAcl, false, true);
        Reject(() => AppUpdateWorkspace.ValidateControlledDescriptor(publicAcl, true, true));
        foreach (var sddl in new[]
        {
            "O:BUG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)",
            "O:BAG:BAD:P(A;;FA;;;BA)",
            "O:BAG:BAD:P(A;;FA;;;SY)",
            "O:BAG:BAD:P",
            "O:BAG:BAD:NO_ACCESS_CONTROL",
            "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FA;;;BU)",
            "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;0x1301bf;;;BU)",
            "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;AU)",
            "O:BAG:BAD:P(A;;FA;;;SY)(A;;FR;;;BA)",
            "O:BAG:BAD:P(A;;FA;;;SY)(A;OIIO;FA;;;BA)",
            "O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(D;;FW;;;BU)"
        }) Reject(() => AppUpdateWorkspace.ValidateControlledDescriptor(new RawSecurityDescriptor(sddl), false, true));
        var inherited = new RawSecurityDescriptor(AppUpdateWorkspace.PrivateDirectorySddl.Replace("D:P", "D:", StringComparison.Ordinal));
        Reject(() => AppUpdateWorkspace.ValidateControlledDescriptor(inherited, true, true));
        AppUpdateWorkspace.ValidateControlledDescriptor(inherited, true, false); // Safe file inheritance from the held private parent.

    }

    private static void CheckCandidateIdentity()
    {
        var hash = new string('A', 64);
        var signer = new string('B', 40);
        var release = Manifest(hash, 1024);
        var actual = new AppExecutableInfo("1.1.0", hash, 1024, null);
        AppUpdateWorkspace.VerifyExecutableInfo(actual, release, null); // Explicit unsigned policy, not signature trust.
        AppUpdateWorkspace.VerifyExecutableInfo(actual with { Version = "1.1.0+commit.abc" }, release, null);
        AppUpdateWorkspace.VerifyExecutableInfo(actual with { SignerThumbprint = signer.ToLowerInvariant() }, release, signer);
        foreach (var invalid in new[]
        {
            actual with { Version = "1.1.0-rc.8" }, actual with { Version = "1.1.1" },
            actual with { Version = "01.1.0" }, actual with { SizeBytes = 1023 },
            actual with { Sha256 = new string('C', 64) }, actual with { Sha256 = "" }
        }) Reject(() => AppUpdateWorkspace.VerifyExecutableInfo(invalid, release, null));
        Reject(() => AppUpdateWorkspace.VerifyExecutableInfo(actual, release, signer));
        Reject(() => AppUpdateWorkspace.VerifyExecutableInfo(actual with { SignerThumbprint = new string('C', 40) }, release, signer));
        Reject(() => AppUpdateWorkspace.VerifyExecutableInfo(actual with { SignerThumbprint = signer }, release, "not-a-thumbprint"));
        Reject(() => AppUpdateWorkspace.VerifyExecutableInfo(actual, release with { Product = "Another application" }, null));
    }

    private static void CheckRequests()
    {
        var parsed = AppUpdateWorkspace.ParseRequest<FixtureRequest>(Encoding.UTF8.GetBytes("{\"schema\":1,\"nonce\":\"fixture\"}"));
        Require(parsed == new FixtureRequest(1, "fixture"), "Bounded request JSON did not roundtrip.");
        foreach (var json in new[] { "null", "", "{", "{\"schema\":1,\"schema\":2,\"nonce\":\"fixture\"}",
            "{\"schema\":1,\"nonce\":\"fixture\",\"unknown\":true}", "{\"Schema\":1,\"nonce\":\"fixture\"}",
            new string(' ', 16385), "{\"schema\":1,\"nonce\":\"" + new string('x', 16385) + "\"}" })
            Reject(() => AppUpdateWorkspace.ParseRequest<FixtureRequest>(Encoding.UTF8.GetBytes(json)));
        Reject(() => AppUpdateWorkspace.ParseRequest<JsonElement>(Encoding.UTF8.GetBytes(new string('[', 20) + "0" + new string(']', 20))));
    }

    private static void CheckInvalidExecutables()
    {
        var parent = NewFixtureParent();
        Directory.CreateDirectory(parent);
        try
        {
            var path = Path.Combine(parent, "not-an-application.exe");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            Reject(() => AppUpdateWorkspace.InspectExecutable(path));
            File.WriteAllBytes(path, new byte[512]);
            Reject(() => AppUpdateWorkspace.InspectExecutable(path));
            File.WriteAllBytes(path, [(byte)'M', (byte)'Z']);
            Reject(() => AppUpdateWorkspace.InspectExecutable(path));
            Reject(() => AppUpdateWorkspace.InspectExecutable(@"\\fixture-server\share\candidate.exe"));
            Reject(() => AppUpdateWorkspace.InspectExecutable(@"C:relative.exe"));
            // This harmless running test host has no PcNinja GUI-product identity.
            // Inspection reads metadata only and must never execute or replace it.
            if (Environment.ProcessPath is string ownHost) Reject(() => AppUpdateWorkspace.InspectExecutable(ownHost));
        }
        finally { RemoveFixtureParent(parent); }
    }

    private static void CheckNativeFixture()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.User?.Value != "S-1-5-18" && !new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            Console.WriteLine("SKIP: Protected updater filesystem creation needs an elevated fixture token; native private-folder tests were not executed.");
            return;
        }
        var parent = NewFixtureParent();
        Directory.CreateDirectory(parent);
        try
        {
            var nonce = Guid.NewGuid().ToString("N");
            using (var workspace = AppUpdateWorkspace.CreateFixture(parent, nonce, 4096, 4096))
            {
                Require(workspace.SourceDirectory == Path.GetFullPath(parent) && workspace.DirectoryPath.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Fixture escaped its portable parent.");
                workspace.WriteRequest(new FixtureRequest(1, nonce));
                Require(workspace.ReadRequest<FixtureRequest>() == new FixtureRequest(1, nonce), "Private request did not survive protected native file I/O.");
                Reject(() => workspace.WriteRequest(new FixtureRequest(2, nonce))); // CreateNew only.
                Reject(() => workspace.WriteRequest(new FixtureRequest(1, new string('x', 16385))));
                Reject(() => Directory.Move(parent, parent + "-renamed")); // Held ancestor denies rename.
                using (var opened = AppUpdateWorkspace.OpenFixture(parent, nonce))
                {
                    Require(opened.ReadRequest<FixtureRequest>().Nonce == nonce, "Helper lookup did not bind the same private operation.");
                    Reject(opened.CleanupUncommitted);
                }
                using (var candidate = new FileStream(workspace.CandidatePath, FileMode.CreateNew, FileAccess.Write)) candidate.Write([1, 2, 3]);
                using (var helper = new FileStream(workspace.HelperPath, FileMode.CreateNew, FileAccess.Write)) helper.Write([4, 5, 6]);
                var unknown = Path.Combine(workspace.DirectoryPath, "unrelated.keep");
                File.WriteAllText(unknown, "leave this file alone");
                using (var active = new FileStream(workspace.HelperPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Reject(workspace.CleanupUncommitted);
                Require(File.Exists(workspace.CandidatePath) && File.Exists(workspace.HelperPath), "A locked helper allowed partial cleanup.");
                workspace.CleanupUncommitted();
                Require(!File.Exists(workspace.CandidatePath) && !File.Exists(workspace.HelperPath) &&
                    !File.Exists(workspace.RequestPath) && File.Exists(unknown), "Cleanup was broad or failed to remove exact uncommitted files.");
                workspace.MarkHandoffStarted();
                Reject(workspace.CleanupUncommitted);
            }
            Console.WriteLine("PASS: Elevated protected temp-only workspace creation, handoff/request binding, held-ancestor rename refusal, exact cleanup/lock guards. No executable launched.");
        }
        finally { RemoveFixtureParent(parent); }

        var existingParent = NewFixtureParent();
        Directory.CreateDirectory(existingParent);
        try
        {
            var existing = Directory.CreateDirectory(Path.Combine(existingParent, AppUpdateWorkspace.StateDirectoryName));
            var before = FileSystemAclExtensions.GetAccessControl(existing).GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
            Reject(() => AppUpdateWorkspace.CreateFixture(existingParent, Guid.NewGuid().ToString("N"), 1, 1));
            var after = FileSystemAclExtensions.GetAccessControl(existing).GetSecurityDescriptorSddlForm(AccessControlSections.Owner | AccessControlSections.Access);
            Require(before == after, "An untrusted existing application root was silently hardened/taken over.");
        }
        finally { RemoveFixtureParent(existingParent); }
    }

    private static AppUpdateManifest Manifest(string hash, long size) => new(1, AppUpdateIdentity.Product,
        AppUpdateIdentity.Repository, "stable", "1.1.0", "v1.1.0", AppUpdateIdentity.AssetNameFor("1.1.0"),
        $"https://github.com/{AppUpdateIdentity.Repository}/releases/download/v1.1.0/{AppUpdateIdentity.AssetNameFor("1.1.0")}", size, hash,
        $"https://github.com/{AppUpdateIdentity.Repository}/releases/tag/v1.1.0");
    private static string NewFixtureParent() => Path.Combine(Path.GetTempPath(), "PcNinja-AppUpdate-Fixture-" + Guid.NewGuid().ToString("N"));
    private static void RemoveFixtureParent(string parent)
    {
        var resolved = Path.GetFullPath(parent);
        if (!resolved.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar +
            "PcNinja-AppUpdate-Fixture-", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Refused to clean an unexpected updater fixture path.");
        Directory.Delete(resolved, recursive: true);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or JsonException or BadImageFormatException or FormatException) { return; }
        throw new Exception("An unsafe application-update filesystem/identity operation was accepted.");
    }
    internal sealed record FixtureRequest(int Schema, string Nonce);
}
