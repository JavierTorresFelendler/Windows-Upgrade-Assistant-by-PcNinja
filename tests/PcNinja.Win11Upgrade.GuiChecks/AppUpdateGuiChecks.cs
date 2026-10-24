using System.Diagnostics;
using System.Reflection;
using PcNinja.Win11Upgrade.Core;
using PcNinja.Win11UpgradeAssistant;

internal static class AppUpdateGuiChecks
{
    internal static void Run()
    {
        var previousContext = SynchronizationContext.Current;
        Form? form = null;
        try
        {
            var assembly = Assembly.Load("Windows-Upgrade-Assistant-By-PcNinja");
            var type = assembly.GetType("PcNinja.Win11UpgradeAssistant.MainForm", true)!;
            form = (Form)Activator.CreateInstance(type, nonPublic: true)!;
            var context = new FormPumpContext(form);
            SynchronizationContext.SetSynchronizationContext(context);
            T Field<T>(string name) => (T)type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
            void Set(string name, object? value) => type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, value);
            object? Call(string name, params object?[] args) => type.GetMethod(name,
                BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, args);
            Task Begin(string name, params object?[] args)
            {
                SynchronizationContext.SetSynchronizationContext(context);
                return (Task)Call(name, args)!;
            }

            var failures = new List<string>();
            var confirmations = new List<string>();
            var windowsConfirmations = 0;
            var installs = 0;
            Set("_automaticAppUpdateCheck", false);
            Set("_offerAppUpdates", false);
            Set("_confirmUpgrade", new Func<string, string, bool>((_, _) => { windowsConfirmations++; return false; }));
            Set("_showAppUpdateFailure", new Action<string>(failures.Add));
            Set("_confirmAppUpdate", new Func<string, bool>(message => { confirmations.Add(message); return false; }));
            Set("_installAppUpdate", new Func<AppUpdateManifest, IProgress<AppUpdateProgress>, CancellationToken, Task<AppUpdateHandoffResult>>((_, _, _) =>
            { installs++; throw new Exception("Unexpected install: only an explicitly configured fake may run."); }));
            Set("_checkAppUpdate", new Func<CancellationToken, Task<AppUpdateCheckResult>>(_ =>
                throw new Exception("Unexpected check: the automatic fixture check must be disabled.")));
            form.Show();
            Application.DoEvents();

            var installed = CurrentWindows.Read();
            var image = new InstallationImage(6, "Media fixture", installed.Edition, "x64",
                $"10.0.{Math.Max(26300, installed.Build)}", "Client", installed.Language);
            var source = new MountedSource(@"X:\app-update-fixture", @"X:\app-update-fixture\setup.exe",
                @"X:\app-update-fixture\sources\install.wim", [image]);
            var existing = Field<RadioButton>("_existingMedia");
            var sources = Field<ComboBox>("_sources");
            var images = Field<ComboBox>("_installationImages");
            existing.Checked = true;
            sources.Text = @"X:\app-update-fixture.iso";
            Set("_preparedSource", source);
            Set("_preparedKey", sources.Text);
            images.Items.Clear();
            images.Items.Add(image);
            images.SelectedItem = image;
            Call("UpdateImageInfo");
            Call("UpdateSourceControls");
            var status = Field<Label>("_status");
            var notice = Field<Label>("_operationNotice");
            var details = Field<LinkLabel>("_details");
            var link = Field<LinkLabel>("_appUpdatesLink");
            var progress = Field<ProgressBar>("_progress");
            var start = Field<Button>("_start");
            const string mediaStatus = "Fixture Windows media diagnostic remains visible.";
            const string mediaNotice = "Fixture media warning, not an app-update notice.";
            const string mediaDetails = "Fixture media diagnostics.";
            status.Text = mediaStatus;
            notice.Text = mediaNotice;
            notice.Visible = true;
            details.Visible = true;
            Set("_lastErrorDetails", mediaDetails);
            var restart = Field<CheckBox>("_autoRestart").Checked;
            var quiet = Field<CheckBox>("_quietSetup").Checked;
            var assetName = AppUpdateIdentity.AssetNameFor("9.9.9");
            var release = new AppUpdateManifest(1, AppUpdateIdentity.Product, AppUpdateIdentity.Repository, "stable",
                "9.9.9", "v9.9.9", assetName,
                $"https://github.com/{AppUpdateIdentity.Repository}/releases/download/v9.9.9/{assetName}",
                123456, new string('a', 64), $"https://github.com/{AppUpdateIdentity.Repository}/releases/tag/v9.9.9");
            CheckOfferDialogs(release);

            void AssertMediaPreserved()
            {
                if (!ReferenceEquals(Field<MountedSource?>("_preparedSource"), source) ||
                    Field<string?>("_preparedKey") != @"X:\app-update-fixture.iso" || sources.Text != @"X:\app-update-fixture.iso" ||
                    !ReferenceEquals(images.SelectedItem, image) || !existing.Checked || status.Text != mediaStatus ||
                    notice.Text != mediaNotice || !notice.Visible || !details.Visible || Field<string?>("_lastErrorDetails") != mediaDetails ||
                    Field<CheckBox>("_autoRestart").Checked != restart || Field<CheckBox>("_quietSetup").Checked != quiet ||
                    Field<bool>("_setupHasStarted") || windowsConfirmations != 0)
                    throw new Exception("App-update UI changed Windows media, diagnostics, restart consent or entered Setup.");
            }
            void AssertIdle()
            {
                if (Field<CancellationTokenSource?>("_operation") is not null || Field<CancellationTokenSource?>("_appUpdateCheck") is not null ||
                    !link.Enabled || progress.Visible || Field<bool>("_closingForAppUpdate"))
                    throw new Exception("App-update UI did not recover its idle state.");
                AssertMediaPreserved();
            }
            void Available()
            {
                Set("_checkAppUpdate", new Func<CancellationToken, Task<AppUpdateCheckResult>>(token =>
                {
                    if (!token.CanBeCanceled) throw new Exception("Discovery did not use a cancellable token.");
                    return Task.FromResult(new AppUpdateCheckResult(AppUpdateCheckState.Available, "Available fixture", release));
                }));
                Pump(Begin("CheckAppUpdatesAsync", true));
                if (!ReferenceEquals(Field<AppUpdateManifest?>("_availableAppUpdate"), release) || !link.Text.Contains(release.Version))
                    throw new Exception("Available application discovery did not expose the exact release.");
                AssertIdle();
            }

            Available();
            Pump(Begin("InstallAppUpdateAsync"));
            if (confirmations.Count != 1 || installs != 0 || !confirmations[0].Contains("No Windows upgrade or computer restart") ||
                !confirmations[0].Contains("original portable copy", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Declined app-update consent entered installation or omitted its app-only destination contract.");
            AssertIdle();

            Set("_offerAppUpdates", true);
            Available();
            if (confirmations.Count != 2 || installs != 0)
                throw new Exception("New-version offer did not require consent before any download action.");
            Set("_offerAppUpdates", false);

            foreach (var state in new[] { AppUpdateCheckState.UpToDate, AppUpdateCheckState.NoStableRelease, AppUpdateCheckState.Unavailable })
            {
                Set("_checkAppUpdate", new Func<CancellationToken, Task<AppUpdateCheckResult>>(_ =>
                    Task.FromResult(new AppUpdateCheckResult(state, "Fixture " + state))));
                Pump(Begin("CheckAppUpdatesAsync", true));
                if (Field<AppUpdateManifest?>("_availableAppUpdate") is not null || failures.Count != 0)
                    throw new Exception("No-update/offline discovery retained a candidate or showed an error popup.");
                AssertIdle();
            }
            Set("_checkAppUpdate", new Func<CancellationToken, Task<AppUpdateCheckResult>>(_ =>
                Task.FromException<AppUpdateCheckResult>(new IOException("Fixture check failure"))));
            Pump(Begin("CheckAppUpdatesAsync", true));
            if (!link.Text.Contains("unavailable", StringComparison.OrdinalIgnoreCase) || failures.Count != 0)
                throw new Exception("Manual discovery failure was hidden or displayed a blocking message.");
            AssertIdle();

            // A delayed discovery cannot publish a stale result after Windows work
            // begins. Busy cancellation must preserve the independent media notice.
            var pendingCheck = new TaskCompletionSource<AppUpdateCheckResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken checkToken = default;
            Set("_checkAppUpdate", new Func<CancellationToken, Task<AppUpdateCheckResult>>(token =>
            { checkToken = token; return pendingCheck.Task; }));
            var discovery = Begin("CheckAppUpdatesAsync", true);
            using (var windowsOperation = new CancellationTokenSource())
            {
                Set("_operation", windowsOperation);
                Call("SetBusy", true);
                pendingCheck.SetResult(new AppUpdateCheckResult(AppUpdateCheckState.Available, "Late fixture", release));
                Pump(discovery);
                if (!checkToken.IsCancellationRequested || Field<AppUpdateManifest?>("_availableAppUpdate") is not null || link.Enabled)
                    throw new Exception("Late app discovery escaped cancellation or enabled updating during Windows work.");
                Set("_checkAppUpdate", new Func<CancellationToken, Task<AppUpdateCheckResult>>(_ =>
                    throw new Exception("Busy discovery made an HTTP-equivalent call.")));
                Pump(Begin("CheckAppUpdatesAsync", true));
                Set("_operation", null);
                Call("SetBusy", false);
            }
            AssertIdle();

            Available();
            Set("_confirmAppUpdate", new Func<string, bool>(_ => true));
            var pendingInstall = new TaskCompletionSource<AppUpdateHandoffResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken installToken = default;
            IProgress<AppUpdateProgress>? capturedProgress = null;
            Set("_installAppUpdate", new Func<AppUpdateManifest, IProgress<AppUpdateProgress>, CancellationToken, Task<AppUpdateHandoffResult>>((candidate, updates, token) =>
            {
                if (!ReferenceEquals(candidate, release)) throw new Exception("App install lost the exact discovered candidate.");
                installs++;
                capturedProgress = updates;
                installToken = token;
                return pendingInstall.Task;
            }));
            var installation = Begin("InstallAppUpdateAsync");
            if (installation.IsCompleted || !installToken.CanBeCanceled || link.Enabled || !progress.Visible ||
                !start.Text.Contains("CANCEL", StringComparison.OrdinalIgnoreCase))
                throw new Exception("App installation did not enter the existing cancellable busy UI.");
            capturedProgress!.Report(new(10, 20));
            Application.DoEvents();
            if (progress.Value != 50) throw new Exception("App-update progress did not reach the GUI.");
            Pump(Begin("StartOrCancelAsync"));
            if (!installToken.IsCancellationRequested) throw new Exception("The existing Cancel action did not cancel app installation.");
            pendingInstall.SetCanceled(installToken);
            Pump(installation);
            if (!link.Text.Contains("cancelled", StringComparison.OrdinalIgnoreCase) || failures.Count != 0)
                throw new Exception("Cancelled app installation showed failure or failed to offer retry.");
            capturedProgress.Report(new(20, 20));
            Application.DoEvents();
            AssertIdle();

            Set("_installAppUpdate", new Func<AppUpdateManifest, IProgress<AppUpdateProgress>, CancellationToken, Task<AppUpdateHandoffResult>>((_, _, _) =>
                Task.FromException<AppUpdateHandoffResult>(new IOException("Fixture install failure"))));
            Pump(Begin("InstallAppUpdateAsync"));
            if (failures.Count != 1 || !failures[0].Contains("Fixture install failure") ||
                !link.Text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                !link.AccessibleDescription!.Contains("Fixture install failure"))
                throw new Exception("App install failure was hidden or bypassed the injected message sink.");
            AssertIdle();

            var closed = 0;
            form.FormClosing += (_, _) =>
            {
                if (!Field<bool>("_closingForAppUpdate") || Field<CancellationTokenSource?>("_operation") is not null ||
                    Field<bool>("_setupHasStarted") || windowsConfirmations != 0)
                    throw new Exception("App handoff closed with an active Windows operation or without its close flag.");
                closed++;
            };
            Set("_installAppUpdate", new Func<AppUpdateManifest, IProgress<AppUpdateProgress>, CancellationToken, Task<AppUpdateHandoffResult>>((candidate, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(candidate, release)) throw new Exception("Handoff lost the discovered candidate.");
                return Task.FromResult(new AppUpdateHandoffResult(release.Version, @"C:\Fixture-only\" + assetName,
                    Guid.NewGuid().ToString("N"), 12345, "Fixture handoff only; no helper process started."));
            }));
            Pump(Begin("InstallAppUpdateAsync"));
            if (closed != 1 || !form.IsDisposed || form.Visible || Field<CancellationTokenSource?>("_operation") is not null || failures.Count != 1)
                throw new Exception("Successful app-only handoff did not close cleanly exactly once.");
            Console.WriteLine("PASS: GUI app discovery/media-diagnostic preservation, late-result cancellation, explicit consent, mocked cancel/failure recovery/progress and handoff-only close. No HTTP/helper/install/Setup/browser/clipboard or real message box.");
        }
        finally
        {
            form?.Dispose();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void Pump(Task task)
    {
        var elapsed = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (elapsed.Elapsed > TimeSpan.FromSeconds(5)) throw new Exception("GUI app-update fixture exceeded its bounded UI-pump timeout.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
        task.GetAwaiter().GetResult();
        Application.DoEvents();
    }

    private static void CheckOfferDialogs(AppUpdateManifest release)
    {
        foreach (var critical in new[] { false, true })
        {
            var pages = new List<string>();
            var candidate = release with { Importance = critical ? "critical" : "normal", Message = "Publisher summary fixture" };
            using var offer = MainForm.CreateAppUpdateOffer(candidate, candidate.Message, pages.Add);
            IEnumerable<Control> Children(Control owner) => owner.Controls.Cast<Control>()
                .SelectMany(child => new[] { child }.Concat(Children(child)));
            var controls = Children(offer).ToArray();
            var badge = controls.OfType<Label>().Single(label => label is not LinkLabel);
            if (!badge.Text.StartsWith(critical ? "CRITICAL UPDATE" : "REGULAR UPDATE") ||
                !controls.OfType<TextBox>().Single().Text.Contains(candidate.Message) ||
                !controls.OfType<Button>().Any(button => button.Text == "Download and run the new version") ||
                ((Button)offer.CancelButton!).Text != "Not now" || !ReferenceEquals(offer.AcceptButton, offer.CancelButton))
                throw new Exception("Regular/critical offer lost its badge, summary, wording or safe default.");
            var notes = controls.OfType<LinkLabel>().Single();
            typeof(LinkLabel).GetMethod("OnLinkClicked", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(notes, [new LinkLabelLinkClickedEventArgs(notes.Links[0])]);
            if (!pages.SequenceEqual(new[] { candidate.ReleaseNotesUrl }) || offer.DialogResult != DialogResult.None)
                throw new Exception("Release notes did not open the exact GitHub release or implied download consent.");
        }
    }

    private sealed class FormPumpContext(Control owner) : SynchronizationContext
    {
        private readonly int _threadId = Environment.CurrentManagedThreadId;
        public override SynchronizationContext CreateCopy() => this;
        public override void Post(SendOrPostCallback callback, object? state) =>
            owner.BeginInvoke(new Action(() => Invoke(callback, state)));
        public override void Send(SendOrPostCallback callback, object? state)
        {
            if (Environment.CurrentManagedThreadId == _threadId) Invoke(callback, state);
            else owner.Invoke(new Action(() => Invoke(callback, state)));
        }
        private void Invoke(SendOrPostCallback callback, object? state)
        {
            if (Environment.CurrentManagedThreadId != _threadId) throw new Exception("App-update fixture continuation escaped the UI thread.");
            var previous = Current;
            SetSynchronizationContext(this);
            try { callback(state); }
            finally { SetSynchronizationContext(previous); }
        }
    }
}
