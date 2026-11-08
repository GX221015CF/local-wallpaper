using System;using System.IO;using System.Linq;using System.Threading.Tasks;using System.Diagnostics;
internal static class PreviewControlRequests {
 internal static bool Matches(System.Collections.Generic.Dictionary<string,object> request,PreviewFrameTarget target){
  if(target==null)return false;
  try{
   string action=Common.S(request,"action");
   return Convert.ToInt32(request["version"])==1
    &&Common.S(request,"token")==target.Token
    &&Common.S(request,"run")==target.Run
    &&Common.S(request,"resourceSha256")==target.ResourceSha256
    &&Convert.ToInt32(request["playerPid"])==target.PlayerPid
    &&Convert.ToInt64(request["playerCreation"])==target.PlayerCreation
    &&Convert.ToInt64(request["window"])==target.Window
    &&Convert.ToInt64(request["render"])==target.Render
    &&(action=="confirm"||action=="close"||action=="capture-cover");
  }catch{return false;}
 }
}
internal sealed partial class ControlWindow {
 long previewControlSequence;string previewControlRun,previewControlStamp;bool previewStopPending;
 WallpaperItem LoadedPreview(out PreviewFrameTarget target,bool allowStarting=false){string why;if(!TryPreviewTarget(out target,out why,allowStarting))return null;var t=target;return library.Doc.Items.SingleOrDefault(x=>x.Id==t.CatalogId&&x.RegisteredSha256==t.ResourceSha256);}
 bool CanConfirmCurrentPreview(bool exact=false){
  try{PreviewFrameTarget t;var item=LoadedPreview(out t);return item!=null&&!library.IsBuiltin(item)&&item.PreviewConfirmedSha256!=t.ResourceSha256&&(!exact||Common.Hash(item.SourcePath)==t.ResourceSha256)&&!previewStopPending;}catch{return false;}
 }
 void ConfirmCurrentPreview(string method){
  if(!CanConfirmCurrentPreview(true)){Write("preview-confirmation-rejected",new{reason="verified-actual-loaded-preview-required-or-already-confirmed"});return;}
  try{PreviewFrameTarget t;var item=LoadedPreview(out t);if(item==null)throw new InvalidDataException("预览已经改变，未保存确认");
   var proof=new{catalogId=item.Id,source=item.SourcePath,sha256=t.ResourceSha256,run=t.Run,method=method,scope="window-image-only",automaticFirstFrameDetection=false,desktopVerified=false,local=DateTimeOffset.Now.ToString("o")};
   library.ConfirmPreview(item,t.ResourceSha256,t.Run,method);string record=Path.Combine(t.Run,"human-preview-confirmation.json");if(!File.Exists(record))Common.SaveNew(record,proof);Write("preview-human-confirmed",proof);libraryHint.Text="本次实际预览画面已人工确认；没有新增桌面验证。";RefreshCards();
  }catch(Exception ex){libraryHint.Text="确认未保存："+ex.Message;Write("preview-confirmation-error",new{error=ex.ToString()});}
 }
 void SyncPreviewControls(){
  if(CurrentRun==null||activeRunRequest==null||Common.S(activeRunRequest,"mode")!="preview")return;
  if(previewControlRun!=CurrentRun){previewControlRun=CurrentRun;previewControlSequence=0;previewControlStamp=null;previewStopPending=false;}
  try{
   string path=Path.Combine(CurrentRun,"preview-control-request.json");
   if(File.Exists(path)&&new FileInfo(path).Length<32768){var d=Common.Read(path);long seq=Convert.ToInt64(d["sequence"]);
    if(seq>previewControlSequence){PreviewFrameTarget t;string action=Common.S(d,"action");var item=LoadedPreview(out t,action=="close");
     bool valid=item!=null&&PreviewControlRequests.Matches(d,t);
     if(!valid){string rejected=Common.Json.Serialize(d);if(rejected!=lastRejectedPreviewControl){lastRejectedPreviewControl=rejected;Write("preview-control-rejected",new{reason="run-token-process-window-or-loaded-resource-mismatch"});}}
     else{previewControlSequence=seq;Write("preview-control-accepted",new{action=action,sequence=seq,run=CurrentRun,resourceSha256=t.ResourceSha256,loadedId=item.Id,selectedId=selected==null?null:selected.Id});
      if(action=="confirm"&&!previewStopPending)ConfirmCurrentPreview("human-preview-window-button");
      if(action=="capture-cover"&&!previewStopPending&&!capturingCover&&!previewCoverRequested){previewCoverRequested=true;string requestedRun=CurrentRun;BeginInvoke((Action)(async()=>{try{if(CurrentRun==requestedRun&&!closing)await SavePreviewCover();}finally{previewCoverRequested=false;}}));}
      if(action=="close"&&!previewStopPending){previewStopPending=true;string run=CurrentRun;BeginInvoke((Action)(async()=>{if(CurrentRun==run&&!closing)await StopManaged("preview-window-close");if(CurrentRun==run)previewStopPending=false;}));}
     }
    }
   }
   PreviewFrameTarget target;var loaded=LoadedPreview(out target);bool ready=loaded!=null&&!previewStopPending,confirmed=loaded!=null&&(library.IsBuiltin(loaded)||loaded.PreviewConfirmedSha256==target.ResourceSha256);
   bool coverBusy=capturingCover||previewCoverRequested;bool coverAllowed=ready&&!coverBusy&&!PreviewCovers.HasIndependentCover(loaded);
   string message=previewStopPending||State=="stopping"?"正在停止预览，等待清理确认…":State=="cleanup-unconfirmed"||State=="stop-timeout"?"清理尚未确认，请查看主窗口状态。":coverBusy?"正在截取并保存封面…":previewCoverResultRun==CurrentRun&&!String.IsNullOrEmpty(previewCoverResult)?previewCoverResult:confirmed?"此资源已有窗口确认 · 可随时截取封面":ready?"可截取当前画面作封面；画面确认单独保存。":"正在准备或停止预览…";
   var statusRecord=new{version=1,token=runToken,resourceSha256=Common.S(activeRunRequest,"expectedSha256"),sequence=previewControlSequence,ready=ready,confirmed=confirmed,coverAllowed=coverAllowed,coverBusy=coverBusy,coverExists=loaded!=null&&PreviewCovers.Resolve(loaded.RegisteredSha256)!=null,message=message};string stamp=Common.Json.Serialize(statusRecord);
   if(stamp!=previewControlStamp){Common.ReplaceJson(Path.Combine(CurrentRun,"preview-control-status.json"),statusRecord);previewControlStamp=stamp;}
  }catch(IOException){}catch(Exception ex){Write("preview-control-error",new{reason=ex.Message});}
 }
 string lastRejectedPreviewControl;
}
