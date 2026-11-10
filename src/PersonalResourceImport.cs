using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

// Uses the normal managed-library transaction. No source store or confirmation is modified.
internal static class PersonalResourceImport {
#if TEST_BUILD
    internal static Action TestBeforeCommit;
#endif
    internal static WallpaperItem Import(WallpaperLibrary library, WallpaperItem source, CancellationToken token, out bool duplicate) {
        var clock = Stopwatch.StartNew();
        Action check = () => {
            token.ThrowIfCancellationRequested();
            if (clock.Elapsed.TotalSeconds > 45) throw new TimeoutException("个人资源接入超过45秒，未报告成功。");
        };
        check();
        if (String.IsNullOrWhiteSpace(source.SourcePath) || !Path.IsPathRooted(source.SourcePath) || source.SourcePath.StartsWith(@"\\"))
            throw new InvalidDataException("个人资源必须是明确的本地绝对文件路径。");
        // The data-root length budget reserves space for future children. A read-only
        // source file uses the existing importer file-path rules instead.
        string path = Path.GetFullPath(source.SourcePath);
        LocalImport.NoLinks(path);
        var file = new FileInfo(path);
        if (file.Length > 256L * 1048576) throw new InvalidDataException("资源超过单项256MiB接入预算。");
        string signature = CoverCache.Signature(path);
        string hash = LocalImport.HashCancelable(path, check);
        if (!PackageAnalysis.HashText(source.RegisteredSha256) || hash != source.RegisteredSha256)
            throw new InvalidDataException("现存资源与原登记身份不符，未接入。");
        string format = LocalImport.CheckHeader(path);
        CwpPolicy.Validate(path, hash, check);
        if (format == "CWP-v2") CoverCache.ValidateV2Directory(path, check);
        string id = String.IsNullOrWhiteSpace(source.ResourceId) ? "file-" + hash.Substring(0, 16) : source.ResourceId;
        var candidate = new ImportCandidate {
            Id = id, Name = source.Name, NameSource = source.NameSource, Source = path,
            Record = source.SourceRecordPath, Bytes = file.Length, Format = format
        };
        string operation = Path.Combine(Common.WorkRoot, "personal-" + Guid.NewGuid().ToString("N"));
        string payload = Path.Combine(operation, "payload");
        Directory.CreateDirectory(payload);
        try {
            string copy = Path.Combine(payload, "resource" + VideoPackage.ResourceExtension(format));
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                byte[] buffer = new byte[262144];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0) {
                    check();
                    if (output.Length + read > file.Length) throw new InvalidDataException("复制时源文件增长。");
                    output.Write(buffer, 0, read);
                }
                output.Flush(true);
            }
            if (CoverCache.Signature(path) != signature || LocalImport.HashCancelable(copy, check) != hash)
                throw new InvalidDataException("复制后资源身份不符，未接入。");
            // Validate the owned copy as well. Images remain local personal material.
            CwpPolicy.Validate(copy, hash, check);
            if (format == "CWP-v2") CoverCache.ValidateV2Directory(copy, check);
            if (!String.IsNullOrWhiteSpace(source.CoverPath) && File.Exists(source.CoverPath)) {
                LocalImport.NoLinks(source.CoverPath);
                if (new FileInfo(source.CoverPath).Length > 8L * 1048576) throw new InvalidDataException("原封面超出8MiB读取预算。");
                using (var stream = File.OpenRead(source.CoverPath))
                using (var image = Image.FromStream(stream)) {
                    if ((long)image.Width * image.Height > 32000000) throw new InvalidDataException("原封面像素超出预算。");
                    image.Save(Path.Combine(payload, "cover.jpg"), ImageFormat.Jpeg);
                }
            }
            var receipt = new ImportReceipt {
                Candidate = candidate, Sha256 = hash, Bytes = file.Length, Format = format,
                ImportedAt = DateTimeOffset.Now.ToString("o"), Cover = File.Exists(Path.Combine(payload, "cover.jpg")) ? "cover.jpg" : null
            };
            Common.SaveNew(Path.Combine(payload, "source.json"), receipt);
            string existing = Path.Combine(Common.Root, "library", id, hash, id + VideoPackage.ResourceExtension(format));
            bool verified = File.Exists(existing) && LocalImport.HashCancelable(existing, check) == hash;
#if TEST_BUILD
            if (TestBeforeCommit != null) TestBeforeCommit();
#endif
            check();
            var item = ManagedCatalog.Commit(library, receipt, operation, verified, check, out duplicate);
            LocalImport.CleanPayload(operation);
            return item;
        } catch (Exception original) {
            try { LocalImport.CleanPayload(operation); }
            catch (Exception cleanup) { throw new IOException("接入失败，临时材料清理未确认。", new AggregateException(original, cleanup)); }
            throw;
        }
    }
}
