// Local records only. No URL is retained, requested or used. No engine loading here.
using System;using System.IO;using System.Text;using System.Linq;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Security.Cryptography;using System.Collections.Generic;using System.Windows.Forms;
internal sealed class ImportCandidate { public CandidateCheckResult Check{get;set;} public AnalysisReport Analysis{get;set;} public string AnalysisStatus{get;set;} public string AnalysisStage{get;set;} public string AnalysisError{get;set;} public string CacheDisposition{get;set;}public string Format{get;set;}public string SourceIssueKind{get;set;}
 public string FirstDiscoveredAt{get;set;}public string RecordUpdatedAt{get;set;}public string SourceModifiedAt{get;set;}public bool HistoricalAtFirstScan{get;set;}public bool NewlyDiscovered{get;set;}public string DiscoveryStatus{get;set;}public string DiscoveredSha256{get;set;}public string Id{get;set;} public string Name{get;set;} public NameEvidence NameSource{get;set;} public string Record{get;set;} public string Source{get;set;} public string Md5{get;set;} public string RecordHash{get;set;} public long Bytes{get;set;} public long Ticks{get;set;} public string Status{get;set;} public string Cover{get;set;} public string CoverReason{get;set;} public string CoverSourceSha256{get;set;} public string CoverMember{get;set;} public bool CoverCacheHit{get;set;}
}
internal sealed class ImportReceipt {
 public ImportCandidate Candidate{get;set;} public string Sha256{get;set;} public string Md5{get;set;} public string Cover{get;set;} public string Format{get;set;} public long Bytes{get;set;} public string ImportedAt{get;set;}
}
internal sealed class ImportCover {public string Md5{get;set;}public string Sha256{get;set;}public string Cover{get;set;}}
internal static class LocalImport {
 internal static string OfficialRoot{get{return SourceLocation.DefaultRoot;}}
 internal static string DataRoot{get{
#if TEST_BUILD
  string config=Path.Combine(Common.Root,"tests","import-root.txt");if(File.Exists(config))return Path.GetFullPath(File.ReadAllText(config).Trim());
#endif
  return SourceLocation.ResolveRoot();
 }}
 internal static bool Under(string path,string root){return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase);}
 internal static void NoLinks(string path){for(string p=Path.GetFullPath(path);!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)){if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("不读取链接或重解析路径");}}
 static bool Hex(string s,int n){return s!=null&&s.Length==n&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f'||c>='A'&&c<='F');}
 static byte[] SharedBytes(string p,int limit){using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(f.Length>limit)throw new InvalidDataException("本地记录过大");byte[] b=new byte[(int)f.Length];int n=0,k;while(n<b.Length&&(k=f.Read(b,n,b.Length-n))>0)n+=k;if(n!=b.Length||f.ReadByte()!=-1)throw new InvalidDataException("本地记录正在变化");return b;}}
 static string Digest(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
 internal static ImportCandidate ReadRecord(string record){
  string records=Path.Combine(DataRoot,"cache_info");if(!Under(record,records)||Path.GetDirectoryName(Path.GetFullPath(record))!=Path.GetFullPath(records))throw new InvalidDataException("不是已知联想记录目录");NoLinks(record);
  byte[] raw=SharedBytes(record,65536);string fingerprint=Digest(raw);for(int i=0;i<raw.Length;i++)raw[i]^=0xe7;if(raw.Length<6)throw new InvalidDataException("本地记录不完整");
  // Parse the existing record encoding, then immediately discard fields outside this allowlist.
  var d=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(raw,4,raw.Length-4));
  string id=Common.S(d,"wpId"),name=Common.S(d,"wpName"),md5=Common.S(d,"wpMd5").ToLowerInvariant();int type=Convert.ToInt32(d["wpType"]);
  bool video=type==1&&name.EndsWith(".mp4",StringComparison.OrdinalIgnoreCase);if(!video&&(type!=2||!name.EndsWith(".cwp",StringComparison.OrdinalIgnoreCase)))return null;string ext=video?".mp4":".cwp";
  if(id.Length==0||id.Length>12||!id.All(Char.IsDigit)||!Path.GetFileName(record).StartsWith(id+"_",StringComparison.Ordinal)||!Hex(md5,32)||!String.Equals(name,md5+ext,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("记录ID、MD5或文件名不符合已知规则");
  string source=Path.Combine(DataRoot,"wallpaper","wpcache","dynamic",video?"lxwp_"+name:name);var result=new ImportCandidate{Id=id,Name="本地壁纸 "+id,Record=record,RecordHash=fingerprint,Source=source,Md5=md5,Status="文件缺失"};
  if(File.Exists(source)){NoLinks(source);var f=new FileInfo(source);result.Bytes=f.Length;result.Ticks=f.LastWriteTimeUtc.Ticks;try{result.Format=CheckHeader(source);if(video!=VideoPackage.IsVideo(result.Format))throw new InvalidDataException("记录类型与资源内容不一致，未导入");result.Status="可导入 · 待完整校验";}catch(Exception ex){result.SourceIssueKind=ex is UnauthorizedAccessException?"read-failed":ex is IOException?"read-failed":ex is InvalidDataException&&ex.Message.IndexOf("不是CWP",StringComparison.Ordinal)>=0?"unsupported-format":"damaged-resource";result.Status="暂不可导入："+ex.Message;}}
  return result;
 }
 internal static string CheckHeader(string path){if(VideoPackage.Recognize(path))return VideoPackage.Format;using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var r=new BinaryReader(s)){if(s.Length<32||s.Length>UInt32.MaxValue)throw new InvalidDataException("文件长度异常");uint m=r.ReadUInt32(),n=r.ReadUInt32();if((m&0xffffff)!=0x505743)throw new InvalidDataException("不是CWP容器");if(n!=s.Length)throw new InvalidDataException("容器声明长度与文件不符，可能尚未下载完成");return "CWP-v"+(m>>24);}}
 internal static List<ImportCandidate> Discover(out int ignored,Action check=null){if(check==null)check=()=>{};var summary=ResourceDiscovery.Scan(true,check);var list=summary.Candidates;ignored=0;foreach(var c in list){check();if(!CandidateChecks.Load(c,check))c.CoverReason="已排队，后台自动检查并读取封面";}OfficialNames.Apply(list,check);return list;}
 internal static string FindCover(string md5,string sha){try{var covers=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<List<ImportCover>>(File.ReadAllText(Path.Combine(Common.Root,"data","import-covers.json")));var x=covers.FirstOrDefault(c=>c.Md5==md5&&(sha==null||c.Sha256==sha));if(x==null||Path.IsPathRooted(x.Cover))return null;string p=Path.GetFullPath(Path.Combine(Common.Root,x.Cover));return Under(p,Common.Root)&&File.Exists(p)?p:null;}catch{return null;}}
 internal static void CleanPayload(string op){foreach(string name in new[]{"payload","cover-temp"}){string p=Path.Combine(op,name);if(!Under(p,Common.WorkRoot))throw new InvalidOperationException("清理范围不符");if(Directory.Exists(p))Directory.Delete(p,true);}}
 internal static int Worker(string op){
  string workerStage="validate-operation-path";
  try{if(!Under(op,Common.WorkRoot))return 2;NoLinks(op);CoverCache.WorkFolder=Path.Combine(op,"cover-temp");var req=Common.Read(Path.Combine(op,"request.json"));int pid=Convert.ToInt32(req["parentPid"]);long creation=Convert.ToInt64(req["parentCreation"]);using(var parent=Process.GetProcessById(pid)){
   workerStage="verify-parent-process";ProcessProof.Verify(parent.Handle,pid,creation,System.Windows.Forms.Application.ExecutablePath);var watch=Stopwatch.StartNew();workerStage="wait-for-controller-go";while(!File.Exists(Path.Combine(op,"go"))){if(parent.HasExited||watch.Elapsed.TotalSeconds>5)return 3;Thread.Sleep(20);}
   Action check=()=>{if(File.Exists(Path.Combine(op,"cancel"))||parent.HasExited)throw new OperationCanceledException();if(watch.Elapsed.TotalMinutes>3)throw new TimeoutException("导入任务超过3分钟，请稍后重试");};
   #if TEST_BUILD
   if(req.ContainsKey("testChunkDelay"))CoverCache.TestDelay=Convert.ToInt32(req["testChunkDelay"]);
#endif
   if(Common.S(req,"mode")=="capture-frame"){var target=Common.Json.Deserialize<PreviewFrameTarget>(Common.Json.Serialize(req["capture"]));
#if TEST_BUILD
 if(req.ContainsKey("testChunkDelay")&&Convert.ToInt32(req["testChunkDelay"])>0)Thread.Sleep(Convert.ToInt32(req["testChunkDelay"]));
#endif
 workerStage="capture-target-validation";var frame=PreviewFrame.Capture(op,target,check,name=>workerStage=name);workerStage="capture-result-publish";Common.SaveNew(Path.Combine(op,"result.json"),new{ok=true,frame=frame});return 0;}
   if(Common.S(req,"mode")=="check"||Common.S(req,"mode")=="recheck"){var item=Common.Json.Deserialize<ImportCandidate>(Common.Json.Serialize(req["candidate"]));CandidateChecks.Run(item,check,name=>{workerStage=name;Common.ReplaceJson(Path.Combine(op,"progress.json"),new{phase=name});},Common.S(req,"mode")=="recheck");Common.SaveNew(Path.Combine(op,"result.json"),new{ok=true,candidate=item});return 0;}
   if(Common.S(req,"mode")=="diagnostic"){
#if TEST_BUILD
    if(req.ContainsKey("testChunkDelay")&&Convert.ToInt32(req["testChunkDelay"])>0)Thread.Sleep(Convert.ToInt32(req["testChunkDelay"]));
#endif
    var fault=Common.Json.Deserialize<FaultRecord>(Common.Json.Serialize(req["fault"]));var diagnostic=DiagnosticBundle.Build(op,fault,check);Common.SaveNew(Path.Combine(op,"result.json"),new{ok=true,diagnostic=diagnostic});return 0;}
   if(Common.S(req,"mode")=="names"){var ids=Common.Json.Deserialize<string[]>(Common.Json.Serialize(req["nameIds"]));var names=OfficialNameStore.Refresh(ids,check);Common.SaveNew(Path.Combine(op,"result.json"),new{ok=true,names=names});return 0;}
   if(Common.S(req,"mode")=="discover"){var hashes=req.ContainsKey("knownHashes")?Common.Json.Deserialize<string[]>(Common.Json.Serialize(req["knownHashes"])):new string[0];var discovery=ResourceDiscovery.Scan(false,check,hashes);Common.SaveNew(Path.Combine(op,"result.json"),new{ok=true,discovery=discovery});return 0;}
   if(Common.S(req,"mode")=="cover"){var item=Common.Json.Deserialize<ImportCandidate>(Common.Json.Serialize(req["candidate"]));var coverRecord=ReadRecord(item.Record);if(coverRecord==null||coverRecord.Id!=item.Id||coverRecord.Source!=item.Source||coverRecord.Md5!=item.Md5||coverRecord.RecordHash!=item.RecordHash||coverRecord.Bytes!=item.Bytes)throw new InvalidDataException("源记录已变化，请重新检查");CoverCache.Ensure(item,check);Common.SaveNew(Path.Combine(op,"result.json"),new{ok=true,candidate=item});return 0;}
    if(Common.S(req,"mode")=="analyze"){var item=Common.Json.Deserialize<ImportCandidate>(Common.Json.Serialize(req["candidate"]));workerStage="verify-source-record";var analysisRecord=ReadRecord(item.Record);if(analysisRecord==null||analysisRecord.Id!=item.Id||analysisRecord.Source!=item.Source||analysisRecord.Md5!=item.Md5||analysisRecord.RecordHash!=item.RecordHash||analysisRecord.Bytes!=item.Bytes||analysisRecord.Ticks!=item.Ticks)throw new InvalidDataException("源记录或文件状态已变化，请重新检查");Action<string> phase=name=>{check();workerStage=name;Common.ReplaceJson(Path.Combine(op,"progress.json"),new{phase=name});};CoverCache.AnalyzeForUser(item,check,phase);workerStage="save-analysis-result";Common.SaveNew(Path.Combine(op,"result.json"),new{ok=true,candidate=item});return 0;}
   if(Common.S(req,"mode")=="scan"){int ignored;var found=Discover(out ignored,check);check();Common.SaveNew(Path.Combine(op,"result.json"),new{ok=true,candidates=found,ignored=ignored,coverStats=new{hashedBytes=CoverCache.HashedBytes,previewDecodedBytes=CoverCache.PreviewBytes,cacheHits=CoverCache.Hits,previewMembersRead=CoverCache.Reads,metadataCandidates=CandidateChecks.MetadataCandidates,metadataMilliseconds=CandidateChecks.MetadataMilliseconds,sourceHashedBytes=CandidateChecks.SourceHashedBytes,imageValidationBytes=CandidateChecks.ImageValidationBytes,coverDecodedBytes=CandidateChecks.PreviewDecodedBytes,coverMembersRead=CandidateChecks.PreviewMembersRead}});return 0;}
   var c=Common.Json.Deserialize<ImportCandidate>(Common.Json.Serialize(req["candidate"]));var current=ReadRecord(c.Record);
   if(current==null||c.Id!=current.Id||c.Md5!=current.Md5||c.Source!=current.Source||c.RecordHash!=current.RecordHash||c.Bytes!=current.Bytes||c.Ticks!=current.Ticks)throw new InvalidDataException("记录或源文件已变化，请重新检查");
   if(!CandidateChecks.Passed(c)||c.Check.Signature!=CoverCache.Signature(c.Source)||c.Check.RecordHash!=c.RecordHash||c.Check.Rules!=CandidateChecks.Rules())throw new InvalidDataException("目标尚未通过入库前检查，或检查后已变化；请重新检查");
   check();string format=CheckHeader(c.Source);string payload=Path.Combine(op,"payload");Directory.CreateDirectory(payload);string temp=Path.Combine(payload,"resource"+VideoPackage.ResourceExtension(format));string hash,md5;long total=0;byte[] buffer=new byte[262144];
   using(var input=new FileStream(c.Source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var sha=SHA256.Create())using(var m=MD5.Create()){
    if(input.Length!=c.Bytes)throw new InvalidDataException("源长度已变化");int n;var progress=Stopwatch.StartNew();while((n=input.Read(buffer,0,buffer.Length))>0){check();output.Write(buffer,0,n);sha.TransformBlock(buffer,0,n,null,0);m.TransformBlock(buffer,0,n,null,0);total+=n;
#if TEST_BUILD
     if(req.ContainsKey("testChunkDelay"))Thread.Sleep(Convert.ToInt32(req["testChunkDelay"]));
#endif
     if(progress.ElapsedMilliseconds>=150){Common.ReplaceJson(Path.Combine(op,"progress.json"),new{phase="复制并校验",bytes=total,total=c.Bytes});progress.Restart();}if(total>c.Bytes)throw new InvalidDataException("源文件在读取中增长");}
    sha.TransformFinalBlock(new byte[0],0,0);m.TransformFinalBlock(new byte[0],0,0);hash=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();md5=BitConverter.ToString(m.Hash).Replace("-","").ToLowerInvariant();output.Flush(true);
   }
   check();var after=ReadRecord(c.Record);if(total!=c.Bytes||md5!=c.Md5||after==null||after.RecordHash!=c.RecordHash||after.Ticks!=c.Ticks||after.Bytes!=c.Bytes)throw new InvalidDataException("长度、MD5或读取前后状态不一致，未导入；请稍后重试");
   if(hash!=c.Check.Sha256)throw new InvalidDataException("复制内容与入库前检查的完整哈希不符");
   Common.ReplaceJson(Path.Combine(op,"progress.json"),new{phase="复核私有副本",bytes=total,total=c.Bytes});string copyHash=HashCancelable(temp,check);if(copyHash!=hash||CheckHeader(temp)!=format)throw new InvalidDataException("复制后校验不符");
   string cover=CoverCache.ForImport(c,temp,hash,md5,check);if(cover!=null){using(var im=System.Drawing.Image.FromFile(cover)){if((long)im.Width*im.Height>40000000)throw new InvalidDataException("封面过大");im.Save(Path.Combine(payload,"cover.jpg"),System.Drawing.Imaging.ImageFormat.Jpeg);}}
   check();string existing=Path.Combine(Common.Root,"library",c.Id,hash,c.Id+VideoPackage.ResourceExtension(format));bool existingVerified=false;if(Directory.Exists(Path.GetDirectoryName(existing))){LocalImport.NoLinks(Path.GetDirectoryName(existing));if(!File.Exists(existing)||HashCancelable(existing,check)!=hash)throw new InvalidDataException("管理目录已存在但内容不符；未覆盖");existingVerified=true;}var receipt=new ImportReceipt{Candidate=c,Sha256=hash,Md5=md5,Bytes=total,Format=format,Cover=cover==null?null:"cover.jpg",ImportedAt=DateTimeOffset.Now.ToString("o")};Common.SaveNew(Path.Combine(payload,"source.json"),receipt);Common.SaveNew(Path.Combine(op,"result.json"),new{ok=true,receipt=receipt,existingTargetVerified=existingVerified,dependenciesVerified=false,autoPlayed=false});return 0;
  }}catch(Exception ex){try{CleanPayload(op);Common.SaveNew(Path.Combine(op,"result.json"),new{ok=false,cancelled=ex is OperationCanceledException,stage=workerStage,error=ex.Message});}catch{}return 1;}
 }
 internal static string HashCancelable(string p,Action check){using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.Read))using(var h=SHA256.Create()){byte[] b=new byte[262144];int n;while((n=f.Read(b,0,b.Length))>0){check();h.TransformBlock(b,0,n,null,0);}h.TransformFinalBlock(new byte[0],0,0);return BitConverter.ToString(h.Hash).Replace("-","").ToLowerInvariant();}}
}
// Own held process handle and a separate Job: import cancellation never touches the playback Job.
internal sealed class ImportOperation {
 internal readonly string Folder;internal bool Cancelled;Process child;JobFence job;long creation;internal bool Busy;internal Task Completion;readonly Action<string,object> log;
 internal ImportOperation(Action<string,object> logger){log=logger;Folder=Path.Combine(Common.WorkRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Folder);}
 internal async Task<Dictionary<string,object>> Run(string mode,ImportCandidate candidate,Action<string,long,long> progress,int testDelay=0,string[] knownHashes=null,object capture=null,string[] nameIds=null,FaultRecord fault=null){
  Busy=true;var done=new TaskCompletionSource<bool>();Completion=done.Task;var clock=Stopwatch.StartNew();bool killed=false,timedOut=false;string lastProgress=null;
  try{using(var self=Process.GetCurrentProcess()){var request=new Dictionary<string,object>{{"mode",mode},{"candidate",candidate},{"parentPid",self.Id},{"parentCreation",ProcessProof.Creation(self.Handle).ToString()}};
#if TEST_BUILD
   if(testDelay>0)request["testChunkDelay"]=testDelay;
#endif
   if(fault!=null)request["fault"]=fault;if(nameIds!=null)request["nameIds"]=nameIds;if(capture!=null)request["capture"]=capture;if(knownHashes!=null)request["knownHashes"]=knownHashes;Common.SaveNew(Path.Combine(Folder,"request.json"),request);}
   job=new JobFence();child=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--local-import "+Common.Quote(Folder)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Common.Root});creation=ProcessProof.Creation(child.Handle);ProcessProof.Verify(child.Handle,child.Id,creation,Application.ExecutablePath);job.Assign(child.Handle);File.WriteAllText(Path.Combine(Folder,"go"),"");log("local-import-task-start",new{mode=mode,pid=child.Id,creationFileTime=creation,job=true,operation=Folder,network=false});
   var cancelClock=new Stopwatch();string stamp="";
   while(Common.WaitForSingleObject(child.Handle,0)==258){if(clock.Elapsed.TotalSeconds>(mode=="diagnostic"?15:mode=="names"?12:mode=="capture-frame"?5:mode=="scan"||mode=="discover"||mode=="cover"||mode=="analyze"||mode=="check"||mode=="recheck"?45:180)){timedOut=true;Cancel();}if(Cancelled){if(!cancelClock.IsRunning)cancelClock.Start();if(cancelClock.ElapsedMilliseconds>1500){ProcessProof.Verify(child.Handle,child.Id,creation,Application.ExecutablePath);Common.TerminateProcess(child.Handle,196);killed=true;break;}}
    try{string p=Path.Combine(Folder,"progress.json");if(File.Exists(p)){string text=await Task.Run(()=>Common.ReadShared(p));lastProgress=text;if(text!=stamp){stamp=text;var v=Common.Json.Deserialize<Dictionary<string,object>>(text);long bytes=v.ContainsKey("bytes")?Convert.ToInt64(v["bytes"]):0;long total=v.ContainsKey("total")?Convert.ToInt64(v["total"]):0;if(progress!=null)progress(Common.S(v,"phase"),bytes,total);}}}catch(IOException){}await Task.Delay(80);
   }
   if(killed){for(int i=0;i<10&&Common.WaitForSingleObject(child.Handle,0)==258;i++)await Task.Delay(50);}
   if(timedOut)throw new TimeoutException("本地任务超时，已停止并清理，请稍后重试");if(Cancelled)throw new OperationCanceledException("导入已取消，未登记");string result=Path.Combine(Folder,"result.json");if(!File.Exists(result))throw new IOException("导入任务未返回完整结果");var data=await Task.Run(()=>{var json=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=8388608};return json.Deserialize<Dictionary<string,object>>(Common.ReadShared(result));});if(!Convert.ToBoolean(data["ok"])){string stage=Common.S(data,"stage"),error=Common.S(data,"error");throw new InvalidDataException(String.IsNullOrEmpty(stage)?error:"在“"+stage+"”阶段失败："+error);}return data;
   }finally{try{if(job!=null){job.Dispose();job=null;}if(child!=null){Common.WaitForSingleObject(child.Handle,500);bool exited=Common.WaitForSingleObject(child.Handle,0)==0;int? exitCode=null;try{if(exited)exitCode=child.ExitCode;}catch{}log("local-import-task-exit",new{pid=child.Id,exited=exited,exitCode=exitCode,forced=killed,cancelled=Cancelled,elapsed=clock.Elapsed.TotalSeconds,resultExists=File.Exists(Path.Combine(Folder,"result.json")),progress=lastProgress});child.Dispose();child=null;}if(Cancelled)LocalImport.CleanPayload(Folder);}finally{if(mode=="diagnostic")try{DiagnosticBundle.CleanOperation(Folder);}catch{}Busy=false;done.TrySetResult(true);}}
 }
 internal void Cancel(){Cancelled=true;try{File.WriteAllText(Path.Combine(Folder,"cancel"),"");}catch{}}
 internal async Task Stop(){Cancel();if(Completion!=null)await Completion;LocalImport.CleanPayload(Folder);}
}
