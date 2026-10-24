using System.Reflection;
using PcNinja.Win11Upgrade.Core;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Explicit initialization avoids the two friend assemblies' generated
        // internal ApplicationConfiguration types colliding in this harness.
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var assembly = Assembly.Load("Windows-Upgrade-Assistant-By-PcNinja");
        var type = assembly.GetType("PcNinja.Win11UpgradeAssistant.MainForm", true)!;
        using var form = (Form)Activator.CreateInstance(type, nonPublic: true)!;
        object Field(string name) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
        void SetField(string name, object? value) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, value);
        void Invoke(string name, params object[] values) => type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, values);
        SetField("_automaticAppUpdateCheck", false);
        form.Show();
        Application.DoEvents();
        var actualLanguage = CurrentWindows.Read().Language;
        Invoke("ApplyDownloadLanguagePolicy", "en-US");
        var start = (Button)Field("_start");
        if (!start.Enabled) throw new Exception("Start is disabled after startup on the test machine.");
        var choices = new[] { "_autoDownload", "_existingMedia" }.Select(name => (RadioButton)Field(name)).ToArray();
        IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
            .SelectMany(control => new[] { control }.Concat(Descendants(control)));
        ResizeChecks.Run(form, Descendants(form).OfType<Panel>()
            .Where(panel => panel.Padding == new Padding(20)).ToArray());
        if (form.Text != "Windows Upgrade Assistant By PcNinja" ||
            Descendants(form).Any(control => control.Text.Contains("Version 1.1.0-rc.")))
            throw new Exception("Product branding exposed an obsolete name or release label.");
        var captions = Descendants(form).Select(control => control.Text).ToHashSet();
        foreach (var caption in new[]
        {
            "Upgrade Windows 10 or Windows 11 while keeping your apps and files.",
            "Choose your installation source", "Windows installation image",
            "Manual ISO downloads", "Your computer && upgrade settings"
        })
            if (!captions.Contains(caption)) throw new Exception($"Approved GUI heading is missing: {caption}");
        if (captions.Contains("INSTALLATION SOURCE") || captions.Contains("THIS COMPUTER") ||
            captions.Contains("Upgrade readiness") || ((Button)Field("_inspectSource")).Text != "Validate")
            throw new Exception("Duplicate headings or the old Inspect images caption remain.");
        if (!Descendants(form).OfType<LinkLabel>().Any(link => link.Text == "www.pcninja.pro" && link.Links.Count > 0) ||
            UpgradeEngine.PcNinjaBrowserFallbackPage != "https://iso.pcninja.pro" ||
            UpgradeEngine.MicrosoftDownloadPage != "https://www.microsoft.com/software-download/windows11")
            throw new Exception("Branding or public download links changed during the copy review.");
        if (Descendants(form).OfType<PictureBox>().Count(picture => picture.Image is not null) != 1)
            throw new Exception("The single PcNinja header logo is missing or duplicated.");
        using (var mascot = assembly.GetManifestResourceStream("PcNinja.Win11UpgradeAssistant.Assets.PcNinja-mascot.png")!)
            if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mascot)) !=
                "4FDA4958ECA3DD6A337985BB95AE00CE5A06CDA269B95EBC20D2A54ED65A09BC")
                throw new Exception("The original embedded PcNinja logo was replaced.");
        var previous = new UpgradeAttempt(DateTimeOffset.UtcNow, "unused", "unused", 1,
            19043, 19041, "Professional", 0x40010004, "RollbackObserved", "Recovery collision: 0x800700B7 - 0x2000A.")
        { SetupFailure = new("0x800700B7", "0x2000A", "Safe OS", "Recovery collision", "fixture.log") };
        Invoke("PresentPreviousUpgrade", previous);
        if (!((Label)Field("_operationNotice")).Text.Contains("0x800700B7") ||
            !((LinkLabel)Field("_details")).Visible)
            throw new Exception("Rollback diagnostics were hidden in the footer or missing details.");
        Invoke("RefreshSources", false);
        if (!((Label)Field("_operationNotice")).Text.Contains("0x800700B7"))
            throw new Exception("Refresh erased the rollback explanation.");
        if (Descendants(form).OfType<RadioButton>().Count() != 2)
            throw new Exception("The GUI must contain exactly two primary source choices.");
        foreach (var choice in choices)
        {
            choice.Checked = true;
            if (choices.Count(item => item.Checked) != 1) throw new Exception("Source choices are not exclusive.");
            var automaticPanel = (Control)Field("_autoSourcePanel");
            var existingPanel = (Control)Field("_existingSourcePanel");
            if (automaticPanel.BackColor == existingPanel.BackColor)
                throw new Exception("The selected source is not uniquely highlighted.");
            if (!choices[0].Text.EndsWith("Download Windows 11 automatically") ||
                !choices[1].Text.EndsWith("Use an ISO, drive or folder"))
                throw new Exception("Selecting a source restored the old source captions.");
        }
        foreach (var language in new[] { "he-IL", "en-GB", "ru-RU" })
        {
            choices[0].Checked = true;
            Invoke("ApplyDownloadLanguagePolicy", language);
            Invoke("SetBusy", true);
            Invoke("SetBusy", false);
            var note = (Label)Field("_automaticDownloadInfo");
            if (choices[0].Enabled || choices[0].Checked || !choices[1].Checked ||
                ((ComboBox)Field("_downloadLocations")).Enabled || ((Button)Field("_chooseDownloadFolder")).Enabled ||
                !((Button)Field("_browse")).Enabled || !((Button)Field("_browseInstallationFolder")).Enabled ||
                !((Button)Field("_pcNinjaDownload")).Enabled || !((Button)Field("_microsoft")).Enabled ||
                !note.Text.Contains(language) || !note.Text.Contains("en-US"))
                throw new Exception("Language gating hid manual alternatives, lost its explanation, or re-enabled automatic download.");
        }
        Invoke("ApplyDownloadLanguagePolicy", "en-US");
        if (!choices[0].Enabled || !((ComboBox)Field("_downloadLocations")).Enabled)
            throw new Exception("Automatic download did not return for matching en-US media.");
        Invoke("ApplyDownloadLanguagePolicy", actualLanguage);
        // Exercise the real Click event, using an empty source so Setup can NEVER start.
        var sources = (ComboBox)Field("_sources");
        sources.Items.Clear();
        sources.Text = "";
        start.PerformClick();
        Application.DoEvents();
        if (!((Label)Field("_status")).Text.Contains("No existing installation media"))
            throw new Exception("Start click did not reach validation or its message was overwritten.");
        if (!start.Enabled) throw new Exception("Start was not restored after validation failure.");
        var notice = (Label)Field("_operationNotice");
        Invoke("RefreshSources", false);
        if (!notice.Visible || !notice.Text.Contains("No existing installation media"))
            throw new Exception("The actionable failure explanation disappeared during Refresh.");

        // Test the real consent decision methods without downloads, mounting or Setup.
        var confirmations = 0;
        var accept = true;
        var lastConfirmation = "";
        SetField("_confirmUpgrade", new Func<string, string, bool>((message, title) =>
        {
            confirmations++;
            lastConfirmation = message;
            if (title == "Download and upgrade" && (!message.Contains("validate") || !message.Contains("Back up")))
                throw new Exception("Automatic consent did not explain the complete operation.");
            return accept;
        }));
        object? Call(string name, params object?[] values) => type.GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, values);
        // Only the current, non-cancelled preparation may update status. No media work.
        // Unlike production Application.Run, this fixture uses DoEvents: explicitly
        // supply its UI context so Progress<T> cannot choose the ThreadPool fallback.
        var priorContext = SynchronizationContext.Current;
        using var preparationContext = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(preparationContext);
        try
        {
        using (var preparation = new CancellationTokenSource())
        using (var replacement = new CancellationTokenSource())
        {
            SetField("_operation", preparation);
            var progress = (IProgress<string>)Call("CreatePreparationProgress", preparation)!;
            var status = (Label)Field("_status");
            progress.Report("Reading Windows image 1 of 2...");
            Application.DoEvents();
            if (status.Text != "Reading Windows image 1 of 2...")
                throw new Exception("Active preparation progress was not displayed.");
            preparation.Cancel();
            status.Text = "Operation cancelled.";
            progress.Report("late cancelled progress");
            Application.DoEvents();
            if (status.Text != "Operation cancelled.") throw new Exception("Cancelled progress overwrote the outcome.");
            SetField("_operation", replacement);
            progress.Report("late previous operation");
            Application.DoEvents();
            if (status.Text != "Operation cancelled.") throw new Exception("An old operation overwrote current status.");
            SetField("_operation", null);
        }
        }
        finally { SynchronizationContext.SetSynchronizationContext(priorContext); }
        var consentImage = new InstallationImage(6, "Windows 11 Pro", "Professional", "x64", "10.0.26300", "Client", "en-US");
        var consentSource = new MountedSource("X:\\", "X:\\setup.exe", "X:\\sources\\install.wim", [consentImage]);
        foreach (var restart in new[] { true, false })
        {
            ((CheckBox)Field("_autoRestart")).Checked = restart;
            confirmations = 0;
            if (!(bool)Call("ConfirmAutomaticUpgrade", "Z:\\Downloads", restart, false)! ||
                !(bool)Call("ConfirmPreparedUpgrade", consentSource, consentImage, 1, consentSource, restart, false)! || confirmations != 1)
                throw new Exception("Automatic mode requires more than one consent or failed to retain restart intent.");
        }
        confirmations = 0;
        Call("ConfirmPreparedUpgrade", consentSource, consentImage, 1, null, false, false);
        Call("ConfirmPreparedUpgrade", consentSource with { Root = "Y:\\" }, consentImage, 1, consentSource, false, false);
        Call("ConfirmPreparedUpgrade", consentSource, consentImage, 2, consentSource, false, false);
        Call("ConfirmPreparedUpgrade", consentSource, consentImage, 1, consentSource, true, false);
        if (confirmations != 4) throw new Exception("Manual, changed, ambiguous or changed-restart consent was bypassed.");
        var windows10Image = consentImage with { Name = "Windows 10 Pro", Version = "10.0.19041" };
        var windows10Source = consentSource with { Images = [windows10Image] };
        confirmations = 0;
        Call("ConfirmPreparedUpgrade", windows10Source, windows10Image, 1, null, false, false);
        if (confirmations != 1 || !lastConfirmation.Contains(InstallationImages.Windows10Notice))
            throw new Exception("Windows 10 manual confirmation omitted the POC build-order/migration warning.");
        Call("ConfirmPreparedUpgrade", windows10Source, windows10Image, 1, windows10Source, false, false);
        if (confirmations != 2) throw new Exception("Windows 10 inherited unattended Windows 11 consent.");
        var quiet = (CheckBox)Field("_quietSetup");
        var autoRestart = (CheckBox)Field("_autoRestart");
        foreach (var previousRestart in new[] { false, true })
        {
            autoRestart.Checked = previousRestart;
            quiet.Checked = true;
            Invoke("SetBusy", true);
            Invoke("SetBusy", false);
            Invoke("RefreshSources", false);
            if (!autoRestart.Checked || autoRestart.Enabled || !quiet.Enabled)
                throw new Exception("GUI quiet mode did not force/lock automatic restart through busy/refresh.");
            confirmations = 0;
            Call("ConfirmPreparedUpgrade", consentSource, consentImage, 1, consentSource, true, false);
            if (confirmations != 1 || !lastConfirmation.Contains("silently"))
                throw new Exception("Changed quiet intent bypassed consent or omitted silent Setup guidance.");
            quiet.Checked = false;
            if (autoRestart.Checked != previousRestart || !autoRestart.Enabled)
                throw new Exception("Disabling quiet mode lost the previous restart preference.");
        }
        accept = false;
        if ((bool)Call("ConfirmAutomaticUpgrade", "Z:\\Downloads", true, false)! ||
            (bool)Call("ConfirmPreparedUpgrade", consentSource, consentImage, 1, null, false, false)!)
            throw new Exception("Declining consent did not prevent continuation.");
        // Exercise cancellation through Start itself. A declined prompt returns before
        // DownloadIsoAsync, so this test cannot fetch/mount media or launch Setup.
        Invoke("ApplyDownloadLanguagePolicy", "en-US");
        choices[0].Checked = true;
        confirmations = 0;
        start.PerformClick();
        Application.DoEvents();
        if (confirmations != 1 || !((Label)Field("_status")).Text.Contains("No download") ||
            Field("_operation") is not null)
            throw new Exception("Declining the actual automatic Start did not stop before transfer.");
        choices[1].Checked = true;
        Invoke("ApplyDownloadLanguagePolicy", actualLanguage);

        var restartNotices = 0;
        SetField("_showRestartNotice", new Action<string>(message =>
        {
            restartNotices++;
            if (message != UpgradeResult.ManualRestartMessage)
                throw new Exception("Restart popup did not explain preparation versus final upgrade.");
        }));
        Invoke("PresentSetupResult", new UpgradeResult(0, "unused-setup.exe"), false);
        Invoke("RefreshSources", false);
        if (restartNotices != 1 || !notice.Visible || notice.Text != UpgradeResult.ManualRestartMessage)
            throw new Exception("Manual-restart popup or persistent notice is missing.");
        Invoke("PresentSetupResult", new UpgradeResult(0, "unused-setup.exe"), true);
        Invoke("PresentSetupResult", new UpgradeResult(0x40010004, "unused-setup.exe"), false);
        try
        {
            Invoke("PresentSetupResult", new UpgradeResult(unchecked((int)0xC0000005), "unused-setup.exe"), false);
            throw new Exception("A failed Setup was reported as prepared.");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
        if (restartNotices != 1) throw new Exception("Automatic/failure/transition result requested a manual restart.");

        using var cancellation = new CancellationTokenSource();
        SetField("_operation", cancellation);
        Invoke("SetBusy", true);
        if (!start.Enabled || start.Text != "CANCEL") throw new Exception("Cancel button is unavailable.");
        start.PerformClick();
        if (!cancellation.IsCancellationRequested || start.Enabled || start.Text != "CANCELLING...")
            throw new Exception("Cancel click failed to signal cancellation and prevent repeated clicks.");
        SetField("_setupHasStarted", true);
        Invoke("SetBusy", true);
        if (start.Enabled || start.Text != "SETUP RUNNING") throw new Exception("Setup phase permits cancellation.");
        SetField("_operation", null);
        SetField("_setupHasStarted", false);
        Invoke("SetBusy", false);

        var folder = Path.Combine(Path.GetTempPath(), "PcNinja-Gui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(folder)!);
            var combo = (ComboBox)Field("_downloadLocations");
            combo.Items.Insert(0, new StorageLocation(folder, "Selected folder", drive.DriveType, drive.DriveFormat, drive.AvailableFreeSpace));
            combo.SelectedIndex = 0;
            Invoke("RefreshSources", false);
            if ((combo.SelectedItem as StorageLocation)?.RootPath != folder)
                throw new Exception("Refresh replaced the manually selected download folder.");

            sources.Text = Path.Combine(folder, "exact-selected.iso");
            var current = CurrentWindows.Read();
            var image = new InstallationImage(6, "Test Windows image", current.Edition, "x64", $"10.0.{current.Build}", "Client", current.Language);
            var prepared = new MountedSource(folder, Path.Combine(folder, "setup.exe"), Path.Combine(folder, "sources", "install.wim"), [image]);
            SetField("_preparedSource", prepared);
            SetField("_preparedKey", sources.Text);
            var images = (ComboBox)Field("_installationImages");
            images.Items.Add(image);
            images.SelectedIndex = 0;
            var otherLanguage = current.Language == "he-IL" ? "en-US" : "he-IL";
            images.Items.Add(image with { Index = 7, Language = otherLanguage });
            images.SelectedIndex = 1;
            var mismatchInfo = (Label)Field("_imageInfo");
            if (!mismatchInfo.Text.Contains(current.Language) || !mismatchInfo.Text.Contains(otherLanguage) ||
                !mismatchInfo.Text.Contains("Language mismatch") || mismatchInfo.ForeColor == form.ForeColor)
                throw new Exception("Language mismatch did not remain visible beside the selected image.");
            images.SelectedIndex = 0;
            ((CheckBox)Field("_autoRestart")).Checked = false;
            Invoke("RefreshSources", false);
            if (sources.Text != Path.Combine(folder, "exact-selected.iso") || !Equals(images.SelectedItem, image) ||
                ((CheckBox)Field("_autoRestart")).Checked)
                throw new Exception("Refresh lost the exact source, image or restart policy.");
            sources.Text = Path.Combine(folder, "different.iso");
            if (images.Items.Count != 0 || Field("_preparedSource") is not null)
                throw new Exception("Changing the source retained an old image index.");
        }
        finally { Directory.Delete(folder); }

        ImageSelectionChecks.Run(form);
        DownloadGuidanceChecks.Run();
        AppUpdateGuiChecks.Run();

        if (args.Length > 0)
        {
            Directory.CreateDirectory(args[0]);
            // Clear injected fixture state so the preview is not mistaken for
            // a real pending upgrade or a selected customer installation path.
            sources.Text = "";
            SetField("_preparedSource", null);
            SetField("_preparedKey", null);
            ((ComboBox)Field("_installationImages")).Items.Clear();
            Invoke("UpdateImageInfo");
            ((CheckBox)Field("_autoRestart")).Checked = true;
            Invoke("RefreshSources", false);
            ((Label)Field("_operationNotice")).Visible = false;
            ((LinkLabel)Field("_details")).Visible = false;
            choices[0].Checked = true;
            Save("Ready.png");
            quiet.Checked = true;
            Save("Quiet.png");
            quiet.Checked = false;
            var normalSize = form.Size;
            form.Size = form.MinimumSize;
            Save("Minimum.png");
            form.Size = normalSize;
            using var previewOperation = new CancellationTokenSource();
            SetField("_operation", previewOperation);
            Invoke("SetBusy", true);
            Save("Cancel.png");
            SetField("_operation", null);
            Invoke("SetBusy", false);
            SetField("_lastErrorDetails", null);
            choices[1].Checked = true;
            Save("Manual-browse.png");
            sources.Text = @"X:\Preview-only\Windows.iso";
            Save("Manual-validate.png");
            var previewCurrent = CurrentWindows.Read();
            var previewImage = new InstallationImage(1, "Preview-only matching Windows image", previewCurrent.Edition,
                "x64", $"10.0.{Math.Max(22000, previewCurrent.Build)}", "Client", previewCurrent.Language);
            var previewSource = new MountedSource(@"X:\Preview-only", @"X:\Preview-only\setup.exe",
                @"X:\Preview-only\sources\install.wim", [previewImage, previewImage with { Index = 2 }]);
            SetField("_preparedSource", previewSource);
            SetField("_preparedKey", sources.Text);
            var previewImages = (ComboBox)Field("_installationImages");
            foreach (var item in previewSource.Images!) previewImages.Items.Add(item);
            Invoke("UpdateImageInfo");
            Save("Manual-choose-image.png");
            previewImages.SelectedIndex = 0;
            Save("Manual-start.png");
            void Save(string name)
            {
                form.PerformLayout();
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                image.Save(Path.Combine(args[0], name));
            }
        }
        Console.WriteLine("PASS: Approved GUI copy and original logo/public links; manual-restart completion notice and failure/transition exclusion; automatic single consent and invalidation; persistent notices; language-gated download; two exclusive sources; real Start/Cancel; Setup lockout; refresh retention. No Setup launched.");
        return 0;
    }
}
