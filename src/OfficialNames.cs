// Offline, allowlisted wallpaper response bodies only. Never read HTTP headers or account stores.
using System;using System.IO;using System.Linq;using System.Text;using System.Collections;using System.Collections.Generic;using System.Diagnostics;using System.Security.Cryptography;using System.IO.Compression;
internal sealed class NameEvidence {
 public string Id{get;set;}public string Name{get;set;}public string Kind{get;set;}public string CheckedAt{get;set;}public string SourceFile{get;set;}public long SourceOffset{get;set;}public int SourceBytes{get;set;}public string SourceSha256{get;set;}public string IndexSha256{get;set;}public string ResponsePath{get;set;}public string JsonPath{get;set;}
}
internal sealed class NameReadDiagnostics {
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
 static byte[] Address(string root,uint a,int count,Action check,string role,NameReadDiagnostics d,out string file,out long offset){if((a&0x80000000)==0)throw new InvalidDataException("缓存地址无效");int kind=(int)((a>>28)&7);offset=0;if(kind==0)file=Path.Combine(root,"f_"+(a&0xfffffff).ToString("x6"));else{int size=kind==2?256:kind==3?1024:kind==4?4096:0;if(size==0)throw new InvalidDataException("缓存块布局未支持");file=Path.Combine(root,"data_"+((a>>16)&255));offset=8192L+(a&65535)*size;}return Read(file,offset,count,check,role,d);}
 static byte[] Body(byte[] data,Action check){if(data.Length<2||data[0]!=31||data[1]!=139)return data;using(var input=new MemoryStream(data,false))using(var zip=new GZipStream(input,CompressionMode.Decompress))using(var output=new MemoryStream()){byte[] buffer=new byte[32768];int n;while((n=zip.Read(buffer,0,buffer.Length))>0){check();if(output.Length+n>MaxBody)throw new InvalidDataException("名称响应解压超预算");output.Write(buffer,0,n);}return output.ToArray();}}
 static bool Allowed(string p){return p=="/pcwallpapercontent/wallpaper/jingling/wallpaperListDynamic"||p=="/pcwallpapercontent/wallpaper/jingling/queryCarouselWallpaperListByPage"||p=="/pcwallpapercontent/wallpaper/jingling/detailDynamic";}
 internal static Dictionary<string,NameEvidence> ReadNames(IEnumerable<string> ids,Action taskCheck){
  var wanted=new HashSet<string>(ids);var names=new Dictionary<string,NameEvidence>();var conflicts=new HashSet<string>();var clock=Stopwatch.StartNew();LastReason=null;BodiesRead=0;var diag=LastDiagnostics=new NameReadDiagnostics{Outcome="running",Stage="cache-root-check"};Action check=()=>{taskCheck();if(clock.Elapsed.TotalSeconds>5)throw new TimeoutException("名称核查超过5秒预算");};
  try{
   check();string root=CacheRoot,indexPath=Path.Combine(root,"index");FileAttributes rootAttributes;
   try{rootAttributes=File.GetAttributes(root);}catch(FileNotFoundException){diag.Outcome="cache-missing";diag.FailureKind="missing-cache";diag.Stage="cache-root-check";LastReason="未找到官方名称缓存";return names;}catch(DirectoryNotFoundException){diag.Outcome="cache-missing";diag.FailureKind="missing-cache";diag.Stage="cache-root-check";LastReason="未找到官方名称缓存";return names;}
   if((rootAttributes&FileAttributes.Directory)==0)throw new InvalidDataException("名称缓存位置不是目录");
   FileAttributes indexAttributes;diag.Stage="index-check";
   try{indexAttributes=File.GetAttributes(indexPath);}catch(FileNotFoundException){diag.Outcome="cache-missing";diag.FailureKind="missing-index";LastReason="未找到官方列表名称缓存";return names;}catch(DirectoryNotFoundException){diag.Outcome="cache-missing";diag.FailureKind="missing-index";LastReason="未找到官方列表名称缓存";return names;}
   if((indexAttributes&FileAttributes.Directory)!=0)throw new InvalidDataException("名称索引位置不是文件");
   diag.Stage="index-read";diag.ActualIndexLength=new FileInfo(indexPath).Length;byte[] index=Read(indexPath,0,checked((int)diag.ActualIndexLength.Value),check,"index",diag);
   diag.Stage="index-validate";if(index.Length<368)throw new InvalidDataException("官方名称缓存索引头不完整");uint count=U(index,8),buckets=U(index,28),magic=U(index,0),version=U(index,4);diag.IndexMagic=magic;diag.IndexVersion=version;diag.BucketCount=buckets;diag.RecordCount=count;
   if(magic!=0xc103cac3){diag.ValidationField="magic";throw new NotSupportedException("名称缓存索引标识不属于已支持格式");}
   if(version!=0x30000){diag.ValidationField="version";throw new NotSupportedException("名称缓存索引版本尚未支持");}
   if(buckets!=131072){diag.ValidationField="bucket-count";throw new NotSupportedException("名称缓存索引桶数量尚未支持");}
   if(count>2048){diag.ValidationField="record-count";throw new InvalidDataException("名称缓存索引记录数量超过2048项预算");}
   if(index.Length!=368L+(long)buckets*4){diag.ValidationField="index-length";throw new InvalidDataException("名称缓存索引长度与桶数量不一致");}
   string indexHash=Digest(index);var seen=new HashSet<uint>();var fingerprints=new HashSet<string>();int total=0,responses=0;
   diag.Stage="index-traverse";
   for(int bucket=0;bucket<buckets;bucket++){if((bucket&1023)==0){diag.Stage="index-traverse";check();}uint a=U(index,368+bucket*4);while(a!=0){
    if(!seen.Add(a)||seen.Count>2048)throw new InvalidDataException("名称索引循环或超预算");string entryFile;long entryOffset;diag.Stage="index-entry-read";byte[] entry=Address(root,a,256,check,"entry",diag,out entryFile,out entryOffset);diag.Stage="index-entry-validate";if(U(entry,0)%buckets!=bucket)throw new InvalidDataException("名称缓存索引变化");a=U(entry,4);
    // The inline prefix is enough to identify these exact response paths. Never fetch arbitrary long URL keys.
    int length=checked((int)U(entry,32));if(length<=0||length>4096)continue;byte[] key=entry.Skip(96).Take(Math.Min(length,160)).ToArray();
    string text=Encoding.UTF8.GetString(key);int start=text.IndexOf("http",StringComparison.Ordinal);if(start<0)continue;Uri uri;if(!Uri.TryCreate(text.Substring(start),UriKind.Absolute,out uri)||uri.Host!="pc-store.lenovomm.cn"||!Allowed(uri.AbsolutePath))continue;
    if(uri.AbsolutePath.EndsWith("/detailDynamic",StringComparison.Ordinal)){if(length>160)continue;string pair=uri.Query.TrimStart('?').Split('&').FirstOrDefault(q=>q.StartsWith("wallpaperId=",StringComparison.Ordinal));if(pair==null||!wanted.Contains(Uri.UnescapeDataString(pair.Substring(12))))continue;}
    if(responses>=16){LastReason="本轮名称响应达到16项预算，已核验结果保留，缺项稍后核查";continue;}responses++;int size=checked((int)U(entry,44));if(size<=0||size>MaxBody||(total+=size)>8*MaxBody/2)continue;
    try{string file;long offset;diag.Stage="response-read";byte[] stored=Address(root,U(entry,60),size,check,"response",diag,out file,out offset);string fingerprint=Digest(stored);if(!fingerprints.Add(fingerprint))continue;BodiesRead++;diag.BodiesRead=BodiesRead;
     diag.Stage="response-parse";var json=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=MaxBody,RecursionLimit=24};var obj=json.DeserializeObject(new UTF8Encoding(false,true).GetString(Body(stored,check)))as Dictionary<string,object>;
     if(obj==null||!obj.ContainsKey("status")||Convert.ToString(obj["status"])!="0"||!obj.ContainsKey("data"))continue;
     var pending=new Dictionary<string,NameEvidence>();Walk(obj["data"],"$.data",0,wanted,pending,conflicts,new NameEvidence{Kind="official-local-wallpaper-response",CheckedAt=DateTimeOffset.Now.ToString("o"),SourceFile=file,SourceOffset=offset,SourceBytes=size,SourceSha256=fingerprint,IndexSha256=indexHash,ResponsePath=uri.AbsolutePath});
     // Re-read only this entry and body: do not commit a mixed response while official app updates cache.
     diag.Stage="response-consistency-read";if(!entry.SequenceEqual(Read(entryFile,entryOffset,256,check,"entry",diag))||fingerprint!=Digest(Read(file,offset,size,check,"response",diag))){diag.ChangedDuringRead=true;diag.ValidationField="response-changed";if(LastReason==null)LastReason="名称缓存读取期间发生变化，该项已跳过；原名称保留";continue;}
     foreach(var pair in pending){NameEvidence prior;if(names.TryGetValue(pair.Key,out prior)&&prior.Name!=pair.Value.Name)conflicts.Add(pair.Key);else names[pair.Key]=pair.Value;}
    }catch(OperationCanceledException ex){Failure(diag,diag.Stage,ex);throw;}catch(TimeoutException ex){Failure(diag,diag.Stage,ex);throw;}catch(Exception ex){Failure(diag,diag.Stage,ex);if(LastReason==null)LastReason="部分官方名称记录不可读，已跳过；原名称保留";}
   }}
   diag.Stage="index-consistency-read";if(seen.Count!=count||!index.SequenceEqual(Read(indexPath,0,index.Length,check,"index",diag))){diag.ValidationField="index-changed";throw new IOException("名称索引读取期间发生变化，请稍后重试");}foreach(string id in conflicts)names.Remove(id);if(conflicts.Count>0)LastReason="同ID官方名称冲突，保留原名";
   diag.Stage=diag.FailureStage??(diag.ChangedDuringRead?"response-consistency-read":"completed");if(diag.ChangedDuringRead&&diag.ExceptionType==null)diag.FailureKind="changed-during-read";diag.Outcome=diag.ExceptionType!=null?"partial-failure":diag.ChangedDuringRead?"partial-failure":names.Count==0?"checked-no-match":"checked";
  }catch(OperationCanceledException ex){Failure(diag,diag.Stage,ex);diag.Outcome="cancelled";throw;}catch(Exception ex){Failure(diag,diag.Stage,ex);diag.Outcome=diag.FailureKind=="budget"?"budget-exceeded":diag.FailureKind=="unsupported-format"?"format-unsupported":diag.FailureKind=="corrupt-cache"?"cache-damaged":diag.FailureKind=="changed-during-read"?"changed-during-read":diag.FailureKind=="read-failure"?"read-failed":"failed";names.Clear();LastReason="可选名称缓存未完成核查（"+diag.Stage+"；"+diag.FailureKind+"）："+FaultSafety.Text(ex.Message,240)+"；保留原名";}
  diag.BodiesRead=BodiesRead;return names;
 }
 static void Walk(object value,string path,int depth,HashSet<string> wanted,Dictionary<string,NameEvidence> names,HashSet<string> conflicts,NameEvidence source){
  if(depth>16)return;var obj=value as Dictionary<string,object>;if(obj!=null){object idValue,nameValue;if(obj.TryGetValue("wallpaperId",out idValue)&&obj.TryGetValue("wallpaperName",out nameValue)){string id=Convert.ToString(idValue),name=Clean(nameValue as string);if(wanted.Contains(id)&&name!=null){NameEvidence prior;if(names.TryGetValue(id,out prior)&&prior.Name!=name)conflicts.Add(id);else names[id]=new NameEvidence{Id=id,Name=name,Kind=source.Kind,CheckedAt=source.CheckedAt,SourceFile=source.SourceFile,SourceOffset=source.SourceOffset,SourceBytes=source.SourceBytes,SourceSha256=source.SourceSha256,IndexSha256=source.IndexSha256,ResponsePath=source.ResponsePath,JsonPath=path};}}foreach(var p in obj)if(p.Value is IDictionary||p.Value is IList)Walk(p.Value,path+"."+p.Key,depth+1,wanted,names,conflicts,source);
  }else{var list=value as IList;if(list!=null)for(int i=0;i<list.Count;i++)Walk(list[i],path+"["+i+"]",depth+1,wanted,names,conflicts,source);}
 }
 internal static void Apply(List<ImportCandidate> list,Action check){try{var names=OfficialNameStore.Refresh(list.Select(x=>x.Id),check).Names;foreach(var c in list){NameEvidence e;if(names.TryGetValue(c.Id,out e)){c.Name=e.Name;c.NameSource=e;}}}catch(OperationCanceledException){throw;}catch{LastReason="名称持久记录暂不可用，候选检查继续，保留原名";}}
}
