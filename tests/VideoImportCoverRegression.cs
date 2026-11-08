#if TEST_BUILD
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Collections.Generic;

internal static class VideoImportCoverRegression {
    static readonly List<string> checks = new List<string>();
    static void Need(bool value, string name) {
        if (!value) throw new Exception(name);
        checks.Add(name);
    }

    static string Operation() {
        string operation = Path.Combine(Common.WorkRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(operation);
        CoverCache.WorkFolder = Path.Combine(operation, "cover-temp");
        return operation;
    }

    static void Exercise() {
        SourceLocation.SaveRoot(Path.Combine(Common.Root, "tests", "fixture-source"));
        var library = new WallpaperLibrary();
        library.Doc.Items.Clear();
        library.Save();
        var candidates = new List<ImportCandidate>();
        foreach (string id in new[] {"402242", "398417", "403178"}) {
            string operation = Operation();
            var candidate = LocalImport.ReadRecord(Path.Combine(Common.Root, "tests", "fixture-source", "cache_info", id + "_fixture.dat"));
            CandidateChecks.Run(candidate, () => {}, null, true);
            Need(CandidateChecks.Passed(candidate), id + "-actual-candidate-check-passes");
            Need(candidate.Cover != null && File.Exists(candidate.Cover), id + "-video-frame-present-before-import");
            Need(candidate.Check.CoverKind == VideoCovers.Kind, id + "-cover-provenance-is-auto-video-frame");
            using (var image = Image.FromFile(candidate.Cover))
                Need(image.Width <= 360 && image.Height <= 240, id + "-bounded-image-readable");
            Need(library.Doc.Items.Count == 0, id + "-candidate-does-not-import");
            Need(!Directory.GetFiles(operation, "*.mp4", SearchOption.AllDirectories).Any(), id + "-decoded-temporary-removed");
            var again = LocalImport.ReadRecord(candidate.Record);
            Need(CandidateChecks.Load(again, () => {}), id + "-candidate-check-cache-readable");
            Need(again.Cover == candidate.Cover, id + "-cached-candidate-retains-correct-image");
            candidates.Add(candidate);
            LocalImport.CleanPayload(operation);
        }
        Need(candidates.Select(c => c.Check.CoverSha256).Distinct().Count() == 3,
            "three-resources-have-distinct-image-identities");

        var selected = candidates[0];
        string copyOperation = Operation();
        using (var self = Process.GetCurrentProcess()) {
            Common.SaveNew(Path.Combine(copyOperation, "request.json"), new {
                mode = "copy", candidate = selected, parentPid = self.Id,
                parentCreation = ProcessProof.Creation(self.Handle).ToString()
            });
        }
        File.WriteAllText(Path.Combine(copyOperation, "go"), "");
        using (var worker = Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,
            "--local-import " + Common.Quote(copyOperation)) {UseShellExecute = false, CreateNoWindow = true})) {
            if (!worker.WaitForExit(20000)) throw new TimeoutException("Isolated import worker exceeded20s; left untouched");
            Need(worker.ExitCode == 0, "actual-import-worker-process-normal-exit");
        }
        var result = Common.Read(Path.Combine(copyOperation, "result.json"));
        var receipt = Common.Json.Deserialize<ImportReceipt>(Common.Json.Serialize(result["receipt"]));
        bool duplicate;
        var item = ManagedCatalog.Commit(library, receipt, copyOperation,
            Convert.ToBoolean(result["existingTargetVerified"]), () => {}, out duplicate);
        Need(!duplicate && item.RegisteredSha256 == selected.Check.Sha256, "actual-import-registers-same-video");
        Need(item.PreviewConfirmedSha256 == null && item.DesktopConfirmedSha256 == null, "import-does-not-confirm-playback");
        using (var card = WallpaperLibrary.LoadCover(item))
            Need(card != null, "actual-home-card-uses-auto-frame-after-import");
        Need(!PreviewCovers.HasIndependentCover(item), "auto-video-cover-keeps-manual-snapshot-available");
        library.Select(item);
        LocalImport.CleanPayload(copyOperation);

        string cancelOperation = Operation();
        Directory.CreateDirectory(CoverCache.WorkFolder);
        var cancelledCandidate = LocalImport.ReadRecord(candidates[1].Record);
        int calls = 0;
        bool cancelled = false;
        try {
            VideoPackage.DecodeForCover(cancelledCandidate.Source, Path.Combine(CoverCache.WorkFolder, "cancel.mp4"),
                candidates[1].Check.Sha256, () => { if (++calls == 2) throw new OperationCanceledException(); });
        } catch (OperationCanceledException) { cancelled = true; }
        Need(cancelled, "decode-copy-honors-cancellation");
        LocalImport.CleanPayload(cancelOperation);
        Need(!Directory.Exists(Path.Combine(cancelOperation, "cover-temp")), "cancelled-task-cleans-private-media");

        string failureOperation = Operation();
        string oldImage = VideoCovers.Resolve(selected.Check.Sha256);
        var bad = LocalImport.ReadRecord(selected.Record);
        string failed = VideoCovers.Ensure(bad, bad.Source, new string('a', 64), () => {});
        Need(failed == null && bad.CoverReason.Contains("身份不符"), "wrong-resource-identity-produces-no-image");
        Need(VideoCovers.Resolve(selected.Check.Sha256) == oldImage, "failed-frame-keeps-existing-resource-cover");
        Need(!Directory.GetFiles(failureOperation, "*.mp4", SearchOption.AllDirectories).Any(), "failed-frame-removes-private-media");
        LocalImport.CleanPayload(failureOperation);

        string unavailableOperation = Operation();
        string secondHash = candidates[1].Check.Sha256;
        string secondFolder = Path.GetDirectoryName(VideoCovers.Resolve(secondHash));
        Directory.Delete(secondFolder, true);
        VideoFrameDecoder.TestBeforeDecode = () => { throw new PlatformNotSupportedException("TEST_BUILD unavailable decoder"); };
        var unavailable = LocalImport.ReadRecord(candidates[1].Record);
        try { CandidateChecks.Run(unavailable, () => {}, null, true); }
        finally { VideoFrameDecoder.TestBeforeDecode = null; }
        Need(CandidateChecks.Passed(unavailable) && unavailable.Cover == null,
            "unavailable-decoder-keeps-validated-resource-importable");
        Need(unavailable.CoverReason.Contains("TEST_BUILD unavailable decoder"), "decoder-failure-has-accurate-cover-reason");
        Need(!Directory.GetFiles(unavailableOperation, "*.mp4", SearchOption.AllDirectories).Any(),
            "decoder-failure-cleans-decoded-video");
        LocalImport.CleanPayload(unavailableOperation);

        string cancellationOperation = Operation();
        VideoFrameDecoder.TestBeforeDecode = () => { throw new OperationCanceledException(); };
        bool decodeCancelled = false;
        try { VideoCovers.Ensure(unavailable, unavailable.Source, secondHash, () => {}); }
        catch (OperationCanceledException) { decodeCancelled = true; }
        finally { VideoFrameDecoder.TestBeforeDecode = null; }
        Need(decodeCancelled, "decode-stage-propagates-cancellation");
        Need(!Directory.GetFiles(cancellationOperation, "*.mp4", SearchOption.AllDirectories).Any(),
            "decode-cancellation-cleans-decoded-video");
        LocalImport.CleanPayload(cancellationOperation);

        string retryOperation = Operation();
        CandidateChecks.Run(unavailable, () => {}, null, true);
        Need(unavailable.Cover != null, "real-system-decoder-retry-recovers-cover");
        LocalImport.CleanPayload(retryOperation);

        string manualImage = Path.Combine(Common.Root, "manual-cover-fixture.jpg");
        using (var image = new Bitmap(120, 80)) {
            using (var graphics = Graphics.FromImage(image)) {
                graphics.Clear(Color.Purple);
                graphics.FillEllipse(Brushes.Gold, 10, 10, 60, 60);
            }
            image.Save(manualImage, ImageFormat.Jpeg);
        }
        string manualRun = Path.Combine(Common.LogRoot, "run-manual-cover-storage-test");
        Directory.CreateDirectory(manualRun);
        var target = new PreviewFrameTarget {
            Run = manualRun, Token = Guid.NewGuid().ToString("N"),
            CatalogId = item.Id, ResourceSha256 = item.RegisteredSha256
        };
        PreviewCovers.Publish(manualImage, target, Common.Hash(manualImage), 120, 80,
            "synthetic-manual-cover-storage-test", "TEST_BUILD", false, () => {});
        using (var card = WallpaperLibrary.LoadCover(item))
            Need(card != null && card.Width == 120, "user-selected-snapshot-takes-priority-over-automatic-frame");
        Need(VideoCovers.Resolve(item.RegisteredSha256) == oldImage, "manual-cover-keeps-auto-frame-available");
        Common.SaveNew(Path.Combine(Common.Root, "video-cover-test-state.json"), new {
            id = item.Id, hash = item.RegisteredSha256, autoCover = oldImage
        });
    }

    static void Reopen() {
        var state = Common.Read(Path.Combine(Common.Root, "video-cover-test-state.json"));
        var library = new WallpaperLibrary();
        var item = library.Doc.Items.Single();
        Need(item.Id == Common.S(state, "id") && library.Doc.SelectedId == item.Id,
            "new-process-retains-selected-import");
        Need(library.DisplayHash(item) == Common.S(state, "hash"), "new-process-resource-identity-matches");
        Need(VideoCovers.ForItem(item) != null, "new-process-retains-automatic-video-cover");
        using (var cover = WallpaperLibrary.LoadCover(item))
            Need(cover != null && cover.Width == 120, "new-process-retains-user-snapshot-priority");
        Need(item.PreviewConfirmedSha256 == null && item.DesktopConfirmedSha256 == null,
            "new-process-has-no-playback-confirmation");
    }

    static int Main(string[] args) {
        if (args[0] == "--local-import") return LocalImport.Worker(args[1]);
        try {
            if (args[0] == "exercise") Exercise();
            else Reopen();
            Common.SaveNew(args[1], new {passed = true, checks = checks,
                systemOfflineFrameDecoderExecuted = true, thirdPartyEngineLoaded = false,
                windowCreated = false, playbackStarted = false});
            return 0;
        } catch (Exception error) {
            Common.SaveNew(args[1], new {passed = false, checks = checks, error = error.ToString()});
            return 1;
        }
    }
}
#endif
