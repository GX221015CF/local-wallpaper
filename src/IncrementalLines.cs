using System;using System.IO;using System.Text;using System.Collections.Generic;using System.Web.Script.Serialization;
// Stable critical stream plus complete-record heartbeat segments. Partial UTF8 bytes persist.
internal sealed class IncrementalLines:IDisposable {
 readonly ByteTail audit;readonly string directory;ByteTail heartbeat;int part;
 internal long SkippedHeartbeatParts,LookupFailures;internal long ReadFailures{get{return LookupFailures+audit.ReadFailures+(heartbeat==null?0:heartbeat.ReadFailures);}}internal long BytesRead{get{return audit.BytesRead+(heartbeat==null?0:heartbeat.BytesRead);}}
 internal IncrementalLines(string path){audit=new ByteTail(path);directory=path+".hb";}
 string Part(int n){return Path.Combine(directory,n.ToString("D8")+".jsonl");}
 int AvailableAfter(int n){if(!Directory.Exists(directory))return 0;int lowest=Int32.MaxValue;try{foreach(string file in Directory.GetFiles(directory,"????????.jsonl")){int i;if(Int32.TryParse(Path.GetFileNameWithoutExtension(file),out i)&&i>n&&i<lowest)lowest=i;}}catch(IOException){LookupFailures++;}catch(UnauthorizedAccessException){LookupFailures++;}return lowest==Int32.MaxValue?0:lowest;}
 internal List<Dictionary<string,object>> Pull(){
  var result=audit.Pull();if(heartbeat==null){int first=File.Exists(Part(1))?1:AvailableAfter(0);if(first>0){part=first;heartbeat=new ByteTail(Part(part));}}
  if(heartbeat!=null){for(int turns=0;turns<8;turns++){
   result.AddRange(heartbeat.Pull());int next=File.Exists(Part(part+1))?part+1:AvailableAfter(part);if(next==0)break;
   if(next>part+1)SkippedHeartbeatParts+=next-part-1;part=next;heartbeat.ChangePath(Part(part));
  }}result.Sort((a,b)=>Seq(a).CompareTo(Seq(b)));return result;
 }
 static long Seq(Dictionary<string,object> row){object v;return row.TryGetValue("seq",out v)?Convert.ToInt64(v):0;}
 public void Dispose(){audit.Dispose();if(heartbeat!=null)heartbeat.Dispose();}
 sealed class ByteTail:IDisposable {
  string path;FileStream stream;long position;readonly MemoryStream pending=new MemoryStream();readonly byte[] buffer=new byte[8192];readonly JavaScriptSerializer json=new JavaScriptSerializer();internal long BytesRead,ReadFailures;
  internal ByteTail(string p){path=p;}
  internal void ChangePath(string p){if(stream!=null)stream.Dispose();stream=null;path=p;position=0;}
  internal List<Dictionary<string,object>> Pull(){
   var result=new List<Dictionary<string,object>>();if(stream==null){if(!File.Exists(path))return result;try{stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);stream.Position=position;}catch(IOException){ReadFailures++;if(stream!=null)stream.Dispose();stream=null;return result;}catch(UnauthorizedAccessException){ReadFailures++;return result;}}
   while(true){int count;try{count=stream.Read(buffer,0,buffer.Length);}catch(IOException){ReadFailures++;stream.Dispose();stream=null;break;}if(count==0)break;position+=count;BytesRead+=count;for(int i=0;i<count;i++){
    if(buffer[i]==10){string line=Encoding.UTF8.GetString(pending.ToArray()).TrimEnd('\r');pending.SetLength(0);try{result.Add(json.Deserialize<Dictionary<string,object>>(line));}catch{}}
    else{if(pending.Length>4194304)throw new InvalidDataException("Oversized log line");pending.WriteByte(buffer[i]);}
   }}return result;
  }
  public void Dispose(){if(stream!=null)stream.Dispose();pending.Dispose();}
 }
}
