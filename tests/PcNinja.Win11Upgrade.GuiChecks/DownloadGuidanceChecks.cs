using System.Diagnostics;
using System.Reflection;
using PcNinja.Win11Upgrade.Core;

internal static class DownloadGuidanceChecks
{
    internal static void Run()
    {
        var priorContext = SynchronizationContext.Current;
        Form? form = null;
        try
        {
            var assembly = Assembly.Load("Windows-Upgrade-Assistant-By-PcNinja");
            var type = assembly.GetType("PcNinja.Win11UpgradeAssistant.MainForm", throwOnError: true)!;
            form = (Form)Activator.CreateInstance(type, nonPublic: true)!;
            var context = new FormPumpContext(form);
            SynchronizationContext.SetSynchronizationContext(context);
            T Field<T>(string name) => (T)type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            void Set(string name, object? value) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, value);
            object? Call(string name, params object?[] values) => type.GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, values);
            Task Begin(string name)
            {
                // DoEvents may install/restore its own thread context. Each new
                // async operation must capture this fixture's live form instead.
                SynchronizationContext.SetSynchronizationContext(context);
                return (Task)Call(name)!;
            }

            var client = new DownloadSystem("Professional", "es-ES", 22631, 1, "x64", "Client");
            var server = new DownloadSystem("ServerStandard", "en-US", 20348, 1, "x64", "Server");
            var browser = new List<string>();
            var copied = new List<string>();
            var confirmations = 0;
            Set("_readDownloadSystem", new Func<DownloadSystem>(() => client));
            Set("_launchBrowser", new Action<string>(browser.Add));
            Set("_copyCommandToClipboard", new Action<string>(copied.Add));
            Set("_confirmUpgrade", new Func<string, string, bool>((_, _) => { confirmations++; return false; }));
            Set("_resolveDownloadRecommendation", new Func<CancellationToken, Task<DownloadRecommendation>>(_ =>
                throw new Exception("The fixture resolver must be explicitly configured.")));
            Set("_prepareServerCommand", new Func<MountedSource, int, CancellationToken, Task<string>>((_, _, _) =>
                throw new Exception("The fixture command preparer must be explicitly configured.")));
            Set("_automaticAppUpdateCheck", false);
            form.Show();
            Application.DoEvents();
            Call("ApplyDownloadSystem", client);

            var automatic = Field<RadioButton>("_autoDownload");
            var existing = Field<RadioButton>("_existingMedia");
            var sources = Field<ComboBox>("_sources");
            var images = Field<ComboBox>("_installationImages");
            var notice = Field<Label>("_downloadNotice");
            var status = Field<Label>("_status");
            var start = Field<Button>("_start");
            var copy = Field<Button>("_copyServerCommand");
            var preview = Field<TextBox>("_serverCommandPreview");
            var match = Field<Button>("_matchingDownload");
            var validate = Field<Button>("_inspectSource");
            var browse = Field<Button>("_browse");
            var imageFrame = Field<Panel>("_imageChoiceFrame");
            if (automatic.Enabled || automatic.Checked || !existing.Checked || !match.Enabled ||
                !Field<Button>("_browse").Enabled || !Field<Button>("_browseInstallationFolder").Enabled ||
                !Field<Button>("_pcNinjaDownload").Enabled || !Field<Button>("_microsoft").Enabled)
                throw new Exception("Spanish client gating removed manual alternatives or left automatic download enabled.");

            var clientImage = new InstallationImage(6, "Spanish Pro fixture", "Professional", "x64", "10.0.26100.1", "Client", "es-ES");
            var clientSource = new MountedSource(@"X:\client-fixture", @"X:\client-fixture\setup.exe",
                @"X:\client-fixture\sources\install.wim", [clientImage]);
            void Populate(MountedSource source, InstallationImage? selection = null)
            {
                sources.Text = source.Root;
                Set("_preparedKey", source.Root);
                Set("_preparedSource", source);
                images.Items.Clear();
                foreach (var item in source.Images ?? []) images.Items.Add(item);
                if (selection is not null && !images.Items.Contains(selection)) images.Items.Add(selection);
                images.SelectedItem = selection;
                Call("UpdateImageInfo");
                Call("UpdateSourceControls");
            }
            void AssertNoSetup()
            {
                if (Field<bool>("_setupHasStarted") || confirmations != 0)
                    throw new Exception("Download/manual Server guidance entered Setup or upgrade consent.");
            }
            void AssertIdle()
            {
                if (Field<CancellationTokenSource?>("_operation") is not null || !match.Enabled)
                    throw new Exception("Download/manual command operation did not return to idle.");
                AssertNoSetup();
            }

            if (validate.Enabled || browse.FlatAppearance.BorderSize != 2)
                throw new Exception("Empty manual source did not cue ISO browsing or disabled Validate.");
            var downloadsType = assembly.GetType("PcNinja.Win11UpgradeAssistant.UserDownloads", true)!;
            var downloadsPath = (string)downloadsType.GetMethod("GetPath", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
            var resolver = downloadsType.GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)!;
            var redirected = @"D:\Descargas\בדיקה á";
            if ((string)resolver.Invoke(null, [new Func<string?>(() => redirected), @"X:\profile"])! != redirected ||
                (string)resolver.Invoke(null, [new Func<string?>(() => null), @"X:\profile"])! != @"X:\profile" ||
                (string)resolver.Invoke(null, [new Func<string?>(() => throw new System.Runtime.InteropServices.COMException()), @"X:\profile"])! != @"X:\profile")
                throw new Exception("Downloads resolution lost redirected/Unicode paths or safe fallback.");
            var originalDirectory = Environment.CurrentDirectory;
            using (var dialog = (OpenFileDialog)Call("CreateIsoDialog")!)
                if (dialog.InitialDirectory != downloadsPath || !Path.IsPathFullyQualified(downloadsPath) ||
                    !dialog.RestoreDirectory || !dialog.CheckFileExists || dialog.Multiselect || !dialog.Filter.Contains("*.iso"))
                    throw new Exception("ISO dialog did not default to registered Downloads with exact-file checks/CWD restoration.");
            if (Environment.CurrentDirectory != originalDirectory) throw new Exception("Creating the ISO dialog changed CWD.");

            sources.Text = @"X:\exact-new.iso"; // No filesystem probe while typing.
            if (!validate.Enabled || validate.FlatAppearance.BorderSize != 2 || browse.FlatAppearance.BorderSize != 1 ||
                start.FlatAppearance.BorderSize != 1 || !start.Enabled)
                throw new Exception("Source selection did not move the cue to Validate or blocked the advanced Start shortcut.");
            Populate(clientSource);
            if (imageFrame.BackColor == form.BackColor || start.FlatAppearance.BorderSize != 1 || validate.FlatAppearance.BorderSize != 1)
                throw new Exception("Awaiting image selection retained a button cue.");
            var imageCueColor = imageFrame.BackColor;
            images.SelectedItem = clientImage;
            if (start.FlatAppearance.BorderSize != 2 || imageFrame.BackColor == imageCueColor || validate.FlatAppearance.BorderSize != 1)
                throw new Exception("A matching selected image did not move the cue to Start.");
            var wrongImage = clientImage with { Language = "en-US" };
            Populate(clientSource with { Images = [wrongImage] }, wrongImage);
            if (start.FlatAppearance.BorderSize != 1 || imageFrame.BackColor == imageCueColor)
                throw new Exception("Incompatible media advertised a ready/choose-image cue.");
            Populate(clientSource, clientImage);
            using (var fixtureOperation = new CancellationTokenSource())
            {
                Set("_operation", fixtureOperation);
                Call("SetBusy", true);
                if (start.BackColor != Color.FromArgb(177, 55, 61) || validate.Enabled ||
                    validate.FlatAppearance.BorderSize != 1 || browse.FlatAppearance.BorderSize != 1 ||
                    start.AccessibleDescription!.Contains("Recommended"))
                    throw new Exception("Preparation cues overwrote actual red Cancel or left stale recommendations.");
                Set("_operation", null);
                Call("SetBusy", false);
            }
            var english = client with { Language = "en-US" };
            Set("_readDownloadSystem", new Func<DownloadSystem>(() => english));
            Call("ApplyDownloadSystem", english);
            automatic.Checked = true;
            if (start.FlatAppearance.BorderSize != 2 || validate.Enabled || validate.FlatAppearance.BorderSize != 1)
                throw new Exception("Automatic flow gained a manual Validate step or lost its Start cue.");
            Set("_readDownloadSystem", new Func<DownloadSystem>(() => client));
            Call("ApplyDownloadSystem", client);
            sources.Text = "";
            var missingPath = Path.Combine(AppContext.BaseDirectory, "missing-cue-fixture-" + Guid.NewGuid().ToString("N") + ".iso");
            Populate(clientSource with { Root = missingPath }, clientImage);
            SynchronizationContext.SetSynchronizationContext(context);
            Pump((Task)Call("PrepareOrUpgradeAsync", false)!);
            if (Field<MountedSource?>("_preparedSource") is not null || images.Items.Count != 0 ||
                start.FlatAppearance.BorderSize != 1 || validate.FlatAppearance.BorderSize != 2 ||
                !Field<Label>("_operationNotice").Visible)
                throw new Exception("Failed revalidation retained stale Ready metadata/cue or hid the error.");
            sources.Text = "";
            Set("_lastErrorDetails", null);
            Console.WriteLine("PASS: Registered Downloads/fallback/Unicode ISO dialog; exclusive source/Validate/image/Start cues; unchanged automatic Start and red Cancel. No dialog shown or media accessed.");

            // Regression: readable matching LTSC media must not advertise START
            // while an unrelated download-profile gate silently disables it.
            var ltsc = new DownloadSystem("EnterpriseS", "he-IL", 19044, 1288, "x64", "Client", true);
            Set("_readDownloadSystem", new Func<DownloadSystem>(() => ltsc));
            Call("ApplyDownloadSystem", ltsc);
            Call("SetBusy", true);
            Call("SetBusy", false);
            if (!start.Enabled || automatic.Enabled || !existing.Checked ||
                !Field<CheckBox>("_quietSetup").Enabled || !Field<CheckBox>("_autoRestart").Enabled)
                throw new Exception("Manual LTSC client upgrade is incorrectly blocked by automatic-download availability.");
            var ltscImage = new InstallationImage(1, "Hebrew Enterprise LTSC fixture", "EnterpriseS", "x64", "10.0.26100.1", "Client", "he-IL");
            var ltscSource = clientSource with { Images = [ltscImage] };
            Populate(ltscSource, ltscImage);
            Call("PresentImageInspection");
            if (!status.Text.Contains("START UPGRADE") || !start.Enabled)
                throw new Exception("Matching LTSC inspection does not agree with the Start control.");
            var wrongLanguage = ltscImage with { Language = "en-US" };
            Populate(ltscSource with { Images = [wrongLanguage] }, wrongLanguage);
            Call("PresentImageInspection");
            if (status.Text.Contains("START UPGRADE") || !Field<Label>("_imageInfo").Text.Contains("Language mismatch") ||
                !Field<Label>("_operationNotice").Visible)
                throw new Exception("LTSC language mismatch lost its original rejection details.");
            var unsupported = ltsc with { Architecture = "arm64" };
            Set("_readDownloadSystem", new Func<DownloadSystem>(() => unsupported));
            Call("ApplyDownloadSystem", unsupported);
            Populate(ltscSource, ltscImage);
            Call("PresentImageInspection");
            if (start.Enabled || status.Text.Contains("START UPGRADE") ||
                !Field<Label>("_operationNotice").Text.Contains("x64"))
                throw new Exception("Unsupported-host inspection advertised Start instead of its shared Core blocker.");
            sources.Text = "";
            Call("ExistingPathChanged");
            Set("_readDownloadSystem", new Func<DownloadSystem>(() => client));
            Call("ApplyDownloadSystem", client);

            var recommendation = new DownloadRecommendation("Microsoft", "https://download.invalid/es-ES",
                "Windows11_es-es_fixture.iso", "Windows 11", "10.0.26100.1", true, "Matching Spanish ISO fixture.");
            Populate(clientSource, clientImage);
            Set("_resolveDownloadRecommendation", new Func<CancellationToken, Task<DownloadRecommendation>>(_ => Task.FromResult(recommendation)));
            Pump(Begin("FindMatchingDownloadAsync"));
            if (!browser.SequenceEqual([recommendation.PageUrl]) || copied.Count != 0 ||
                !existing.Checked || sources.Text != "" || images.Items.Count != 0 ||
                Field<MountedSource?>("_preparedSource") is not null || Field<string?>("_preparedKey") is not null ||
                !notice.Visible || !notice.Text.Contains(recommendation.FileName!) || !notice.Text.Contains("not continue automatically"))
                throw new Exception("Matching download guidance opened the wrong target, retained stale media or failed to explain manual continuation.");
            var persistentNotice = notice.Text;
            Call("UpdateSourceControls");
            if (!notice.Visible || notice.Text != persistentNotice)
                throw new Exception("Source-control refresh erased browser download instructions.");
            AssertIdle();

            // Even a resolver returning success after cancellation cannot launch a
            // browser, replace the selected source or carry automatic consent.
            Populate(clientSource, clientImage);
            var pendingSearch = new TaskCompletionSource<DownloadRecommendation>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken searchToken = default;
            Set("_resolveDownloadRecommendation", new Func<CancellationToken, Task<DownloadRecommendation>>(token =>
            { searchToken = token; return pendingSearch.Task; }));
            var search = Begin("FindMatchingDownloadAsync");
            var searchOperation = Field<CancellationTokenSource>("_operation");
            if (search.IsCompleted || !searchToken.CanBeCanceled || searchToken != searchOperation.Token)
                throw new Exception("The asynchronous download fixture did not use the active cancellable operation.");
            searchOperation.Cancel();
            pendingSearch.SetResult(recommendation);
            Pump(search);
            if (!searchToken.IsCancellationRequested || browser.Count != 1 || copied.Count != 0 ||
                sources.Text != clientSource.Root || !ReferenceEquals(Field<MountedSource>("_preparedSource"), clientSource) ||
                !status.Text.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Cancelled download discovery changed media or opened a browser after its late result.");
            AssertIdle();

            Set("_resolveDownloadRecommendation", new Func<CancellationToken, Task<DownloadRecommendation>>(_ => Task.FromResult(recommendation)));
            Set("_launchBrowser", new Action<string>(_ => throw new InvalidOperationException("Fixture browser unavailable.")));
            Pump(Begin("FindMatchingDownloadAsync"));
            if (!status.Text.Contains("Fixture browser unavailable") || !Field<Label>("_operationNotice").Visible ||
                !notice.Visible || copied.Count != 0 || sources.Text != "")
                throw new Exception("Browser-launch failure was hidden or continued with stale installation media.");
            Set("_launchBrowser", new Action<string>(browser.Add));
            AssertIdle();

            Set("_readDownloadSystem", new Func<DownloadSystem>(() => server));
            Call("ApplyDownloadSystem", server);
            Call("SetBusy", true);
            Call("SetBusy", false);
            if (start.Enabled || Field<CheckBox>("_quietSetup").Enabled || Field<CheckBox>("_autoRestart").Enabled ||
                automatic.Enabled || automatic.Checked || !copy.Visible || !preview.Visible || copy.Enabled)
                throw new Exception("Server guidance re-enabled client Setup/restart controls or copying without a selected image.");

            var serverImage = new InstallationImage(2, "Server Standard fixture", "ServerStandard", "x64",
                "10.0.26100.1", "Server", "en-US");
            var serverSource = new MountedSource(@"X:\server-fixture", @"X:\server-fixture\setup.exe",
                @"X:\server-fixture\sources\install.wim", [serverImage]);
            Populate(serverSource);
            if (copy.Enabled || images.SelectedIndex != -1)
                throw new Exception("Server media granted copying or implicitly selected an image before an explicit choice.");
            images.SelectedItem = serverImage;
            Call("UpdateImageInfo");
            if (!copy.Enabled || start.Enabled)
                throw new Exception("A matching explicit Server image did not enable manual copying exclusively.");

            var order = new List<string>();
            var command = ManualServerUpgrade.BuildCommand(serverSource, serverImage, server);
            Set("_prepareServerCommand", new Func<MountedSource, int, CancellationToken, Task<string>>((source, index, token) =>
            {
                if (!ReferenceEquals(source, serverSource) || index != serverImage.Index || !token.CanBeCanceled)
                    throw new Exception("Manual preparation lost its exact source, index or cancellation token.");
                order.Add("prepare");
                return Task.FromResult(command);
            }));
            Set("_copyCommandToClipboard", new Action<string>(value => { order.Add("copy"); copied.Add(value); }));
            Pump(Begin("CopyServerCommandAsync"));
            if (!order.SequenceEqual(["prepare", "copy"]) || !copied.SequenceEqual([command]) ||
                preview.Text != command || !status.Text.Contains("not executed") || start.Enabled)
                throw new Exception("Server command was not freshly prepared before the explicit fake clipboard write.");
            AssertIdle();

            // Both button availability and handler entry must reject stale or
            // foreign selections, even when a foreign record shares a valid index.
            foreach (var foreign in new[]
            {
                serverImage with { Index = 99 },
                serverImage with { Name = "Foreign metadata with the same index" },
                serverImage with { Language = "es-ES" }
            })
            {
                Populate(serverSource, foreign);
                if (copy.Enabled) throw new Exception("Foreign/stale/incompatible Server image retained an enabled Copy command.");
                var calls = order.Count;
                Pump(Begin("CopyServerCommandAsync"));
                if (copied.Count != 1 || order.Count != calls)
                    throw new Exception("A stale/private Copy invocation reached preparation or clipboard for a foreign image.");
                AssertIdle();
            }

            var unsafeSource = serverSource with { Root = @"X:\%TEMP%\server-fixture", SetupPath = @"X:\%TEMP%\server-fixture\setup.exe",
                ImagePath = @"X:\%TEMP%\server-fixture\sources\install.wim" };
            Populate(unsafeSource, serverImage);
            Set("_prepareServerCommand", new Func<MountedSource, int, CancellationToken, Task<string>>((source, _, _) =>
                Task.FromResult(ManualServerUpgrade.BuildCommand(source, serverImage, server))));
            Pump(Begin("CopyServerCommandAsync"));
            if (copied.Count != 1 || !Field<Label>("_operationNotice").Visible ||
                !status.Text.Contains("absolute source paths", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Rejected command paths reached the clipboard or hid the actionable failure.");
            AssertIdle();

            Populate(serverSource, serverImage);
            var pendingCommand = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken commandToken = default;
            Set("_prepareServerCommand", new Func<MountedSource, int, CancellationToken, Task<string>>((_, _, token) =>
            { commandToken = token; return pendingCommand.Task; }));
            var copyTask = Begin("CopyServerCommandAsync");
            if (copyTask.IsCompleted || !commandToken.CanBeCanceled || !start.Text.Contains("CANCEL", StringComparison.OrdinalIgnoreCase))
                throw new Exception("The asynchronous manual-command fixture did not start a cancellable operation.");
            Field<CancellationTokenSource>("_operation").Cancel();
            pendingCommand.SetResult(command);
            Pump(copyTask);
            if (!commandToken.IsCancellationRequested || copied.Count != 1 ||
                !status.Text.Contains("Nothing was copied", StringComparison.OrdinalIgnoreCase) || start.Enabled)
                throw new Exception("Cancellation during manual preparation allowed a late clipboard write or Setup action.");
            AssertIdle();

            preview.Text = command;
            sources.Text = @"X:\different-server-fixture";
            Call("ExistingPathChanged");
            if (Field<MountedSource?>("_preparedSource") is not null || images.Items.Count != 0 || preview.Text != "" || copy.Enabled)
                throw new Exception("Changing Server source retained a stale image/preview/copy action.");
            AssertNoSetup();
            Console.WriteLine("PASS: Independent GUI browser guidance, Spanish manual alternatives, persistent instructions, cancellation/failure recovery and Server manual-copy-only source/index/clipboard guards. All network/browser/clipboard/command preparation effects mocked; no media/Setup used.");
        }
        finally
        {
            form?.Dispose();
            SynchronizationContext.SetSynchronizationContext(priorContext);
        }
    }

    private static void Pump(Task task)
    {
        var elapsed = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(5))
                throw new Exception("GUI guidance fixture exceeded its bounded UI-pump timeout.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
        Application.DoEvents();
    }

    // With no surrounding Application.Run, an outermost DoEvents can tear down
    // WinForms' implicit thread marshalling control. A WindowsFormsSynchronizationContext
    // captured before that teardown can then strand later asynchronous callbacks.
    // Bind this test context to the actual form's lifetime/BeginInvoke queue instead;
    // continuations still run on the real WinForms UI thread, never a thread pool.
    private sealed class FormPumpContext : SynchronizationContext
    {
        private readonly Control _owner;
        private readonly int _threadId = Environment.CurrentManagedThreadId;

        internal FormPumpContext(Control owner)
        {
            _owner = owner;
            _ = owner.Handle;
        }

        public override SynchronizationContext CreateCopy() => this;

        public override void Post(SendOrPostCallback callback, object? state) =>
            _owner.BeginInvoke(new Action(() => InvokeCallback(callback, state)));

        public override void Send(SendOrPostCallback callback, object? state)
        {
            if (Environment.CurrentManagedThreadId == _threadId) InvokeCallback(callback, state);
            else _owner.Invoke(new Action(() => InvokeCallback(callback, state)));
        }

        private void InvokeCallback(SendOrPostCallback callback, object? state)
        {
            if (Environment.CurrentManagedThreadId != _threadId)
                throw new Exception("A GUI fixture continuation escaped the real WinForms UI thread.");
            var previous = Current;
            SetSynchronizationContext(this);
            try { callback(state); }
            finally { SetSynchronizationContext(previous); }
        }
    }
}
