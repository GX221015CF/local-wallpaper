using System;using System.IO;using System.Linq;using System.Text;using System.Threading;using System.Diagnostics;using System.Security.Cryptography;
internal sealed class CandidateCheckResult {
 public string CoverKind{get;set;}public int Version{get;set;}public string State{get;set;}public string Phase{get;set;}public string Reason{get;set;}public string Signature{get;set;}public string RecordHash{get;set;}public string Rules{get;set;}public string Sha256{get;set;}public string Md5{get;set;}public string CheckedAt{get;set;}public string CoverRelative{get;set;}public string CoverSha256{get;set;}public string CoverMember{get;set;}public string CoverReason{get;set;}public AnalysisReport Analysis{get;set;}public bool CacheHit{get;set;}public long HashedBytes{get;set;}
}
internal static class CandidateChecks {
 internal static string Rules(){return "candidate-check-3-video-frame|"+Common.Hash(PackageAnalysis.RulesPath);}
 static string Cache(ImportCandidate c){return Path.Combine(Common.Root,"data","candidate-checks",PackageAnalysis.Digest(Encoding.UTF8.GetBytes(c.Source.ToUpperInvariant()+"|"+c.Record.ToUpperInvariant()))+".json");}
 static void Apply(ImportCandidate c,CandidateCheckResult r){c.Check=r;c.Analysis=r.Analysis;c.AnalysisStage=r.Phase;c.AnalysisError=r.State=="failed"?r.Reason:null;c.AnalysisStatus=r.Reason;c.Cover=r.CoverRelative==null?null:Path.GetFullPath(Path.Combine(Common.Root,r.CoverRelative));c.CoverReason=r.CoverReason;c.CoverSourceSha256=r.Sha256;c.CoverMember=r.CoverMember;c.CoverCacheHit=r.CacheHit;}
 internal static bool Load(ImportCandidate c,Action check){try{check();string cache=Cache(c);if(!File.Exists(cache)||new FileInfo(cache).Length>4194304)return false;var r=Common.Json.Deserialize<CandidateCheckResult>(Common.ReadShared(cache));if(r.Version!=1||r.Signature!=CoverCache.Signature(c.Source)||r.RecordHash!=c.RecordHash||r.Rules!=Rules()||!PackageAnalysis.HashText(r.Sha256)||r.Md5!=c.Md5)return false;PreviewCovers.Attach(r);if((r.State=="failed"||r.State=="passed"&&r.CoverRelative==null)&&DateTimeOffset.UtcNow-DateTimeOffset.Parse(r.CheckedAt)>TimeSpan.FromSeconds(5))return false;if(r.CoverRelative!=null){if(Path.IsPathRooted(r.CoverRelative))return false;string cover=Path.GetFullPath(Path.Combine(Common.Root,r.CoverRelative));if(!LocalImport.Under(cover,Common.Root)||!File.Exists(cover)||new FileInfo(cover).Length>8388608||Common.Hash(cover)!=r.CoverSha256)return false;}r.CacheHit=true;r.HashedBytes=0;Apply(c,r);return true;}catch(OperationCanceledException){throw;}catch{return false;}}
 internal static void Run(ImportCandidate c,Action cancel,Action<string> progress,bool force=false){
  var watch=Stopwatch.StartNew();Action check=()=>{cancel();if(watch.Elapsed.TotalSeconds>35)throw new TimeoutException("单项检查超过35秒预算");};
  var result=new CandidateCheckResult{Version=1,State="checking",Phase="核对本地记录",CheckedAt=DateTimeOffset.UtcNow.ToString("o")};
  Action<string> phase=p=>{result.Phase=p;if(progress!=null)progress(p);check();};c.Check=null;c.Analysis=null;c.Cover=null;c.CoverReason="等待后台读取";
  try{
   phase("核对本地记录");var current=LocalImport.ReadRecord(c.Record);if(current==null||current.Id!=c.Id||current.Source!=c.Source||current.Md5!=c.Md5)throw new InvalidDataException("本地关联记录已变化，请刷新候选");c.RecordHash=current.RecordHash;c.Bytes=current.Bytes;c.Ticks=current.Ticks;c.Format=current.Format;
   if(!force&&Load(c,check)){if(progress!=null)progress("复用已验证的检查及封面缓存");return;}
   phase("检查文件稳定性");string before=CoverCache.Signature(c.Source);int delay=(int)Math.Max(150,Math.Min(2100,2100-(DateTime.UtcNow-File.GetLastWriteTimeUtc(c.Source)).TotalMilliseconds));for(int elapsed=0;elapsed<delay;elapsed+=50){check();Thread.Sleep(50);}
   if(CoverCache.Signature(c.Source)!=before){result.State="waiting";result.Reason="本次观察到文件仍在变化，等待稳定后再检查";Apply(c,result);return;}
   c.Format=LocalImport.CheckHeader(c.Source);if(c.Bytes>256L*1048576)throw new InvalidDataException("文件超过256MiB单项自动检查预算；未入库，可保留记录供后续处理");result.Signature=before;result.RecordHash=c.RecordHash;result.Rules=Rules();
   phase("完整SHA-256及本地记录MD5校验");
   using(var f=new FileStream(c.Source,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var sha=SHA256.Create())using(var md5=MD5.Create()){
    byte[] buffer=new byte[262144];int n;while((n=f.Read(buffer,0,buffer.Length))>0){check();result.HashedBytes+=n;if(result.HashedBytes>c.Bytes)throw new InvalidDataException("源文件读取中增长");sha.TransformBlock(buffer,0,n,null,0);md5.TransformBlock(buffer,0,n,null,0);
#if TEST_BUILD
     if(CoverCache.TestDelay>0)Thread.Sleep(CoverCache.TestDelay);
#endif
    }sha.TransformFinalBlock(new byte[0],0,0);md5.TransformFinalBlock(new byte[0],0,0);result.Sha256=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();result.Md5=BitConverter.ToString(md5.Hash).Replace("-","").ToLowerInvariant();
   }
   if(result.HashedBytes!=c.Bytes||result.Md5!=c.Md5)throw new InvalidDataException("文件长度或MD5与本地关联记录不符；尚未通过入库前检查");
   if(VideoPackage.IsVideo(c.Format)){phase("视频封装、AVC轨道及媒体引用检查");result.Reason=VideoPackage.Validate(c.Source,check);result.State="passed";phase("读取视频画面作为封面");VideoCovers.Ensure(c,c.Source,result.Sha256,check);result.CoverKind=VideoCovers.Kind;}else if(c.Format=="CWP-v2"){
    phase("v2目录范围及成员约束检查");CoverCache.ValidateV2Directory(c.Source,check);phase("读取真实静态封面");CoverCache.EnsureVerified(c,result.Sha256,result.Md5,check);result.State="passed";result.Reason="v2声明长度、稳定性、完整哈希、记录MD5及已知目录约束通过；未解码全部媒体，本次不验证播放";
   }else if(c.Format=="CWP-v3"){
    var r=PackageAnalysis.Analyze(c.Source,check,force,result.Sha256,p=>phase(p));result.Analysis=r;c.Analysis=r;c.Cover=PackageAnalysis.CoverPath(r);c.CoverReason=r.CoverReason??r.Reason;c.CoverMember=r.CoverMember;result.State=r.StructureRecognized&&r.ContainerValidated&&r.FileStable?"passed":r.Structure==null?"unknown":"failed";result.Reason=r.Reason;
   }else{result.State="unknown";result.Reason="当前规则没有实现此容器版本的入库前结构检查："+c.Format;}
   phase("复核源文件身份");var after=LocalImport.ReadRecord(c.Record);if(after==null||after.RecordHash!=c.RecordHash||after.Bytes!=c.Bytes||CoverCache.Signature(c.Source)!=before)throw new InvalidDataException("检查中记录或资源发生变化，结果不用于导入；请重新检查");
   if(c.Cover!=null){if(!LocalImport.Under(c.Cover,Common.Root))throw new InvalidDataException("封面缓存不属于本数据目录");result.CoverRelative=c.Cover.Substring(Common.Root.TrimEnd('\\').Length+1);result.CoverSha256=Common.Hash(c.Cover);result.CoverMember=c.CoverMember;}
   result.CoverReason=c.CoverReason??"未取得可靠静态图片";PreviewCovers.Attach(result);result.Phase="检查完成";string target=Cache(c);Directory.CreateDirectory(Path.GetDirectoryName(target));check();Common.ReplaceJson(target,result);Apply(c,result);ResourceDiscovery.RecordChecked(c);
  }catch(OperationCanceledException){result.State="cancelled";result.Reason="已取消；未导入，可重新检查此文件";Apply(c,result);throw;}
  catch(Exception ex){result.State="failed";result.Reason=ex.Message;result.CoverReason="检查未完成，未使用旧封面";Apply(c,result);}
 }
 internal static bool Passed(ImportCandidate c){return c!=null&&c.Check!=null&&c.Check.State=="passed"&&PackageAnalysis.HashText(c.Check.Sha256);}
}
