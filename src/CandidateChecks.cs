using System;using System.IO;using System.Linq;using System.Text;using System.Threading;using System.Diagnostics;using System.Security.Cryptography;
internal sealed class CandidateCheckResult {
 public long VideoTransformHashedBytes{get;set;}public int VideoFrameAttempts{get;set;}public long VideoFrameImageBytes{get;set;}public string DeclaredMd5{get;set;}public string FailureKind{get;set;}public int AutoCheckAttempts{get;set;}public string CacheReason{get;set;}public bool CoverRefreshDue{get;set;}public int CoverAttempts{get;set;}public string CoverCheckedAt{get;set;}public long CoverValidationBytes{get;set;}public long CoverDecodedBytes{get;set;}public int CoverReads{get;set;}public double HashMilliseconds{get;set;}public double StructureMilliseconds{get;set;}public double CoverMilliseconds{get;set;}public string CoverKind{get;set;}public int Version{get;set;}public string State{get;set;}public string Phase{get;set;}public string Reason{get;set;}public string Signature{get;set;}public string RecordHash{get;set;}public string Rules{get;set;}public string Sha256{get;set;}public string Md5{get;set;}public string CheckedAt{get;set;}public string CoverRelative{get;set;}public string CoverSha256{get;set;}public string CoverMember{get;set;}public string CoverReason{get;set;}public AnalysisReport Analysis{get;set;}public bool CacheHit{get;set;}public long HashedBytes{get;set;}
}
internal static class CandidateChecks {
 internal static string Rules(){return "candidate-check-3-video-frame|"+Common.Hash(PackageAnalysis.RulesPath);}
 static string Cache(ImportCandidate c){return Path.Combine(Common.Root,"data","candidate-checks",PackageAnalysis.Digest(Encoding.UTF8.GetBytes(c.Source.ToUpperInvariant()+"|"+c.Record.ToUpperInvariant()))+".json");}
 static void Apply(ImportCandidate c,CandidateCheckResult r){c.Check=r;c.Analysis=r.Analysis;c.AnalysisStage=r.Phase;c.AnalysisError=r.State=="failed"?r.Reason:null;c.AnalysisStatus=r.Reason;c.Cover=r.CoverRelative==null?null:Path.GetFullPath(Path.Combine(Common.Root,r.CoverRelative));c.CoverReason=r.CoverReason;c.CoverSourceSha256=r.Sha256;c.CoverMember=r.CoverMember;c.CoverCacheHit=r.CacheHit;}
 internal static long MetadataCandidates, SourceHashedBytes, ImageValidationBytes, PreviewDecodedBytes;
 internal static int PreviewMembersRead;
 internal static double MetadataMilliseconds;
 static bool Miss(ImportCandidate candidate,string reason){candidate.CacheDisposition=reason;return false;}
 static bool PermanentNoCover(string reason){return reason!=null&&new[]{"没有","未找到","不支持","尚未","缺少"}.Any(reason.Contains);}
 static bool Due(string time,int seconds){DateTimeOffset at;return !DateTimeOffset.TryParse(time,out at)||DateTimeOffset.UtcNow-at>=TimeSpan.FromSeconds(seconds);}
 internal static bool Load(ImportCandidate candidate,Action check) {
  var clock=Stopwatch.StartNew();MetadataCandidates++;
  try {
   check();string cache=Cache(candidate);
   if(!File.Exists(cache))return Miss(candidate,"cache-missing");
   LocalImport.NoLinks(cache);
   if(new FileInfo(cache).Length>4194304)return Miss(candidate,"cache-over-budget");
   var result=Common.Json.Deserialize<CandidateCheckResult>(Common.ReadShared(cache));
   if(result==null||result.Version!=1)return Miss(candidate,"cache-format");
   if(result.Signature!=CoverCache.Signature(candidate.Source))return Miss(candidate,"source-identity-changed");
   if(result.RecordHash!=candidate.RecordHash)return Miss(candidate,"record-changed");
   if(result.Rules!=Rules())return Miss(candidate,"rules-changed");
   if((result.DeclaredMd5??result.Md5)!=candidate.Md5)return Miss(candidate,"declared-md5-changed");
   if(!new[]{"passed","unknown","failed"}.Contains(result.State))return Miss(candidate,"unfinished-check");
   if(result.State!="failed"&&!PackageAnalysis.HashText(result.Sha256))return Miss(candidate,"source-hash-missing");
   if(result.State=="passed"&&result.Md5!=candidate.Md5)return Miss(candidate,"source-md5-mismatch");
   if(result.State=="failed"&&(result.FailureKind=="io"||result.FailureKind=="timeout")&&result.AutoCheckAttempts<3&&Due(result.CheckedAt,60)){candidate.Check=result;return Miss(candidate,"transient-check-retry-due");}
   result.HashedBytes=0;result.CoverValidationBytes=0;result.CoverDecodedBytes=0;result.CoverReads=0;
   result.HashMilliseconds=result.StructureMilliseconds=result.CoverMilliseconds=0;result.VideoFrameAttempts=0;result.VideoFrameImageBytes=0;result.VideoTransformHashedBytes=0;
   result.CoverRefreshDue=false;
   PreviewCovers.Attach(result);
   if(result.CoverRelative!=null){
    bool valid=false;
    if(!Path.IsPathRooted(result.CoverRelative)){
     string cover=Path.GetFullPath(Path.Combine(Common.Root,result.CoverRelative));
     if(LocalImport.Under(cover,Common.Root)&&File.Exists(cover)&&new FileInfo(cover).Length<=8388608){
      LocalImport.NoLinks(cover);result.CoverValidationBytes=new FileInfo(cover).Length;ImageValidationBytes+=result.CoverValidationBytes;
      valid=Common.Hash(cover)==result.CoverSha256;
     }
    }
    if(!valid){result.CoverRelative=null;result.CoverSha256=null;result.CoverReason="封面缓存失效，将单独重试";result.CoverCheckedAt=null;result.CoverRefreshDue=result.State=="passed";}
   }
   if(result.State=="passed"&&result.CoverRelative==null&&!PermanentNoCover(result.CoverReason)&&result.CoverAttempts<3&&Due(result.CoverCheckedAt??result.CheckedAt,60))result.CoverRefreshDue=true;
   result.CacheHit=true;
   result.CacheReason=result.CoverRefreshDue?"identity-structure-hit-cover-pending":result.CoverRelative!=null?"complete-hit":result.State=="passed"?"stable-negative-cover-hit":"structural-negative-hit";
   candidate.CacheDisposition=result.CacheReason;Apply(candidate,result);return true;
  }catch(OperationCanceledException){throw;}
  catch(Exception error){return Miss(candidate,error is IOException||error is UnauthorizedAccessException?"cache-or-source-unreadable":"cache-corrupt");}
  finally{MetadataMilliseconds+=clock.Elapsed.TotalMilliseconds;}
 }
 static void SaveResult(ImportCandidate candidate,CandidateCheckResult result,Action check){
  string target=Cache(candidate);Directory.CreateDirectory(Path.GetDirectoryName(target));check();Common.ReplaceJson(target,result);Apply(candidate,result);
 }
 static void FinishCover(ImportCandidate candidate,CandidateCheckResult result){
  result.CoverRelative=null;result.CoverSha256=null;result.CoverMember=null;
  if(candidate.Cover!=null){
   if(!LocalImport.Under(candidate.Cover,Common.Root))throw new InvalidDataException("封面缓存不属于本数据目录");
   result.CoverRelative=candidate.Cover.Substring(Common.Root.TrimEnd('\\').Length+1);result.CoverSha256=Common.Hash(candidate.Cover);result.CoverMember=candidate.CoverMember;
  }
  result.CoverReason=candidate.CoverReason??"未取得可靠静态图片";result.CoverCheckedAt=DateTimeOffset.UtcNow.ToString("o");result.CoverRefreshDue=false;
  PreviewCovers.Attach(result);
 }
 static void RefreshCover(ImportCandidate candidate,Action check,Action<string> progress){
  var result=candidate.Check;var clock=Stopwatch.StartNew();long bytes=CoverCache.PreviewBytes+V3Package.PreviewDecodedBytes;int reads=CoverCache.Reads+V3Package.PreviewReads;int frames=VideoCovers.FrameAttempts;long frameBytes=VideoCovers.FrameImageBytes;long transformBytes=VideoPackage.CoverTransformHashedBytes;
  check();if(progress!=null)progress("复用身份与结构，单独更新封面");result.CoverAttempts++;
  if(VideoPackage.IsVideo(candidate.Format))VideoCovers.Ensure(candidate,candidate.Source,result.Sha256,check);
  else CoverCache.EnsureVerified(candidate,result.Sha256,result.Md5,check);
  if(CoverCache.Signature(candidate.Source)!=result.Signature||LocalImport.ReadRecord(candidate.Record).RecordHash!=result.RecordHash)throw new InvalidDataException("封面处理期间资源或记录发生变化，请重新检查");
  FinishCover(candidate,result);result.CoverMilliseconds=clock.Elapsed.TotalMilliseconds;
  result.CoverDecodedBytes=CoverCache.PreviewBytes+V3Package.PreviewDecodedBytes-bytes;result.CoverReads=CoverCache.Reads+V3Package.PreviewReads-reads;
  result.VideoFrameAttempts=VideoCovers.FrameAttempts-frames;result.VideoFrameImageBytes=VideoCovers.FrameImageBytes-frameBytes;result.VideoTransformHashedBytes=VideoPackage.CoverTransformHashedBytes-transformBytes;PreviewDecodedBytes+=result.CoverDecodedBytes;PreviewMembersRead+=result.CoverReads;
  result.CacheReason="identity-structure-hit-cover-refreshed";SaveResult(candidate,result,check);
 }
 internal static void Run(ImportCandidate c,Action cancel,Action<string> progress,bool force=false){
  var watch=Stopwatch.StartNew();Action check=()=>{cancel();if(watch.Elapsed.TotalSeconds>35)throw new TimeoutException("单项检查超过35秒预算");};
  var result=new CandidateCheckResult{Version=1,State="checking",Phase="核对本地记录",CheckedAt=DateTimeOffset.UtcNow.ToString("o")};
  var phaseClock=Stopwatch.StartNew();string costPhase="metadata";
  Action<string> phase=p=>{
   double elapsed=phaseClock.Elapsed.TotalMilliseconds;
   if(costPhase=="structure")result.StructureMilliseconds+=elapsed;
   else if(costPhase=="cover")result.CoverMilliseconds+=elapsed;
   costPhase=p.Contains("封面")||p.Contains("视频画面")?"cover":p.Contains("完整SHA")?"hash":p.Contains("复核源")||p.Contains("核对本地")?"metadata":"structure";
   phaseClock.Restart();result.Phase=p;if(progress!=null)progress(p);check();
  };c.Check=null;c.Analysis=null;c.Cover=null;c.CoverReason="等待后台读取";
  try{
   phase("核对本地记录");var current=LocalImport.ReadRecord(c.Record);if(current==null||current.Id!=c.Id||current.Source!=c.Source||current.Md5!=c.Md5)throw new InvalidDataException("本地关联记录已变化，请刷新候选");c.RecordHash=current.RecordHash;c.Bytes=current.Bytes;c.Ticks=current.Ticks;c.Format=current.Format;
   if(!force&&Load(c,check)){if(c.Check.CoverRefreshDue)RefreshCover(c,check,progress);else if(progress!=null)progress("复用已验证的检查及封面缓存");return;}
   string miss=c.CacheDisposition;c.CacheDisposition=force?"explicit-recheck":miss;
   phase("检查文件稳定性");string before=CoverCache.Signature(c.Source);int delay=(int)Math.Max(150,Math.Min(2100,2100-(DateTime.UtcNow-File.GetLastWriteTimeUtc(c.Source)).TotalMilliseconds));for(int elapsed=0;elapsed<delay;elapsed+=50){check();Thread.Sleep(50);}
   if(CoverCache.Signature(c.Source)!=before){result.State="waiting";result.Reason="本次观察到文件仍在变化，等待稳定后再检查";Apply(c,result);return;}
   result.Signature=before;result.RecordHash=c.RecordHash;result.Rules=Rules();result.DeclaredMd5=c.Md5;result.AutoCheckAttempts=c.Check==null?1:c.Check.AutoCheckAttempts+1;
   c.Format=LocalImport.CheckHeader(c.Source);if(c.Bytes>256L*1048576)throw new InvalidDataException("文件超过256MiB单项自动检查预算；未入库，可保留记录供后续处理");
   phase("完整SHA-256及本地记录MD5校验");var hashClock=Stopwatch.StartNew();
   using(var f=new FileStream(c.Source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var sha=SHA256.Create())using(var md5=MD5.Create()){
    byte[] buffer=new byte[262144];int n;while((n=f.Read(buffer,0,buffer.Length))>0){check();result.HashedBytes+=n;if(result.HashedBytes>c.Bytes)throw new InvalidDataException("源文件读取中增长");sha.TransformBlock(buffer,0,n,null,0);md5.TransformBlock(buffer,0,n,null,0);
#if TEST_BUILD
     if(CoverCache.TestDelay>0)Thread.Sleep(CoverCache.TestDelay);
#endif
    }sha.TransformFinalBlock(new byte[0],0,0);md5.TransformFinalBlock(new byte[0],0,0);result.Sha256=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();result.Md5=BitConverter.ToString(md5.Hash).Replace("-","").ToLowerInvariant();
   }
   result.HashMilliseconds=hashClock.Elapsed.TotalMilliseconds;SourceHashedBytes+=result.HashedBytes;
   if(result.HashedBytes!=c.Bytes||result.Md5!=c.Md5)throw new InvalidDataException("文件长度或MD5与本地关联记录不符；尚未通过入库前检查");
   long coverBytesBefore=CoverCache.PreviewBytes+V3Package.PreviewDecodedBytes;int coverReadsBefore=CoverCache.Reads+V3Package.PreviewReads;int frames=VideoCovers.FrameAttempts;long frameBytes=VideoCovers.FrameImageBytes;long transformBytes=VideoPackage.CoverTransformHashedBytes;
   if(VideoPackage.IsVideo(c.Format)){phase("视频封装、AVC轨道及媒体引用检查");result.Reason=VideoPackage.Validate(c.Source,check);result.State="passed";phase("读取视频画面作为封面");VideoCovers.Ensure(c,c.Source,result.Sha256,check);result.CoverKind=VideoCovers.Kind;}else if(c.Format=="CWP-v2"){
    phase("v2目录范围及成员约束检查");CoverCache.ValidateV2Directory(c.Source,check);phase("读取真实静态封面");CoverCache.EnsureVerified(c,result.Sha256,result.Md5,check);result.State="passed";result.Reason="v2声明长度、稳定性、完整哈希、记录MD5及已知目录约束通过；未解码全部媒体，本次不验证播放";
   }else if(c.Format=="CWP-v3"){
    var r=PackageAnalysis.Analyze(c.Source,check,force,result.Sha256,p=>phase(p));result.Analysis=r;c.Analysis=r;c.Cover=PackageAnalysis.CoverPath(r);c.CoverReason=r.CoverReason??r.Reason;c.CoverMember=r.CoverMember;result.State=r.StructureRecognized&&r.ContainerValidated&&r.FileStable?"passed":r.Structure==null?"unknown":"failed";result.Reason=r.Reason;
   }else{result.State="unknown";result.Reason="当前规则没有实现此容器版本的入库前结构检查："+c.Format;}
   result.CoverDecodedBytes=CoverCache.PreviewBytes+V3Package.PreviewDecodedBytes-coverBytesBefore;result.CoverReads=CoverCache.Reads+V3Package.PreviewReads-coverReadsBefore;result.VideoFrameAttempts=VideoCovers.FrameAttempts-frames;result.VideoFrameImageBytes=VideoCovers.FrameImageBytes-frameBytes;result.VideoTransformHashedBytes=VideoPackage.CoverTransformHashedBytes-transformBytes;PreviewDecodedBytes+=result.CoverDecodedBytes;PreviewMembersRead+=result.CoverReads;
   phase("复核源文件身份");var after=LocalImport.ReadRecord(c.Record);if(after==null||after.RecordHash!=c.RecordHash||after.Bytes!=c.Bytes||CoverCache.Signature(c.Source)!=before)throw new InvalidDataException("检查中记录或资源发生变化，结果不用于导入；请重新检查");
   FinishCover(c,result);result.CoverAttempts=result.CoverRelative==null?1:0;result.Phase="检查完成";result.CacheReason=force?"explicit-recheck":"full-check";
   SaveResult(c,result,check);ResourceDiscovery.RecordChecked(c);
  }catch(OperationCanceledException){result.State="cancelled";result.Reason="已取消；未导入，可重新检查此文件";Apply(c,result);throw;}
  catch(Exception ex){
   result.State="failed";result.Reason=ex.Message;result.CoverReason="检查未完成";
   result.FailureKind=ex is IOException||ex is UnauthorizedAccessException?"io":ex is TimeoutException?"timeout":"deterministic";
   Apply(c,result);
   if(result.Signature!=null&&result.RecordHash!=null&&result.Rules!=null)try{SaveResult(c,result,check);}catch(OperationCanceledException){throw;}catch(Exception storage){result.Reason+="；检查结果缓存未保存："+storage.Message;}
  }
 }
 internal static bool Passed(ImportCandidate c){return c!=null&&c.Check!=null&&c.Check.State=="passed"&&PackageAnalysis.HashText(c.Check.Sha256);}
}
