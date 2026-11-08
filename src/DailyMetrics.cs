using System;using System.IO;using System.Diagnostics;
internal sealed partial class ControlWindow {
 void WriteDailyMetrics(){
  if(metricsClock.ElapsedMilliseconds<10000)return;metricsClock.Restart();string path=Path.Combine(Session,"ui-metrics.json"),temp=path+".tmp";
  try{File.WriteAllText(temp,Common.Json.Serialize(new{local=DateTimeOffset.Now.ToString("o"),state=State,fullHashCalls=Common.HashCalls,fullHashBytes=Common.HashBytes,fullHashMs=(double)Common.HashTicks*1000/Stopwatch.Frequency,displayHashCacheHits=library.HintHits,historyCharacters=history.TextLength,historyRevision=historyRevision,eventBytesRead=eventTail==null?0:eventTail.BytesRead,hostBytesRead=hostTail==null?0:hostTail.BytesRead,eventReadRetries=eventTail==null?0:eventTail.ReadFailures,hostReadRetries=hostTail==null?0:hostTail.ReadFailures}));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}catch(Exception ex){Write("metrics-write-error",new{error=ex.Message});}
 }
}
