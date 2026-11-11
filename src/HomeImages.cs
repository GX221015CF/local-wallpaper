using System;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

internal sealed partial class ControlWindow
{
    readonly ImportPerformance homePerformance = new ImportPerformance();
    readonly List<Task> homeImageTasks = new List<Task>();
    ImportImageCache homeImages;
    CancellationTokenSource homeImageRequest;
    string homeImageIdentity;
    bool homeImagesStopped;
    Task homeImageShutdown;

    void RefreshHomeImage()
    {
        if (homeImagesStopped) return;
        string path = WallpaperLibrary.CoverLocation(selected);
        string identity = selected == null ? "" : selected.Id + "|" + selected.RegisteredSha256 + "|" + path;
        try { if (path != null) identity += "|" + CoverCache.Signature(path); }
        catch (Exception) { identity += "|unavailable"; }
        if (identity == homeImageIdentity) return;
        homeImageIdentity = identity;
        if (homeImageRequest != null) homeImageRequest.Cancel();
        if (selectedImage.Image != null) { selectedImage.Image.Dispose(); selectedImage.Image = null; }
        if (path == null || selected == null) { homeImageRequest = null; return; }
        if (homeImages == null) homeImages = new ImportImageCache(homePerformance, true);
        var cancellation = new CancellationTokenSource();
        homeImageRequest = cancellation;
        string itemId = selected.Id;
        homeImageTasks.RemoveAll(task => task.IsCompleted);
        homeImageTasks.Add(LoadHomeImage(path, identity, itemId, cancellation));
    }

    async Task LoadHomeImage(string path, string identity, string itemId, CancellationTokenSource cancellation)
    {
        Bitmap image = null;
        try
        {
            image = await homeImages.GetAsync(path, new Size(640, 480), cancellation.Token);
            if (closing || IsDisposed || homeImagesStopped || cancellation.IsCancellationRequested
                || selected == null || selected.Id != itemId || homeImageIdentity != identity) return;
            if (selectedImage.Image != null) selectedImage.Image.Dispose();
            selectedImage.Image = image;
            image = null;
            selectedImage.AccessibleDescription = selectedImage.Image == null ? "封面暂未取得，壁纸选择保持" : "当前选中壁纸的封面";
            LayoutSide();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { homePerformance.ImageFailure(error.GetType().Name); }
        finally
        {
            if (image != null) image.Dispose();
            if (Object.ReferenceEquals(homeImageRequest, cancellation)) homeImageRequest = null;
            cancellation.Dispose();
        }
    }

    internal async Task WaitForHomeImages()
    {
        while (homeImageTasks.Any(task => !task.IsCompleted)) await Task.WhenAll(homeImageTasks.ToArray());
    }

    internal Task StopHomeImages()
    {
        if (homeImageShutdown != null) return homeImageShutdown;
        homeImagesStopped = true;
        if (homeImageRequest != null) homeImageRequest.Cancel();
        if (homeImages != null) homeImages.Dispose();
        homeImageShutdown = FinishHomeImages();
        return homeImageShutdown;
    }

    async Task FinishHomeImages()
    {
        await WaitForHomeImages();
        if (homeImages != null) await homeImages.Cleanup;
        if (selectedImage.Image != null) { selectedImage.Image.Dispose(); selectedImage.Image = null; }
        if (!IsDisposed && Log != null) Write("home-image-summary", homePerformance.Snapshot());
    }
}
