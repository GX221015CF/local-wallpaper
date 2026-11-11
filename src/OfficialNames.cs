// Offline, allowlisted wallpaper response bodies only. Never read HTTP headers or account stores.
using System;using System.IO;using System.Linq;using System.Text;using System.Collections;using System.Collections.Generic;using System.Diagnostics;using System.Security.Cryptography;using System.IO.Compression;
internal sealed class NameEvidence {
 public string Id{get;set;}public string Name{get;set;}public string Kind{get;set;}public string CheckedAt{get;set;}public string SourceFile{get;set;}public long SourceOffset{get;set;}public int SourceBytes{get;set;}public string SourceSha256{get;set;}public string IndexSha256{get;set;}public string ResponsePath{get;set;}public string JsonPath{get;set;}
}
internal sealed class NameReadDiagnostics {
 public bool MoreWork{get;set;}public int EntriesRead{get;set;}public int EntriesVisited{get;set;}public bool Resumed{get;set;}public bool ScanCacheHit{get;set;}
 public string Outcome{get;set;}public string Stage{get;set;}public string FailureStage{get;set;}public string FailureKind{get;set;}public string ExceptionType{get;set;}public string ExceptionMessage{get;set;}
 public long IndexBytesRead{get;set;}public long EntryBytesRead{get;set;}public long ResponseBytesRead{get;set;}public int BodiesRead{get;set;}
 public uint? IndexMagic{get;set;}public uint? IndexVersion{get;set;}public uint? BucketCount{get;set;}public uint? RecordCount{get;set;}public long? ActualIndexLength{get;set;}public string ValidationField{get;set;}public bool ChangedDuringRead{get;set;}
}
internal static class OfficialNames {
 const int MaxBody=2097152;internal static string LastReason;internal static int BodiesRead;internal static NameReadDiagnostics LastDiagnostics;
 internal static string CacheRoot{get{
#if TEST_BUILD
  string p=Path.Combine(Common.Root,"tests/name-cache-root.txt");if(File.Exists(p))return Path.GetFullPath(File.ReadAllText(p).Trim());
#endif
  return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lenovo/LeAppStore/storecache/Cache/Cache_Data");}}
 internal static string Clean(string s){if(String.IsNullOrEmpty(s)||s.Length>160)return null;var b=new StringBuilder();bool white=false;foreach(char c in s){if(Char.IsControl(c)&&!Char.IsWhiteSpace(c))return null;if(Char.IsWhiteSpace(c)){white=b.Length>0;continue;}if(white)b.Append(' ');white=false;b.Append(c);}string n=b.ToString();if(n.Length==0||n.Length>120||n.EndsWith("…")||n.EndsWith("...")||n.All(Char.IsDigit)||n.EndsWith(".cwp",StringComparison.OrdinalIgnoreCase)||n.Contains("://"))return null;return n;}
 static string Digest(byte[] b){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant();}
 static void Count(NameReadDiagnostics d,string role,int bytes){if(role=="index")d.IndexBytesRead+=bytes;else if(role=="entry")d.EntryBytesRead+=bytes;else if(role=="response")d.ResponseBytesRead+=bytes;}
 static void Failure(NameReadDiagnostics d,string stage,Exception ex){if(d.ExceptionType!=null)return;d.Stage=stage;d.FailureStage=stage;d.ExceptionType=ex.GetType().FullName;d.ExceptionMessage=FaultSafety.Text(ex.Message,360);d.FailureKind=ex is OperationCanceledException?"cancelled":ex is TimeoutException||ex is InvalidDataException&&ex.Message.IndexOf("预算",StringComparison.Ordinal)>=0?"budget":ex is UnauthorizedAccessException?"access-denied":ex is NotSupportedException?"unsupported-format":ex.Message.IndexOf("读取期间发生变化",StringComparison.Ordinal)>=0?"changed-during-read":ex is FileNotFoundException||ex is DirectoryNotFoundException?"read-failure":ex is InvalidDataException?"corrupt-cache":ex is IOException?"read-failure":"unexpected";}
 static byte[] Read(string path,long offset,int count,Action check,string role,NameReadDiagnostics d){check();if(count<0||count>MaxBody)throw new InvalidDataException("名称缓存读取超预算");LocalImport.NoLinks(path);using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(offset<0||offset+count>f.Length)throw new InvalidDataException("名称缓存范围失效");f.Position=offset;byte[] b=new byte[count];int n=0,k;while(n<count&&(k=f.Read(b,n,count-n))>0){Count(d,role,k);check();n+=k;}if(n!=count)throw new IOException("名称缓存截断");return b;}}
 static uint U(byte[] b,int p){return BitConverter.ToUInt32(b,p);}
 static byte[] Address(string root,uint a,int count,Action check,string role,NameReadDiagnostics d,out string file,out long offset,ScanProgress progress=null){if((a&0x80000000)==0)throw new InvalidDataException("缓存地址无效");int kind=(int)((a>>28)&7);offset=0;if(kind==0)file=Path.Combine(root,"f_"+(a&0xfffffff).ToString("x6"));else{int size=kind==2?256:kind==3?1024:kind==4?4096:0;if(size==0)throw new InvalidDataException("缓存块布局未支持");file=Path.Combine(root,"data_"+((a>>16)&255));offset=8192L+(a&65535)*size;}if(progress!=null)Track(progress,file,root);byte[] bytes=Read(file,offset,count,check,role,d);if(progress!=null)Track(progress,file,root);return bytes;}
 static byte[] Body(byte[] data,Action check){if(data.Length<2||data[0]!=31||data[1]!=139)return data;using(var input=new MemoryStream(data,false))using(var zip=new GZipStream(input,CompressionMode.Decompress))using(var output=new MemoryStream()){byte[] buffer=new byte[32768];int n;while((n=zip.Read(buffer,0,buffer.Length))>0){check();if(output.Length+n>MaxBody)throw new InvalidDataException("名称响应解压超预算");output.Write(buffer,0,n);}return output.ToArray();}}
 static bool Allowed(string p){return p=="/pcwallpapercontent/wallpaper/jingling/wallpaperListDynamic"||p=="/pcwallpapercontent/wallpaper/jingling/queryCarouselWallpaperListByPage"||p=="/pcwallpapercontent/wallpaper/jingling/detailDynamic";}
 internal sealed class ScanProgress {
  public int Version{get;set;}public string IndexHash{get;set;}public int Bucket{get;set;}public uint Address{get;set;}
  public List<uint> Seen{get;set;}public Dictionary<string,string> Stamps{get;set;}
  public Dictionary<string,NameEvidence> Names{get;set;}public List<string> Conflicts{get;set;}
  public bool Complete{get;set;}public string PartialReason{get;set;}
 }
 static bool CacheFile(string file,string root){
  return Path.GetDirectoryName(Path.GetFullPath(file))==Path.GetFullPath(root)
   &&System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(file),@"^(data_[0-9]{1,3}|f_[0-9a-fA-F]{6})$");
 }
 static string Stamp(string path){return File.Exists(path)?CoverCache.Signature(path):"missing";}
 static ScanProgress NewProgress(string indexHash){return new ScanProgress{Version=1,IndexHash=indexHash,Seen=new List<uint>(),Stamps=new Dictionary<string,string>(),Names=new Dictionary<string,NameEvidence>(),Conflicts=new List<string>()};}
 static bool StampsMatch(ScanProgress progress,string root,Action check){
  foreach(var pair in progress.Stamps){check();if(!CacheFile(pair.Key,root)||Stamp(pair.Key)!=pair.Value)return false;}return true;
 }
 static void Track(ScanProgress progress,string file,string root){
  if(!CacheFile(file,root))throw new InvalidDataException("名称缓存文件不属于允许范围");
  string signature=Stamp(file),prior;
  if(progress.Stamps.TryGetValue(file,out prior)&&prior!=signature)throw new IOException("名称缓存读取期间发生变化");
  if(!progress.Stamps.ContainsKey(file)&&progress.Stamps.Count>=256)throw new InvalidDataException("名称缓存涉及文件超过256项元数据预算");
  progress.Stamps[file]=signature;
 }
 internal static Dictionary<string,NameEvidence> ReadNames(IEnumerable<string> ids,Action taskCheck){
  const int EntryBudget=1024,MaxEntries=8*1048576/256,ResponseBudget=8*1048576;
  var wanted=new HashSet<string>(ids.Where(OfficialNameStore.Id).Take(128));
  var names=new Dictionary<string,NameEvidence>();var clock=Stopwatch.StartNew();
  LastReason=null;BodiesRead=0;var diag=LastDiagnostics=new NameReadDiagnostics{Outcome="running",Stage="cache-root-check"};
  Action check=()=>{taskCheck();if(clock.Elapsed.TotalSeconds>5)throw new TimeoutException("名称核查超过5秒预算");};
  try{
   check();string root=CacheRoot,indexPath=Path.Combine(root,"index");
   try{
    if((File.GetAttributes(root)&FileAttributes.Directory)==0)throw new InvalidDataException("名称缓存位置不是目录");
    if((File.GetAttributes(indexPath)&FileAttributes.Directory)!=0)throw new InvalidDataException("名称索引位置不是文件");
   }catch(FileNotFoundException){diag.Outcome="cache-missing";diag.FailureKind="missing-cache";LastReason="未找到本地官方名称缓存";return names;}
   catch(DirectoryNotFoundException){diag.Outcome="cache-missing";diag.FailureKind="missing-cache";LastReason="未找到本地官方名称缓存";return names;}
   LocalImport.NoLinks(root);LocalImport.NoLinks(indexPath);
   diag.Stage="index-read";diag.ActualIndexLength=new FileInfo(indexPath).Length;
   if(diag.ActualIndexLength>MaxBody)throw new InvalidDataException("名称索引超过2MiB读取预算");
   byte[] index=Read(indexPath,0,checked((int)diag.ActualIndexLength.Value),check,"index",diag);
   diag.Stage="index-validate";
   if(index.Length<368)throw new InvalidDataException("官方名称缓存索引头不完整");
   uint count=U(index,8),buckets=U(index,28),magic=U(index,0),version=U(index,4);
   diag.IndexMagic=magic;diag.IndexVersion=version;diag.BucketCount=buckets;diag.RecordCount=count;
   if(magic!=0xc103cac3){diag.ValidationField="magic";throw new NotSupportedException("名称缓存索引标识未支持");}
   if(version!=0x30000){diag.ValidationField="version";throw new NotSupportedException("名称缓存索引版本未支持");}
   if(buckets!=131072){diag.ValidationField="bucket-count";throw new NotSupportedException("名称缓存索引桶数量未支持");}
   if(count>MaxEntries){diag.ValidationField="record-count";throw new InvalidDataException("名称索引元数据超过8MiB累计遍历预算");}
   if(index.Length!=368L+(long)buckets*4){diag.ValidationField="index-length";throw new InvalidDataException("名称索引长度与桶数量不一致");}
   string indexHash=Digest(index),key=Digest(Encoding.UTF8.GetBytes(root.ToUpperInvariant()+"|"+String.Join(",",wanted.OrderBy(x=>x))));
   string cursorPath=Path.Combine(Common.Root,"data","official-name-scans",key+".json");
   using(var mutex=new System.Threading.Mutex(false,"Local\\WallpaperNameScan-"+key.Substring(0,24))){
    bool held=false;
    try{
     while(!held){check();try{held=mutex.WaitOne(50);}catch(System.Threading.AbandonedMutexException){held=true;}}
     ScanProgress cursor=null;
     if(File.Exists(cursorPath))try{
      LocalImport.NoLinks(cursorPath);
      if(new FileInfo(cursorPath).Length<=2*1048576)cursor=Common.Json.Deserialize<ScanProgress>(Common.ReadShared(cursorPath));
      if(cursor==null||cursor.Version!=1||cursor.IndexHash!=indexHash||cursor.Bucket<0||cursor.Bucket>buckets||cursor.Seen==null||cursor.Seen.Count>count||cursor.Seen.Distinct().Count()!=cursor.Seen.Count||cursor.Stamps==null||cursor.Stamps.Count>256||cursor.Names==null||cursor.Names.Count>128||cursor.Conflicts==null||cursor.Conflicts.Count>128||cursor.Names.Any(pair=>!wanted.Contains(pair.Key)||!OfficialNameStore.Valid(pair.Value,pair.Key)||!CacheFile(pair.Value.SourceFile,root))||!StampsMatch(cursor,root,check))cursor=null;
     }catch(OperationCanceledException){throw;}catch(TimeoutException){throw;}catch{cursor=null;}
     if(cursor==null)cursor=NewProgress(indexHash);else diag.Resumed=cursor.Seen.Count>0;
     var seen=new HashSet<uint>(cursor.Seen);var conflicts=new HashSet<string>(cursor.Conflicts);
     if(cursor.Complete){
      diag.ScanCacheHit=true;diag.EntriesVisited=seen.Count;diag.Stage="completed-cache";
      LastReason=cursor.PartialReason;names=cursor.Names;diag.Outcome=cursor.PartialReason!=null?"partial-failure":names.Count==0?"checked-no-match":"checked";return names;
     }
     int responses=0;
     while(cursor.Bucket<buckets){
      if((cursor.Bucket&1023)==0)check();
      if(cursor.Address==0)cursor.Address=U(index,368+cursor.Bucket*4);
      if(cursor.Address==0){cursor.Bucket++;continue;}
      if(diag.EntriesRead>=EntryBudget||responses>=16||clock.Elapsed.TotalMilliseconds>=4400)break;
      uint address=cursor.Address;
      if(seen.Contains(address)||seen.Count>=count)throw new InvalidDataException("名称索引链循环或记录数量不一致");
      string entryFile;long entryOffset;diag.Stage="index-entry-read";
      byte[] entry=Address(root,address,256,check,"entry",diag,out entryFile,out entryOffset,cursor);
      if(U(entry,0)%buckets!=cursor.Bucket)throw new InvalidDataException("名称索引桶与链不一致");
      int length=checked((int)U(entry,32));Uri uri=null;
      if(length>0&&length<=4096){
       string text=Encoding.UTF8.GetString(entry.Skip(96).Take(Math.Min(length,160)).ToArray());int begin=text.IndexOf("http",StringComparison.Ordinal);
       Uri found;if(begin>=0&&Uri.TryCreate(text.Substring(begin),UriKind.Absolute,out found)&&(found.Scheme=="http"||found.Scheme=="https")&&found.Host=="pc-store.lenovomm.cn"&&Allowed(found.AbsolutePath))uri=found;
      }
      if(uri!=null&&uri.AbsolutePath.EndsWith("/detailDynamic",StringComparison.Ordinal)){
       string pair=uri.Query.TrimStart('?').Split('&').FirstOrDefault(value=>value.StartsWith("wallpaperId=",StringComparison.Ordinal));
       if(length>160||pair==null||!wanted.Contains(Uri.UnescapeDataString(pair.Substring(12))))uri=null;
      }
      int size=uri==null?0:checked((int)U(entry,44));
      if(size>0&&size<=MaxBody&&diag.ResponseBytesRead+size*2L>ResponseBudget)break;
      if(uri!=null&&size>0&&size<=MaxBody){
       responses++;
       try{
        string file;long offset;diag.Stage="response-read";
        byte[] stored=Address(root,U(entry,60),size,check,"response",diag,out file,out offset,cursor);
        string fingerprint=Digest(stored);BodiesRead++;diag.BodiesRead=BodiesRead;
        diag.Stage="response-parse";
        var json=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=MaxBody,RecursionLimit=24};
        var obj=json.DeserializeObject(new UTF8Encoding(false,true).GetString(Body(stored,check)))as Dictionary<string,object>;
        var pending=new Dictionary<string,NameEvidence>();
        if(obj!=null&&obj.ContainsKey("status")&&Convert.ToString(obj["status"])=="0"&&obj.ContainsKey("data"))
         Walk(obj["data"],"$.data",0,wanted,pending,conflicts,new NameEvidence{Kind="official-local-wallpaper-response",CheckedAt=DateTimeOffset.Now.ToString("o"),SourceFile=file,SourceOffset=offset,SourceBytes=size,SourceSha256=fingerprint,IndexSha256=indexHash,ResponsePath=uri.AbsolutePath});
        diag.Stage="response-consistency-read";
        if(!entry.SequenceEqual(Read(entryFile,entryOffset,256,check,"entry",diag))||fingerprint!=Digest(Read(file,offset,size,check,"response",diag)))throw new IOException("名称缓存读取期间发生变化");
        foreach(var pair in pending){NameEvidence prior;if(cursor.Names.TryGetValue(pair.Key,out prior)&&prior.Name!=pair.Value.Name)conflicts.Add(pair.Key);else cursor.Names[pair.Key]=pair.Value;}
       }catch(OperationCanceledException){throw;}catch(TimeoutException){throw;}
       catch(Exception error){
        Failure(diag,diag.Stage,error);
        if(diag.FailureKind=="changed-during-read"){diag.ChangedDuringRead=true;throw;}
        cursor.PartialReason="部分允许的本地名称响应读取失败，已保留可核验结果";
       }
      }
      seen.Add(address);diag.EntriesRead++;cursor.Address=U(entry,4);if(cursor.Address==0)cursor.Bucket++;
     }
     diag.Stage="index-consistency-read";
     if(!index.SequenceEqual(Read(indexPath,0,index.Length,check,"index",diag))||!StampsMatch(cursor,root,check)){
      diag.ChangedDuringRead=true;throw new IOException("名称索引读取期间发生变化");
     }
     cursor.Seen=seen.ToList();cursor.Conflicts=conflicts.ToList();cursor.Complete=cursor.Bucket>=buckets;
     if(cursor.Complete&&seen.Count!=count)throw new InvalidDataException("名称索引声明数量与实际链不一致");
     foreach(string id in conflicts)cursor.Names.Remove(id);
     if(conflicts.Count>0)cursor.PartialReason="同ID官方名称冲突，已保留原名";
     Directory.CreateDirectory(Path.GetDirectoryName(cursorPath));check();Common.ReplaceJson(cursorPath,cursor);
     diag.EntriesVisited=seen.Count;diag.MoreWork=!cursor.Complete;
     if(cursor.Complete){names=cursor.Names;LastReason=cursor.PartialReason;diag.Stage=diag.FailureStage??"completed";diag.Outcome=cursor.PartialReason==null?(names.Count==0?"checked-no-match":"checked"):"partial-failure";}
     else{LastReason="本地名称分批核对中（"+seen.Count+" / "+count+"）";diag.Stage="batch-completed";diag.Outcome="progress";}
    }finally{if(held)mutex.ReleaseMutex();}
   }
  }catch(OperationCanceledException error){Failure(diag,diag.Stage,error);diag.Outcome="cancelled";throw;}
  catch(Exception error){
   Failure(diag,diag.Stage,error);names.Clear();
   diag.Outcome=diag.FailureKind=="budget"?"budget-exceeded":diag.FailureKind=="unsupported-format"?"format-unsupported":diag.FailureKind=="corrupt-cache"?"cache-damaged":diag.FailureKind=="changed-during-read"?"changed-during-read":"read-failed";
   LastReason="本地名称核对未完成（"+diag.Stage+"；"+diag.FailureKind+"）："+FaultSafety.Text(error.Message,180);
  }
  diag.BodiesRead=BodiesRead;return names;
 }
 static void Walk(object value,string path,int depth,HashSet<string> wanted,Dictionary<string,NameEvidence> names,HashSet<string> conflicts,NameEvidence source){
  if(depth>16)return;var obj=value as Dictionary<string,object>;if(obj!=null){object idValue,nameValue;if(obj.TryGetValue("wallpaperId",out idValue)&&obj.TryGetValue("wallpaperName",out nameValue)){string id=Convert.ToString(idValue),name=Clean(nameValue as string);if(wanted.Contains(id)&&name!=null){NameEvidence prior;if(names.TryGetValue(id,out prior)&&prior.Name!=name)conflicts.Add(id);else names[id]=new NameEvidence{Id=id,Name=name,Kind=source.Kind,CheckedAt=source.CheckedAt,SourceFile=source.SourceFile,SourceOffset=source.SourceOffset,SourceBytes=source.SourceBytes,SourceSha256=source.SourceSha256,IndexSha256=source.IndexSha256,ResponsePath=source.ResponsePath,JsonPath=path};}}foreach(var p in obj)if(p.Value is IDictionary||p.Value is IList)Walk(p.Value,path+"."+p.Key,depth+1,wanted,names,conflicts,source);
  }else{var list=value as IList;if(list!=null)for(int i=0;i<list.Count;i++)Walk(list[i],path+"["+i+"]",depth+1,wanted,names,conflicts,source);}
 }
 internal static void Apply(List<ImportCandidate> list,Action check){try{var names=OfficialNameStore.Refresh(list.Select(x=>x.Id),check).Names;foreach(var c in list){NameEvidence e;if(names.TryGetValue(c.Id,out e)){c.Name=e.Name;c.NameSource=e;}}}catch(OperationCanceledException){throw;}catch{LastReason="名称持久记录暂不可用，候选检查继续，保留原名";}}
}
