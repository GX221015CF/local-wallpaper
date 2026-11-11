using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal sealed class VideoCoverBinding {
    public int Version { get; set; }
    public string ResourceSha256 { get; set; }
    public string ImageRelative { get; set; }
    public string ImageSha256 { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Method { get; set; }
    public string CreatedAt { get; set; }
}

internal static class VideoCovers {
    internal static int FrameAttempts;internal static long FrameImageBytes;
    internal const string Kind = "video-auto-frame-r30";
    static string Folder(string hash) {
        if (!PackageAnalysis.HashText(hash)) throw new InvalidDataException("视频封面需要完整资源身份");
        return Path.Combine(Common.Root, "data", "video-covers", hash);
    }

    internal static string Resolve(string hash) {
        try {
            string folder = Folder(hash), meta = Path.Combine(folder, "binding.json");
            LocalImport.NoLinks(folder);
            if (!File.Exists(meta) || new FileInfo(meta).Length > 8192) return null;
            LocalImport.NoLinks(meta);
            var binding = Common.Json.Deserialize<VideoCoverBinding>(Common.ReadShared(meta));
            if (binding.Version != 1 || binding.ResourceSha256 != hash || binding.Method != Kind
                || binding.Width < 1 || binding.Height < 1 || binding.Width > 360 || binding.Height > 240
                || Path.IsPathRooted(binding.ImageRelative)) return null;
            string image = Path.GetFullPath(Path.Combine(Common.Root, binding.ImageRelative));
            if (!LocalImport.Under(image, folder)) return null;
            LocalImport.NoLinks(image);
            if (!File.Exists(image) || new FileInfo(image).Length > 1048576
                || Common.Hash(image) != binding.ImageSha256) return null;
            var size = CoverCache.ImageDimensions(File.ReadAllBytes(image));
            if (size.Width != binding.Width || size.Height != binding.Height) return null;
            return image;
        } catch { return null; }
    }

    internal static string ForItem(WallpaperItem item) {
        return PreviewCovers.IdentityMatches(item) ? Resolve(item.RegisteredSha256) : null;
    }

    internal static string Stamp(WallpaperItem item) {
        if (item == null || !PackageAnalysis.HashText(item.RegisteredSha256)) return "none";
        try {
            string metadata = Path.Combine(Folder(item.RegisteredSha256), "binding.json");
            return File.Exists(metadata) ? File.GetLastWriteTimeUtc(metadata).Ticks.ToString() : "none";
        } catch { return "none"; }
    }

    internal static string Ensure(ImportCandidate candidate, string source, string hash, Action taskCheck) {
        candidate.Cover = null;
        candidate.CoverReason = "正在读取视频画面";
        var clock = Stopwatch.StartNew();
        Action check = () => {
            taskCheck();
            if (clock.Elapsed.TotalSeconds > 10) throw new TimeoutException("视频取帧超过10秒预算");
        };
        string temporary = null, newImage = null, metadataTemp = null;
        bool committed = false, temporaryCreated = false, imageCreated = false, metadataCreated = false;
        try {
            check();
            string cached = Resolve(hash);
            if (cached != null) {
                candidate.Cover = cached;
                candidate.CoverCacheHit = true;
            } else {
                if (String.IsNullOrEmpty(CoverCache.WorkFolder)
                    || !LocalImport.Under(CoverCache.WorkFolder, Common.WorkRoot))
                    throw new InvalidDataException("视频取帧临时目录不属于当前检查任务");
                LocalImport.NoLinks(source);
                RuntimeInputs.Safe(CoverCache.WorkFolder);
                Directory.CreateDirectory(CoverCache.WorkFolder);
                string id = Guid.NewGuid().ToString("N").Substring(0, 12);
                temporary = Path.Combine(CoverCache.WorkFolder, "v-" + id + ".mp4");
                FrameAttempts++;VideoPackage.DecodeForCover(source, temporary, hash, check);
                temporaryCreated = true;
                using (var frame = VideoFrameDecoder.Decode(temporary, check)) {
                    FrameImageBytes+=(long)frame.Width*frame.Height*4;
                    double scale = Math.Min(1, Math.Min(360.0 / frame.Width, 240.0 / frame.Height));
                    int width = Math.Max(1, (int)Math.Round(frame.Width * scale));
                    int height = Math.Max(1, (int)Math.Round(frame.Height * scale));
                    string folder = Folder(hash);
                    RuntimeInputs.Safe(folder);
                    Directory.CreateDirectory(folder);
                    newImage = Path.Combine(folder, "f-" + id + ".jpg");
                    metadataTemp = Path.Combine(folder, id + ".tmp");
                    if (newImage.Length >= 260 || metadataTemp.Length >= 260)
                        throw new PathTooLongException("数据位置过长，视频封面未保存");
                    using (var thumbnail = new Bitmap(width, height))
                    using (var graphics = Graphics.FromImage(thumbnail)) {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(frame, 0, 0, width, height);
                        check();
                        using (var file = new FileStream(newImage, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                            imageCreated = true;
                            thumbnail.Save(file, ImageFormat.Jpeg);
                        }
                    }
                    if (new FileInfo(newImage).Length > 1048576)
                        throw new InvalidDataException("视频封面超过1MiB预算");
                    var binding = new VideoCoverBinding {
                        Version = 1, ResourceSha256 = hash,
                        ImageRelative = newImage.Substring(Common.Root.TrimEnd('\\').Length + 1),
                        ImageSha256 = Common.Hash(newImage), Width = width, Height = height,
                        Method = Kind, CreatedAt = DateTimeOffset.Now.ToString("o")
                    };
                    Common.SaveNew(metadataTemp, binding);
                    metadataCreated = true;
                    check();
                    string meta = Path.Combine(folder, "binding.json");
                    RuntimeInputs.Safe(meta);
                    if (File.Exists(meta)) File.Replace(metadataTemp, meta, null);
                    else File.Move(metadataTemp, meta);
                    committed = true;
                    candidate.Cover = newImage;
                }
            }
            candidate.CoverSourceSha256 = hash;
            candidate.CoverMember = "视频自动取帧";
            candidate.CoverReason = "视频画面 · 自动截取，用于识别壁纸";
            return candidate.Cover;
        } catch (OperationCanceledException) { throw; }
        catch (Exception error) {
            taskCheck();
            candidate.CoverReason = "视频图片暂未取得：" + error.Message + "；仍可按检查结果选择导入";
            return null;
        } finally {
            if (temporaryCreated && File.Exists(temporary)) File.Delete(temporary);
            if (metadataCreated && File.Exists(metadataTemp)) File.Delete(metadataTemp);
            if (!committed && imageCreated && File.Exists(newImage)) File.Delete(newImage);
        }
    }
}
