// Known local records only. No URL, IPC, engine, account or system-startup access.
using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Security.Cryptography;
internal sealed class DiscoveryEntry {
 public string Key{get;set;}public string Id{get;set;}public string Source{get;set;}public string Signature{get;set;}public string RecordHash{get;set;}public string FirstSeen{get;set;}public long StableSinceTicks{get;set;}public bool Historical{get;set;}public bool Acknowledged{get;set;}public string VerifiedSha256{get;set;}public string VerifiedMd5{get;set;}public string Error{get;set;}public int VerifyAttempts{get;set;}public long RetryAfterTicks{get;set;}
}
internal sealed class DiscoveryDocument {public int Version{get;set;}public bool Initialized{get;set;}public List<DiscoveryEntry> Entries{get;set;}public List<string> AcknowledgedHashes{get;set;}}
internal sealed class DiscoverySummary {public List<ImportCandidate> Candidates{get;set;}public int UnreadComplete{get;set;}public int Pending{get;set;}public long HashedBytes{get;set;}public bool FirstBaseline{get;set;}public string Warning{get;set;}public string Outcome{get;set;}public string FailureStage{get;set;}public string FailureKind{get;set;}public int RecordsSeen{get;set;}public int ValidRecords{get;set;}public int MissingResources{get;set;}public int CorruptRecords{get;set;}public int UnsupportedRecords{get;set;}public int ReadFailures{get;set;}public int UnstableResources{get;set;}}
internal static class ResourceDiscovery {
 internal const int CoverBatch=8;
 internal static string StatePath{get{return Path.Combine(Common.Root,"data/discovery/state.json");}}
 static DiscoveryDocument Load(){if(!File.Exists(StatePath))return new DiscoveryDocument{Version=1,Entries=new List<DiscoveryEntry>()};var d=Common.Json.Deserialize<DiscoveryDocument>(Common.ReadShared(StatePath));if(d==null||d.Version!=1||d.Entries==null||d.Entries.Count>8192)throw new InvalidDataException("资源发现记录不可读，未覆盖原记录");return d;}
 static void Save(DiscoveryDocument d){Directory.CreateDirectory(Path.GetDirectoryName(StatePath));Common.ReplaceJson(StatePath,d);}
 static string Key(ImportCandidate c){return c.Id+"|"+c.Md5+"|"+Path.GetFullPath(c.Record).ToUpperInvariant();}
 static bool Ready(ImportCandidate c){return c.Status!=null&&c.Status.StartsWith("可导入",StringComparison.Ordinal);}
 internal static void RecordChecked(ImportCandidate c){if(!CandidateChecks.Passed(c))return;var doc=Load();var entry=doc.Entries.FirstOrDefault(e=>e.Key==Key(c));if(entry==null||entry.Signature!=c.Check.Signature||entry.RecordHash!=c.RecordHash)return;entry.VerifiedSha256=c.Check.Sha256;entry.VerifiedMd5=c.Md5;entry.Error=null;entry.Acknowledged=true;doc.AcknowledgedHashes=(doc.AcknowledgedHashes??new List<string>()).Concat(new[]{c.Check.Sha256}).Distinct().Take(8192).ToList();Save(doc);}
 internal static DiscoverySummary Scan(bool acknowledge,Action check,IEnumerable<string> knownHashes=null){
  var doc=Load();bool first=!doc.Initialized;var result=new DiscoverySummary{Candidates=new List<ImportCandidate>(),FirstBaseline=first,Outcome="checking"};var known=new HashSet<string>(knownHashes??new string[0]);var unreadHashes=new HashSet<string>();var acknowledgedHashes=new HashSet<string>((doc.AcknowledgedHashes??new List<string>()).Concat(doc.Entries.Where(e=>e.Acknowledged&&e.VerifiedSha256!=null).Select(e=>e.VerifiedSha256)));string dir=Path.Combine(LocalImport.DataRoot,"cache_info");int count=0,verified=0;var now=DateTimeOffset.UtcNow;
  if(!Directory.Exists(dir)){result.Outcome="layout-unavailable";result.FailureStage="record-directory";result.FailureKind="missing-layout";return result;}LocalImport.NoLinks(dir);
  try{foreach(string path in Directory.EnumerateFiles(dir,"*.dat",SearchOption.TopDirectoryOnly)){
   if(++count>2048)throw new InvalidDataException("本地记录超过2048项，本次停止轻量检查");result.RecordsSeen=count;check();ImportCandidate c;
   try{c=LocalImport.ReadRecord(path);if(c==null){result.UnsupportedRecords++;continue;}result.ValidRecords++;}catch(OperationCanceledException){throw;}catch(UnauthorizedAccessException){result.ReadFailures++;continue;}catch(IOException){result.ReadFailures++;continue;}catch(InvalidDataException){result.CorruptRecords++;continue;}catch(NotSupportedException){result.UnsupportedRecords++;continue;}catch{result.ReadFailures++;continue;}
   if(!File.Exists(c.Source)){result.MissingResources++;c.DiscoveryStatus="缓存记录存在，但对应CWP文件缺失；可添加已有CWP文件";c.Status="文件缺失";result.Candidates.Add(c);continue;}
   if(c.Status!=null&&c.Status.StartsWith("暂不可导入",StringComparison.Ordinal)){if(c.SourceIssueKind=="damaged-resource")result.CorruptRecords++;else if(c.SourceIssueKind=="read-failed")result.ReadFailures++;else result.UnsupportedRecords++;c.DiscoveryStatus=c.Status;result.Candidates.Add(c);continue;}
   string sig;try{sig=CoverCache.Signature(c.Source);}catch(UnauthorizedAccessException){result.ReadFailures++;c.DiscoveryStatus="CWP文件无权读取";result.Candidates.Add(c);continue;}catch(IOException){result.UnstableResources++;c.DiscoveryStatus="CWP文件读取失败或正在变化；稍后可重试";result.Candidates.Add(c);continue;}string key=Key(c);var entry=doc.Entries.FirstOrDefault(e=>e.Key==key);
   if(entry==null){if(doc.Entries.Count>=8192)throw new InvalidDataException("资源发现历史达到8192项上限");entry=new DiscoveryEntry{Key=key,Id=c.Id,Source=c.Source,Signature=sig,FirstSeen=now.ToString("o"),StableSinceTicks=now.UtcTicks,Historical=first};doc.Entries.Add(entry);}
   else if(entry.Signature!=sig){entry.Signature=sig;entry.StableSinceTicks=now.UtcTicks;entry.FirstSeen=now.ToString("o");entry.Historical=false;entry.Acknowledged=false;entry.VerifiedSha256=null;entry.VerifiedMd5=null;entry.Error=null;entry.VerifyAttempts=0;entry.RetryAfterTicks=0;}
   entry.RecordHash=c.RecordHash;entry.RecordHash=c.RecordHash;c.FirstDiscoveredAt=entry.FirstSeen;c.RecordUpdatedAt=File.GetLastWriteTimeUtc(c.Record).ToString("o");c.SourceModifiedAt=File.GetLastWriteTimeUtc(c.Source).ToString("o");c.HistoricalAtFirstScan=entry.Historical;c.NewlyDiscovered=!entry.Historical&&!entry.Acknowledged;
   bool stable=Ready(c)&&now.UtcTicks-entry.StableSinceTicks>=TimeSpan.FromSeconds(2).Ticks;
   if(!entry.Historical&&entry.VerifiedSha256==null&&stable&&verified==0&&entry.VerifyAttempts<2&&now.UtcTicks>=entry.RetryAfterTicks){
    if(c.Bytes>256L*1048576)entry.Error="大文件待手动导入完整校验";
    else {verified++;entry.VerifyAttempts++;try{Verify(c,entry,check,result);}catch(OperationCanceledException){throw;}catch(Exception ex){entry.Error=ex is InvalidDataException?ex.Message:"文件校验未完成，请稍后重试";entry.VerifiedSha256=null;entry.RetryAfterTicks=now.AddSeconds(30).UtcTicks;}}
   }
   c.DiscoveredSha256=entry.VerifiedSha256;
   if(!Ready(c)){c.DiscoveryStatus="声明长度或格式尚未通过检查";result.Pending++;}
   else if(entry.Historical)c.DiscoveryStatus="历史资源";
   else if(entry.VerifiedSha256!=null)c.DiscoveryStatus=known.Contains(entry.VerifiedSha256)?"已在列表 · 新发现记录":"新发现 · 文件校验通过";
   else {c.DiscoveryStatus=entry.Error??(stable?"新候选 · 等待完整校验":"新候选 · 等待文件稳定");result.Pending++;}
   if(Ready(c)&&!entry.Historical&&entry.VerifiedSha256==null)c.Status=c.Bytes>256L*1048576||entry.VerifyAttempts>=2?"可导入 · 需手动完整校验":"待后台完整校验";
   if(acknowledge&&entry.VerifiedSha256!=null){entry.Acknowledged=true;acknowledgedHashes.Add(entry.VerifiedSha256);}
   if(!entry.Historical&&entry.VerifiedSha256!=null&&!entry.Acknowledged&&!known.Contains(entry.VerifiedSha256)&&unreadHashes.Add(entry.VerifiedSha256))result.UnreadComplete++;
   result.Candidates.Add(c);
  }
  }catch(OperationCanceledException){throw;}catch(UnauthorizedAccessException ex){result.Outcome="read-failed";result.FailureStage="record-enumeration";result.FailureKind="access-denied";result.Warning=ex.Message;return result;}catch(IOException ex){result.Outcome="read-failed";result.FailureStage="record-enumeration";result.FailureKind="io";result.Warning=ex.Message;return result;}
  result.UnreadComplete=unreadHashes.Except(acknowledgedHashes).Count();doc.AcknowledgedHashes=acknowledgedHashes.Take(8192).ToList();doc.Initialized=true;Save(doc);
  result.Outcome=result.RecordsSeen==0?"no-records":result.Candidates.Count>0?"records-found":result.CorruptRecords>0||result.UnsupportedRecords>0?"records-unusable":"checked-no-importable-candidate";
  result.Candidates=result.Candidates.OrderByDescending(c=>c.NewlyDiscovered).ThenByDescending(c=>c.NewlyDiscovered?c.FirstDiscoveredAt:c.RecordUpdatedAt,StringComparer.Ordinal).ThenBy(c=>c.Id,StringComparer.Ordinal).ToList();return result;
 }
 static void Verify(ImportCandidate c,DiscoveryEntry entry,Action check,DiscoverySummary result){
  string before=CoverCache.Signature(c.Source);string hash,md5;long bytes=0;
  using(var f=new FileStream(c.Source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var h=SHA256.Create())using(var m=MD5.Create()){
   byte[] buffer=new byte[262144];int n;while((n=f.Read(buffer,0,buffer.Length))>0){check();bytes+=n;if(bytes>c.Bytes)throw new InvalidDataException("源文件读取期间增长");h.TransformBlock(buffer,0,n,null,0);m.TransformBlock(buffer,0,n,null,0);}h.TransformFinalBlock(new byte[0],0,0);m.TransformFinalBlock(new byte[0],0,0);hash=BitConverter.ToString(h.Hash).Replace("-","").ToLowerInvariant();md5=BitConverter.ToString(m.Hash).Replace("-","").ToLowerInvariant();
  }
  result.HashedBytes+=bytes;var after=LocalImport.ReadRecord(c.Record);
  if(bytes!=c.Bytes||md5!=c.Md5||after==null||after.RecordHash!=c.RecordHash||after.Bytes!=c.Bytes||before!=CoverCache.Signature(c.Source))throw new InvalidDataException("文件变化或MD5不符，尚未作为完整新资源提示");LocalImport.CheckHeader(c.Source);entry.VerifiedSha256=hash;entry.VerifiedMd5=md5;entry.Error=null;
 }
}
internal static class DiscoveryPresentation {
 static string AddIssues(string text,DiscoverySummary s){var issues=new List<string>();if(s.CorruptRecords>0)issues.Add(s.CorruptRecords+"条记录损坏");if(s.UnsupportedRecords>0)issues.Add(s.UnsupportedRecords+"条格式未支持");if(s.ReadFailures>0)issues.Add(s.ReadFailures+"条记录无法读取");if(s.MissingResources>0)issues.Add(s.MissingResources+"个壁纸文件缺失");if(s.UnstableResources>0)issues.Add(s.UnstableResources+"个文件读取失败或正在变化");return issues.Count==0?text:text+"（另有"+String.Join("、",issues)+"）";}
 internal static string Text(DiscoverySummary s){
  if(s==null)return "联想缓存检查未完成。可以更改来源、添加已有CWP文件，或稍后重试。";
  if(s.Outcome=="read-failed")return "联想缓存暂时无法读取。可以更改来源、添加已有CWP文件，或稍后重试。";
  if(s.Outcome=="layout-unavailable")return "已保存的壁纸来源目前不可用。可以更改来源、添加已有CWP文件，或稍后重试。";
  if(s.Outcome=="no-records")return "已找到联想缓存目录，但没有可导入记录。可以添加已有CWP文件，或以后再检查来源。";
  if(s.UnreadComplete>0)return AddIssues("发现 "+s.UnreadComplete+" 项可导入的新壁纸 · 点击查看并导入",s);
  if(s.Pending>0)return AddIssues("发现 "+s.RecordsSeen+" 条本机记录，正在核验其中可用的壁纸…",s);
  if(s.MissingResources>0&&s.Candidates.Count==s.MissingResources)return AddIssues("缓存记录存在，但对应壁纸文件缺失。可以更改来源，或添加已有CWP文件。",s);
  if(s.RecordsSeen>0&&s.Candidates.Count==0)return AddIssues("已检查 "+s.RecordsSeen+" 条本机记录。可以更改来源或添加已有CWP文件。",s);
  if(s.Candidates.Count>0)return AddIssues("已找到 "+s.Candidates.Count+" 条本机壁纸记录 · 点击查看状态并导入",s);
  return "已检查本机缓存，目前没有可导入内容。可以添加已有CWP文件，或更改来源。";
 }
}
