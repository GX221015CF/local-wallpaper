#if TEST_BUILD
using System;using System.IO;using System.Linq;using System.Threading.Tasks;using System.Drawing;using System.Runtime.InteropServices;
internal sealed partial class ControlWindow {
 [DllImport("user32")]static extern IntPtr GetDC(IntPtr h);
 [DllImport("user32")]static extern int ReleaseDC(IntPtr h,IntPtr dc);
 [DllImport("user32")]static extern bool GetClientRect(IntPtr h,out PreviewFrame.Rect r);
 [DllImport("gdi32")]static extern bool BitBlt(IntPtr d,int x,int y,int w,int h,IntPtr s,int sx,int sy,uint op);
 void PreviewControlsShot(PreviewFrameTarget t){PreviewFrame.Windows(t);Require(PreviewFrame.ForegroundIs(t.Window),"Own preview must be foreground for test-only client image");PreviewFrame.Rect r;GetClientRect(new IntPtr(t.Window),out r);using(var b=new Bitmap(r.Right,r.Bottom))using(var g=Graphics.FromImage(b)){var src=GetDC(new IntPtr(t.Window));var dst=g.GetHdc();try{Require(BitBlt(dst,0,0,r.Right,r.Bottom,src,0,0,0x00cc0020),"Own preview capture failed");}finally{g.ReleaseHdc(dst);ReleaseDC(new IntPtr(t.Window),src);}b.Save(Path.Combine(Common.Root,"evidence","actual-preview-controls.png"));}}
 object HostCommand(PreviewFrameTarget t,long seq,string action,string token){return new{version=1,token=token,run=t.Run,resourceSha256=t.ResourceSha256,sequence=seq,action=action,playerPid=t.PlayerPid,playerCreation=t.PlayerCreation,window=t.Window,render=t.Render};}
 internal async Task RunPreviewControlsSmoke(){string failure=null;PreviewFrameTarget target=null;try{
  var item=library.Doc.Items.Single(x=>x.ResourceId=="28987");Require(item.RegisteredSha256==item.PreviewConfirmedSha256,"Only already-window-confirmed resource may be used");
  var other=library.Doc.Items.First(x=>x.Id!=item.Id);string originalOther=Common.Json.Serialize(other);string audio=Common.ReadShared(Path.Combine(Common.Root,"data","settings.json"));
  // Clear confirmation only in this explicitly isolated library to exercise confirmation routing.
  item.PreviewConfirmedSha256=null;item.PreviewConfirmationTime=null;item.PreviewConfirmationMethod=null;item.PreviewConfirmationRun=null;library.Save();
  SelectWallpaper(item);await StartMode("preview");await WaitOfflineState("preview");await Task.Delay(900);
  string why;Require(TryPreviewTarget(out target,out why),"Own preview proof: "+why);SelectWallpaper(other);Require(CanConfirmCurrentPreview(),"Confirmation wrongly depends on selected card");
  PreviewFrame.SetForegroundWindow(new IntPtr(target.Window));await Task.Delay(250);PreviewControlsShot(target);
  string cmd=Path.Combine(CurrentRun,"preview-control-request.json");Common.ReplaceJson(cmd,HostCommand(target,1,"confirm",new string('0',32)));SyncPreviewControls();Require(item.PreviewConfirmedSha256==null,"Wrong token confirmed resource");
  Common.ReplaceJson(cmd,HostCommand(target,1,"confirm",target.Token));SyncPreviewControls();Require(item.PreviewConfirmedSha256==target.ResourceSha256,"Loaded resource not confirmed");Require(Common.Json.Serialize(other)==originalOther,"Selected other resource was modified");
  string confirmation=Common.Json.Serialize(item);SyncPreviewControls();Common.ReplaceJson(cmd,HostCommand(target,2,"confirm",target.Token));SyncPreviewControls();Require(Common.Json.Serialize(item)==confirmation,"Duplicate confirmation rewrote evidence");
  Require(!CanConfirmCurrentPreview()&&!confirmPreview.Enabled,"Main confirmation state did not synchronize");Require(item.DesktopConfirmedSha256==null,"Desktop confirmation was invented");
  var layout=Common.ReadShared(Path.Combine(CurrentRun,"host.jsonl")).Split(new[]{Environment.NewLine},StringSplitOptions.RemoveEmptyEntries).Select(s=>Common.Json.Deserialize<System.Collections.Generic.Dictionary<string,object>>(s)).First(x=>Common.S(x,"phase")=="preview-layout");Common.SaveNew(Path.Combine(Common.Root,"evidence","actual-preview-layout.json"),layout);
  Common.ReplaceJson(cmd,HostCommand(target,3,"close",target.Token));SyncPreviewControls();var stopWatch=System.Diagnostics.Stopwatch.StartNew();while(!Clean&&stopWatch.Elapsed.TotalSeconds<12)await Task.Delay(100);Require(Clean,"Preview close did not clean owned processes");
  Require(Common.ReadShared(Path.Combine(Common.Root,"data","settings.json"))==audio,"Audio settings changed");Require(!CanConfirmCurrentPreview(),"Confirmation enabled after close");
  Common.SaveNew(Path.Combine(Common.Root,"evidence","preview-controls-result.json"),new{passed=true,actualNativeResource=target.ResourceSha256,renderCount=1,desktopMounted=false,actualHostSha256=Common.HostHash,actualLoadedResourceBinding=true,selectedDifferentCard=true,wrongTokenRejected=true,repeatConfirmationNoRewrite=true,mainAndPreviewStatusSynchronized=true,existingDesktopConfirmationUnchanged=true,closeCleanupConfirmed=Clean,workerState=WorkerRole,playerState=PlayerRole,audioSettingsPreserved=true,userClicked=false,method="TEST_BUILD own request handlers; real shipping native host; confirmation fields modified only in isolated catalog; not new human compatibility evidence"});
 }catch(Exception ex){failure=ex.ToString();Common.SaveNew(Path.Combine(Common.Root,"evidence","preview-controls-failed.json"),new{error=failure});}
 await StopManaged("preview-controls-smoke-end");Close();
 }
}
#endif
