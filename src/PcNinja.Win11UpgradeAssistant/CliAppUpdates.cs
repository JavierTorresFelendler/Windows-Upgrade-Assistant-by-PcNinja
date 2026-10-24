using System.Text.Json;
using PcNinja.Win11Upgrade.Core;

namespace PcNinja.Win11UpgradeAssistant;

internal static class CliAppUpdates
{
    internal static async Task<int> RunAsync(string[] args, CancellationToken token,
        Func<CancellationToken, Task<AppUpdateCheckResult>>? check = null,
        Func<AppUpdateManifest, CancellationToken, Task<AppUpdateHandoffResult>>? install = null,
        Func<bool>? administrator = null)
    {
        var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
        var action = args.Length > 0 ? args[0].ToLowerInvariant() : "";
        if (action == "install") action = "run"; // Unreleased legacy alias; this never installs software.
        var switches = args.Skip(1).Select(item => item.ToLowerInvariant()).ToArray();
        if (action is not ("check" or "run" or "status") ||
            switches.Distinct().Count() != switches.Length ||
            switches.Any(item => item != "--json" && !(item == "--yes" && action == "run")))
            return Report(2, "INVALID_ARGUMENTS", "Usage: app-update check|status [--json], or app-update run --yes [--json].", json);
        if (action == "status") return Report(0, "APP_UPDATE_STATUS", "Local application update status (no network request).", json,
            new { targetDirectory = AppUpdateInstaller.DestinationDirectory, lastAttempt = ReadLastAttempt() });
        if (action == "run" && !switches.Contains("--yes"))
            return Report(6, "APP_UPDATE_CONSENT_REQUIRED", "Downloading and running a new version requires explicit --yes. No download or replacement was started.", json);
        if (action == "run" && !(administrator?.Invoke() ?? Program.IsAdministrator()))
            return Report(5, "ADMINISTRATOR_REQUIRED", "Run this command from an elevated terminal.", json);
        using var client = new AppUpdateClient();
        try
        {
            var result = await (check?.Invoke(token) ?? client.CheckAsync(ProductIdentity.Version, token));
            token.ThrowIfCancellationRequested();
            if (result.State == AppUpdateCheckState.Unavailable)
                return Report(4, "APP_UPDATE_CHECK_UNAVAILABLE", result.Message, json);
            if (result.State == AppUpdateCheckState.NoStableRelease)
                return Report(0, "APP_UPDATE_NO_STABLE_RELEASE", result.Message, json);
            if (result.State == AppUpdateCheckState.NoBetaRelease)
                return Report(0, "APP_UPDATE_NO_BETA_RELEASE", result.Message, json);
            if (result.State != AppUpdateCheckState.Available || result.Release is null)
                return Report(0, "APP_UPDATE_CURRENT", result.Message, json);
            if (action == "check") return Report(0, "APP_UPDATE_AVAILABLE", result.Message, json,
                new { release = result.Release, targetPath = AppUpdateInstaller.DestinationFor(result.Release) });
            var handoff = await (install?.Invoke(result.Release, token)
                ?? AppUpdateInstaller.InstallAsync(client, result.Release, false, null, token));
            return Report(0, "APP_UPDATE_HANDOFF_STARTED", handoff.Message, json, new
            {
                targetVersion = handoff.Version, handoff.TargetPath, handoff.OperationId,
                handoff.HelperProcessId, applicationUpdated = false, relaunchGui = false
            });
        }
        catch (OperationCanceledException) { return Report(6, "APP_UPDATE_CANCELLED", "Download/run cancelled. The current executable is unchanged.", json); }
        catch (UnauthorizedAccessException ex) { return Report(5, "APP_UPDATE_PERMISSION_DENIED", ex.Message, json); }
        catch (InvalidOperationException ex) { return Report(3, "APP_UPDATE_BLOCKED", ex.Message, json); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or System.ComponentModel.Win32Exception)
        { AppLog.Error($"CLI app update failed: {ex}"); return Report(4, "APP_UPDATE_FAILED", ex.Message, json); }
    }

    private static JsonElement? ReadLastAttempt()
    {
        try
        {
            var path = Path.Combine(AppUpdateInstaller.DestinationDirectory, AppUpdateWorkspace.StateDirectoryName, "Last-AppUpdate.json");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > 8192) return null;
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
            return json.RootElement.Clone();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static int Report(int code, string resultCode, string message, bool json, object? details = null)
    {
        var payload = CliAutomationResult.Create(details ?? new { }, resultCode, code, message, false);
        payload["command"] = "app-update";
        payload["applicationUpdated"] = false; // A pre-exit handoff is never proof of replacement.
        payload["setupStarted"] = false;
        if (code != 0) payload["error"] = message;
        var text = $"{message}\nRESULT_CODE={resultCode}; EXIT_CODE={code}; READY_FOR_RESTART=false";
        if (json) Console.WriteLine(JsonSerializer.Serialize(payload));
        else if (code == 0) Console.WriteLine(text);
        else Console.Error.WriteLine(text);
        AppLog.Info($"CLI application update result: {resultCode}; exit {code}; no OS restart authorization.");
        return code;
    }
}
