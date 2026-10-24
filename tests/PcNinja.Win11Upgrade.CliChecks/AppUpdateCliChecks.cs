using System.Text.Json;
using PcNinja.Win11Upgrade.Core;
using PcNinja.Win11UpgradeAssistant;

internal static class AppUpdateCliChecks
{
    internal static async Task RunAsync()
    {
        foreach (var action in new[] { "check", "status", "install" })
        {
            var help = await CaptureAsync(() => CliRunner.RunAsync(["app-update", action, "--help"]));
            if (help.Code != 0 || !help.Output.Contains("COMMAND: app-update"))
                throw new Exception("Real CLI routing rejected updater subcommand help.");
        }
        var assetName = AppUpdateIdentity.AssetNameFor("9.9.9");
        var release = new AppUpdateManifest(1, AppUpdateIdentity.Product, AppUpdateIdentity.Repository, "stable",
            "9.9.9", "v9.9.9", assetName,
            $"https://github.com/{AppUpdateIdentity.Repository}/releases/download/v9.9.9/{assetName}",
            123456, new string('a', 64), $"https://github.com/{AppUpdateIdentity.Repository}/releases/tag/v9.9.9");
        var checks = 0;
        var installs = 0;
        Task<AppUpdateCheckResult> UnexpectedCheck(CancellationToken _)
        { checks++; throw new Exception("This argument/permission case must not perform a release lookup."); }
        Task<AppUpdateHandoffResult> UnexpectedInstall(AppUpdateManifest _, CancellationToken __)
        { installs++; throw new Exception("This case must not perform installation/handoff."); }

        foreach (var args in new[]
        {
            Array.Empty<string>(), new[] { "unknown", "--json" }, new[] { "check", "--yes", "--json" },
            new[] { "check", "--quiet", "--json" }, new[] { "install", "--yes", "--yes", "--json" },
            new[] { "install", "--json", "--json" }, new[] { "install", "--yes", "--iso", "unused.iso", "--json" }
        })
        {
            var result = await CaptureAsync(() => CliAppUpdates.RunAsync(args, CancellationToken.None,
                UnexpectedCheck, UnexpectedInstall, () => throw new Exception("Invalid arguments reached the administrator probe.")));
            if (result.Code != 2) throw new Exception("Invalid app-update options were accepted.");
            if (args.Contains("--json")) AssertEnvelope(result, "INVALID_ARGUMENTS", 2);
        }
        var declined = await CaptureAsync(() => CliAppUpdates.RunAsync(["install", "--json"], CancellationToken.None,
            UnexpectedCheck, UnexpectedInstall, () => throw new Exception("Consent must precede permission/network checks.")));
        AssertEnvelope(declined, "APP_UPDATE_CONSENT_REQUIRED", 6);
        var administratorChecks = 0;
        var denied = await CaptureAsync(() => CliAppUpdates.RunAsync(["install", "--yes", "--json"], CancellationToken.None,
            UnexpectedCheck, UnexpectedInstall, () => { administratorChecks++; return false; }));
        AssertEnvelope(denied, "ADMINISTRATOR_REQUIRED", 5);
        if (checks != 0 || installs != 0 || administratorChecks != 1)
            throw new Exception("Consent/permission/argument rejection reached HTTP or installation.");

        foreach (var state in Enum.GetValues<AppUpdateCheckState>())
        {
            checks = 0;
            var result = await CaptureAsync(() => CliAppUpdates.RunAsync(["check", "--json"], CancellationToken.None,
                _ => { checks++; return Task.FromResult(new AppUpdateCheckResult(state, "Fixture " + state,
                    state == AppUpdateCheckState.Available ? release : null)); },
                UnexpectedInstall, () => throw new Exception("A read-only app check requested administrator permission.")));
            var code = state == AppUpdateCheckState.Unavailable ? 4 : 0;
            var identifier = state switch
            {
                AppUpdateCheckState.Available => "APP_UPDATE_AVAILABLE",
                AppUpdateCheckState.UpToDate => "APP_UPDATE_CURRENT",
                AppUpdateCheckState.NoStableRelease => "APP_UPDATE_NO_STABLE_RELEASE",
                AppUpdateCheckState.NoBetaRelease => "APP_UPDATE_NO_BETA_RELEASE",
                _ => "APP_UPDATE_CHECK_UNAVAILABLE"
            };
            AssertEnvelope(result, identifier, code);
            if (checks != 1 || installs != 0) throw new Exception("Read-only app checking started installation or repeated its lookup.");
        }

        foreach (var state in new[] { AppUpdateCheckState.UpToDate, AppUpdateCheckState.NoStableRelease, AppUpdateCheckState.Unavailable })
        {
            var result = await CaptureAsync(() => CliAppUpdates.RunAsync(["install", "--yes", "--json"], CancellationToken.None,
                _ => Task.FromResult(new AppUpdateCheckResult(state, "No installable fixture")), UnexpectedInstall, () => true));
            if (result.Code != (state == AppUpdateCheckState.Unavailable ? 4 : 0) || installs != 0)
                throw new Exception("An unavailable/current release unexpectedly entered installation.");
        }

        foreach (var failure in new (Exception Error, int Code, string Result)[]
        {
            (new OperationCanceledException("fixture cancel"), 6, "APP_UPDATE_CANCELLED"),
            (new UnauthorizedAccessException("fixture denied"), 5, "APP_UPDATE_PERMISSION_DENIED"),
            (new InvalidOperationException("fixture busy/reboot pending"), 3, "APP_UPDATE_BLOCKED"),
            (new IOException("fixture checksum/transfer error"), 4, "APP_UPDATE_FAILED"),
            (new TimeoutException("fixture timeout"), 4, "APP_UPDATE_FAILED")
        })
        {
            var result = await CaptureAsync(() => CliAppUpdates.RunAsync(["check", "--json"], CancellationToken.None,
                _ => Task.FromException<AppUpdateCheckResult>(failure.Error), UnexpectedInstall, () => false));
            AssertEnvelope(result, failure.Result, failure.Code);
        }

        using var cancellation = new CancellationTokenSource();
        var calls = new List<string>();
        var operationId = Guid.NewGuid().ToString("N");
        var target = @"C:\Program Files\PcNinja\WindowsUpgradeAssistant\" + assetName;
        var handedOff = await CaptureAsync(() => CliAppUpdates.RunAsync(["install", "--yes", "--json"], cancellation.Token,
            token =>
            {
                if (token != cancellation.Token) throw new Exception("App check lost its cancellation token.");
                calls.Add("check");
                return Task.FromResult(new AppUpdateCheckResult(AppUpdateCheckState.Available, "New fixture", release));
            },
            (candidate, token) =>
            {
                if (!ReferenceEquals(candidate, release) || token != cancellation.Token)
                    throw new Exception("App install lost the exact release or cancellation token.");
                calls.Add("install");
                return Task.FromResult(new AppUpdateHandoffResult(release.Version, target, operationId, 12345,
                    "Fixture handoff only; replacement follows after process exit."));
            }, () => { calls.Add("administrator"); return true; }));
        AssertEnvelope(handedOff, "APP_UPDATE_HANDOFF_STARTED", 0);
        using (var document = JsonDocument.Parse(handedOff.Output))
        {
            var root = document.RootElement;
            if (!calls.SequenceEqual(["administrator", "check", "install"]) ||
                root.GetProperty("relaunchGui").GetBoolean() || root.GetProperty("targetVersion").GetString() != release.Version ||
                root.GetProperty("TargetPath").GetString() != target || root.GetProperty("OperationId").GetString() != operationId ||
                root.GetProperty("HelperProcessId").GetInt32() != 12345)
                throw new Exception("CLI handoff lost its exact identity, implied relaunch or bypassed the explicit gates.");
        }
        var text = await CaptureAsync(() => CliAppUpdates.RunAsync(["check"], CancellationToken.None,
            _ => Task.FromResult(new AppUpdateCheckResult(AppUpdateCheckState.UpToDate, "Current fixture")),
            UnexpectedInstall, () => false));
        if (text.Code != 0 || !text.Output.Contains("RESULT_CODE=APP_UPDATE_CURRENT", StringComparison.Ordinal) ||
            !text.Output.Contains("READY_FOR_RESTART=false", StringComparison.Ordinal) || installs != 0)
            throw new Exception("App-update text output omitted the result/non-restart contract.");
        Console.WriteLine("PASS: App-update CLI consent/arguments/admin before HTTP, check states/errors, exact handoff and no GUI/Setup/restart success claim. All lookups/install effects mocked; no network, replacement or Windows operation.");
    }

    private static void AssertEnvelope((int Code, string Output, string Error) result, string identifier, int code)
    {
        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        if (result.Code != code || root.GetProperty("exitCode").GetInt32() != code ||
            root.GetProperty("resultCode").GetString() != identifier || root.GetProperty("schemaVersion").GetInt32() != 1 ||
            root.GetProperty("command").GetString() != "app-update" || root.GetProperty("readyForRestart").GetBoolean() ||
            root.GetProperty("applicationUpdated").GetBoolean() || root.GetProperty("setupStarted").GetBoolean() ||
            root.GetProperty("success").GetBoolean() != (code == 0))
            throw new Exception("App-update CLI output lost its exit contract or invented update/Setup/restart success.");
    }

    private static async Task<(int Code, string Output, string Error)> CaptureAsync(Func<Task<int>> action)
    {
        var output = Console.Out;
        var error = Console.Error;
        using var capture = new StringWriter();
        using var errors = new StringWriter();
        try
        {
            Console.SetOut(capture);
            Console.SetError(errors);
            var code = await action();
            return (code, capture.ToString(), errors.ToString());
        }
        finally { Console.SetOut(output); Console.SetError(error); }
    }
}
