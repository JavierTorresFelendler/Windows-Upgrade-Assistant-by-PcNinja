using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using PcNinja.Win11Upgrade.Core;

namespace PcNinja.Win11UpgradeAssistant;

internal sealed partial class MainForm : Form
{
    // Nocturne design tokens adapted from the PcNinja dashboard handoff.
    private static readonly Color Background = Color.FromArgb(22, 24, 38);
    private static readonly Color Surface = Color.FromArgb(35, 37, 50);
    private static readonly Color SurfaceRaised = Color.FromArgb(41, 43, 55);
    private static readonly Color Divider = Color.FromArgb(63, 66, 77);
    private static readonly Color Accent = Color.FromArgb(145, 132, 217);
    private static readonly Color AccentLight = Color.FromArgb(210, 206, 253);
    private static readonly Color AccentTint = Color.FromArgb(43, 39, 65);
    private static readonly Color Foreground = Color.FromArgb(233, 233, 237);
    private static readonly Color Muted = Color.FromArgb(178, 182, 202);
    private static readonly Color Success = Color.FromArgb(133, 200, 163);
    private static readonly Color Warning = Color.FromArgb(229, 183, 123);

    private readonly UpgradeEngine _engine = new();
    private readonly Label _systemValue = new();
    private readonly Label _processorValue = new();
    private readonly Label _memoryValue = new();
    private readonly Label _systemStorage = new();
    private readonly Label _sourceSummary = new();
    private readonly Label _restartSummary = new();
    private readonly RadioButton _autoDownload = new();
    private Label _automaticDownloadInfo = null!;
    private Label _existingMediaInfo = null!;
    private bool _automaticDownloadAvailable;
    private readonly RadioButton _existingMedia = new();
    private readonly ComboBox _sources = new();
    private readonly ComboBox _downloadLocations = new();
    private readonly Button _browse = new();
    private readonly Button _browseInstallationFolder = new();
    private readonly Button _chooseDownloadFolder = new();
    private readonly Button _refresh = new();
    private readonly Button _pcNinjaDownload = new();
    private readonly Button _matchingDownload = new();
    private readonly Label _downloadNotice = new();
    private readonly Button _copyServerCommand = new();
    private readonly TextBox _serverCommandPreview = new();
    private DownloadSystem _downloadSystem = DownloadSystem.Read();
    private bool _upgradeAllowed = true;
    private Func<DownloadSystem> _readDownloadSystem = DownloadSystem.Read;
    private Func<CancellationToken, Task<DownloadRecommendation>> _resolveDownloadRecommendation;
    private Func<MountedSource, int, CancellationToken, Task<string>> _prepareServerCommand;
    private Action<string> _launchBrowser = url => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private Action<string> _copyCommandToClipboard = Clipboard.SetText;
    private readonly Button _microsoft = new();
    private readonly Button _start = new();
    private readonly CheckBox _autoRestart = new();
    private readonly CheckBox _quietSetup = new();
    private bool _restartBeforeQuiet = true;
    private Label _restartHelp = null!;
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly Label _operationNotice = new();
    private readonly LinkLabel _details = new();
    private Func<string, string, bool> _confirmUpgrade;
    private Action<string> _showRestartNotice;
    private string? _lastErrorDetails;
    private readonly TableLayoutPanel _autoSourcePanel = new();
    private readonly TableLayoutPanel _existingSourcePanel = new();
    private CancellationTokenSource? _operation;
    private bool _setupHasStarted;
    private bool _changingSourceSelection;
    private bool _refreshingSources;
    private readonly ComboBox _installationImages = new();
    private readonly Panel _imageChoiceFrame = new();
    private string _installationImagePrompt = "Validate a source first";
    private readonly Button _inspectSource = new();
    private readonly Label _imageInfo = new();
    private MountedSource? _preparedSource;
    private string? _preparedKey;

    public MainForm()
    {
        Text = ProductIdentity.Name;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1020, 700);
        Size = new Size(1160, 800);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5F);
        BackColor = Background;
        ForeColor = Foreground;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        _confirmUpgrade = (message, title) => MessageBox.Show(this, message, title,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        _showRestartNotice = message => MessageBox.Show(this, message, "Restart required",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        _resolveDownloadRecommendation = token => _engine.GetDownloadRecommendationAsync(token);
        _prepareServerCommand = (source, index, token) => _engine.GetManualServerCommandAsync(source, index, token);
        BuildUi();
        Shown += async (_, _) => { RefreshSources(true); if (_automaticAppUpdateCheck) await CheckAppUpdatesAsync(false); };
        FormClosed += (_, _) => { _appUpdateCheck?.Cancel(); _applicationUpdates.Dispose(); };
        FormClosing += (_, e) =>
        {
            if (_closingForAppUpdate) return;
            if (_operation is null) return;
            e.Cancel = true;
            _status.Text = _setupHasStarted ? "Windows Setup is running. Please wait."
                : "Use CANCEL to stop the current operation first.";
        };
    }

    private void BuildUi()
    {
        SuspendLayout();

        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 18, 24, 14),
            ColumnCount = 1,
            RowCount = 4,
            BackColor = Background
        };
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        page.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(page);

        page.Controls.Add(BuildHeader(), 0, 0);

        var dashboard = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Background,
            Margin = new Padding(0, 16, 0, 12)
        };
        dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        dashboard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        dashboard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var sourceCard = BuildSourceCard();
        sourceCard.Margin = new Padding(0, 0, 8, 0);
        dashboard.Controls.Add(sourceCard, 0, 0);

        var readinessCard = BuildReadinessCard();
        readinessCard.Margin = new Padding(8, 0, 0, 0);
        dashboard.Controls.Add(readinessCard, 1, 0);
        page.Controls.Add(dashboard, 0, 1);

        _progress.Dock = DockStyle.Top;
        _progress.Height = 6;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Visible = false;
        _progress.Margin = new Padding(0, 0, 0, 8);
        page.Controls.Add(_progress, 0, 2);
        page.Controls.Add(BuildStatusBar(), 0, 3);

        _autoDownload.CheckedChanged += (_, _) => SourceSelectionChanged(_autoDownload);
        _existingMedia.CheckedChanged += (_, _) => SourceSelectionChanged(_existingMedia);
        _autoRestart.CheckedChanged += (_, _) => UpdateReadinessSummary();
        _quietSetup.CheckedChanged += (_, _) => QuietModeChanged();
        _sources.SelectedIndexChanged += (_, _) => ExistingPathChanged();
        _sources.TextChanged += (_, _) => ExistingPathChanged();
        _installationImages.SelectedIndexChanged += (_, _) => { _serverCommandPreview.Clear(); UpdateImageInfo(); };
        _downloadLocations.SelectedIndexChanged += (_, _) => UpdateReadinessSummary();

        ApplyDownloadSystem(_readDownloadSystem());
        UpdateSourceControls();
        UpdateReadinessSummary();
        UpdateImageInfo();
        ResumeLayout(true);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Background,
            Margin = new Padding(0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var brand = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Background,
            Margin = new Padding(0)
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var logo = CreateMascot(62);
        logo.Margin = new Padding(0, 0, 14, 0);
        brand.Controls.Add(logo, 0, 0);

        var text = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Background,
            Padding = new Padding(0, 4, 0, 0),
            Margin = new Padding(0)
        };
        var website = new LinkLabel
        {
            Text = "www.pcninja.pro", AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F), LinkColor = AccentLight,
            ActiveLinkColor = Accent, VisitedLinkColor = AccentLight,
            Margin = new Padding(0, 0, 0, 4), BackColor = Color.Transparent
        };
        website.LinkClicked += (_, _) => OpenPage("https://www.pcninja.pro");
        text.Controls.Add(website);
        text.Controls.Add(new Label
        {
            AutoSize = true,
            Text = ProductIdentity.Name,
            Font = new Font("Segoe UI Semibold", 22F),
            ForeColor = Foreground,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 1, 0, 0)
        });
        text.Controls.Add(new Label
        {
            AutoSize = true,
            Text = "Upgrade Windows 10 or Windows 11 while keeping your apps and files.",
            ForeColor = Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(1, 4, 0, 0)
        });
        brand.Controls.Add(text, 1, 0);
        header.Controls.Add(brand, 0, 0);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Background,
            Margin = new Padding(14, 14, 0, 0)
        };
        StyleSecondaryButton(_refresh, "Refresh");
        _refresh.Margin = new Padding(0, 0, 8, 0);
        _refresh.Click += (_, _) => RefreshSources(false);
        actions.Controls.Add(_refresh);
        StyleGoButton(_start, "START UPGRADE");
        _start.Click += async (_, _) => await StartOrCancelAsync();
        actions.Controls.Add(_start);
        header.Controls.Add(actions, 1, 0);
        return header;
    }

    private Control BuildSourceCard()
    {
        var card = CreateCard();
        var scrollHost = CreateScrollHost();

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            BackColor = Surface,
            Margin = new Padding(0)
        };
        scrollHost.Controls.Add(content);

        content.Controls.Add(SectionHeading("Choose your installation source"));
        content.Controls.Add(BodyText("Download automatically, or select an ISO, drive or folder."));

        _autoDownload.Text = "Download Windows 11 automatically";
        StyleRadio(_autoDownload, true);
        var destinationRow = BuildTwoColumnRow();
        _downloadLocations.DropDownStyle = ComboBoxStyle.DropDownList;
        StyleCombo(_downloadLocations);
        _downloadLocations.DropDown += (_, _) => _autoDownload.Checked = true;
        destinationRow.Controls.Add(_downloadLocations, 0, 0);
        StyleSecondaryButton(_chooseDownloadFolder, "Choose folder...");
        _chooseDownloadFolder.Click += (_, _) => ChooseDownloadFolder();
        destinationRow.Controls.Add(_chooseDownloadFolder, 1, 0);
        _automaticDownloadInfo = ConfigureSourcePanel(_autoSourcePanel, _autoDownload,
            "English (en-US). The ISO is checked before the upgrade starts.", destinationRow);
        content.Controls.Add(_autoSourcePanel);

        _existingMedia.Text = "Use an ISO, drive or folder";
        StyleRadio(_existingMedia);
        var existingRow = BuildTwoColumnRow();
        _sources.DropDownStyle = ComboBoxStyle.DropDown;
        StyleCombo(_sources);
        _sources.DropDownWidth = 650;
        _sources.DropDown += (_, _) => _existingMedia.Checked = true;
        StyleSecondaryButton(_browse, "ISO...");
        _browse.Click += (_, _) => BrowseIso();
        StyleSecondaryButton(_browseInstallationFolder, "Folder...");
        _browseInstallationFolder.Click += (_, _) => BrowseInstallationFolder();
        var browseActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        browseActions.Controls.Add(_browse);
        browseActions.Controls.Add(_browseInstallationFolder);
        existingRow.Controls.Add(_sources, 0, 0);
        existingRow.Controls.Add(browseActions, 1, 0);
        _existingMediaInfo = ConfigureSourcePanel(_existingSourcePanel, _existingMedia,
            "Windows 10 or 11 media, on this computer or your network.", existingRow);
        content.Controls.Add(_existingSourcePanel);

        content.Controls.Add(DividerLine());
        content.Controls.Add(Kicker("Windows installation image"));
        var imageRow = BuildTwoColumnRow();
        _installationImages.DropDownStyle = ComboBoxStyle.DropDownList;
        _installationImages.AccessibleName = "Windows installation image";
        StyleCombo(_installationImages, () => _installationImagePrompt);
        _installationImages.DropDownWidth = 700;
        _imageChoiceFrame.Dock = DockStyle.Fill;
        _imageChoiceFrame.Padding = new Padding(2);
        _imageChoiceFrame.MinimumSize = new Size(0, 36);
        _imageChoiceFrame.Height = 36;
        _imageChoiceFrame.Margin = new Padding(0);
        _imageChoiceFrame.Controls.Add(_installationImages);
        imageRow.Controls.Add(_imageChoiceFrame, 0, 0);
        StyleSecondaryButton(_inspectSource, "Validate");
        _inspectSource.Click += async (_, _) => { if (_operation is null) await PrepareOrUpgradeAsync(false); };
        imageRow.Controls.Add(_inspectSource, 1, 0);
        content.Controls.Add(imageRow);
        _imageInfo.AutoSize = true;
        _imageInfo.MaximumSize = new Size(550, 0);
        _imageInfo.ForeColor = Muted;
        _imageInfo.Text = "A matching image is selected automatically when possible.";
        _imageInfo.Margin = new Padding(0, 7, 0, 0);
        content.Controls.Add(_imageInfo);
        StyleSecondaryButton(_copyServerCommand, "Copy upgrade command");
        _copyServerCommand.Visible = false;
        _copyServerCommand.Click += async (_, _) => await CopyServerCommandAsync();
        content.Controls.Add(_copyServerCommand);
        _serverCommandPreview.ReadOnly = true;
        _serverCommandPreview.Multiline = true;
        _serverCommandPreview.Height = 64;
        _serverCommandPreview.Dock = DockStyle.Top;
        _serverCommandPreview.BackColor = SurfaceRaised;
        _serverCommandPreview.ForeColor = Foreground;
        _serverCommandPreview.Visible = false;
        content.Controls.Add(_serverCommandPreview);

        var footer = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            BackColor = Surface,
            Margin = new Padding(0),
            Padding = new Padding(0, 10, 0, 0)
        };
        footer.Controls.Add(DividerLine());
        footer.Controls.Add(Kicker("Manual ISO downloads"));
        footer.Controls.Add(BodyText("Use Microsoft for standard Windows, or PcNinja for PcNinja media. Choose the same language as your installed Windows."));
        var downloadActions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Surface,
            Margin = new Padding(0, 7, 0, 0)
        };
        StyleSecondaryButton(_pcNinjaDownload, "PcNinja downloads");
        _pcNinjaDownload.Margin = new Padding(0, 0, 8, 0);
        _pcNinjaDownload.Click += (_, _) => OpenPage(DownloadSourcePolicy.GetPcNinjaBrowserPage(_readDownloadSystem()));
        StyleSecondaryButton(_microsoft, "Microsoft downloads");
        _microsoft.Margin = new Padding(0);
        _microsoft.Click += (_, _) => OpenPage(DownloadSourcePolicy.GetMicrosoftBrowserPage(_readDownloadSystem()));
        downloadActions.Controls.Add(_pcNinjaDownload);
        downloadActions.Controls.Add(_microsoft);
        StyleSecondaryButton(_matchingDownload, "Find matching ISO");
        _matchingDownload.Margin = new Padding(8, 0, 0, 0);
        _matchingDownload.Click += async (_, _) => await FindMatchingDownloadAsync();
        downloadActions.Controls.Add(_matchingDownload);
        footer.Controls.Add(downloadActions);
        _downloadNotice.AutoSize = true;
        _downloadNotice.MaximumSize = new Size(550, 0);
        _downloadNotice.ForeColor = Warning;
        _downloadNotice.Margin = new Padding(0, 8, 0, 0);
        _downloadNotice.Visible = false;
        footer.Controls.Add(_downloadNotice);

        var cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Surface,
            Margin = new Padding(0)
        };
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cardLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cardLayout.Controls.Add(scrollHost, 0, 0);
        cardLayout.Controls.Add(footer, 0, 1);
        card.Controls.Add(cardLayout);

        return card;
    }

    private Control BuildReadinessCard()
    {
        var card = CreateCard();
        var scrollHost = CreateScrollHost();

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            BackColor = Surface,
            Padding = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth, 0),
            Margin = new Padding(0)
        };
        scrollHost.Controls.Add(content);

        content.Controls.Add(SectionHeading("Your computer && upgrade settings", 280));

        var computer = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            BackColor = SurfaceRaised,
            Padding = new Padding(12),
            Margin = new Padding(0, 8, 0, 12)
        };
        computer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _systemValue.AutoSize = true;
        _systemValue.MaximumSize = new Size(272, 0);
        _systemValue.Text = "Detecting Windows version...";
        _systemValue.Font = new Font("Segoe UI Semibold", 10F);
        _systemValue.ForeColor = Foreground;
        _systemValue.BackColor = Color.Transparent;
        _systemValue.Margin = new Padding(0, 0, 0, 7);
        computer.Controls.Add(_systemValue);

        ConfigureSystemDetailValue(_processorValue, "Detecting processor...");
        ConfigureSystemDetailValue(_memoryValue, "Detecting memory...");
        ConfigureSystemDetailValue(_systemStorage, "Checking storage...");
        computer.Controls.Add(SystemDetailRow("Processor", _processorValue));
        computer.Controls.Add(SystemDetailRow("Memory", _memoryValue));
        computer.Controls.Add(SystemDetailRow("Windows drive", _systemStorage));
        content.Controls.Add(computer);

        content.Controls.Add(ReadinessRow("Selected source", _sourceSummary));
        content.Controls.Add(ReadinessRow("Media validation", ValueLabel("Required", AccentLight)));
        content.Controls.Add(ReadinessRow("Restart", _restartSummary));
        content.Controls.Add(DividerLine());

        _quietSetup.Text = "Run Windows Setup silently";
        _quietSetup.AutoSize = true;
        _quietSetup.ForeColor = Foreground;
        _quietSetup.BackColor = Color.Transparent;
        _quietSetup.Margin = new Padding(0, 2, 0, 2);
        content.Controls.Add(_quietSetup);
        content.Controls.Add(BodyText(
            "Hide Windows Setup windows and allow automatic restart. Errors still appear in this assistant.",
            22, 280));

        _autoRestart.Text = "Restart automatically when needed";
        _autoRestart.Checked = true;
        _autoRestart.AutoSize = true;
        _autoRestart.ForeColor = Foreground;
        _autoRestart.BackColor = Color.Transparent;
        _autoRestart.Margin = new Padding(0, 2, 0, 2);
        content.Controls.Add(_autoRestart);
        _restartHelp = BodyText(
            "Turn off to restart manually after upgrade preparation.",
            22, 280);
        content.Controls.Add(_restartHelp);

        var warning = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            MaximumSize = new Size(340, 0),
            Text = "Experimental compatibility method.\nBack up important files before upgrading.",
            ForeColor = Warning,
            BackColor = Color.FromArgb(53, 45, 43),
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 9, 0, 8)
        };
        content.Controls.Add(warning);
        _operationNotice.AutoSize = true;
        _operationNotice.MaximumSize = new Size(340, 0);
        _operationNotice.Dock = DockStyle.Top;
        _operationNotice.ForeColor = Warning;
        _operationNotice.BackColor = Color.FromArgb(53, 45, 43);
        _operationNotice.Padding = new Padding(12);
        _operationNotice.Margin = new Padding(0, 4, 0, 8);
        _operationNotice.Visible = false;
        content.Controls.Add(_operationNotice);

        card.Controls.Add(scrollHost);

        return card;
    }

    private Control BuildStatusBar()
    {
        var bar = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Background,
            Margin = new Padding(0)
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _status.AutoEllipsis = true;
        _status.Height = 24;
        _status.Dock = DockStyle.Fill;
        _status.Text = "Ready.";
        _status.ForeColor = Muted;
        _status.BackColor = Color.Transparent;
        _status.Margin = new Padding(1, 3, 12, 0);
        bar.Controls.Add(_status, 0, 0);

        var footer = new LinkLabel
        {
            AutoSize = true,
            Text = "View log",
            LinkColor = AccentLight,
            ActiveLinkColor = Accent,
            VisitedLinkColor = AccentLight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 3, 0, 0)
        };
        footer.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(
            "explorer.exe", $"/select,\"{AppLog.Path}\"")
        { UseShellExecute = true });
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        _details.Text = "View details";
        _details.AutoSize = true;
        _details.Visible = false;
        _details.LinkColor = AccentLight;
        _details.Margin = new Padding(0, 3, 14, 0);
        _details.LinkClicked += (_, _) => MessageBox.Show(this, _lastErrorDetails ?? "No details available.",
            "Details", MessageBoxButtons.OK, MessageBoxIcon.Information);
        links.Controls.Add(_details);
        _appUpdatesLink.AutoSize = true;
        _appUpdatesLink.Text = "Check app updates";
        _appUpdatesLink.LinkColor = AccentLight;
        _appUpdatesLink.ActiveLinkColor = Accent;
        _appUpdatesLink.Margin = new Padding(0, 3, 14, 0);
        _appUpdatesLink.LinkClicked += async (_, _) =>
        {
            if (_operation is not null) return;
            if (_availableAppUpdate is null) await CheckAppUpdatesAsync(true);
            else await InstallAppUpdateAsync();
        };
        links.Controls.Add(_appUpdatesLink);
        links.Controls.Add(footer);
        bar.Controls.Add(links, 1, 0);
        return bar;
    }

    private void RefreshSources(bool initial)
    {
        var selected = _sources.Text.Trim();
        var location = _downloadLocations.SelectedItem as StorageLocation;
        try
        {
            ApplyDownloadSystem(_readDownloadSystem());
            AppLog.Info($"Host eligibility: {_downloadSystem.Edition}, {_downloadSystem.Language}, {_downloadSystem.Architecture}, " +
                $"{_downloadSystem.InstallationType}, build {_downloadSystem.Build}.{_downloadSystem.Revision}, LTSC: {_downloadSystem.IsLtsc}; " +
                $"manual upgrade: {_upgradeAllowed}; automatic download: {_automaticDownloadAvailable}; " +
                $"blocker: {ManualUpgradePolicy.GetUnavailableReason(_downloadSystem) ?? "none"}.");
            _systemValue.Text = _engine.GetOperatingSystemDescription();
            _processorValue.Text = GetProcessorDescription();
            _memoryValue.Text = GetMemoryDescription();
            var systemStorage = _engine.CheckSystemDriveSpace();
            _systemStorage.Text = systemStorage.Message;
            _systemStorage.ForeColor = systemStorage.IsValid ? Success : Warning;

            var local = _engine.FindLocalIsos();
            var mounted = _engine.FindMountedSources();
            var candidates = local.Concat(mounted.Select(source => source.Root)).ToList();
            if (!string.IsNullOrWhiteSpace(selected) && !candidates.Contains(selected, StringComparer.OrdinalIgnoreCase))
                candidates.Insert(0, selected); // Retain exact user input even if a drive was temporarily removed.
            _sources.BeginUpdate();
            _refreshingSources = true;
            _sources.Items.Clear();
            foreach (var path in candidates) _sources.Items.Add(path);
            _sources.Text = selected;
            if (initial && local.Count == 1)
            {
                _sources.Text = local[0];
                _existingMedia.Checked = true;
            }
            _sources.EndUpdate();
            _refreshingSources = false;

            var locations = _engine.FindDownloadLocations().ToList();
            if (location?.VolumeLabel == "Selected folder" && Directory.Exists(location.RootPath))
            {
                var drive = new DriveInfo(Path.GetPathRoot(location.RootPath)!);
                if (drive.IsReady && UpgradeEngine.IsSupportedDownloadFileSystem(drive.DriveFormat))
                    locations.Insert(0, location with { AvailableBytes = drive.AvailableFreeSpace });
            }
            _downloadLocations.Items.Clear();
            foreach (var item in locations) _downloadLocations.Items.Add(item);
            var match = locations.FindIndex(item => item.RootPath.Equals(location?.RootPath, StringComparison.OrdinalIgnoreCase));
            if (locations.Count > 0) _downloadLocations.SelectedIndex = match >= 0 ? match : 0;
            _status.Text = systemStorage.IsValid
                ? $"Refresh complete: {local.Count} ISO(s), {mounted.Count} mounted source(s), {locations.Count} download drive(s)."
                : systemStorage.Message;
            if (initial && UpgradeHistory.ReadStatus() is { } previous)
                PresentPreviousUpgrade(previous);
            _start.Enabled = _upgradeAllowed;
            if (_downloadSystem.IsServer)
                ShowOperationNotice("Windows Server: download and manual command guidance only. This assistant will not run Setup. " +
                    ManualServerUpgrade.Advisory);
            else if (ManualUpgradePolicy.GetUnavailableReason(_downloadSystem) is { } blocked)
                ShowOperationNotice(blocked);
        }
        catch (Exception ex) { ShowIssue(ex); _start.Enabled = false; }
        ExistingPathChanged();
        UpdateSourceControls();
        UpdateReadinessSummary();
    }

    private void PresentPreviousUpgrade(UpgradeAttempt previous)
    {
        _status.Text = $"Previous upgrade: {previous.State}. {previous.Message}";
        if (previous.SetupFailure is not { } failure) return;
        ShowOperationNotice(previous.Message);
        _lastErrorDetails = $"{failure.Summary}\nResult: {failure.ResultCode} - {failure.ExtendedCode}" +
            $"\nPhase: {failure.Phase}\nWindows Setup log: {failure.LogPath}";
        _details.Visible = true;
    }

    private void BrowseIso()
    {
        using var dialog = CreateIsoDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!_sources.Items.Contains(dialog.FileName)) _sources.Items.Insert(0, dialog.FileName);
        _sources.Text = dialog.FileName;
        _existingMedia.Checked = true;
    }

    private OpenFileDialog CreateIsoDialog() => new()
        {
            Title = _downloadSystem.IsServer ? "Select a Windows Server x64 ISO for manual guidance"
                : "Select a Windows 10 or Windows 11 x64 ISO",
            Filter = "Windows ISO images (*.iso)|*.iso",
            CheckFileExists = true,
            Multiselect = false,
            InitialDirectory = UserDownloads.GetPath(),
            RestoreDirectory = true
        };

    private void BrowseInstallationFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select a mounted drive or extracted Windows Setup folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _sources.Text = dialog.SelectedPath;
        _existingMedia.Checked = true;
    }

    private void ChooseDownloadFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder for the Windows ISO download",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var check = _engine.CheckDownloadDestination(dialog.SelectedPath, 0);
            if (!check.IsValid) throw new InvalidOperationException(check.Message);
            var root = Path.GetPathRoot(dialog.SelectedPath)!;
            var drive = new DriveInfo(root);
            var custom = new StorageLocation(dialog.SelectedPath, "Selected folder", drive.DriveType,
                drive.DriveFormat, drive.AvailableFreeSpace);
            _downloadLocations.Items.Insert(0, custom);
            _downloadLocations.SelectedIndex = 0;
            _autoDownload.Checked = true;
        }
        catch (Exception ex)
        {
            ShowIssue(ex);
        }
    }

    private async Task StartOrCancelAsync()
    {
        AppLog.Info($"Start/Cancel button clicked. Operation active: {_operation is not null}. Setup started: {_setupHasStarted}.");
        if (_operation is not null)
        {
            if (_setupHasStarted) return;
            _status.Text = "Cancelling the current operation...";
            _start.Text = "CANCELLING...";
            _start.Enabled = false;
            _operation.Cancel();
            return;
        }

        await PrepareOrUpgradeAsync(true);
    }

    private async Task PrepareOrUpgradeAsync(bool startSetup)
    {
        _lastErrorDetails = null;
        _details.Visible = false;
        _operationNotice.Visible = false;
        _operation = new CancellationTokenSource();
        _setupHasStarted = false;
        SetBusy(true);
        try
        {
            if (startSetup && !_upgradeAllowed)
                ManualUpgradePolicy.EnsureAvailable(_downloadSystem);
            if (_autoDownload.Checked) AutomaticDownloadPolicy.EnsureAvailable(_readDownloadSystem());
            if (startSetup)
            {
                var storage = _engine.CheckSystemDriveSpace();
                if (!storage.IsValid) throw new InvalidOperationException(storage.Message);
            }
            var previousSource = _preparedSource;
            var previousKey = _preparedKey;
            var previousIndex = (_installationImages.SelectedItem as InstallationImage)?.Index;
            MountedSource source;
            MountedSource? automaticSource = null;
            var consentedRestart = _autoRestart.Checked;
            var consentedQuiet = _quietSetup.Checked;
            if (_autoDownload.Checked)
            {
                if (!startSetup) throw new InvalidOperationException("Choose existing media to inspect its images.");
                if (_downloadLocations.SelectedItem is not StorageLocation location)
                    throw new InvalidOperationException("Choose a download destination. Connect a drive and Refresh if necessary.");
                var destination = location.VolumeLabel == "Selected folder" ? location.RootPath
                    : Path.Combine(location.RootPath, "PcNinja Downloads");
                if (!ConfirmAutomaticUpgrade(destination, consentedRestart, consentedQuiet))
                {
                    _status.Text = "Upgrade cancelled. No download or Windows Setup was started.";
                    return;
                }
                _status.Text = "Checking or downloading the Windows ISO...";
                _progress.Visible = true;
                var operation = _operation;
                var progress = new Progress<DownloadProgress>(value =>
                {
                    if (!ReferenceEquals(_operation, operation) || operation.IsCancellationRequested) return;
                    _progress.Style = value.Percentage.HasValue ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                    if (value.Percentage.HasValue) _progress.Value = Math.Clamp(value.Percentage.Value, 0, 100);
                    _status.Text = value.Stage ?? $"Downloading {Path.GetFileName(value.DestinationPath)}... " +
                        (value.Percentage.HasValue ? $"{value.Percentage}%" : $"{UpgradeEngine.FormatBytes(value.BytesReceived)} received");
                });
                try
                {
                    var download = await _engine.DownloadIsoAsync(destination, progress, operation.Token);
                    automaticSource = download.ValidatedSource
                        ?? throw new InvalidOperationException("The downloaded ISO did not complete media validation.");
                    _sources.Text = download.IsoPath;
                    _existingMedia.Checked = true;
                }
                catch (DownloadTransferException ex)
                {
                    AppLog.Error($"Automatic download failed: {ex}");
                    var opened = false;
                    try { _launchBrowser(DownloadSourcePolicy.GetPcNinjaBrowserPage(_readDownloadSystem())); opened = true; }
                    catch (Exception browserError) { AppLog.Error($"Browser fallback failed: {browserError}"); }
                    _existingMedia.Checked = true;
                    _sources.SelectedIndex = -1;
                    _sources.Text = "";
                    ShowManualDownloadNotice("PcNinja browser download", null);
                    throw new InvalidOperationException(opened
                        ? "Browser download opened. When it finishes, use ISO... to select the downloaded file, then Start."
                        : "Automatic download unavailable. Download manually, then use ISO... to select the file.", ex);
                }
            }
            var path = _sources.Text.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("No existing installation media selected. Choose an ISO, drive or folder.");
            _progress.Visible = true;
            _progress.Style = ProgressBarStyle.Marquee;
            _status.Text = "Validating the selected installation media and images...";
            var preparationProgress = CreatePreparationProgress(_operation);
            // An unsuccessful/cancelled recheck must not leave a stale Ready cue.
            _preparedSource = null;
            _preparedKey = null;
            _installationImages.Items.Clear();
            _serverCommandPreview.Clear();
            UpdateImageInfo();
            if (automaticSource is not null) source = automaticSource;
            else if (path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            {
                source = (await _engine.PrepareIsoAsync(path, UpgradeEngine.ConfiguredIsoSha256,
                    _operation.Token, preparationProgress)).Source;
            }
            else source = await _engine.ValidateMountedSourceAsync(path, _operation.Token, progress: preparationProgress);

            var sameSource = previousSource is not null && previousKey == path &&
                source.ImagePath == previousSource.ImagePath &&
                (source.Images ?? []).SequenceEqual(previousSource.Images ?? []);
            _preparedSource = source;
            _preparedKey = path;
            _installationImages.Items.Clear();
            foreach (var image in source.Images ?? []) _installationImages.Items.Add(image);
            var current = new CurrentWindows(_downloadSystem.Edition, _downloadSystem.Language, _downloadSystem.Build);
            IReadOnlyList<InstallationImage> compatible = _downloadSystem.IsServer
                ? (source.Images ?? []).Where(item => ManualServerUpgrade.GetIncompatibility(item, _downloadSystem) is null).ToArray()
                : InstallationImages.Compatible(source, current);
            var index = _downloadSystem.IsServer ? (sameSource ? previousIndex : null)
                : sameSource && automaticSource is null ? previousIndex
                : compatible.Count == 1 ? compatible[0].Index : (int?)null;
            _installationImages.SelectedItem = (source.Images ?? []).FirstOrDefault(image => image.Index == index);
            UpdateImageInfo();
            _progress.Visible = false;
            if (_downloadSystem.IsServer)
            {
                PresentServerMediaInspection();
                return;
            }
            if (!startSetup)
            {
                PresentImageInspection();
                return;
            }
            if (_installationImages.SelectedItem is not InstallationImage selection)
            {
                _status.Text = compatible.Count == 0
                    ? $"No matching image for {current.Edition}, {current.Language}. Choose matching Windows media."
                    : "Choose an installation image above, then click START UPGRADE again.";
                ShowOperationNotice(compatible.Count == 0 ? _imageInfo.Text : _status.Text);
                return;
            }
            selection = InstallationImages.Select(source, selection.Index, current);
            if (!ConfirmPreparedUpgrade(source, selection, compatible.Count, automaticSource, consentedRestart, consentedQuiet))
            {
                _status.Text = "Upgrade cancelled. Windows Setup was not started.";
                return;
            }
            _operation.Token.ThrowIfCancellationRequested();
            _status.Text = "Rechecking safety and starting Windows Setup...";
            var automaticRestart = _autoRestart.Checked;
            var result = await _engine.StartUpgradeAsync(source, selection.Index, automaticRestart, _operation.Token,
                () => { _setupHasStarted = true; SetBusy(true); }, quiet: _quietSetup.Checked);
            PresentSetupResult(result, automaticRestart);
        }
        catch (OperationCanceledException) { _engine.InvalidateIsoValidation(); _status.Text = "Operation cancelled."; }
        catch (Exception ex) { _engine.InvalidateIsoValidation(); ShowIssue(ex); }
        finally
        {
            _operation?.Dispose();
            _operation = null;
            _setupHasStarted = false;
            _progress.Visible = false;
            _progress.Style = ProgressBarStyle.Continuous;
            SetBusy(false);
            var outcome = _status.Text;
            RefreshSources(false);
            _status.Text = outcome;
        }
    }

    private IProgress<string> CreatePreparationProgress(CancellationTokenSource operation) =>
        new Progress<string>(stage =>
        {
            if (!ReferenceEquals(_operation, operation) || operation.IsCancellationRequested) return;
            _status.Text = stage;
        });

    private void PresentSetupResult(UpgradeResult result, bool automaticRestart)
    {
        if (result.NeedsManualRestartNotice(automaticRestart))
        {
            _status.Text = "Waiting for manual restart. Restart your computer to continue the upgrade.";
            ShowOperationNotice(UpgradeResult.ManualRestartMessage);
            _operationNotice.ForeColor = Success;
            _showRestartNotice(UpgradeResult.ManualRestartMessage);
        }
        else if (result.ExitCode == 0)
            _status.Text = "Setup finished this stage. Check the upgrade result after restarting.";
        else if (result.ExitCode == 0x40010004)
            _status.Text = "Setup exited during the transition. Outcome not confirmed; reopen the assistant after restarting.";
        else throw new InvalidOperationException(
            $"Setup returned 0x{unchecked((uint)result.ExitCode):X8}. See View details/log. No forced restart was issued.");
    }

    private bool ConfirmAutomaticUpgrade(string destination, bool restart, bool quiet) => _confirmUpgrade(
        $"Download or reuse the Windows ISO in:\n{destination}\n\n" +
        "Then validate it and start the compatible in-place upgrade automatically, keeping apps and personal files.\n" +
        (restart ? "Windows may restart automatically." : "Restart manually when Setup is ready.") +
        (quiet ? "\nWindows Setup will run silently." : "") +
        "\n\nExperimental compatibility method. Back up important files and save your work. Continue?",
        "Download and upgrade");

    private bool ConfirmPreparedUpgrade(MountedSource source, InstallationImage selection, int compatibleCount,
        MountedSource? automaticSource, bool consentedRestart, bool consentedQuiet)
    {
        // Consent applies only to the result returned during this automatic attempt.
        // A later manual Start, ambiguous image or changed restart policy must confirm again.
        if (ReferenceEquals(source, automaticSource) && compatibleCount == 1 &&
            InstallationImages.GetSetupProfile(selection) == WindowsSetupProfile.Windows11 &&
            (source.Images ?? []).Contains(selection) && _autoRestart.Checked == consentedRestart &&
            _quietSetup.Checked == consentedQuiet) return true;
        var restart = _autoRestart.Checked ? "Windows may restart automatically." : "Restart manually when Setup is ready.";
        var targetNotice = InstallationImages.GetSetupProfile(selection) == WindowsSetupProfile.Windows10
            ? $"\n\n{InstallationImages.Windows10Notice}" : "";
        var quietNotice = _quietSetup.Checked ? "\nWindows Setup will run silently." : "";
        return _confirmUpgrade($"{selection.Name} (image {selection.Index})\n{restart}{quietNotice}{targetNotice}\n\nSave your work. Start the upgrade?",
            "Start upgrade");
    }

    private void ShowOperationNotice(string message)
    {
        _operationNotice.Text = message;
        _operationNotice.ForeColor = Warning;
        _operationNotice.Visible = true;
        if (_operationNotice.Parent?.Parent is ScrollableControl scrollHost)
            scrollHost.ScrollControlIntoView(_operationNotice);
    }

    private void ExistingPathChanged()
    {
        if (_refreshingSources) return;
        if (_sources.Focused && _operation is null) _existingMedia.Checked = true;
        if (_preparedKey is not null && !string.Equals(_sources.Text.Trim().Trim('"'), _preparedKey, StringComparison.OrdinalIgnoreCase))
        {
            _preparedSource = null;
            _preparedKey = null;
            _serverCommandPreview.Clear();
            _installationImages.Items.Clear();
            UpdateImageInfo();
            _imageInfo.Text = _downloadSystem.IsServer ? "Source changed. Click Validate to check this Server media."
                : ManualUpgradePolicy.GetUnavailableReason(_downloadSystem) is { } blocked
                ? "Source changed. " + blocked
                : "Source changed. Click Validate or START UPGRADE to check this media.";
        }
        if (_operation is null && !string.IsNullOrWhiteSpace(_sources.Text)) _downloadNotice.Visible = false;
        UpdateSourceControls();
        UpdateReadinessSummary();
    }

    private void UpdateImageInfo()
    {
        if (_downloadSystem.IsServer) { UpdateServerImageInfo(); return; }
        var current = new CurrentWindows(_downloadSystem.Edition, _downloadSystem.Language, _downloadSystem.Build);
        var noCompatibleImage = _preparedSource is not null && InstallationImages.Compatible(_preparedSource, current).Count == 0;
        _installationImagePrompt = _preparedSource is null ? "Validate a source first"
            : noCompatibleImage ? "No compatible Windows image" : "Choose a Windows image…";
        _installationImages.Invalidate();
        var incompatibility = _installationImages.SelectedItem is InstallationImage selected
            ? InstallationImages.GetIncompatibility(selected, current) : null;
        var hostBlock = ManualUpgradePolicy.GetUnavailableReason(_downloadSystem);
        _imageInfo.ForeColor = noCompatibleImage || incompatibility is not null ||
            _installationImages.SelectedItem is InstallationImage selectedImage &&
            InstallationImages.GetSetupProfile(selectedImage) == WindowsSetupProfile.Windows10 ? Warning : Muted;
        _imageInfo.Text = _installationImages.SelectedItem is InstallationImage image
            ? $"This PC: {current.Edition}, {current.Language}. " +
                (incompatibility ?? $"Selected image {image.Index}: {image.Language}, {image.Architecture}, image version {image.Version}." +
                    (InstallationImages.GetSetupProfile(image) == WindowsSetupProfile.Windows10
                        ? $" {InstallationImages.Windows10Notice}" : ""))
            : $"This PC: {current.Edition}, {current.Language}. " +
                (_preparedSource is null ? "Validate a source to read its Windows images."
                : noCompatibleImage ? "No compatible Windows image. Choose matching Windows media."
                : hostBlock is null ? "Choose a Windows image from the list, then click START UPGRADE."
                : "Choose matching media for inspection.");
        if (noCompatibleImage && _installationImages.SelectedItem is not InstallationImage)
        {
            // Reuse Core's existing reasons; readable media is not necessarily compatible.
            var reasons = (_preparedSource!.Images ?? [])
                .Select(image => InstallationImages.GetIncompatibility(image, current))
                .Where(reason => reason is not null).Distinct(StringComparer.Ordinal);
            _imageInfo.Text += Environment.NewLine + string.Join(Environment.NewLine, reasons);
        }
        if (hostBlock is not null)
        {
            _imageInfo.Text += Environment.NewLine + hostBlock;
            _imageInfo.ForeColor = Warning;
        }
        _installationImages.AccessibleDescription = _installationImages.SelectedItem is InstallationImage
            ? _imageInfo.Text : _installationImagePrompt;
        UpdateNextActionCue();
    }

    private void PresentImageInspection()
    {
        if (_preparedSource is null) return;
        if (ManualUpgradePolicy.GetUnavailableReason(_downloadSystem) is not null)
        {
            _status.Text = "Media inspected, but upgrade is blocked on this computer.";
            ShowOperationNotice(_imageInfo.Text);
            return;
        }
        var current = new CurrentWindows(_downloadSystem.Edition, _downloadSystem.Language, _downloadSystem.Build);
        var compatible = InstallationImages.Compatible(_preparedSource, current);
        var selected = _installationImages.SelectedItem as InstallationImage;
        if (compatible.Count == 0 || selected is not null && InstallationImages.GetIncompatibility(selected, current) is not null)
        {
            _status.Text = compatible.Count == 0
                ? $"No matching image for {current.Edition}, {current.Language}. Choose matching Windows media."
                : "The selected image is not compatible. Choose a matching Windows image.";
            ShowOperationNotice(_imageInfo.Text);
            return;
        }
        _status.Text = selected is not null
            ? $"Image {selected.Index} selected. Click START UPGRADE when ready."
            : $"Read {_preparedSource.Images?.Count} image(s). Choose a Windows image, then click START UPGRADE.";
    }

    private void ShowIssue(Exception error)
    {
        AppLog.Error(error.ToString());
        _lastErrorDetails = error + $"\n\nLog: {AppLog.Path}";
        _details.Visible = true;
        if (error is UnsafeInstallationMediaException)
        {
            _status.Text = "Upgrade blocked. Remove the interfering installation media, then Refresh. See View details.";
            ShowOperationNotice(_status.Text);
            MessageBox.Show(this, error.Message, "Unsafe installation media", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _status.Text = error is IOException and not DownloadTransferException and not CorruptDownloadException
            ? "Could not read or save the file. Check the drive and available space."
            : error.Message;
        ShowOperationNotice(_status.Text);
    }

    private void OpenPage(string url)
    {
        try { _launchBrowser(url); }
        catch (Exception ex) { ShowIssue(ex); }
    }

    private async Task FindMatchingDownloadAsync()
    {
        if (_operation is not null) return;
        var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        _progress.Visible = true;
        _progress.Style = ProgressBarStyle.Marquee;
        _status.Text = "Finding a matching Windows download...";
        try
        {
            var recommendation = await _resolveDownloadRecommendation(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            // Browser fallback ends the automatic attempt. It carries no source or Setup consent.
            _existingMedia.Checked = true;
            _sources.SelectedIndex = -1;
            _sources.Text = "";
            _preparedSource = null;
            _preparedKey = null;
            _installationImages.Items.Clear();
            _serverCommandPreview.Clear();
            UpdateImageInfo();
            ShowManualDownloadNotice(recommendation.Message, recommendation.FileName);
            _launchBrowser(recommendation.PageUrl);
            _status.Text = recommendation.ExactMatch
                ? "Download page opened. When finished, select the exact file with ISO... and Validate."
                : "No exact download confirmed. Choose matching media manually, then use ISO... and Validate.";
        }
        catch (OperationCanceledException) { _status.Text = "Download search cancelled. Windows Setup was not started."; }
        catch (Exception ex) { ShowIssue(ex); }
        finally
        {
            _operation = null;
            operation.Dispose();
            _progress.Visible = false;
            _progress.Style = ProgressBarStyle.Continuous;
            SetBusy(false);
        }
    }

    private void ShowManualDownloadNotice(string message, string? fileName)
    {
        _downloadNotice.Text = message + (fileName is null ? "" : $"\n{fileName}") +
            "\nComplete the download in your browser. Then click ISO... to select the file and Validate. " +
            "The assistant will not continue automatically.";
        _downloadNotice.Visible = true;
    }

    private void UpdateServerCommandState()
    {
        _copyServerCommand.Enabled = _operation is null && _downloadSystem.IsServer &&
            _preparedSource is not null && _installationImages.SelectedItem is InstallationImage image &&
            (_preparedSource.Images ?? []).Contains(image) &&
            (_preparedSource.Images ?? []).Count(item => item.Index == image.Index) == 1 &&
            ManualServerUpgrade.GetIncompatibility(image, _downloadSystem) is null;
    }

    private void UpdateServerImageInfo()
    {
        var available = (_preparedSource?.Images ?? [])
            .Any(item => ManualServerUpgrade.GetIncompatibility(item, _downloadSystem) is null);
        _installationImagePrompt = _preparedSource is null ? "Validate a source first"
            : available ? "Choose a Windows Server image…" : "No compatible Windows Server image";
        _installationImages.Invalidate();
        var selected = _installationImages.SelectedItem as InstallationImage;
        var reason = selected is null ? null : ManualServerUpgrade.GetIncompatibility(selected, _downloadSystem);
        _imageInfo.Text = $"This server: {_downloadSystem.Edition}, {_downloadSystem.Language}, {_downloadSystem.InstallationType}. " +
            (selected is not null ? reason ?? $"Selected image {selected.Index}: {selected.Name}. Manual command only; Setup will not run here."
                : _preparedSource is null ? "Select media and Validate to prepare manual command guidance."
                : available ? "Choose the exact Server image, then Copy upgrade command."
                : "No matching Server image. Check edition, language, installation type and version.");
        _imageInfo.ForeColor = reason is not null || _preparedSource is not null && !available ? Warning : Muted;
        _installationImages.AccessibleDescription = selected is null ? _installationImagePrompt : _imageInfo.Text;
        UpdateServerCommandState();
        UpdateNextActionCue();
    }

    private void PresentServerMediaInspection()
    {
        _status.Text = "Server media inspected. Select a matching image to copy a manual command. Setup will not run here.";
        ShowOperationNotice(ManualServerUpgrade.Advisory);
        UpdateServerCommandState();
    }

    private async Task CopyServerCommandAsync()
    {
        if (_operation is not null || !_downloadSystem.IsServer || _preparedSource is null ||
            _installationImages.SelectedItem is not InstallationImage image ||
            !(_preparedSource.Images ?? []).Contains(image) ||
            (_preparedSource.Images ?? []).Count(item => item.Index == image.Index) != 1 ||
            ManualServerUpgrade.GetIncompatibility(image, _downloadSystem) is not null) return;
        var operation = new CancellationTokenSource();
        _operation = operation;
        SetBusy(true);
        _status.Text = "Rechecking Server media before copying the manual command...";
        try
        {
            var command = await _prepareServerCommand(_preparedSource, image.Index, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            _copyCommandToClipboard(command);
            _serverCommandPreview.Text = command;
            _status.Text = "Manual command copied, not executed. Review it before running it yourself.";
            ShowOperationNotice(ManualServerUpgrade.Advisory);
        }
        catch (OperationCanceledException) { _status.Text = "Command preparation cancelled. Nothing was copied or started."; }
        catch (Exception ex) { ShowIssue(ex); }
        finally
        {
            _operation = null;
            operation.Dispose();
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        if (busy) _appUpdateCheck?.Cancel();
        _appUpdatesLink.Enabled = !busy;
        if (!busy) { StyleGoButton(_start, _downloadSystem.IsServer ? "MANUAL GUIDANCE ONLY" : "START UPGRADE"); _start.Enabled = _upgradeAllowed; }
        else if (_setupHasStarted) { StyleRunningButton(_start, "SETUP RUNNING"); _start.Enabled = false; }
        else { StyleCancelButton(_start, "CANCEL"); _start.Enabled = true; }
        _refresh.Enabled = !busy;
        _pcNinjaDownload.Enabled = !busy;
        _matchingDownload.Enabled = !busy;
        _microsoft.Enabled = !busy;
        _autoDownload.Enabled = !busy;
        _existingMedia.Enabled = !busy;
        _quietSetup.Enabled = !busy && _upgradeAllowed;
        _autoRestart.Enabled = !busy && _upgradeAllowed && !_quietSetup.Checked;
        UpdateSourceControls();
    }

    private void SourceSelectionChanged(RadioButton selectedSource)
    {
        if (_changingSourceSelection) return;
        _changingSourceSelection = true;
        try
        {
            if (selectedSource.Checked)
                foreach (var source in new[] { _autoDownload, _existingMedia })
                    if (!ReferenceEquals(source, selectedSource)) source.Checked = false;
        }
        finally { _changingSourceSelection = false; }
        UpdateSourceControls();
        UpdateReadinessSummary();
    }

    private void UpdateSourceControls()
    {
        var idle = _operation is null;
        _autoDownload.Enabled = idle && _automaticDownloadAvailable;
        _browse.Enabled = idle;
        _browseInstallationFolder.Enabled = idle;
        _sources.Enabled = idle;
        _downloadLocations.Enabled = idle && _automaticDownloadAvailable;
        _chooseDownloadFolder.Enabled = idle && _automaticDownloadAvailable;
        _inspectSource.Enabled = idle && _existingMedia.Checked && !string.IsNullOrWhiteSpace(_sources.Text);
        _installationImages.Enabled = idle && _preparedSource is not null;
        _matchingDownload.Enabled = idle;
        UpdateServerCommandState();
        _autoSourcePanel.BackColor = _autoDownload.Checked ? AccentTint : SurfaceRaised;
        _existingSourcePanel.BackColor = _existingMedia.Checked ? AccentTint : SurfaceRaised;
        _autoDownload.Text = _autoDownload.Checked ? "✓ SELECTED  ·  Download Windows 11 automatically" : "Download Windows 11 automatically";
        _existingMedia.Text = _existingMedia.Checked ? "✓ SELECTED  ·  Use an ISO, drive or folder" : "Use an ISO, drive or folder";
        _autoDownload.ForeColor = _autoDownload.Checked ? Success : Foreground;
        _existingMedia.ForeColor = _existingMedia.Checked ? Success : Foreground;
        UpdateNextActionCue();
    }

    private void UpdateNextActionCue()
    {
        CueButton(_browse, false);
        CueButton(_inspectSource, false);
        CueButton(_copyServerCommand, false);
        _imageChoiceFrame.BackColor = Surface;
        _start.AccessibleDescription = "";
        if (_operation is not null || _setupHasStarted) return; // Keep real red Cancel / running styles.
        _start.FlatAppearance.BorderSize = 1;
        _start.FlatAppearance.BorderColor = Success;
        if (_autoDownload.Checked)
        {
            if (_automaticDownloadAvailable && _upgradeAllowed && _start.Enabled) CueStart();
            return;
        }
        if (!_existingMedia.Checked) return;
        var path = _sources.Text.Trim().Trim('"');
        if (path.Length == 0) { CueButton(_browse, true); return; }
        if (_preparedSource is null || !string.Equals(_preparedKey, path, StringComparison.OrdinalIgnoreCase))
        { CueButton(_inspectSource, true); return; }
        var images = _preparedSource.Images ?? [];
        var current = new CurrentWindows(_downloadSystem.Edition, _downloadSystem.Language, _downloadSystem.Build);
        bool Matches(InstallationImage item) => _downloadSystem.IsServer
            ? ManualServerUpgrade.GetIncompatibility(item, _downloadSystem) is null
            : InstallationImages.GetIncompatibility(item, current) is null;
        if (!images.Any(Matches) || _lastErrorDetails is not null || !_downloadSystem.IsServer && !_upgradeAllowed) return;
        if (_installationImages.SelectedItem is not InstallationImage selected || !images.Contains(selected) || !Matches(selected))
        { _imageChoiceFrame.BackColor = Accent; return; }
        if (_downloadSystem.IsServer) CueButton(_copyServerCommand, _copyServerCommand.Enabled);
        else if (_start.Enabled) CueStart();
    }

    private void CueStart()
    {
        _start.FlatAppearance.BorderColor = Color.FromArgb(173, 242, 198);
        _start.FlatAppearance.BorderSize = 2;
        _start.AccessibleDescription = "Recommended next action: start the checked upgrade workflow.";
    }

    private static void CueButton(Button button, bool active)
    {
        button.BackColor = active && button.Enabled ? Color.FromArgb(49, 82, 133) : SurfaceRaised;
        button.ForeColor = active && button.Enabled ? Color.White : Foreground;
        button.FlatAppearance.BorderColor = active && button.Enabled ? AccentLight : Divider;
        button.FlatAppearance.BorderSize = active && button.Enabled ? 2 : 1;
        button.FlatAppearance.MouseOverBackColor = active && button.Enabled ? Color.FromArgb(62, 99, 157) : Color.FromArgb(52, 54, 68);
        button.FlatAppearance.MouseDownBackColor = active && button.Enabled ? Color.FromArgb(40, 66, 109) : AccentTint;
        button.AccessibleDescription = active && button.Enabled ? "Recommended next action" : "";
    }

    private void ApplyDownloadLanguagePolicy(string language)
    {
        ApplyDownloadSystem(_readDownloadSystem() with { Language = language });
    }

    private void ApplyDownloadSystem(DownloadSystem system)
    {
        _downloadSystem = system;
        // Download-route discovery must not silently change the existing manual client upgrade policy.
        _upgradeAllowed = ManualUpgradePolicy.GetUnavailableReason(system) is null;
        _automaticDownloadAvailable = AutomaticDownloadPolicy.IsAvailable(system);
        _automaticDownloadInfo.Text = _automaticDownloadAvailable
            ? $"English ({system.Language}). The ISO is checked before the upgrade starts."
            : AutomaticDownloadPolicy.GetUnavailableReason(system) + " Use Find matching ISO for browser download guidance.";
        _automaticDownloadInfo.ForeColor = _automaticDownloadAvailable ? Muted : Warning;
        _copyServerCommand.Visible = system.IsServer;
        _serverCommandPreview.Visible = system.IsServer;
        _existingMediaInfo.Text = system.IsServer
            ? "Windows Server media for inspection and manual command guidance only. This assistant will not run Server Setup."
            : "Windows 10 or 11 media, on this computer or your network.";
        _start.Enabled = _operation is not null || _upgradeAllowed;
        _quietSetup.Enabled = _operation is null && _upgradeAllowed;
        _autoRestart.Enabled = _operation is null && _upgradeAllowed && !_quietSetup.Checked;
        if (!_automaticDownloadAvailable) _existingMedia.Checked = true;
        UpdateSourceControls();
    }

    private void UpdateReadinessSummary()
    {
        _sourceSummary.Text = _existingMedia.Checked
            ? (string.IsNullOrWhiteSpace(_sources.Text) ? "ISO, drive or folder" : _sources.Text)
            : _downloadLocations.SelectedItem is StorageLocation location ? $"Automatic · {location.RootPath}" : "Automatic download";
        _restartSummary.Text = _autoRestart.Checked ? "Automatic" : "Manual restart";
        _restartSummary.ForeColor = _autoRestart.Checked ? AccentLight : Warning;
    }

    private void QuietModeChanged()
    {
        if (_quietSetup.Checked)
        {
            _restartBeforeQuiet = _autoRestart.Checked;
            _autoRestart.Checked = true;
        }
        else _autoRestart.Checked = _restartBeforeQuiet;
        _autoRestart.Enabled = _operation is null && _upgradeAllowed && !_quietSetup.Checked;
        _restartHelp.Text = _quietSetup.Checked
            ? "Controlled by silent mode. Turn off silent mode to change."
            : "Turn off to restart manually after upgrade preparation.";
        UpdateReadinessSummary();
    }

    private static Panel CreateCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Surface,
            Padding = new Padding(20)
        };
        card.Paint += (_, e) =>
        {
            var rectangle = new Rectangle(0, 0, Math.Max(0, card.Width - 1), Math.Max(0, card.Height - 1));
            using var pen = new Pen(Divider);
            e.Graphics.DrawRectangle(pen, rectangle);
        };
        return card;
    }

    private sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            // The border follows Width/Height. Repaint the old border's entire
            // surface as well as newly exposed strips when the card resizes.
            ResizeRedraw = true;
            DoubleBuffered = true;
        }
    }

    private static Panel CreateScrollHost() => new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        BackColor = Surface,
        Margin = new Padding(0)
    };

    private static Label ConfigureSourcePanel(TableLayoutPanel panel, RadioButton radio, string description, Control selector)
    {
        panel.AutoSize = true;
        panel.Dock = DockStyle.Top;
        panel.ColumnCount = 1;
        panel.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
        panel.BackColor = SurfaceRaised;
        panel.Padding = new Padding(12, 7, 12, 8);
        panel.Margin = new Padding(0, 6, 0, 0);
        panel.Controls.Add(radio);
        var explanation = BodyText(description, 22, 470);
        panel.Controls.Add(explanation);
        selector.Margin = new Padding(22, 7, 0, 0);
        panel.Controls.Add(selector);
        return explanation;
    }

    private static TableLayoutPanel BuildTwoColumnRow()
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return row;
    }

    private static Control ReadinessRow(string title, Label value)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = false,
            Height = 27,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            BackColor = Surface,
            Padding = new Padding(0, 4, 0, 4),
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        row.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title,
            ForeColor = Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 8, 0)
        }, 0, 0);
        value.AutoSize = false;
        value.Dock = DockStyle.Fill;
        value.TextAlign = ContentAlignment.MiddleRight;
        value.MaximumSize = Size.Empty;
        value.Margin = new Padding(0, 0, SystemInformation.VerticalScrollBarWidth, 0);
        row.Controls.Add(value, 1, 0);
        return row;
    }

    private static Control SystemDetailRow(string title, Label value)
    {
        var row = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            BackColor = SurfaceRaised,
            Padding = new Padding(0, 1, 0, 1),
            Margin = new Padding(0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.Controls.Add(new Label
        {
            AutoSize = true,
            Text = title,
            ForeColor = Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 2, 8, 0)
        }, 0, 0);
        value.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        value.MaximumSize = new Size(197, 0);
        row.Controls.Add(value, 1, 0);
        return row;
    }

    private static void ConfigureSystemDetailValue(Label label, string text)
    {
        label.AutoSize = true;
        label.Text = text;
        label.ForeColor = AccentLight;
        label.BackColor = Color.Transparent;
        label.Margin = new Padding(0, 2, 0, 0);
    }

    private static string GetProcessorDescription()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string processorName &&
                !string.IsNullOrWhiteSpace(processorName))
            {
                return string.Join(" ", processorName.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
        catch
        {
            // Fall back to a useful value even when the registry cannot be queried.
        }

        return $"{Environment.ProcessorCount} logical processor(s)";
    }

    private static string GetMemoryDescription()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };
        return GlobalMemoryStatusEx(ref status)
            ? $"{UpgradeEngine.FormatBytes((long)status.TotalPhysical)} installed"
            : "Unavailable";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    private static Label ValueLabel(string text, Color color) => new()
    {
        Text = text,
        ForeColor = color,
        BackColor = Color.Transparent
    };

    private static Label Kicker(string text) => new()
    {
        AutoSize = true,
        Text = text,
        Font = new Font("Segoe UI Semibold", 8.5F),
        ForeColor = Accent,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 0, 4)
    };

    private static Label SectionHeading(string text, int maximumWidth = 520) => new()
    {
        AutoSize = true,
        MaximumSize = new Size(maximumWidth, 0),
        Text = text,
        Font = new Font("Segoe UI Semibold", 17F),
        ForeColor = Foreground,
        BackColor = Color.Transparent,
        Margin = new Padding(0, 0, 0, 4)
    };

    private static Label BodyText(string text, int leftMargin = 0, int maximumWidth = 520) => new()
    {
        AutoSize = true,
        MaximumSize = new Size(maximumWidth, 0),
        Text = text,
        ForeColor = Muted,
        BackColor = Color.Transparent,
        Margin = new Padding(leftMargin, 0, 0, 2)
    };

    private static Panel DividerLine() => new()
    {
        Dock = DockStyle.Top,
        Height = 1,
        BackColor = Divider,
        Margin = new Padding(0, 14, 0, 14)
    };

    private static PictureBox CreateMascot(int size)
    {
        var logo = new PictureBox
        {
            Size = new Size(size, size),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent
        };
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("PcNinja.Win11UpgradeAssistant.Assets.PcNinja-mascot.png");
            if (stream is not null)
            {
                using var image = Image.FromStream(stream);
                logo.Image = new Bitmap(image);
            }
        }
        catch { }
        return logo;
    }

    private static void StyleRadio(RadioButton radio, bool isChecked = false)
    {
        radio.AutoSize = true;
        radio.Checked = isChecked;
        radio.Font = new Font("Segoe UI Semibold", 9.5F);
        radio.ForeColor = Foreground;
        radio.BackColor = Color.Transparent;
        radio.Margin = new Padding(0, 0, 0, 3);
    }

    private static void StyleCombo(ComboBox combo, Func<string>? emptyText = null)
    {
        combo.Dock = DockStyle.Fill;
        combo.BackColor = Background;
        combo.ForeColor = Foreground;
        combo.FlatStyle = FlatStyle.Flat;
        combo.DrawMode = DrawMode.OwnerDrawFixed;
        combo.ItemHeight = 24;
        combo.IntegralHeight = false;
        combo.MaxDropDownItems = 8;
        combo.DrawItem += (_, e) =>
        {
            var isPrompt = e.Index < 0;
            if (isPrompt && emptyText is null) return;
            using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? AccentTint : Background);
            e.Graphics.FillRectangle(background, e.Bounds);
            var textBounds = new Rectangle(e.Bounds.X + 7, e.Bounds.Y, Math.Max(0, e.Bounds.Width - 7), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, isPrompt ? emptyText!() : combo.Items[e.Index]?.ToString() ?? "", combo.Font,
                textBounds, isPrompt ? Muted : Foreground, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
    }

    private static void StylePrimaryButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(14, 7, 14, 7);
        button.BackColor = Surface;
        button.ForeColor = AccentLight;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Accent;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = AccentTint;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(66, 58, 106);
        button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI Semibold", 9.5F);
        button.Margin = new Padding(0);
    }

    private static void StyleGoButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(14, 7, 14, 7);
        button.BackColor = Color.FromArgb(48, 142, 86);
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Success;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(57, 163, 99);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(39, 119, 72);
        button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI Semibold", 9.5F);
        button.Margin = new Padding(0);
    }

    private static void StyleCancelButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(14, 7, 14, 7);
        button.BackColor = Color.FromArgb(177, 55, 61);
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(236, 119, 124);
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(199, 67, 73);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(145, 42, 47);
        button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI Semibold", 9.5F);
        button.Margin = new Padding(0);
    }

    private static void StyleRunningButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(14, 7, 14, 7);
        button.BackColor = SurfaceRaised;
        button.ForeColor = Muted;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Divider;
        button.FlatAppearance.BorderSize = 1;
        button.UseVisualStyleBackColor = false;
        button.Font = new Font("Segoe UI Semibold", 9.5F);
        button.Margin = new Padding(0);
    }

    private static void StyleSecondaryButton(Button button, string text)
    {
        button.Text = text;
        button.AutoSize = true;
        button.Padding = new Padding(9, 5, 9, 5);
        button.BackColor = SurfaceRaised;
        button.ForeColor = Foreground;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Divider;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 54, 68);
        button.FlatAppearance.MouseDownBackColor = AccentTint;
        button.UseVisualStyleBackColor = false;
        button.Margin = new Padding(6, 0, 0, 0);
    }
}
