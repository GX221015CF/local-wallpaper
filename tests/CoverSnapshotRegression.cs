#if TEST_BUILD
using System;using System.IO;using System.Linq;using System.Drawing;using System.Drawing.Imaging;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;
internal static class CoverSnapshotRegression {
 static readonly List<string> checks=new List<string>();static void Need(bool v,string name){if(!v)throw new Exception(name);checks.Add(name);}static void Reject(Action action,string name){bool failed=false;try{action();}catch(InvalidDataException){failed=true;}catch(IOException){failed=true;}catch(InvalidOperationException){failed=true;}Need(failed,name);}
 static PreviewFrameTarget target;static RuntimeInputLease lease;static string hash,run,source;
 static void Setup(){source=Path.Combine(Common.Root,"video.mp4");hash=Common.Hash(source);string token=Guid.NewGuid().ToString("N");run=Path.Combine(Common.LogRoot,"run-cover-"+token.Substring(0,8));Directory.CreateDirectory(run);Common.SaveNew(Path.Combine(run,"request.json"),new{mode="preview",runToken=token,expectedSha256=hash,selectedCatalogId="video-A",cwp=source});lease=RuntimeInputs.Begin(run,source,hash,CancellationToken.None);lease.Copy(source,CancellationToken.None);Common.SaveNew(Path.Combine(run,"load-input.json"),new{sourceSha256=hash,runtimeSha256=hash,runtimeCwp=lease.Path});target=new PreviewFrameTarget{Run=run,Token=token,ResourceSha256=hash,RuntimeCwp=lease.Path,CatalogId="video-A",PlayerPid=Process.GetCurrentProcess().Id,PlayerCreation=ProcessProof.Creation(Process.GetCurrentProcess().Handle),PlayerImage=System.Windows.Forms.Application.ExecutablePath};}
 static void Exercise(){Setup();PreviewFrame.ValidateTarget(target);Need(true,"actual-capture-target-accepts-registered-managed-video");CheckRequests();CheckCwpTarget();string original=target.RuntimeCwp;target.RuntimeCwp=source;Reject(()=>PreviewFrame.ValidateTarget(target),"persistent-library-not-a-capture-runtime");target.RuntimeCwp=original;string token=target.Token;target.Token=Guid.NewGuid().ToString("N");Reject(()=>PreviewFrame.ValidateTarget(target),"wrong-run-token-rejected");target.Token=token;
  string h=target.ResourceSha256;target.ResourceSha256=new string('a',64);Reject(()=>PreviewFrame.ValidateTarget(target),"wrong-content-hash-rejected");target.ResourceSha256=h;lease.End(true,false,"test","not-live");Reject(()=>PreviewFrame.ValidateTarget(target),"ended-lease-rejected");lease.Record["terminal"]=false;RuntimeInputs.Save(lease.Directory,lease.Record);PreviewFrame.ValidateTarget(target);
  var lib=new WallpaperLibrary();lib.Doc.Items.Clear();var video=new WallpaperItem{Id="video-A",Name="video",SourcePath=source,RegisteredSha256=hash,PreviewConfirmedSha256=hash};var other=new WallpaperItem{Id="other-B",Name="other",SourcePath=Path.Combine(Common.Root,"other.cwp"),RegisteredSha256=Common.Hash(Path.Combine(Common.Root,"other.cwp"))};lib.Doc.Items.Add(video);lib.Doc.Items.Add(other);lib.Doc.SelectedId=other.Id;lib.Save();lib.DisplayHash(video);string before=File.ReadAllText(lib.FilePath);string image=Path.Combine(Common.Root,"fixture.jpg");using(var bmp=new Bitmap(160,100))using(var g=Graphics.FromImage(bmp)){g.Clear(Color.CornflowerBlue);g.FillRectangle(Brushes.Gold,10,10,65,75);g.FillEllipse(Brushes.White,70,15,75,65);bmp.Save(image,ImageFormat.Jpeg);}string pictureHash=Common.Hash(image);
  var first=PreviewCovers.Publish(image,target,pictureHash,160,100,"synthetic-image-storage-test","TEST_BUILD",false,()=>{});Need(PreviewCovers.ForItem(video)!=null,"cover-bound-to-loaded-resource-despite-other-selection");Need(PreviewCovers.ForItem(other)==null,"other-resource-does-not-inherit-cover");using(var cover=WallpaperLibrary.LoadCover(video))Need(cover!=null&&cover.Width==160&&cover.Height==100,"actual-home-card-cover-loader-reads-snapshot");Need(File.ReadAllText(lib.FilePath)==before,"saving-cover-keeps-selection-and-confirmations-byte-exact");string meta=Path.Combine(Path.GetDirectoryName(PreviewCovers.ImagePath(first)),"binding.json"),saved=Common.Hash(meta);
  Reject(()=>PreviewCovers.Publish(image,target,pictureHash,160,100,"test","test",false,()=>{}),"repeat-requires-explicit-replacement");bool cancel=false;int call=0;try{PreviewCovers.Publish(image,target,pictureHash,160,100,"test","test",true,()=>{if(++call==3)throw new OperationCanceledException();});}catch(OperationCanceledException){cancel=true;}Need(cancel&&Common.Hash(meta)==saved,"cancelled-publish-preserves-previous-cover");using(var locked=new FileStream(meta,FileMode.Open,FileAccess.Read,FileShare.Read)){Reject(()=>PreviewCovers.Publish(image,target,pictureHash,160,100,"test","test",true,()=>{}),"locked-metadata-save-failure");}Need(Common.Hash(meta)==saved,"failed-save-keeps-old-cover");var second=PreviewCovers.Publish(image,target,pictureHash,160,100,"test","explicit-replacement",true,()=>{});Need(second.ImageRelative!=first.ImageRelative&&PreviewCovers.Resolve(hash).ImageRelative==second.ImageRelative,"explicit-replacement-commits-current-frame");Need(!Directory.GetFiles(Path.GetDirectoryName(meta),"*.tmp").Any(),"publication-temp-files-cleaned");Need(!Path.IsPathRooted(second.ImageRelative),"cover-relative-to-current-data-root");using(var black=new Bitmap(120,80))Need(!PreviewFrame.Informative(black),"black-snapshot-rejected-by-actual-image-check");
  File.SetLastWriteTimeUtc(source,DateTime.UtcNow.AddMinutes(1));Need(PreviewCovers.ForItem(video)==null,"changed-resource-signature-does-not-reuse-cover");lib.DisplayHash(video);Need(PreviewCovers.ForItem(video)!=null,"same-content-reverified-restores-correct-cover");Common.SaveNew(Path.Combine(Common.Root,"snapshot-test-state.json"),new{hash=hash,selectedId=other.Id,image=second.ImageRelative});
 }
 static void CheckRequests(){
  var request=new Dictionary<string,object>{
   {"version",1},{"token",target.Token},{"run",target.Run},
   {"resourceSha256",target.ResourceSha256},{"playerPid",target.PlayerPid},
   {"playerCreation",target.PlayerCreation},{"window",target.Window},
   {"render",target.Render},{"action","capture-cover"}
  };
  Need(PreviewControlRequests.Matches(request,target),"actual-request-validator-accepts-capture-action");
  foreach(string field in new[]{"token","run","resourceSha256","playerPid","playerCreation","window","render","version","action"}){
   object original=request[field];request[field]="wrong";
   Need(!PreviewControlRequests.Matches(request,target),"request-rejects-wrong-"+field);
   request[field]=original;
  }
  foreach(string action in new[]{"confirm","close"}){
   request["action"]=action;
   Need(PreviewControlRequests.Matches(request,target),"existing-request-action-preserved-"+action);
  }
 }
 static void CheckCwpTarget(){
  string cwp=Path.Combine(Common.Root,"other.cwp"),cwpHash=Common.Hash(cwp),token=Guid.NewGuid().ToString("N");
  string cwpRun=Path.Combine(Common.LogRoot,"run-cover-"+token.Substring(0,8));
  Directory.CreateDirectory(cwpRun);
  Common.SaveNew(Path.Combine(cwpRun,"request.json"),new{mode="preview",runToken=token,expectedSha256=cwpHash,selectedCatalogId="other-B",cwp=cwp});
  using(var input=RuntimeInputs.Begin(cwpRun,cwp,cwpHash,CancellationToken.None)){
   input.Copy(cwp,CancellationToken.None);
   Common.SaveNew(Path.Combine(cwpRun,"load-input.json"),new{sourceSha256=cwpHash,runtimeSha256=cwpHash,runtimeCwp=input.Path});
   var cwpTarget=new PreviewFrameTarget{Run=cwpRun,Token=token,ResourceSha256=cwpHash,RuntimeCwp=input.Path,CatalogId="other-B"};
   PreviewFrame.ValidateTarget(cwpTarget);
   Need(true,"registered-managed-cwp-target-remains-supported");
   input.End(true,false,"test","normal");
  }
 }
 static void Reopen(){var state=Common.Read(Path.Combine(Common.Root,"snapshot-test-state.json"));var lib=new WallpaperLibrary();var video=lib.Doc.Items.Single(x=>x.Id=="video-A");lib.DisplayHash(video);var b=PreviewCovers.ForItem(video);Need(lib.Doc.SelectedId==Common.S(state,"selectedId"),"independent-process-retains-selection");Need(b!=null&&b.ImageRelative==Common.S(state,"image"),"independent-process-retains-cover-binding");Need(video.PreviewConfirmedSha256==Common.S(state,"hash"),"independent-process-confirmation-unchanged");using(var im=WallpaperLibrary.LoadCover(video))Need(im!=null,"independent-process-card-cover-loads");}
 static int Main(string[] args){if(args.Length==2&&args[0]=="--local-import-worker")return LocalImport.Worker(args[1]);int code=1;try{if(args[0]=="exercise")Exercise();else if(args[0]=="reopen")Reopen();else throw new Exception("unknown mode");Common.SaveNew(args[1],new{passed=true,checks=checks,engineLoaded=false,windowCreated=false,captureActuallyExecuted=false,evidenceKind="Actual runtime-input/capture-target validation, screenshot storage and card loader; synthetic picture, real named video/CWP bytes; no native capture"});code=0;}catch(Exception e){Common.SaveNew(args[1],new{passed=false,error=e.ToString(),checks=checks,windowCreated=false});}finally{if(lease!=null)lease.Dispose();}return code;}
}
#endif
