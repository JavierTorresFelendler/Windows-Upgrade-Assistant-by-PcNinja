using System.Text.Json;
using PcNinja.Win11Upgrade.Core;

namespace PcNinja.Win11UpgradeAssistant;

internal static class CliRunner
{
    internal const int Success = 0;
    internal const int UnexpectedError = 1;
    internal const int InvalidArguments = 2;
    internal const int ValidationFailed = 3;
    internal const int DownloadFailed = 4;
    internal const int AdministratorRequired = 5;
    internal const int Cancelled = 6;
    internal const int SetupFailed = 7;
    internal const int OutcomeUnconfirmed = 8;
    private static readonly string Version = CliHelp.Version;
    private static volatile bool _setupRunning;

    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            if (!_setupRunning) cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            if (CliHelp.TryGetRequestedTopic(args, out var helpTopic))
            {
                if (args.Length > 2 && !(args.Length == 3 && helpTopic == "app-update" &&
                    args[0].Equals("app-update", StringComparison.OrdinalIgnoreCase)))
                    return Fail(InvalidArguments, "Help accepts one topic. Use 'help all' for the complete manual.", false);
                if (!CliHelp.TryPrint(helpTopic))
                    return Fail(InvalidArguments,
                        $"Unknown help topic: {helpTopic}. Use 'help' to list available topics.", false);
                return Success;
            }

            if (args[0].Equals("completion", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length != 2 || !args[1].Equals("powershell", StringComparison.OrdinalIgnoreCase))
                    return Fail(InvalidArguments,
                        "Usage: completion powershell. Run 'help completion' for setup instructions.", false);
                var executableName = Path.GetFileName(Environment.ProcessPath)
                    ?? ProductIdentity.ExecutableName;
                Console.WriteLine(CliHelp.GetPowerShellCompletion(executableName));
                return Success;
            }

            if (args[0].Equals("app-update", StringComparison.OrdinalIgnoreCase))
                return await CliAppUpdates.RunAsync(args.Skip(1).ToArray(), cancellation.Token);
            var options = CliOptions.Parse(args);
            if (options.Quiet && !options.Yes && !options.DryRun)
                return Fail(Cancelled, "Quiet upgrade requires explicit --yes consent. Windows Setup was not started.", options.Json);
            if (options.Command == "version") { Console.WriteLine(Version); return Success; }
            if (options.Command == "log-path") { Console.WriteLine(AppLog.Path); return Success; }

            var engine = new UpgradeEngine();
            return options.Command switch
            {
                "status" => ShowStatus(engine, options.Json),
                "downloads" => await ShowDownloadsAsync(engine, options.Json, cancellation.Token),
                "scan" or "refresh" or "rescan" => ShowScan(engine, options.Json),
                "validate" => await ValidateOrUpgradeAsync(engine, options, false, cancellation.Token),
                "upgrade" => await ValidateOrUpgradeAsync(engine, options, true, cancellation.Token),
                _ => Fail(InvalidArguments,
                    $"Unknown command: {options.Command}. Use 'help' to list commands.", options.Json)
            };
        }
        catch (OperationCanceledException)
        {
            return Fail(Cancelled, "Operation cancelled. Windows Setup was not started.", args.Contains("--json", StringComparer.OrdinalIgnoreCase));
        }
        catch (CliArgumentException ex)
        {
            return Fail(InvalidArguments, ex.Message, args.Contains("--json", StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            AppLog.Error($"CLI failure: {ex}");
            return Fail(UnexpectedError, ex.Message, args.Contains("--json", StringComparer.OrdinalIgnoreCase));
        }
        finally { Console.CancelKeyPress -= cancelHandler; _setupRunning = false; }
    }

    private static int ShowStatus(UpgradeEngine engine, bool json)
    {
        var storage = engine.CheckSystemDriveSpace();
        var profile = DownloadSystem.Read();
        var automaticSource = DownloadSourcePolicy.GetAutomaticSource(profile);
        string? supportError = null;
        try { engine.ValidateSupportedComputer(); }
        catch (Exception ex) { supportError = ex.Message; }
        var value = new
        {
            version = Version,
            build = ProductIdentity.BuildVersion,
            operatingSystem = engine.GetOperatingSystemDescription(),
            upgradeSupported = supportError is null,
            supportError,
            systemUiLanguage = profile.Language,
            automaticDownloadAvailable = AutomaticDownloadPolicy.IsAvailable(profile),
            automaticDownloadUnavailableReason = AutomaticDownloadPolicy.GetUnavailableReason(profile),
            downloadProfile = new { edition = profile.Edition, language = profile.Language, build = profile.Build,
                revision = profile.Revision, architecture = profile.Architecture, installationType = profile.InstallationType,
                isLtsc = profile.IsLtsc, isServer = profile.IsServer, isSupportedClient = profile.IsSupportedClient },
            automaticDownloadSourceId = automaticSource?.Id,
            automaticDownloadSourceName = automaticSource?.Name,
            pcNinjaDownloadPage = DownloadSourcePolicy.GetPcNinjaBrowserPage(profile),
            isAdministrator = Program.IsAdministrator(),
            systemDrive = storage.RootPath,
            systemDriveFreeBytes = storage.AvailableBytes,
            systemDriveReady = storage.IsValid,
            logPath = AppLog.Path,
            previousUpgrade = UpgradeHistory.ReadStatus()
        };
        Write(value, json,
            $"{ProductIdentity.Name}\n" +
            $"OS: {value.operatingSystem}\nUpgrade supported: {value.upgradeSupported}" +
            (supportError is null ? "" : $" ({supportError})") +
            $"\nSystem UI language: {profile.Language}\nAutomatic download available: {value.automaticDownloadAvailable}" +
            (value.automaticDownloadUnavailableReason is null ? "" : $"\n{value.automaticDownloadUnavailableReason}") +
            $"\nDownload profile: {profile.Edition}, {profile.Architecture}, {profile.InstallationType}, " +
            $"build {profile.Build}.{profile.Revision}\nPcNinja download page: {value.pcNinjaDownloadPage}" +
            $"\nAdministrator: {value.isAdministrator}\n" +
            $"System storage: {storage.Message}\nLog: {AppLog.Path}" +
            (value.previousUpgrade is null ? "" : $"\nPrevious upgrade: {value.previousUpgrade.State}. {value.previousUpgrade.Message}"),
            "STATUS_REPORTED");
        return Success;
    }

    private static async Task<int> ShowDownloadsAsync(UpgradeEngine engine, bool json, CancellationToken token)
    {
        try { return ReportDownloadRecommendation(await engine.GetDownloadRecommendationAsync(token), json); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            AppLog.Error($"CLI download recommendation failed: {ex}");
            return Fail(DownloadFailed, $"Could not retrieve a matching download recommendation. {ex.Message}", json);
        }
    }

    private static int ReportDownloadRecommendation(DownloadRecommendation recommendation, bool json)
    {
        Write(new { recommendation, automaticTransferStarted = false, setupStarted = false }, json,
            $"{recommendation.Message}\nSource: {recommendation.Source}\nDownload page: {recommendation.PageUrl}" +
            (recommendation.FileName is null ? "" : $"\nFile: {recommendation.FileName}") +
            (recommendation.TargetRelease is null ? "" : $"\nTarget release: {recommendation.TargetRelease}") +
            (recommendation.TargetVersion is null ? "" : $"\nTarget version: {recommendation.TargetVersion}") +
            "\nThis command does not download an ISO or open a browser. Download the matching ISO manually, " +
            "then use validate --iso <path> or upgrade --iso <path>.", "DOWNLOAD_RECOMMENDATION");
        return Success;
    }

    private static string? GetAutomaticDownloadBlock(DownloadSystem profile) =>
        AutomaticDownloadPolicy.GetUnavailableReason(profile) is string reason
            ? reason + " Run 'downloads' or 'downloads --json' for a matching manual-download recommendation; " +
              "then supply the exact ISO with --iso <path>. No automatic transfer or Setup was started."
            : null;

    private static int ShowScan(UpgradeEngine engine, bool json)
    {
        var local = engine.FindLocalIsos();
        var mounted = engine.FindMountedSources();
        var drives = engine.FindDownloadLocations();
        if (json)
        {
            Write(new { localIsos = local, mountedSources = mounted, downloadLocations = drives }, true, "", "SOURCES_SCANNED");
        }
        else
        {
            Console.WriteLine("Local ISO files:");
            WriteList(local);
            Console.WriteLine("Mounted Windows sources:");
            WriteList(mounted.Select(source => source.Root));
            Console.WriteLine("Download locations:");
            WriteList(drives.Select(drive => drive.ToString()));
            Console.WriteLine("RESULT_CODE=SOURCES_SCANNED; EXIT_CODE=0; READY_FOR_RESTART=false");
            AppLog.Info("CLI result: SOURCES_SCANNED; exit: 0; readyForRestart: False.");
        }
        return Success;
    }

    private static string DownloadFailureMessage(string error, bool browserOpened, DownloadSystem system) =>
        error + (browserOpened ? " The PcNinja browser fallback was opened. After the ISO downloads, rerun with --iso <path>."
            : " Automatic download did not complete; provide the ISO with --iso <path>.") +
        $" Download: {DownloadSourcePolicy.GetPcNinjaBrowserPage(system)}.";

    private static async Task<int> ValidateOrUpgradeAsync(UpgradeEngine engine, CliOptions options, bool startSetup, CancellationToken token)
    {
        try { engine.ValidateSupportedComputer(); }
        catch (Exception ex) { return Fail(ValidationFailed, ex.Message, options.Json); }

        if (!Program.IsAdministrator())
            return Fail(AdministratorRequired,
                "Run this command from an elevated Command Prompt or PowerShell window.", options.Json);

        var sourceCount = (options.IsoPath is null ? 0 : 1) + (options.MountedRoot is null ? 0 : 1) + (options.Download ? 1 : 0);
        if (sourceCount != 1)
            return Fail(InvalidArguments,
                "Choose exactly one source: --iso <path>, --mounted <drive>, or --download.", options.Json);

        if (options.Download && GetAutomaticDownloadBlock(DownloadSystem.Read()) is string reason)
            return Fail(ValidationFailed, reason, options.Json);

        var storage = engine.CheckSystemDriveSpace();
        if (!storage.IsValid) return Fail(ValidationFailed, storage.Message, options.Json);

        MountedSource source;
        string? exactIsoPath = null;
        try
        {
            if (options.Download)
            {
                var destination = options.Destination ?? GetDefaultDestination(engine);
                var progress = options.Json || options.Quiet ? null : new Progress<DownloadProgress>(item =>
                {
                    var progressText = item.Stage ?? (item.Percentage.HasValue ? $"{item.Percentage}%" : UpgradeEngine.FormatBytes(item.BytesReceived));
                    Console.Error.Write($"\rDownloading {Path.GetFileName(item.DestinationPath)}: {progressText}     ");
                });
                DownloadResult download;
                try
                {
                    download = await engine.DownloadIsoAsync(destination, progress, token, options.Sha256);
                    if (!options.Json && !options.Quiet) Console.Error.WriteLine();
                }
                catch (DownloadTransferException ex)
                {
                    if (!options.Json && !options.Quiet) Console.Error.WriteLine();
                    var browserOpened = false;
                    if (!options.Json && !options.Quiet && !Console.IsInputRedirected && !Console.IsOutputRedirected)
                    {
                        try
                        {
                            UpgradeEngine.OpenPcNinjaDownloadPage();
                            browserOpened = true;
                        }
                        catch (Exception browserError)
                        {
                            AppLog.Error($"CLI browser fallback failed: {browserError}");
                        }
                    }
                    return Fail(DownloadFailed, DownloadFailureMessage(ex.Message, browserOpened, DownloadSystem.Read()), options.Json);
                }
                exactIsoPath = download.IsoPath;
                source = download.ValidatedSource
                    ?? throw new InvalidOperationException("The downloaded ISO did not complete media validation.");
            }
            else if (options.IsoPath is not null)
            {
                exactIsoPath = Path.GetFullPath(options.IsoPath.Trim('"'));
                source = (await engine.PrepareIsoAsync(exactIsoPath,
                    options.Sha256 ?? UpgradeEngine.ConfiguredIsoSha256, token)).Source;
            }
            else
            {
                source = await engine.ValidateMountedSourceAsync(options.MountedRoot!, token);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return Fail(ValidationFailed, ex.Message, options.Json);
        }

        InstallationImage? selectedImage = null;
        try
        {
            if (options.ImageIndex.HasValue || (startSetup && !options.DryRun))
                selectedImage = InstallationImages.Select(source, options.ImageIndex, CurrentWindows.Read());
        }
        catch (InvalidOperationException ex) { return Fail(ValidationFailed, ex.Message, options.Json); }
        var matchingImages = InstallationImages.Compatible(source, CurrentWindows.Read());
        var previewImage = selectedImage ?? (matchingImages.Count == 1 ? matchingImages[0] : null);
        var previewProfile = previewImage is null ? null : InstallationImages.GetSetupProfile(previewImage);
        if (!startSetup || options.DryRun)
        {
            Write(new { valid = true, isoPath = exactIsoPath, mountedRoot = source.Root, setupPath = source.SetupPath,
                    imagePath = source.ImagePath, images = source.Images, compatibleImageIndexes = matchingImages.Select(image => image.Index),
                    selectedImageIndex = previewImage?.Index, setupProfile = previewProfile?.ToString(),
                    quiet = options.Quiet, dryRun = options.DryRun,
                    buildOrderChecked = previewProfile is null ? (bool?)null : previewProfile != WindowsSetupProfile.Windows10,
                    policyNotice = previewProfile == WindowsSetupProfile.Windows10 ? InstallationImages.Windows10Notice : null },
                options.Json, $"Validation passed.\nSource: {source.Root}\nImage file: {source.ImagePath}\n" +
                string.Join("\n", (source.Images ?? []).Select(image => image.ToString())) +
                $"\nCompatible indexes: {string.Join(", ", matchingImages.Select(image => image.Index))}" +
                (previewProfile == WindowsSetupProfile.Windows10 ? $"\n{InstallationImages.Windows10Notice}" : ""),
                startSetup ? "DRY_RUN_COMPLETED" : "VALIDATION_PASSED");
            return Success;
        }

        if (!options.Yes)
        {
            if (Console.IsInputRedirected)
                return Fail(Cancelled, "Confirmation is required. Re-run interactively or add --yes.", options.Json);
            Console.Error.WriteLine(InstallationImages.GetSetupProfile(selectedImage!) == WindowsSetupProfile.Windows10
                ? $"WARNING: {InstallationImages.Windows10Notice}"
                : "WARNING: This uses an experimental unsupported-hardware upgrade method.");
            Console.Error.WriteLine($"Image {selectedImage!.Index}: {selectedImage.Name}");
            Console.Error.WriteLine(options.NoRestart
                ? "Automatic restart is disabled."
                : "Windows Setup may restart this computer automatically.");
            Console.Error.Write("Type YES to start Windows Setup: ");
            if (!string.Equals(Console.ReadLine(), "YES", StringComparison.Ordinal))
                return Fail(Cancelled, "Upgrade cancelled. Windows Setup was not started.", options.Json);
        }

        token.ThrowIfCancellationRequested();
        UpgradeResult result;
        try { result = await engine.StartUpgradeAsync(source, selectedImage!.Index, !options.NoRestart, token,
            () => _setupRunning = true, quiet: options.Quiet); }
        catch (OperationCanceledException) { throw; }
        catch (InvalidOperationException ex) { return Fail(ValidationFailed, ex.Message, options.Json); }
        return ReportSetupResult(result, options, source, selectedImage!, exactIsoPath);
    }

    private static int ReportSetupResult(UpgradeResult result, CliOptions options, MountedSource source,
        InstallationImage selectedImage, string? exactIsoPath)
    {
        if (result.ExitCode == 0x40010004)
            return Fail(OutcomeUnconfirmed, "Setup exited during the transition. The upgrade outcome is not confirmed. After restarting, run status --json and verify your files/apps.", options.Json, result.ExitCode);
        if (result.ExitCode != 0)
            return Fail(SetupFailed,
                $"Windows Setup stopped with exit code {result.ExitCode}. No forced restart was issued.", options.Json, result.ExitCode);
        Write(new { started = true, setupExitCode = result.ExitCode, automaticRestart = !options.NoRestart,
                setupExitCodeHex = $"0x{unchecked((uint)result.ExitCode):X8}", quiet = options.Quiet,
                manualRestartRequired = result.NeedsManualRestartNotice(!options.NoRestart),
                setupProfile = InstallationImages.GetSetupProfile(selectedImage)?.ToString(),
                buildOrderChecked = InstallationImages.GetSetupProfile(selectedImage) != WindowsSetupProfile.Windows10,
                isoPath = exactIsoPath, mountedRoot = source.Root, imageIndex = selectedImage.Index,
                outcome = "PreparationCompleted" },
            options.Json, result.NeedsManualRestartNotice(!options.NoRestart)
                ? UpgradeResult.ManualRestartMessage
                : "Windows Setup completed its current stage. Verify the final outcome after restarting.",
            options.NoRestart ? "UPGRADE_PREPARED_RESTART_REQUIRED" : "UPGRADE_PREPARED_AUTOMATIC_RESTART",
            result.NeedsManualRestartNotice(!options.NoRestart));
        return Success;
    }

    private static string GetDefaultDestination(UpgradeEngine engine)
    {
        var location = engine.FindDownloadLocations().FirstOrDefault(item => item.DriveType == DriveType.Fixed)
            ?? throw new InvalidOperationException("No suitable download drive was found. Use --destination <folder>.");
        return Path.Combine(location.RootPath, "PcNinja Downloads");
    }

    private static int Fail(int code, string message, bool json, int? setupExitCode = null)
    {
        var resultCode = code switch
        {
            InvalidArguments => "INVALID_ARGUMENTS", ValidationFailed => "VALIDATION_FAILED",
            DownloadFailed => "DOWNLOAD_FAILED", AdministratorRequired => "ADMINISTRATOR_REQUIRED",
            Cancelled => "CANCELLED", SetupFailed => "SETUP_FAILED", OutcomeUnconfirmed => "SETUP_OUTCOME_UNCONFIRMED",
            _ => "UNEXPECTED_ERROR"
        };
        var setupExitCodeHex = setupExitCode.HasValue ? $"0x{unchecked((uint)setupExitCode.Value):X8}" : null;
        var payload = CliAutomationResult.Create(new { error = message, setupExitCode, setupExitCodeHex },
            resultCode, code, message, readyForRestart: false);
        AppLog.Info($"CLI result: {resultCode}; exit: {code}; Setup: {setupExitCodeHex ?? "not reported"}; {message}");
        if (json) Console.WriteLine(JsonSerializer.Serialize(payload));
        else Console.Error.WriteLine($"ERROR: {message}\nRESULT_CODE={resultCode}; EXIT_CODE={code}" +
            (setupExitCodeHex is null ? "" : $"; SETUP_EXIT_CODE={setupExitCodeHex}"));
        return code;
    }

    private static void Write(object value, bool json, string text, string resultCode, bool readyForRestart = false)
    {
        var payload = CliAutomationResult.Create(value, resultCode, Success, text, readyForRestart);
        AppLog.Info($"CLI result: {resultCode}; exit: 0; readyForRestart: {readyForRestart}.");
        Console.WriteLine(json ? JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true })
            : $"{text}\nRESULT_CODE={resultCode}; EXIT_CODE=0; READY_FOR_RESTART={readyForRestart.ToString().ToLowerInvariant()}");
    }

    private static void WriteList(IEnumerable<string> items)
    {
        var values = items.ToArray();
        if (values.Length == 0) Console.WriteLine("  (none)");
        else foreach (var value in values) Console.WriteLine($"  {value}");
    }

}

internal sealed record CliOptions(
    string Command,
    string? IsoPath,
    string? MountedRoot,
    bool Download,
    string? Destination,
    bool NoRestart,
    bool Yes,
    bool DryRun,
    bool Json,
    string? Sha256 = null,
    int? ImageIndex = null,
    bool Quiet = false)
{
    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0) throw new CliArgumentException("No command was supplied.");
        if (args[0].Equals("--version", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 1) throw new CliArgumentException("--version does not accept additional options.");
            return Empty("version");
        }

        var command = args[0].ToLowerInvariant();
        string? iso = null;
        string? mounted = null;
        string? destination = null;
        string? sha256 = null;
        int? imageIndex = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var download = false;
        var noRestart = false;
        var yes = false;
        var dryRun = false;
        var json = false;
        var quiet = false;

        for (var index = 1; index < args.Length; index++)
        {
            if (!seen.Add(args[index])) throw new CliArgumentException($"Repeated option: {args[index]}.");
            switch (args[index].ToLowerInvariant())
            {
                case "--iso": iso = RequireValue(args, ref index, "--iso"); break;
                case "--mounted": mounted = RequireValue(args, ref index, "--mounted"); break;
                case "--download": download = true; break;
                case "--destination": destination = RequireValue(args, ref index, "--destination"); break;
                case "--no-restart": noRestart = true; break;
                case "--quiet": quiet = true; break;
                case "--yes": yes = true; break;
                case "--dry-run": dryRun = true; break;
                case "--json": json = true; break;
                case "--image-index":
                    if (!int.TryParse(RequireValue(args, ref index, "--image-index"), out var value) || value < 1)
                        throw new CliArgumentException("--image-index must be a positive integer.");
                    imageIndex = value;
                    break;
                case "--sha256":
                    try { sha256 = UpgradeEngine.NormalizeSha256(RequireValue(args, ref index, "--sha256")); }
                    catch (ArgumentException ex) { throw new CliArgumentException(ex.Message); }
                    break;
                default: throw new CliArgumentException(
                    $"Unknown option for {command}: {args[index]}. Run '{command} --help' for valid options.");
            }
        }

        var allowed = command switch
        {
            "status" or "downloads" or "scan" or "refresh" or "rescan" => new[] { "--json" },
            "validate" => new[] { "--iso", "--mounted", "--sha256", "--image-index", "--json" },
            "upgrade" => new[] { "--iso", "--mounted", "--download", "--destination", "--sha256", "--image-index", "--no-restart", "--quiet", "--yes", "--dry-run", "--json" },
            _ => Array.Empty<string>()
        };
        foreach (var option in seen)
            if (!allowed.Contains(option, StringComparer.OrdinalIgnoreCase))
                throw new CliArgumentException($"{option} is not valid for {command}. See '{command} --help'.");
        if (destination is not null && !download) throw new CliArgumentException("--destination requires --download.");
        if (sha256 is not null && mounted is not null) throw new CliArgumentException("--sha256 requires --iso or --download.");
        return new CliOptions(command, iso, mounted, download, destination, noRestart, yes, dryRun, json, sha256, imageIndex, quiet);
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
            throw new CliArgumentException($"{option} requires a value.");
        return args[index];
    }

    private static CliOptions Empty(string command) => new(command, null, null, false, null, false, false, false, false);
}

internal sealed class CliArgumentException(string message) : Exception(message);
