using System;using System.IO;using System.Linq;using System.Drawing;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;
internal sealed partial class ControlWindow {
 readonly Button keepPlaying=new SoftButton(),loginOptions=new SoftButton();InstanceChannel instanceChannel;bool interfaceHidden;string rememberedRun;readonly CancellationTokenSource restoreCancel=new CancellationTokenSource();Task restoreTask;bool restoring;string launchNotice;
 void StartDailyFlow(bool restore){StartTray();instanceChannel=new InstanceChannel(this);instanceChannel.Start();Write("daily-flow-ready",new{startupEnabled=LoginRestore.IsEnabled(new WindowsStartupStore()),hidden=false,closeStillExits=true});if(restore){if(LoginRestore.IsEnabled(new WindowsStartupStore()))restoreTask=RestoreLastWallpaper();else{libraryHint.Text="登录恢复尚未启用；没有自动播放。";Write("login-restore-not-enabled",new{noPlayback=true});}}}
 internal bool RestoreInterface(){if(closing)return false;Show();ShowInTaskbar=true;if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();interfaceHidden=false;return true;}
 internal async Task HideAndKeepPlaying(){
  if(closing||transition||restoring||PlayerRole!="alive"||State!="mounted"){libraryHint.Text="请先将一张已确认的壁纸设为桌面，再选择继续播放。";return;}
  if(importDialog!=null||detailsWindow!=null||settingsWindow!=null&&settingsWindow.Visible||renameWindow!=null&&!renameWindow.IsDisposed){libraryHint.Text="请先关闭导入、设置或编辑窗口，再选择继续播放。";return;}
  if(tray==null||trayFailed){libraryHint.Text="托盘入口不可用；保留主界面以便停止与退出。";return;}await PauseDiscovery();if(closing||PlayerRole!="alive"||State!="mounted")return;FlushAudio();interfaceHidden=true;Write("interface-hidden",new{explicitUserChoice=true,player=player.Proof,controllerRemains=true,heartbeatsAndJobRemain=true,deadline=false});Hide();
 }
 void RefreshDailyFlow(){RefreshTray();keepPlaying.Enabled=!closing&&!transition&&!restoring&&State=="mounted"&&PlayerRole=="alive";if(interfaceHidden&&State!="mounted"&&!transition&&!closing){RestoreInterface();libraryHint.Text="壁纸已停止或发生异常，界面已恢复；请查看状态或退出。";Write("hidden-interface-auto-restored",new{state=State});}}
 void RememberRestoreTarget(){
  if(rememberedRun==CurrentRun||CurrentRun==null||State!="mounted"||PlayerRole!="alive"||activeRunRequest==null)return;rememberedRun=CurrentRun;
  try{string id=Common.S(activeRunRequest,"selectedCatalogId"),hash=Common.S(activeRunRequest,"expectedSha256");var x=library.Doc.Items.SingleOrDefault(v=>v.Id==id);if(x==null||x.RegisteredSha256!=hash||!LocalImport.Under(x.SourcePath,Path.Combine(Common.Root,"library")))throw new InvalidDataException("此资源不是管理副本，不作为登录恢复目标");
   if(!library.IsBuiltin(x)&&(x.PreviewConfirmedSha256!=hash||x.DesktopConfirmedSha256!=hash))throw new InvalidDataException("目标尚未完成同哈希桌面验收，不保存登录恢复目标");
   Common.ReplaceJson(LoginRestore.TargetPath,new RestoreTarget{Version=1,CatalogId=x.Id,Source=x.SourcePath,Sha256=hash,SelectedAt=DateTimeOffset.Now.ToString("o")});Write("restore-target-saved",new{id=x.Id,sha256=hash,source=x.SourcePath,fromRun=CurrentRun,noNewConfirmation=true});
  }catch(Exception ex){libraryHint.Text="本次播放保持；登录恢复目标未更新："+ex.Message;Write("restore-target-not-saved",new{reason=ex.Message});}
 }
 internal async Task RestoreLastWallpaper(){
  if(restoring||closing)return;restoring=true;launchNotice="正在核对上次壁纸，仅检查目标文件…";libraryHint.Text=launchNotice;RestoreTarget target=null;
  try{target=LoginRestore.ReadTarget();var x=await Task.Run(()=>LoginRestore.Validate(library,target,restoreCancel.Token));restoreCancel.Token.ThrowIfCancellationRequested();if(closing)return;
   // Recheck mutable catalog/confirmation fields on the UI thread before starting the same path.
   if(library.Doc.Items.Find(v=>v.Id==x.Id)!=x||x.SourcePath!=target.Source||x.RegisteredSha256!=target.Sha256||(!library.IsBuiltin(x)&&(x.PreviewConfirmedSha256!=target.Sha256||x.DesktopConfirmedSha256!=target.Sha256)))throw new InvalidDataException("校验后登记已变化");
   restoring=false;SelectWallpaper(x);if(selected!=x||library.Doc.SelectedId!=x.Id)throw new InvalidDataException("恢复目标选择未保存；未启动其他壁纸");Write("login-restore-validated",new{id=x.Id,sha256=target.Sha256,source=x.SourcePath,visibleInterface=Visible,onlyTargetHashed=true});restoring=false;await StartMode("desktop");libraryHint.Text="已请求恢复上次壁纸；结果见下方状态。";
  }catch(OperationCanceledException){Write("login-restore-cancelled",new{});}
  catch(Exception ex){if(!closing){RestoreInterface();libraryHint.Text="未恢复壁纸："+ex.Message;SetState("restore-failed","未恢复壁纸",ex.Message);}Write("login-restore-failed",new{reason=ex.Message,otherWallpaperStarted=false,target=target});}
  finally{restoring=false;}
 }
 void ShowLoginOptions(){
  var store=new WindowsStartupStore();var f=new MaterialDialog{Text="登录Windows后恢复上次壁纸",ClientSize=new Size(620,370),StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=Theme.Canvas};
  var box=new SpringToggle{Text="登录Windows后恢复上次壁纸",Checked=LoginRestore.IsEnabled(store),AutoSize=false,Width=500,Location=new Point(20,18)};
  var text=new TextBox{ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Vertical,Bounds=new Rectangle(20,55,580,235),Text="默认关闭；登录时显示本界面，不默认隐藏。\r\n只恢复上次主动设为桌面且保存在管理目录、已有同哈希窗口和桌面确认的资源；失败不换播。\r\n\r\n入口："+Application.ExecutablePath+"\r\n参数：--restore-last\r\n位置：HKEY_CURRENT_USER\\"+WindowsStartupStore.KeyPath+"\r\n值名："+WindowsStartupStore.ValueName+"\r\n\r\n本轮启用前需集中明确确认。勾选只准备计划，不写入系统。启用后取消勾选可仅删除归属匹配的本软件启动项。"};
  var close=new SoftButton{Text="关闭",Bounds=new Rectangle(480,310,120,36)};close.Click+=(s,e)=>f.Close();f.Controls.Add(box);f.Controls.Add(text);f.Controls.Add(close);bool changing=false;
  box.CheckedChanged+=(s,e)=>{if(changing)return;try{if(box.Checked){string hash=LoginRestore.Prepare();Write("login-startup-prepared",new{plan=LoginRestore.PlanPath,planSha256=hash,systemWritten=false});text.AppendText("\r\n\r\n已准备，尚未启用。待本轮集中明确确认后应用该计划。");changing=true;box.Checked=false;changing=false;}
   else{LoginRestore.Disable(store);Write("login-startup-disabled",new{onlyOwnedValue=true});text.AppendText("\r\n已关闭登录恢复，未修改其他启动项。");}}
   catch(Exception ex){text.AppendText("\r\n操作未完成："+ex.Message);changing=true;box.Checked=LoginRestore.IsEnabled(store);changing=false;}};
  f.ShowDialog(this);f.Dispose();
 }
 async Task StopDailyFlow(){restoreCancel.Cancel();if(instanceChannel!=null){instanceChannel.Dispose();instanceChannel=null;}if(restoreTask!=null)await Task.WhenAny(restoreTask,Task.Delay(1500));Write("daily-flow-close-clean",new{restoreTaskEnded=restoreTask==null||restoreTask.IsCompleted,instanceChannelClosed=true});}
}
