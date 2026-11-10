#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

internal static class PersonalExperienceRegression {
    static readonly List<string> checks = new List<string>();
    static void Need(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
    static void Reject<T>(Action action, string name) where T : Exception {
        bool rejected = false;
        try { action(); } catch (T) { rejected = true; }
        Need(rejected, name);
    }
    static void Components(string outer) {
        string components = Path.Combine(outer, "components");
        Directory.CreateDirectory(components);
        string file = Path.Combine(components, "synthetic.dll");
        File.WriteAllText(file, "owned synthetic identity fixture; never loaded");
        RuntimeProvisioning.TestContractRows = new Dictionary<string, string> { { "synthetic.dll", Common.Hash(file) } };
        string config = Path.Combine(Common.Root, "data", "runtime-location.json");
        var found = RuntimeDiscovery.Check(new[] { components, components }, CancellationToken.None);
        Need(found.State == "candidate-ready" && found.Candidates.Count == 1, "bounded-discovery-deduplicates-locations");
        Need(!RuntimeProvisioning.Available && !File.Exists(config), "discovery-never-adopts-or-enables-playback");
        RuntimeProvisioning.SelectAndSave(components);
        string saved = Common.Hash(config);
        var ready = RuntimeDiscovery.Check(new string[0], CancellationToken.None);
        Need(ready.State == "saved-ready" && RuntimeProvisioning.Available, "saved-location-reverified-on-open");
        Need(Common.Hash(config) == saved, "automatic-check-does-not-rewrite-config");
        using (var cancel = new CancellationTokenSource()) {
            cancel.Cancel();
            Reject<OperationCanceledException>(() => RuntimeDiscovery.Check(new[] { components }, cancel.Token), "cancel-discovery-has-normal-cancellation-path");
        }
        Need(Common.Hash(config) == saved, "cancellation-preserves-saved-config");
        File.WriteAllText(file, "changed unknown component version");
        var changed = RuntimeDiscovery.Check(new[] { components }, CancellationToken.None);
        Need(!RuntimeProvisioning.Available && changed.State == "version-mismatch", "changed-component-rejected-and-explained");
        Need(Common.Hash(config) == saved, "version-failure-keeps-original-config");
        File.Delete(file);
        var missing = RuntimeDiscovery.Check(new[] { components }, CancellationToken.None);
        Need(missing.State == "missing-files" && !RuntimeProvisioning.Available, "missing-files-distinguished-from-missing-install");
        File.WriteAllText(config, "invalid-config");
        var invalid = RuntimeDiscovery.Check(new string[0], CancellationToken.None);
        Need(invalid.State == "saved-invalid" && !RuntimeProvisioning.Available, "invalid-saved-configuration-is-visible");
        File.Delete(config);
        Directory.CreateDirectory(config);
        var occupied = RuntimeDiscovery.Check(new string[0], CancellationToken.None);
        Need(occupied.State == "saved-invalid" && !RuntimeProvisioning.Available, "directory-at-config-path-is-not-treated-as-first-use");
        Directory.Delete(config);
        var absent = RuntimeDiscovery.Check(new[] { Path.Combine(outer, "not-installed") }, CancellationToken.None);
        Need(absent.State == "not-found", "unknown-install-location-is-not-claimed-compatible");
        RuntimeDiscovery.TestBudgetMilliseconds = 40;
        RuntimeDiscovery.TestBeforeVerify = token => { token.WaitHandle.WaitOne(200); token.ThrowIfCancellationRequested(); };
        try { Reject<TimeoutException>(() => RuntimeDiscovery.Check(new[] { components }, CancellationToken.None), "automatic-check-timeout-finishes-with-explicit-result"); }
        finally { RuntimeDiscovery.TestBudgetMilliseconds = 30000; RuntimeDiscovery.TestBeforeVerify = null; }
        Need(!File.Exists(config), "timed-out-discovery-does-not-save-config");
        Need(RuntimeDiscovery.KnownRoots().Count() <= 3, "production-discovery-has-at-most-three-standard-roots");
        RuntimeProvisioning.TestContractRows = null;
    }
    static void Resources(string outer) {
        string fixtureFolder = Path.Combine(outer, "resources");
        Directory.CreateDirectory(fixtureFolder);
        var fixture = typeof(MultiWallpaperRegression).GetMethod("Fixture", BindingFlags.NonPublic | BindingFlags.Static);
        string path = (string)fixture.Invoke(null, new object[] { fixtureFolder, "owned", (byte)5 });
        string second = (string)fixture.Invoke(null, new object[] { fixtureFolder, "second", (byte)9 });
        var library = new WallpaperLibrary();
        library.Doc.Items.Clear(); library.Doc.SelectedId = null; library.Save();
        var source = new WallpaperItem { ResourceId = "owned", Name = "自有夹具", SourcePath = path, RegisteredSha256 = Common.Hash(path), PreviewConfirmedSha256 = Common.Hash(path) };
        bool duplicate;
        var item = PersonalResourceImport.Import(library, source, CancellationToken.None, out duplicate);
        Need(!duplicate && library.Doc.Items.Count == 1, "actual-managed-commit-registers-personal-copy");
        Need(LocalImport.Under(item.SourcePath, Path.Combine(Common.Root, "library")) && Common.Hash(item.SourcePath) == source.RegisteredSha256, "personal-resource-is-owned-and-identity-exact");
        Need(item.PreviewConfirmedSha256 == null && item.DesktopConfirmedSha256 == null, "old-confirmations-never-unconditionally-transferred");
        Need(File.Exists(path) && Common.Hash(path) == source.RegisteredSha256, "source-resource-remains-intact");
        PersonalResourceImport.Import(library, source, CancellationToken.None, out duplicate);
        Need(duplicate && library.Doc.Items.Count == 1, "repeat-ingress-deduplicates-same-content");
        Need(!Directory.GetDirectories(Common.WorkRoot, "payload", SearchOption.AllDirectories).Any(), "repeat-ingress-cleans-owned-payload");
        string longFolder = Path.Combine(outer, new string('s', 230 - outer.Length - "retained.cwp".Length - 2));
        Directory.CreateDirectory(longFolder);
        string longFile = Path.Combine(longFolder, "retained.cwp");
        File.Copy(path, longFile);
        source.SourcePath = longFile;
        PersonalResourceImport.Import(library, source, CancellationToken.None, out duplicate);
        Need(longFile.Length > 220 && duplicate && library.Doc.Items.Count == 1, "long-retained-file-is-not-rejected-by-data-root-budget");
        source.SourcePath = path;
        var before = Common.Hash(library.FilePath);
        source.RegisteredSha256 = new string('a', 64);
        Reject<InvalidDataException>(() => PersonalResourceImport.Import(library, source, CancellationToken.None, out duplicate), "changed-registered-resource-rejected");
        Need(Common.Hash(library.FilePath) == before, "failed-ingress-keeps-catalog-bytes");
        source.SourcePath = second; source.ResourceId = "second"; source.RegisteredSha256 = Common.Hash(second);
        using (var cancel = new CancellationTokenSource()) {
            PersonalResourceImport.TestBeforeCommit = () => cancel.Cancel();
            try { Reject<OperationCanceledException>(() => PersonalResourceImport.Import(library, source, cancel.Token, out duplicate), "cancel-before-commit-rejects-pending-resource"); }
            finally { PersonalResourceImport.TestBeforeCommit = null; }
        }
        Need(Common.Hash(library.FilePath) == before && library.Doc.Items.Count == 1, "cancelled-commit-has-no-false-card");
        Need(!Directory.GetDirectories(Common.WorkRoot, "payload", SearchOption.AllDirectories).Any(), "cancel-cleans-only-owned-staging");
        var secondItem = PersonalResourceImport.Import(library, source, CancellationToken.None, out duplicate);
        Need(!duplicate && library.Doc.Items.Count == 2 && item.SourcePath != secondItem.SourcePath, "two-resource-identities-remain-distinct");
        library.Select(secondItem);
        Need(library.Selected.Id == secondItem.Id && item.PreviewConfirmedSha256 == null, "selection-is-independent-from-confirmation");
    }
    static async Task Commands() {
        var coordinator = new PlaybackSwitch();
        var prepared = new TaskCompletionSource<string[]>();
        int launches = 0;
        var first = coordinator.Run(() => prepared.Task, () => true, data => false, () => false, () => Task.FromResult(true), data => launches++, CancellationToken.None);
        string repeated = await coordinator.Run(() => Task.FromResult(new string[0]), () => true, data => false, () => false, () => Task.FromResult(true), data => launches++, CancellationToken.None);
        Need(repeated == "busy" && launches == 0, "repeat-click-cannot-create-second-launch");
        prepared.SetResult(new[] { "synthetic" });
        Need(await first == "launch-requested" && launches == 1 && !coordinator.Busy, "async-completion-releases-real-switch-gate");
        int opens = 0, stops = 0, exits = 0;
        var stopping = new TaskCompletionSource<bool>();
        var tray = new TrayCommands(() => { opens++; return true; }, async () => { stops++; await stopping.Task; }, () => exits++);
        var state = TrayState.For(false, false, true, false, "desktop", "A");
        Need(tray.Open(state) && opens == 1, "tray-show-routes-to-existing-controller-callback");
        var pending = tray.Stop(state); await tray.Stop(state);
        Need(stops == 1, "repeated-tray-stop-is-serialized");
        stopping.SetResult(true); await pending; tray.Exit(state);
        Need(exits == 1, "tray-exit-reuses-close-callback");
        Need(MultiWallpaperPresentation.Selection("B", "A", "A", true, "已选B").Contains("只选择卡片不会切换"), "selection-and-active-status-remain-distinct");
    }
    static int Main(string[] args) {
        try {
            if (args.Length > 1 && args[1] == "reopen") {
                var library = new WallpaperLibrary();
                Need(library.Doc.Items.Count == 2 && library.Selected.ResourceId == "second", "independent-process-restores-personal-catalog-selection");
                Need(library.Doc.Items.All(item => File.Exists(item.SourcePath) && Common.Hash(item.SourcePath) == item.RegisteredSha256), "independent-process-checks-owned-resource-identity");
                Need(library.Doc.Items.All(item => item.PreviewConfirmedSha256 == null), "independent-reopen-does-not-create-confirmations");
            } else {
                string outer = Directory.GetParent(Common.AppRoot).FullName;
                Components(outer); Resources(outer); Commands().GetAwaiter().GetResult();
            }
            Common.SaveNew(args[0], new { passed = true, checks = checks, evidenceKind = "TEST_BUILD actual handlers, synthetic components/resources, no GUI or engine" });
            return 0;
        } catch (Exception error) {
            Common.SaveNew(args[0], new { passed = false, checks = checks, error = error.ToString() });
            return 1;
        }
    }
}
#endif
