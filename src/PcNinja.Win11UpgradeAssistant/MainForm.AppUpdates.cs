using PcNinja.Win11Upgrade.Core;

namespace PcNinja.Win11UpgradeAssistant;

internal sealed partial class MainForm
{
    private readonly AppUpdateClient _applicationUpdates = new();
    private readonly LinkLabel _appUpdatesLink = new();
    private CancellationTokenSource? _appUpdateCheck;
    private AppUpdateManifest? _availableAppUpdate;
    private bool _automaticAppUpdateCheck = true;
    private bool _offerAppUpdates = true;
    private bool _closingForAppUpdate;
    private Func<CancellationToken, Task<AppUpdateCheckResult>>? _checkAppUpdate = null;
    private Func<AppUpdateManifest, IProgress<AppUpdateProgress>, CancellationToken, Task<AppUpdateHandoffResult>>? _installAppUpdate = null;
    private Func<string, bool>? _confirmAppUpdate = null;
    private Action<string>? _showAppUpdateFailure = null;

    private async Task CheckAppUpdatesAsync(bool manual)
    {
        if (_operation is not null || _appUpdateCheck is not null || _setupHasStarted) return;
        using var check = new CancellationTokenSource();
        _appUpdateCheck = check;
        _appUpdatesLink.Enabled = false;
        var offer = false;
        if (manual) _appUpdatesLink.Text = "Checking app updates...";
        try
        {
            var result = await (_checkAppUpdate?.Invoke(check.Token)
                ?? _applicationUpdates.CheckAsync(ProductIdentity.Version, check.Token));
            if (check.IsCancellationRequested || IsDisposed || _operation is not null) return;
            _availableAppUpdate = result.State == AppUpdateCheckState.Available ? result.Release : null;
            _appUpdatesLink.Text = _availableAppUpdate is not null ?
                $"{(_availableAppUpdate.Importance == "critical" ? "Critical update" : "New version")}: {_availableAppUpdate.Version}"
                : manual ? result.State switch
                {
                    AppUpdateCheckState.UpToDate => "App is up to date",
                    AppUpdateCheckState.NoStableRelease => "No stable app release yet",
                    AppUpdateCheckState.NoBetaRelease => "No Beta app release yet",
                    _ => "App updates unavailable — retry"
                } : "Check app updates";
            _appUpdatesLink.LinkColor = _availableAppUpdate is null ? AccentLight :
                _availableAppUpdate.Importance == "critical" ? Warning : Success;
            offer = _availableAppUpdate is not null && _offerAppUpdates;
            _appUpdatesLink.AccessibleDescription = result.Message;
            AppLog.Info($"Application update check: {result.State}. {result.Message}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Error($"Application update check failed: {ex}");
            if (!IsDisposed && manual) _appUpdatesLink.Text = "App updates unavailable — retry";
        }
        finally
        {
            if (ReferenceEquals(_appUpdateCheck, check)) _appUpdateCheck = null;
            if (!IsDisposed) _appUpdatesLink.Enabled = _operation is null && !_setupHasStarted;
        }
        if (offer && !IsDisposed && _operation is null && !_setupHasStarted) await InstallAppUpdateAsync();
    }

    private async Task InstallAppUpdateAsync()
    {
        if (_operation is not null || _setupHasStarted || _availableAppUpdate is not { } release) return;
        AppUpdateClient.ValidateManifest(release);
        var message = $"{(release.Importance == "critical" ? "Critical update" : "Regular update")} — {release.Version}\n\n" +
            release.Message + $"\n\nSave and run:\n{AppUpdateInstaller.DestinationFor(release)}\n\n" +
            "The current tool will close only after verification. Your original portable copy stays unchanged. " +
            "No Windows upgrade or computer restart will start.";
        var accepted = _confirmAppUpdate?.Invoke(message) ?? ShowAppUpdateOffer(release, message);
        if (!accepted) return;
        _operation = new CancellationTokenSource();
        var operation = _operation;
        var oldStatus = _status.Text;
        SetBusy(true);
        _progress.Visible = true;
        _progress.Style = ProgressBarStyle.Continuous;
        var handedOff = false;
        try
        {
            var progress = new Progress<AppUpdateProgress>(item =>
            {
                if (!ReferenceEquals(_operation, operation) || operation.IsCancellationRequested) return;
                var percent = item.TotalBytes == 0 ? 0 : (int)(item.BytesReceived * 100 / item.TotalBytes);
                _progress.Value = Math.Clamp(percent, 0, 100);
                _status.Text = $"Downloading app update... {_progress.Value}%";
            });
            _status.Text = "Downloading and verifying the app update...";
            var result = await (_installAppUpdate?.Invoke(release, progress, operation.Token)
                ?? AppUpdateInstaller.InstallAsync(_applicationUpdates, release, true, progress, operation.Token));
            AppLog.Info(result.Message);
            handedOff = true;
            _closingForAppUpdate = true;
            operation.Dispose();
            _operation = null;
            Close();
        }
        catch (OperationCanceledException)
        { _appUpdatesLink.Text = "App update cancelled — retry"; }
        catch (Exception ex)
        {
            AppLog.Error($"Application update installation failed: {ex}");
            _appUpdatesLink.Text = "App update failed — retry";
            _appUpdatesLink.AccessibleDescription = ex.Message;
            var explanation = "The app update could not be started. Your current copy was not replaced.\n\n" + ex.Message;
            if (_showAppUpdateFailure is not null) _showAppUpdateFailure(explanation);
            else MessageBox.Show(this, explanation, "Application update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!handedOff)
            {
                operation.Dispose();
                if (ReferenceEquals(_operation, operation)) _operation = null;
                _progress.Visible = false;
                SetBusy(false);
                _status.Text = oldStatus; // App discovery/errors never replace Windows-media diagnostics.
            }
        }
    }

    private bool ShowAppUpdateOffer(AppUpdateManifest release, string message)
    {
        using var dialog = CreateAppUpdateOffer(release, message, _launchBrowser);
        return dialog.ShowDialog(this) == DialogResult.Yes;
    }

    internal static Form CreateAppUpdateOffer(AppUpdateManifest release, string message, Action<string> openPage)
    {
        AppUpdateClient.ValidateManifest(release);
        var critical = release.Importance == "critical";
        var dialog = new Form { Text = "New version available", ClientSize = new Size(660, 370),
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, BackColor = Background, ForeColor = Foreground,
            Font = new Font("Segoe UI", 10), AutoScaleMode = AutoScaleMode.Dpi };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22),
            ColumnCount = 1, RowCount = 4, BackColor = Background };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = critical ? "CRITICAL UPDATE — updating is recommended" : "REGULAR UPDATE",
            AutoSize = true, ForeColor = critical ? Color.FromArgb(255, 139, 128) : Success,
            Font = new Font("Segoe UI", 12, FontStyle.Bold), Margin = new Padding(0, 0, 0, 12) });
        var body = new TextBox { Text = message, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = Background, ForeColor = Foreground, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
            TabStop = false };
        layout.Controls.Add(body);
        var notes = new LinkLabel { Text = "View release notes on GitHub", AutoSize = true,
            LinkColor = AccentLight, Margin = new Padding(0, 12, 0, 16) };
        notes.LinkClicked += (_, _) => openPage(release.ReleaseNotesUrl);
        layout.Controls.Add(notes);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var later = new Button { Text = "Not now", AutoSize = true, Padding = new Padding(12, 6, 12, 6),
            DialogResult = DialogResult.No, UseVisualStyleBackColor = true, ForeColor = Color.Black };
        var download = new Button { Text = "Download and run the new version", AutoSize = true,
            Padding = new Padding(12, 6, 12, 6), DialogResult = DialogResult.Yes,
            BackColor = critical ? Color.FromArgb(159, 54, 48) : Color.FromArgb(38, 110, 73), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat };
        buttons.Controls.Add(later); buttons.Controls.Add(download); layout.Controls.Add(buttons);
        dialog.Controls.Add(layout); dialog.AcceptButton = later; dialog.CancelButton = later;
        dialog.ActiveControl = later; // Enter must not turn a newly appearing alert into consent.
        return dialog;
    }
}
