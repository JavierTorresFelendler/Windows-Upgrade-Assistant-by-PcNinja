using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PcNinja.Win11Upgrade.Core;

namespace PcNinja.Win11UpgradeAssistant;

internal sealed record AppUpdateRequest(int SchemaVersion, string OperationId, DateTimeOffset CreatedUtc,
    int ParentProcessId, long ParentStartUtcTicks, string ParentPath, string ParentHash,
    string? RequiredSigner, AppUpdateManifest Release, bool RelaunchGui);

internal sealed record AppUpdateHandoffResult(string Version, string TargetPath, string OperationId,
    int HelperProcessId, string Message);

/// <summary>Explicit app-only handoff. Never passes CLI/OS-upgrade arguments to a successor.</summary>
internal static class AppUpdateInstaller
{
    internal const string HelperArgument = "--internal-app-update";
    private const string Ready = "PCNINJA_APP_UPDATE_READY_V2";
    private const string Go = "PCNINJA_APP_UPDATE_GO_V2";
    internal static string DestinationDirectory => Path.GetDirectoryName(Environment.ProcessPath
        ?? throw new InvalidOperationException("Application path is unavailable."))!;
    internal static string DestinationFor(AppUpdateManifest release) => Path.Combine(DestinationDirectory, release.AssetName);

    internal static async Task<AppUpdateHandoffResult> InstallAsync(AppUpdateClient client,
        AppUpdateManifest release, bool relaunchGui, IProgress<AppUpdateProgress>? progress, CancellationToken token)
    {
        AppUpdateClient.ValidateManifest(release);
        if (!Program.IsAdministrator()) throw new UnauthorizedAccessException("Run app update installation as administrator.");
        if (ReleaseVersion.Parse(release.Version).CompareTo(ReleaseVersion.Parse(ProductIdentity.Version)) <= 0)
            throw new InvalidOperationException("Only a newer application version can be downloaded and run.");
        if (release.Channel != AppUpdateIdentity.Channel)
            throw new InvalidDataException("The release is outside this application's update channel.");
        using var gate = AppOperationGate.EnterUpdate();
        using var parent = Process.GetCurrentProcess();
        var parentPath = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable.");
        var current = AppUpdateWorkspace.InspectExecutable(parentPath);
        if (ReleaseVersion.Parse(current.Version).CompareTo(ReleaseVersion.Parse(ProductIdentity.Version)) != 0)
            throw new InvalidDataException("The running application's file identity changed.");
        var id = Guid.NewGuid().ToString("N");
        using var workspace = AppUpdateWorkspace.Create(Path.GetDirectoryName(parentPath)!, id, release.SizeBytes, release.SizeBytes);
        var requiredSigner = current.SignerThumbprint;
        var target = workspace.TargetPath(release);
        var handedOff = false;
        try
        {
            if (File.Exists(target) || Directory.Exists(target)) workspace.CopyExistingCandidate(release, requiredSigner);
            else
            {
                await using var candidate = new FileStream(workspace.CandidatePath, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous);
                await client.DownloadAsync(release, candidate, progress, token);
                await candidate.FlushAsync(token);
            }
            AppUpdateWorkspace.VerifyCandidate(workspace.CandidatePath, release, requiredSigner);
            // Run the verified successor's protocol-v2 helper, not a mutable source-path copy.
            workspace.PrepareHelper(release, requiredSigner);
            var request = new AppUpdateRequest(2, id, DateTimeOffset.UtcNow, parent.Id,
                parent.StartTime.ToUniversalTime().Ticks, Path.GetFullPath(parentPath), current.Sha256,
                requiredSigner, release, relaunchGui);
            workspace.WriteRequest(request);
            token.ThrowIfCancellationRequested();
            AppOperationGate.EnsureUpdateAllowed();
            var start = new ProcessStartInfo
            {
                FileName = workspace.HelperPath, WorkingDirectory = workspace.DirectoryPath,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(HelperArgument);
            using var helper = Process.Start(start) ?? throw new IOException("The application update helper could not start.");
            workspace.MarkHandoffStarted(); // Running helper images must never be removed by download cleanup.
            using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            readyTimeout.CancelAfter(TimeSpan.FromSeconds(20));
            string? response;
            try { response = await ReadHandshakeAsync(helper.StandardOutput, readyTimeout.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            { throw new TimeoutException("The verified update helper did not become ready in time. The current app was not replaced."); }
            if (response != Ready) throw new IOException("The update helper was not ready. The current app was not replaced.");
            token.ThrowIfCancellationRequested();
            AppOperationGate.EnsureUpdateAllowed();
            // Disarm BEFORE GO. On a failed write neither process releases a live operation;
            // closing all remaining handles safely destroys the semaphore instead.
            gate.TransferToHelper();
            await helper.StandardInput.WriteLineAsync(Go.AsMemory(), CancellationToken.None);
            await helper.StandardInput.FlushAsync(CancellationToken.None);
            helper.StandardInput.Close();
            handedOff = true;
            AppLog.Info($"App update handoff {id}: {ProductIdentity.Version} -> {release.Version}; target {target}.");
            return new(release.Version, target, id, helper.Id,
                "Application run handoff started. The previous file stays unchanged. No Windows upgrade or restart was requested.");
        }
        finally
        {
            if (!handedOff)
            {
                try { workspace.CleanupUncommitted(); }
                catch (Exception ex) { AppLog.Info($"App update staging retained for safe recovery: {ex.Message}"); }
            }
        }
    }

    // Dispatched before normal logging, console attachment, elevation or the GUI.
    internal static async Task<int> RunHelperAsync()
    {
        AppUpdateRequest? request = null;
        var parentExited = false;
        try
        {
            if (!Program.IsAdministrator()) return 5; // Never auto-elevate an internal request.
            var self = Environment.ProcessPath ?? throw new InvalidOperationException("Helper identity unavailable.");
            if (Path.GetFileName(self) != "UpdateHelper.exe") throw new InvalidDataException("Invalid helper executable.");
            var folder = Path.GetDirectoryName(self)!;
            var id = Path.GetFileName(folder);
            var stateFolder = Path.GetDirectoryName(folder)!;
            if (Path.GetFileName(stateFolder) != AppUpdateWorkspace.StateDirectoryName)
                throw new InvalidDataException("Invalid portable update workspace.");
            using var workspace = AppUpdateWorkspace.Open(Path.GetDirectoryName(stateFolder)!, id);
            if (!SamePath(folder, workspace.DirectoryPath) || !SamePath(self, workspace.HelperPath))
                throw new InvalidDataException("The helper is outside its protected workspace.");
            request = workspace.ReadRequest<AppUpdateRequest>();
            ValidateRequest(request, id);
            if (!SamePath(Path.GetDirectoryName(request.ParentPath)!, workspace.SourceDirectory) ||
                SamePath(request.ParentPath, workspace.TargetPath(request.Release)))
                throw new InvalidDataException("The new version must be beside, not replace, the original executable.");
            AppUpdateWorkspace.VerifyCandidate(self, request.Release, request.RequiredSigner);
            AppUpdateWorkspace.VerifyCandidate(workspace.CandidatePath, request.Release, request.RequiredSigner);
            using var parent = Process.GetProcessById(request.ParentProcessId);
            _ = parent.SafeHandle; // Retain the exact process object, not a reusable PID-only poll.
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartUtcTicks ||
                !SamePath(parent.MainModule?.FileName ?? "", request.ParentPath))
                throw new InvalidDataException("The original application process identity changed.");
            var parentFile = AppUpdateWorkspace.InspectExecutable(request.ParentPath);
            if (!parentFile.Sha256.Equals(request.ParentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The original executable changed before handoff.");
            if (parentFile.SignerThumbprint is not null &&
                !parentFile.SignerThumbprint.Equals(request.RequiredSigner, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The app update cannot drop the original publisher policy.");
            if (ReleaseVersion.Parse(parentFile.Version).CompareTo(ReleaseVersion.Parse(request.Release.Version)) >= 0)
                throw new InvalidDataException("The successor is not newer than the original app.");
            if (AppUpdateIdentity.ChannelFor(parentFile.Version) != request.Release.Channel)
                throw new InvalidDataException("The successor changed the parent's update channel.");
            using var originalGuard = AppUpdateWorkspace.HoldOriginal(request.ParentPath, request.ParentHash);
            using var gate = AppOperationGate.OpenHandoff();
            Console.WriteLine(Ready);
            Console.Out.Flush();
            using (var goTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
                if (await ReadHandshakeAsync(Console.In, goTimeout.Token) != Go)
                    throw new InvalidDataException("App update handoff was not committed.");
            gate.AdoptHandoff();
            using (var exitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                await parent.WaitForExitAsync(exitTimeout.Token);
            parentExited = true;
            ValidateRequest(request, id);
            AppOperationGate.EnsureUpdateAllowed();
            ApplyVerified(workspace, request, LaunchGui);
            try { WriteResult(workspace, request, true, "New portable version is ready."); }
            catch (Exception statusError)
            {
                // Replacement/relaunch already succeeded. A diagnostic write failure
                // must not claim the previous application was restored or undo it.
                AppLog.Error($"Application update {request.OperationId} applied, but its status file could not be saved: {statusError}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Application update helper failed: {ex}");
            if (request is not null)
            {
                try
                {
                    using var workspace = AppUpdateWorkspace.Open(Path.GetDirectoryName(request.ParentPath)!, request.OperationId);
                    WriteResult(workspace, request, false, "The new version could not be run. The original executable is unchanged.");
                }
                catch { }
                if (parentExited && request.RelaunchGui)
                    MessageBox.Show("The app update could not be completed. Your previous copy was not removed. Open it again and check View log.",
                        "Application update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return 1;
        }
    }

    internal static void ValidateRequest(AppUpdateRequest request, string id)
    {
        var now = DateTimeOffset.UtcNow;
        if (request is null || request.SchemaVersion != 2 || request.OperationId != id ||
            id is not { Length: 32 } || !id.All(char.IsAsciiHexDigit) ||
            !Guid.TryParseExact(id, "N", out var operation) || operation == Guid.Empty || request.ParentProcessId <= 0 ||
            request.ParentProcessId == Environment.ProcessId || request.ParentStartUtcTicks <= 0 ||
            request.ParentStartUtcTicks > DateTime.MaxValue.Ticks ||
            request.ParentStartUtcTicks > request.CreatedUtc.UtcDateTime.Ticks ||
            request.ParentStartUtcTicks > now.UtcDateTime.Ticks ||
            request.CreatedUtc > now.AddSeconds(30) || now - request.CreatedUtc > TimeSpan.FromMinutes(5) ||
            !IsOrdinaryLocalPath(request.ParentPath) ||
            !IsHash(request.ParentHash) ||
            request.RequiredSigner is not null && (request.RequiredSigner.Length != 40 || !request.RequiredSigner.All(Uri.IsHexDigit)))
            throw new InvalidDataException("The app update handoff is invalid or expired.");
        AppUpdateClient.ValidateManifest(request.Release);
    }

    internal static void ApplyVerified(AppUpdateWorkspace workspace, AppUpdateRequest request, Action<string> launch)
    {
        if (!SamePath(workspace.SourceDirectory, Path.GetDirectoryName(request.ParentPath)!))
            throw new InvalidDataException("The update destination differs from the original application folder.");
        AppUpdateWorkspace.VerifyCandidate(workspace.CandidatePath, request.Release, request.RequiredSigner);
        AppOperationGate.EnsureUpdateAllowed();
        using var candidateGuard = workspace.PublishAndHold(request.Release, request.RequiredSigner);
        AppOperationGate.EnsureUpdateAllowed();
        if (request.RelaunchGui) launch(workspace.TargetPath(request.Release));
    }

    private static void LaunchGui(string target)
    {
        using var launched = Process.Start(new ProcessStartInfo
        {
            FileName = target, WorkingDirectory = Path.GetDirectoryName(target)!, UseShellExecute = false
            // Intentionally no Arguments/ArgumentList. An updater must never resume an OS operation.
        }) ?? throw new IOException("The updated app could not be reopened.");
    }

    private static void WriteResult(AppUpdateWorkspace workspace, AppUpdateRequest request, bool success, string message)
    {
        workspace.WriteResult(new { schemaVersion = 1, timestamp = DateTimeOffset.UtcNow,
            operationId = request.OperationId, success, version = request.Release.Version,
            targetPath = workspace.TargetPath(request.Release), originalPath = request.ParentPath, message });
    }

    // Console.In can implement ReadAsync with a synchronous underlying pipe read.
    // Move invocation off the caller and bound the wait itself, not just the read's
    // cooperative cancellation. On failure the helper exits without applying.
    internal static Task<string?> ReadHandshakeAsync(TextReader reader, CancellationToken token) =>
        Task.Run(() => ReadBoundedLineAsync(reader, 64, token), CancellationToken.None).WaitAsync(token);

    private static async Task<string?> ReadBoundedLineAsync(TextReader reader, int limit, CancellationToken token)
    {
        var text = new StringBuilder();
        var one = new char[1];
        while (await reader.ReadAsync(one.AsMemory(), token) != 0)
        {
            if (one[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= limit) throw new InvalidDataException("Invalid app update handshake.");
            text.Append(one[0]);
        }
        return null;
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(Path.GetFullPath(first), Path.GetFullPath(second), StringComparison.OrdinalIgnoreCase);
    private static bool IsOrdinaryLocalPath(string? path) => !string.IsNullOrWhiteSpace(path) && path == path.Trim() &&
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' &&
        !path.Any(char.IsControl) && path.IndexOfAny(['"', '*', '?']) < 0 && !path.AsSpan(2).Contains(':') &&
        Path.IsPathFullyQualified(path);
    private static bool IsHash(string? hash) => hash is { Length: 64 } && hash.All(Uri.IsHexDigit);
}
