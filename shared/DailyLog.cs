using System;using System.IO;using System.Text;using System.Collections.Generic;using System.Web.Script.Serialization;
// Critical records stay in the base JSONL. Only heartbeat diagnostics use a bounded ring.
internal sealed class DailyLog:IDisposable {
 internal const int DefaultPartBytes=8192,DefaultKeep=4;
 readonly StreamWriter audit;readonly HeartbeatParts parts;readonly object gate=new object();readonly JavaScriptSerializer json=new JavaScriptSerializer();long seq,suppressed;string reportedError;bool diagnosticsDisabled;
 internal DailyLog(string path):this(path,DefaultPartBytes,DefaultKeep){}
 internal DailyLog(string path,int bytes,int keep){audit=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read|FileShare.Delete),new UTF8Encoding(false));parts=new HeartbeatParts(path+".hb",bytes,keep);}
 internal void Write(string phase,object data,long? ms=null){lock(gate){
  var row=new Dictionary<string,object>{{"local",DateTimeOffset.Now.ToString("o")},{"seq",++seq},{"phase",phase},{"data",data}};if(ms.HasValue)row["ms"]=ms.Value;string line=json.Serialize(row);
  if(phase=="heartbeat"||phase=="worker-heartbeat"){
   if(diagnosticsDisabled){suppressed++;return;}
   try{parts.Write(line);}catch(IOException ex){DisableDiagnostics(ex);}catch(UnauthorizedAccessException ex){DisableDiagnostics(ex);}
   if(parts.LastError!=null&&parts.LastError!=reportedError){reportedError=parts.LastError;audit.WriteLine(json.Serialize(new{local=DateTimeOffset.Now.ToString("o"),seq=++seq,phase="log-retention-warning",data=new{error=reportedError}}));audit.Flush();}
  }else{audit.WriteLine(line);audit.Flush();}
 }}
 void DisableDiagnostics(Exception ex){diagnosticsDisabled=true;suppressed++;audit.WriteLine(json.Serialize(new{local=DateTimeOffset.Now.ToString("o"),seq=++seq,phase="heartbeat-diagnostics-disabled",data=new{error=ex.Message,controlHeartbeatIndependent=true}}));audit.Flush();}
 public void Dispose(){lock(gate){if(diagnosticsDisabled){audit.WriteLine(json.Serialize(new{local=DateTimeOffset.Now.ToString("o"),seq=++seq,phase="heartbeat-diagnostics-summary",data=new{suppressed=suppressed}}));audit.Flush();}parts.Dispose();audit.Dispose();}}
}
internal sealed class HeartbeatParts:IDisposable {
 readonly string directory;readonly int maximum,keep;FileStream stream;int index;internal string LastError;
 internal HeartbeatParts(string dir,int bytes,int count){directory=Path.GetFullPath(dir);maximum=Math.Max(128,bytes);keep=Math.Max(1,count);if(Directory.Exists(directory))throw new IOException("Existing heartbeat stream preserved: "+directory);}
 internal void Write(string line){byte[] b=Encoding.UTF8.GetBytes(line+"\n");if(stream==null||stream.Length>0&&stream.Length+b.Length>maximum)Rotate();stream.Write(b,0,b.Length);stream.Flush();}
 void Rotate(){
  if(stream!=null)stream.Dispose();if(index==0){Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,".owned.txt"),"Owned heartbeat diagnostics; only numbered .jsonl parts in this new stream are retained.");}
  ++index;stream=new FileStream(Path.Combine(directory,index.ToString("D8")+".jsonl"),FileMode.CreateNew,FileAccess.Write,FileShare.Read|FileShare.Delete,1);
  foreach(string file in Directory.GetFiles(directory,"????????.jsonl")){
   int n;if(Int32.TryParse(Path.GetFileNameWithoutExtension(file),out n)&&n>0&&n<=index-keep){try{File.Delete(file);}catch(IOException ex){LastError=ex.Message;}catch(UnauthorizedAccessException ex){LastError=ex.Message;}}
  }
 }
 public void Dispose(){if(stream!=null)stream.Dispose();}
}
