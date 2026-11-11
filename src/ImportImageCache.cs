using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
// This cache owns decoded bitmaps. Every caller receives a separate, disposable bitmap.
internal sealed class ImportImageCache : IDisposable
{
    sealed class Entry
    {
        internal Bitmap Image;
        internal long Bytes;
        internal LinkedListNode<string> Node;
    }
    readonly object sync=new object();
    readonly SemaphoreSlim decodeGate=new SemaphoreSlim(2);
    readonly CancellationTokenSource lifetime=new CancellationTokenSource();
    readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
    readonly LinkedList<string> order=new LinkedList<string>();
    readonly HashSet<Task> pending=new HashSet<Task>();
    readonly ImportPerformance performance;
    readonly bool personalCover;
    long bytes;
    bool disposed;
    int activeDecoders,maximumDecoders;
    internal Task Cleanup=Task.FromResult(0);
#if TEST_BUILD
    internal static Action<string,CancellationToken> TestBeforeDecode;
#endif
    internal int EntryCount
    {
        get
        {
            lock(sync)return entries.Count;
        }
    }
    internal int PendingCount
    {
        get
        {
            lock(sync)return pending.Count;
        }
    }
    internal long RetainedBytes
    {
        get
        {
            lock(sync)return bytes;
        }
    }
    internal int MaximumDecoders
    {
        get
        {
            return maximumDecoders;
        }
    }
    internal ImportImageCache(ImportPerformance performance):this(performance,false){}
    internal ImportImageCache(ImportPerformance performance,bool personalCover)
    {
        this.performance=performance;
        this.personalCover=personalCover;
    }
    internal Task<Bitmap> GetAsync(string path,Size size,CancellationToken token)
    {
        Task<Bitmap> task;
        lock(sync)
        {
            if(disposed)return Task.FromResult<Bitmap>(null);
            task=Task.Run(()=>Load(path,size,token));
            pending.Add(task);
        }
        task.ContinueWith(done=>
        {
            lock(sync)pending.Remove(done);
        }
        ,CancellationToken.None,TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        return task;
    }
    async Task<Bitmap> Load(string path,Size size,CancellationToken token)
    {
        using(var linked=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token))
        {
            var cancel=linked.Token;
            await decodeGate.WaitAsync(cancel).ConfigureAwait(false);
            lock(sync)
            {
                activeDecoders++;
                maximumDecoders=Math.Max(maximumDecoders,activeDecoders);
            }
            try
            {
                using(performance.Measure("worker.image"))
                {
                    cancel.ThrowIfCancellationRequested();
                    if(String.IsNullOrEmpty(path)||!File.Exists(path))
                    {
                        performance.ImageFailure("missing-image");
                        return null;
                    }
                    LocalImport.NoLinks(path);
                    string signature=CoverCache.Signature(path);
                    string key=path.ToUpperInvariant()+"|"+signature+"|"+size.Width+"x"+size.Height;
                    lock(sync)
                    {
                        Entry cached;
                        if(entries.TryGetValue(key,out cached))
                        {
                            order.Remove(cached.Node);
                            order.AddLast(cached.Node);
                            Interlocked.Increment(ref performance.ImageHits);
                            return new Bitmap(cached.Image);
                        }
                    }
                    byte[] data;
#if TEST_BUILD
                    if(TestBeforeDecode!=null)TestBeforeDecode(path,cancel);
#endif
                    using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                    {
                        if(stream.Length>(personalCover?20971520:8388608))throw new InvalidDataException("图片超过当前显示读取预算");
                        data=new byte[checked((int)stream.Length)];
                        int offset=0,count;
                        while(offset<data.Length&&(count=stream.Read(data,offset,Math.Min(32768,data.Length-offset)))>0)
                        {
                            cancel.ThrowIfCancellationRequested();
                            offset+=count;
                        }
                        if(offset!=data.Length)throw new EndOfStreamException("导入图片读取期间变化");
                    }
                    Interlocked.Increment(ref performance.ImageReads);
                    Size dimensions=personalCover?Size.Empty:CoverCache.ImageDimensions(data);
                    Bitmap scaled=null;
                    try
                    {
                        using(var memory=new MemoryStream(data,false))using(var image=Image.FromStream(memory,true,true))
                        {
                            cancel.ThrowIfCancellationRequested();
                            if(personalCover){if((long)image.Width*image.Height>40000000)throw new InvalidDataException("个人封面像素过大");}
                            else if(image.Width!=dimensions.Width||image.Height!=dimensions.Height)throw new InvalidDataException("导入图片尺寸不一致");
                            double ratio=Math.Min(1,Math.Min((double)size.Width/image.Width,(double)size.Height/image.Height));
                            scaled=new Bitmap(Math.Max(1,(int)Math.Round(image.Width*ratio)),Math.Max(1,(int)Math.Round(image.Height*ratio)));
                            using(var graphics=Graphics.FromImage(scaled))
                            {
                                graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
                                graphics.DrawImage(image,0,0,scaled.Width,scaled.Height);
                            }
                        }
                        cancel.ThrowIfCancellationRequested();
                        if(CoverCache.Signature(path)!=signature)throw new IOException("导入图片读取期间变化");
                        lock(sync)
                        {
                            if(disposed)throw new OperationCanceledException();
                            Entry prior;
                            if(entries.TryGetValue(key,out prior))
                            {
                                Interlocked.Increment(ref performance.ImageHits);
                                return new Bitmap(prior.Image);
                            }
                            var entry=new Entry
                            {
                                Image=scaled,Bytes=(long)scaled.Width*scaled.Height*4,Node=order.AddLast(key)
                            };
                            entries.Add(key,entry);
                            bytes+=entry.Bytes;
                            scaled=null;
                            while(entries.Count>96||bytes>8*1048576)
                            {
                                string oldest=order.First.Value;
                                var old=entries[oldest];
                                order.RemoveFirst();
                                entries.Remove(oldest);
                                bytes-=old.Bytes;
                                old.Image.Dispose();
                            }
                            return new Bitmap(entry.Image);
                        }
                    }
                    finally
                    {
                        if(scaled!=null)scaled.Dispose();
                    }
                }
            }
            catch(OperationCanceledException)
            {
                throw;
            }
            catch(Exception error)
            {
                performance.ImageFailure(error.GetType().Name);
                return null;
            }
            finally
            {
                lock(sync)activeDecoders--;
                decodeGate.Release();
            }
        }
    }
    public void Dispose()
    {
        Task[] tasks;
        lock(sync)
        {
            if(disposed)return;
            disposed=true;
            lifetime.Cancel();
            tasks=pending.ToArray();
            foreach(var entry in entries.Values)entry.Image.Dispose();
            entries.Clear();
            order.Clear();
            bytes=0;
        }
        Cleanup=Finish(tasks);
    }
    async Task Finish(Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch(OperationCanceledException)
        {
        }
        finally
        {
            decodeGate.Dispose();
            lifetime.Dispose();
        }
    }
}
