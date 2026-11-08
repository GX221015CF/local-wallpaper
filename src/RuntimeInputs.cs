using System;using System.IO;using System.Diagnostics;using System.Collections.Generic;using System.Threading;using System.Runtime.InteropServices;

// Only this version's explicitly registered input copies. Never enumerate logs or the library.
internal sealed class RuntimeInputLease:IDisposable {
 internal string Directory,Path;internal Dictionary<string,object> Record;internal FileStream Lock;
 public void Dispose(){if(Lock!=null){Lock.Dispose();Lock=null;}}
 internal void Copy(string source,CancellationToken token){
  Exception original=null;try{PlaybackResourceCheck.Copy(source,Path,token,Convert.ToInt64(Record["reservedBytes"]));}
  catch(Exception e){original=e;throw;}
  finally{try{if(File.Exists(Path)){Record["fileIdentity"]=RuntimeInputs.FileIdentity(Path);Record["copiedBytes"]=new FileInfo(Path).Length;}RuntimeInputs.Save(Directory,Record);}catch(Exception metadata){if(original!=null)throw new IOException(original.Message,new AggregateException(original,metadata));throw;}}
 }
 internal void End(bool childEnded,bool failed,string stage,string reason){
  Record["terminal"]=childEnded;Record["failed"]=failed;Record["stage"]=stage;Record["reason"]=reason;Record["endedUtc"]=DateTime.UtcNow.ToString("o");
  RuntimeInputs.Save(Directory,Record);
 }
}
internal static class RuntimeInputs {
 internal const long TotalLimit=512L*1024*1024,FailureLimit=128L*1024*1024;
 internal const int FailureCount=2,MaxEntries=64;
 static string InputName(Dictionary<string,object> r){string name=r.ContainsKey("inputName")?Common.S(r,"inputName"):"input.cwp";if(name!="input.cwp"&&name!="input.mp4")throw new InvalidDataException("运行资源文件名无效");return name;}
 internal const string Scheme="owned-runtime-input-v1-r24";
 internal static string Root{get{return System.IO.Path.Combine(Common.Root,"runtime-inputs-r24");}}
 internal static string LastReport;
 [StructLayout(LayoutKind.Sequential)]struct FileInfoNative{public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Created,Accessed,Written;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
 [DllImport("kernel32",SetLastError=true)]static extern bool GetFileInformationByHandle(IntPtr handle,out FileInfoNative info);
 internal static string FileIdentity(string p){Safe(p);using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){FileInfoNative n;if(!GetFileInformationByHandle(f.SafeFileHandle.DangerousGetHandle(),out n))throw new System.ComponentModel.Win32Exception();return n.Volume+":"+n.IndexHigh+":"+n.IndexLow+":"+n.Created.dwHighDateTime+":"+n.Created.dwLowDateTime;}}
 static string Full(string p){return System.IO.Path.GetFullPath(p).TrimEnd('\\','/');}
 static bool Under(string p,string root){return Full(p).StartsWith(Full(root)+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
 internal static void Safe(string p){
  string full=Full(p);for(string q=full;!String.IsNullOrEmpty(q);q=System.IO.Path.GetDirectoryName(q)){
   if((File.Exists(q)||System.IO.Directory.Exists(q))&&(File.GetAttributes(q)&FileAttributes.ReparsePoint)!=0)throw new IOException("运行材料路径含重解析点，保留文件并拒绝清理/准备。");
  }
 }
 static void EnsureRoot(){
  Safe(Root);if(!System.IO.Directory.Exists(Root)){
   System.IO.Directory.CreateDirectory(Root);
   Common.SaveNew(System.IO.Path.Combine(Root,"owner.json"),new{scheme=Scheme,dataRoot=Full(Common.Root)});
  }
  string marker=System.IO.Path.Combine(Root,"owner.json");Safe(marker);var m=Common.Read(marker);
  if(Common.S(m,"scheme")!=Scheme||!String.Equals(Common.S(m,"dataRoot"),Full(Common.Root),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("运行材料拥有标记不匹配；未访问旧材料。");
 }
 static FileStream Gate(){EnsureRoot();string p=System.IO.Path.Combine(Root,"manager.lock");Safe(p);return new FileStream(p,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
 internal static void Save(string dir,Dictionary<string,object> r){Safe(dir);Safe(System.IO.Path.Combine(dir,"owner.json"));Common.ReplaceJson(System.IO.Path.Combine(dir,"owner.json"),r);}
 static List<string> Entries(){
  var rows=new List<string>();foreach(string d in System.IO.Directory.EnumerateDirectories(Root)){if(rows.Count>=MaxEntries)throw new IOException("运行材料登记超过64项；需先处理未确认材料，未创建新副本。");rows.Add(d);}return rows;
 }
 static Dictionary<string,object> Validate(string dir){
  Safe(dir);if(!Under(dir,Root)||System.IO.Path.GetDirectoryName(Full(dir))!=Full(Root))throw new InvalidDataException("运行材料越界");
  foreach(string f in System.IO.Directory.EnumerateFileSystemEntries(dir)){
   Safe(f);string name=System.IO.Path.GetFileName(f);if(System.IO.Directory.Exists(f)||(name!="owner.json"&&name!="input.cwp"&&name!="input.mp4"&&name!="lease.lock"))throw new InvalidDataException("运行材料含未知文件，保留并拒绝清理");
  }
  var r=Common.Read(System.IO.Path.Combine(dir,"owner.json"));string id=System.IO.Path.GetFileName(dir),run=Common.S(r,"run");
  Guid parsed;if(!Guid.TryParseExact(id,"N",out parsed)||Common.S(r,"scheme")!=Scheme||Common.S(r,"id")!=id||!Under(run,Common.LogRoot))throw new InvalidDataException("运行材料身份或日志范围不匹配");
  Safe(run);Safe(System.IO.Path.Combine(run,"request.json"));var req=Common.Read(System.IO.Path.Combine(run,"request.json"));
  if(Common.S(req,"runToken")!=id||Common.S(req,"expectedSha256")!=Common.S(r,"sha256"))throw new InvalidDataException("运行材料与请求身份不匹配");
  string inputName=InputName(r);if(inputName!=(VideoPackage.Extension(Common.S(req,"cwp"))?"input.mp4":"input.cwp")||File.Exists(System.IO.Path.Combine(dir,inputName=="input.mp4"?"input.cwp":"input.mp4")))throw new InvalidDataException("运行副本类型或唯一文件归属不匹配，保留材料");long n=Convert.ToInt64(r["reservedBytes"]);if(n<0||n>TotalLimit)throw new InvalidDataException("运行材料容量登记无效");return r;
 }
 // Read-only proof for a user-requested capture. Never scans or cleans runtime materials.
 internal static void VerifyCaptureInput(string run,string path,string token,string hash){
  Safe(Root);Safe(path);string marker=System.IO.Path.Combine(Root,"owner.json");Safe(marker);var root=Common.Read(marker);
  if(Common.S(root,"scheme")!=Scheme||!String.Equals(Common.S(root,"dataRoot"),Full(Common.Root),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("截图运行材料拥有标记不匹配");
  string dir=System.IO.Path.GetDirectoryName(Full(path));var r=Validate(dir);
  if(!String.Equals(Full(run),Full(Common.S(r,"run")),StringComparison.OrdinalIgnoreCase)||Common.S(r,"id")!=token||Common.S(r,"sha256")!=hash||Convert.ToBoolean(r["terminal"])||!String.Equals(Full(path),Full(System.IO.Path.Combine(dir,InputName(r))),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("截图目标不是本次仍在使用的登记运行副本");
  if(!File.Exists(path)||!r.ContainsKey("copiedBytes")||new FileInfo(path).Length!=Convert.ToInt64(r["copiedBytes"])||Convert.ToInt64(r["copiedBytes"])!=Convert.ToInt64(r["reservedBytes"])||FileIdentity(path)!=Common.S(r,"fileIdentity"))throw new InvalidDataException("截图运行副本身份或长度已变化");
 }
 // Never identify a process solely by PID. Access denied is unknown, so preserve.
 static bool WorkerEnded(Dictionary<string,object> r){
  try{using(var p=Process.GetProcessById(Convert.ToInt32(r["workerPid"]))){if(p.HasExited)return true;return ProcessProof.Creation(p.Handle)!=Convert.ToInt64(r["workerCreation"]);}}
  catch(ArgumentException){return true;}catch{return false;}
 }
 static long Bytes(string d,Dictionary<string,object> r){string p=System.IO.Path.Combine(d,InputName(r));return Math.Max(Convert.ToInt64(r["reservedBytes"]),File.Exists(p)?new FileInfo(p).Length:0);}
 static void Note(Dictionary<string,object> r,string state,string error,long bytes){
  string run=Common.S(r,"run");Safe(run);string report=System.IO.Path.Combine(run,"runtime-input-cleanup.json");Safe(report);
  Common.ReplaceJson(report,new{scheme=Scheme,state=state,error=error,bytes=bytes,sha256=Common.S(r,"sha256"),stage=Common.S(r,"stage"),originalResult=Common.S(r,"reason"),runToken=Common.S(r,"id"),workerPid=r["workerPid"],workerCreation=r["workerCreation"],at=DateTime.UtcNow.ToString("o"),persistentLibraryTouched=false});
 }
 static bool Delete(string dir,Dictionary<string,object> r){
  long bytes=0;try{
   Validate(dir);if(!Convert.ToBoolean(r["terminal"])||!WorkerEnded(r))return false;
   int attempts=r.ContainsKey("deleteAttempts")?Convert.ToInt32(r["deleteAttempts"]):0;
   if(attempts>=3){Note(r,"retained-retry-limit","已达3次删除尝试；保持材料并计入容量，不无限重试。",Bytes(dir,r));return false;}
   r["deleteAttempts"]=attempts+1;Save(dir,r);
   string lockPath=System.IO.Path.Combine(dir,"lease.lock");Safe(lockPath);
   using(var held=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
    string file=System.IO.Path.Combine(dir,InputName(r));Safe(file);bytes=File.Exists(file)?new FileInfo(file).Length:0;
    if(File.Exists(file)&&(Common.S(r,"fileIdentity")==""||FileIdentity(file)!=Common.S(r,"fileIdentity")||bytes!=Convert.ToInt64(r["copiedBytes"])))throw new InvalidDataException("运行副本文件身份或长度变化，保留而不删除。");
    // Summary first; deletion failure must never replace the original playback error.
    Note(r,"delete-pending",null,bytes);if(File.Exists(file))File.Delete(file);Note(r,"reclaimed",null,bytes);
   }
   File.Delete(lockPath);File.Delete(System.IO.Path.Combine(dir,"owner.json"));System.IO.Directory.Delete(dir,false);return true;
  }catch(Exception e){try{Note(r,"retained-cleanup-failed",e.Message,bytes);}catch{}return false;}
 }
 internal static void Maintain(int budgetMilliseconds=500){
  if(!System.IO.Directory.Exists(Root))return;
  var watch=Stopwatch.StartNew();try{using(var gate=Gate()){
   var failed=new List<KeyValuePair<string,Dictionary<string,object>>>();long failureBytes=0;int failureCount=0;
   foreach(string dir in Entries()){
    if(watch.ElapsedMilliseconds>budgetMilliseconds)break;
    Dictionary<string,object> r;try{r=Validate(dir);}catch(Exception e){LastReport="运行材料身份未知，安全保留："+e.Message;continue;}
    if(!Convert.ToBoolean(r["terminal"])||!WorkerEnded(r)){LastReport="运行或结束状态未确认，材料保留。";try{Note(r,"retained-active-or-unknown",LastReport,Bytes(dir,r));}catch{}continue;}
    if(!Convert.ToBoolean(r["failed"]))Delete(dir,r);else failed.Add(new KeyValuePair<string,Dictionary<string,object>>(dir,r));
   }
   // Newest failures have diagnostic priority; ordering is not deletion authority.
   failed.Sort((a,b)=>String.CompareOrdinal(Common.S(b.Value,"endedUtc"),Common.S(a.Value,"endedUtc")));
   foreach(var pair in failed){
    if(watch.ElapsedMilliseconds>budgetMilliseconds)break;
    long n=Bytes(pair.Key,pair.Value);
    if(failureCount<FailureCount&&failureBytes+n<=FailureLimit){failureCount++;failureBytes+=n;Note(pair.Value,"retained-failure-budget",null,n);}
    else Delete(pair.Key,pair.Value);
   }
  }}catch(Exception e){LastReport="运行材料维护未完成："+e.Message;}
 }
 internal static RuntimeInputLease Begin(string run,string source,string hash,CancellationToken token){
  token.ThrowIfCancellationRequested();Maintain();using(var gate=Gate()){
   Safe(run);Safe(source);if(!Under(run,Common.LogRoot))throw new InvalidDataException("运行请求越界");var req=Common.Read(System.IO.Path.Combine(run,"request.json"));string id=Common.S(req,"runToken");Guid guid;
   if(!Guid.TryParseExact(id,"N",out guid)||Common.S(req,"expectedSha256")!=hash)throw new InvalidDataException("运行请求拥有标记无效");
   long size=new FileInfo(source).Length,total=0,failedBytes=0;int failedCount=0;var entries=Entries();if(entries.Count>=MaxEntries)throw new IOException("运行材料数量达到上限");
   foreach(string dir in entries){var r=Validate(dir);long n=Bytes(dir,r);total=checked(total+n);if(Convert.ToBoolean(r["terminal"])&&Convert.ToBoolean(r["failed"])){failedCount++;failedBytes+=n;}}
   if(failedCount>FailureCount||failedBytes>FailureLimit)throw new IOException("故障材料超过保留预算且尚不能安全回收，暂不创建新运行副本。");
   if(size>TotalLimit||total>TotalLimit-size)throw new IOException("运行副本达到512 MiB上限，部分材料仍占用或清理未确认；未继续复制。已导入壁纸保持。");
   string owned=System.IO.Path.Combine(Root,id);Safe(owned);if(System.IO.Directory.Exists(owned))throw new IOException("重复运行拥有标识，未覆盖已有材料");System.IO.Directory.CreateDirectory(owned);
   using(var p=Process.GetCurrentProcess()){
    var r=new Dictionary<string,object>{{"scheme",Scheme},{"inputName",VideoPackage.Extension(source)?"input.mp4":"input.cwp"},{"id",id},{"run",Full(run)},{"sha256",hash},{"reservedBytes",size},{"workerPid",p.Id},{"workerCreation",ProcessProof.Creation(p.Handle).ToString()},{"terminal",false},{"failed",false},{"stage","private-resource-copy"},{"reason","not-ended"}};
    Common.SaveNew(System.IO.Path.Combine(owned,"owner.json"),r);
    var result=new RuntimeInputLease{Directory=owned,Path=System.IO.Path.Combine(owned,InputName(r)),Record=r};
    result.Lock=new FileStream(System.IO.Path.Combine(owned,"lease.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);return result;
   }
  }
 }
}
