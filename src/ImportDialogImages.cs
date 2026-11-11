using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
internal sealed partial class ControlWindow
{
    sealed partial class ImportDialog
    {
        sealed class ThumbnailRequest
        {
            internal ListViewItem Row;
            internal string Identity,Path;
        }
        ImportImageCache imageCache;
        readonly CancellationTokenSource imageLifetime=new CancellationTokenSource();
        CancellationTokenSource imageGeneration=new CancellationTokenSource(),detailCancellation;
        Queue<ThumbnailRequest> imageQueue=new Queue<ThumbnailRequest>();
        readonly Dictionary<ListViewItem,string> thumbnailIdentities=new Dictionary<ListViewItem,string>();
        readonly List<Task> imageTasks=new List<Task>();
        int imageVersion;
        bool imagePumpRunning,imagesStopped;
        string requestedDetail;
        Task imageShutdown=Task.FromResult(0);
        string PictureIdentity(ImportCandidate candidate)
        {
            return candidate==null?"":ImportCandidateSearch.Key(candidate)+"|"+candidate.Cover+"|"+(candidate.Check==null?"":candidate.Check.CoverSha256);
        }
        void BeginImageGeneration()
        {
            var previous=imageGeneration;
            previous.Cancel();
            var release=Task.WhenAll(imageTasks.ToArray()).ContinueWith(done=>previous.Dispose(),TaskScheduler.Default);
            TrackImageTask(release);
            imageGeneration=CancellationTokenSource.CreateLinkedTokenSource(imageLifetime.Token);
            imageVersion++;
            imagePumpRunning=false;
            imageQueue=new Queue<ThumbnailRequest>();
            thumbnailIdentities.Clear();
            requestedDetail=null;
            if(detailCancellation!=null)detailCancellation.Cancel();
        }
        void TrackImageTask(Task task)
        {
            imageTasks.RemoveAll(item=>item.IsCompleted);
            imageTasks.Add(task);
        }
        void Thumbnail(ListViewItem row)
        {
            using(performance.Measure("ui.thumbnail"))
            {
                if(imagesStopped||IsDisposed)return;
                var candidate=(ImportCandidate)row.Tag;
                string identity=PictureIdentity(candidate),prior;
                if(thumbnailIdentities.TryGetValue(row,out prior)&&prior==identity)return;
                row.ImageKey="";
                if(prior!=null)thumbs.Images.RemoveByKey(prior);
                thumbnailIdentities[row]=identity;
                if(candidate.Cover==null)return;
                imageQueue.Enqueue(new ThumbnailRequest
                {
                    Row=row,Identity=identity,Path=candidate.Cover
                }
                );
                if(!imagePumpRunning)
                {
                    var task=FillThumbnails(imageVersion,imageQueue,imageGeneration.Token);
                    TrackImageTask(task);
                }
            }
        }
        async Task FillThumbnails(int version,Queue<ThumbnailRequest> queue,CancellationToken token)
        {
            imagePumpRunning=true;
            try
            {
                while(queue.Count>0)
                {
                    token.ThrowIfCancellationRequested();
                    var request=queue.Dequeue();
                    using(var image=await imageCache.GetAsync(request.Path,new Size(64,40),token))
                    {
                        if(image==null||IsDisposed||imagesStopped||token.IsCancellationRequested||version!=imageVersion)continue;
                        string latest;
                        if(!thumbnailIdentities.TryGetValue(request.Row,out latest)||latest!=request.Identity||!candidateRows.Contains(request.Row))continue;
                        using(performance.Measure("ui.image-submit"))
                        {
                            int existing=thumbs.Images.IndexOfKey(request.Identity);
                            if(existing>=0)
                            {
                                thumbs.Images[existing]=image;
                                thumbs.Images.SetKeyName(existing,request.Identity);
                            }
                            else thumbs.Images.Add(request.Identity,image);
                            request.Row.ImageKey=request.Identity;
                        }
                    }
                }
            }
            catch(OperationCanceledException)
            {
            }
            catch(Exception error)
            {
                performance.ImageFailure(error.GetType().Name);
            }
            finally
            {
                if(version==imageVersion)imagePumpRunning=false;
            }
        }
        void UpdateDetailImage(ImportCandidate candidate)
        {
            string identity=PictureIdentity(candidate);
            if(identity==requestedDetail||imagesStopped)return;
            requestedDetail=identity;
            if(detailCancellation!=null)detailCancellation.Cancel();
            if(cover.Image!=null)
            {
                cover.Image.Dispose();
                cover.Image=null;
            }
            if(candidate==null||candidate.Cover==null)
            {
                detailCancellation=null;
                return;
            }
            detailCancellation=CancellationTokenSource.CreateLinkedTokenSource(imageGeneration.Token);
            cover.AccessibleDescription="正在加载所选候选的识别图片";
            TrackImageTask(LoadDetail(candidate,identity,candidate.Cover,detailCancellation));
        }
        async Task LoadDetail(ImportCandidate candidate,string identity,string path,CancellationTokenSource cancellation)
        {
            var token=cancellation.Token;
            Bitmap image=null;
            try
            {
                image=await imageCache.GetAsync(path,new Size(480,300),token);
                if(IsDisposed||imagesStopped||token.IsCancellationRequested||requestedDetail!=identity||Selected==null||PictureIdentity(Selected)!=identity)return;
                using(performance.Measure("ui.detail-submit"))
                {
                    if(cover.Image!=null)cover.Image.Dispose();
                    cover.Image=image;
                    image=null;
                    cover.AccessibleDescription=cover.Image==null?"图片暂未显示，可重新选择或刷新候选":"当前所选候选的识别图片";
                }
            }
            catch(OperationCanceledException)
            {
            }
            catch(Exception error)
            {
                performance.ImageFailure(error.GetType().Name);
            }
            finally
            {
                if(image!=null)image.Dispose();
                if(Object.ReferenceEquals(detailCancellation,cancellation))detailCancellation=null;
                cancellation.Dispose();
            }
        }
        internal async Task WaitForImages()
        {
            if(imagesStopped)
            {
                await imageShutdown;
                return;
            }
            while(imageTasks.Any(task=>!task.IsCompleted))
            {
                var tasks=imageTasks.ToArray();
                try
                {
                    await Task.WhenAll(tasks);
                }
                catch(OperationCanceledException)
                {
                }
            }
        }
        internal Task StopImageLoading()
        {
            if(imagesStopped)return imageShutdown;
            imagesStopped=true;
            imageLifetime.Cancel();
            imageGeneration.Cancel();
            if(detailCancellation!=null)detailCancellation.Cancel();
            imageQueue.Clear();
            imageCache.Dispose();
            imageShutdown=FinishImageLoading(imageTasks.ToArray());
            return imageShutdown;
        }
        async Task FinishImageLoading(Task[] tasks)
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch(OperationCanceledException)
            {
            }
            catch(Exception error)
            {
                performance.ImageFailure(error.GetType().Name);
            }
            await imageCache.Cleanup;
            imageGeneration.Dispose();
            if(detailCancellation!=null)detailCancellation.Dispose();
            imageLifetime.Dispose();
        }
    }
}
