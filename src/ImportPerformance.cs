using System;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
internal sealed class ImportPerformance
{
    sealed class Stage
    {
        internal int Calls;
        internal double Milliseconds,Longest;
    }
    sealed class Sample : IDisposable
    {
        readonly ImportPerformance owner;
        readonly string name;
        readonly Stopwatch clock=Stopwatch.StartNew();
        internal Sample(ImportPerformance owner,string name)
        {
            this.owner=owner;
            this.name=name;
        }
        public void Dispose()
        {
            owner.Record(name,clock.Elapsed.TotalMilliseconds);
        }
    }
    readonly object sync=new object();
    readonly Dictionary<string,Stage> stages=new Dictionary<string,Stage>();
    readonly Dictionary<string,double> marks=new Dictionary<string,double>();
    readonly Stopwatch lifetime=Stopwatch.StartNew();
    internal long ImageReads,ImageHits,ImageFailures,ListRebuilds,LayoutPasses,RowUpdates;
    string lastImageFailure;
    internal void ImageFailure(string kind)
    {
        Interlocked.Increment(ref ImageFailures);
        lock(sync)lastImageFailure=kind;
    }
    internal IDisposable Measure(string name)
    {
        return new Sample(this,name);
    }
    void Record(string name,double milliseconds)
    {
        lock(sync)
        {
            Stage stage;
            if(!stages.TryGetValue(name,out stage))stages[name]=stage=new Stage();
            stage.Calls++;
            stage.Milliseconds+=milliseconds;
            stage.Longest=Math.Max(stage.Longest,milliseconds);
        }
    }
    internal void Mark(string name)
    {
        lock(sync)
        {
            if(!marks.ContainsKey(name))marks[name]=lifetime.Elapsed.TotalMilliseconds;
        }
    }
    internal object Snapshot()
    {
        lock(sync)
        {
            return new
            {
                stages=stages.OrderBy(row=>row.Key).Select(row=>new
                {
                    name=row.Key,calls=row.Value.Calls,totalMilliseconds=row.Value.Milliseconds,longestMilliseconds=row.Value.Longest
                }
                ).ToArray(),marks=new Dictionary<string,double>(marks),imageReads=Interlocked.Read(ref ImageReads),imageHits=Interlocked.Read(ref ImageHits),imageFailures=Interlocked.Read(ref ImageFailures),lastImageFailure=lastImageFailure,listRebuilds=Interlocked.Read(ref ListRebuilds),layoutPasses=Interlocked.Read(ref LayoutPasses),rowUpdates=Interlocked.Read(ref RowUpdates)
            };
        }
    }
}
