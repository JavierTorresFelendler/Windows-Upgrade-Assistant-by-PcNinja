using PcNinja.Win11Upgrade.Core;
using PcNinja.Win11UpgradeAssistant;

internal static class AppUpdateHelperChecks
{
    internal static void Run()
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        var assetName = AppUpdateIdentity.AssetNameFor("9.9.9");
        var manifest = new AppUpdateManifest(1, AppUpdateIdentity.Product, AppUpdateIdentity.Repository,
            "stable", "9.9.9", "v9.9.9", assetName,
            $"https://github.com/{AppUpdateIdentity.Repository}/releases/download/v9.9.9/{assetName}",
            10, new string('a', 64), $"https://github.com/{AppUpdateIdentity.Repository}/releases/tag/v9.9.9");
        var request = new AppUpdateRequest(2, id, now, Environment.ProcessId + 10000,
            now.AddMinutes(-1).UtcDateTime.Ticks, @"C:\Fixture\app.exe", new string('a', 64),
            null, manifest, false);
        AppUpdateInstaller.ValidateRequest(request, id);
        // Read the built product host, not this test runner; never execute it.
        // This proves that the negative PE fixtures did not create an always-reject verifier.
        var builtHost = Path.Combine(AppContext.BaseDirectory, AppUpdateIdentity.AssetName);
        var builtInfo = AppUpdateWorkspace.InspectExecutable(builtHost);
        Require(builtInfo.Version == ProductIdentity.Version && builtInfo.SizeBytes == new FileInfo(builtHost).Length &&
            builtInfo.Sha256.Length == 64, "The real built product host failed read-only executable inspection.");
        PortablePublishChecks.Run(builtHost, builtInfo);
        foreach (var invalid in new[]
        {
            request with { SchemaVersion = 1 }, request with { OperationId = Guid.NewGuid().ToString("N") },
            request with { ParentProcessId = 0 }, request with { ParentProcessId = Environment.ProcessId },
            request with { ParentStartUtcTicks = 0 }, request with { ParentStartUtcTicks = long.MaxValue },
            request with { ParentStartUtcTicks = now.AddMinutes(1).UtcDateTime.Ticks },
            request with { CreatedUtc = now.AddMinutes(-6) }, request with { CreatedUtc = now.AddMinutes(1) },
            request with { ParentPath = "relative.exe" }, request with { ParentPath = @"\\server\share\app.exe" },
            request with { ParentPath = @"\\?\C:\app.exe" }, request with { ParentPath = @"C:\app.exe:ads" },
            request with { ParentPath = @"C:\app*.exe" }, request with { ParentPath = "C:\\bad\nname.exe" },
            request with { ParentHash = "" },
            request with { RequiredSigner = new string('a', 39) }, request with { RequiredSigner = new string('z', 40) },
            request with { Release = manifest with { Repository = "other/project" } }
        }) Reject(() => AppUpdateInstaller.ValidateRequest(invalid, id));
        Reject(() => AppUpdateInstaller.ValidateRequest(request with { OperationId = new string('0', 32) }, new string('0', 32)));
        Require(AppUpdateInstaller.ReadHandshakeAsync(new StringReader("READY\r\n"), CancellationToken.None)
            .GetAwaiter().GetResult() == "READY", "Handshake line decoding changed.");
        Reject(() => AppUpdateInstaller.ReadHandshakeAsync(new StringReader(new string('x', 65) + "\n"), CancellationToken.None)
            .GetAwaiter().GetResult());
        using (var blocked = new BlockingReader())
        using (var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
        {
            try
            {
                AppUpdateInstaller.ReadHandshakeAsync(blocked, timeout.Token).GetAwaiter().GetResult();
                throw new Exception("An uncooperative handshake read escaped its deadline.");
            }
            catch (OperationCanceledException) { }
        }

        Console.WriteLine("PASS: Parent-bound protocol-v2 request identity, expiry, local paths, real product-host inspection and bounded handshake. No app replacement, helper, network, Setup or reboot.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException) { return; }
        throw new Exception("Unsafe helper request/replacement was accepted.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    private sealed class BlockingReader : TextReader
    {
        private readonly TaskCompletionSource<int> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken token = default) => new(_pending.Task);
        protected override void Dispose(bool disposing) { _pending.TrySetResult(0); base.Dispose(disposing); }
    }
}
