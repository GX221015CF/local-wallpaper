using System;using System.Threading;using System.IO;using System.Diagnostics;using System.Threading.Tasks;using System.Windows.Forms;using System.Collections.Generic;
internal sealed partial class ControlWindow {
 readonly PlaybackPreparation playbackPreparation=new PlaybackPreparation();readonly PlaybackSwitch playbackSwitch=new PlaybackSwitch();Task playbackPreparationTask;bool activeRunReady;string lastAttemptFailure;
 HeldPlayer player;string lastRunFailure;string runToken;long workerCreation;int workerPid;bool handoffRejected,noPlayerProven;readonly Stopwatch beatAge=Stopwatch.StartNew();bool beatSeen;string lastRoles="";
 internal string WorkerRole{get{if(worker==null)return "not-started";try{uint s=Common.WaitForSingleObject(worker.Handle,0);if(s==0)return "exited";if(s!=258)return "unknown";return beatSeen&&beatAge.Elapsed.TotalSeconds>3?"unresponsive":"alive";}catch{return "unknown";}}}
 internal string PlayerRole{get{if(player!=null)return player.State;if(noPlayerProven)return "not-created";return CurrentRun==null?"not-started":"unknown";}}
 bool Clean{get{return (WorkerRole=="exited"||WorkerRole=="not-started")&&(PlayerRole=="exited"||PlayerRole=="not-created"||CurrentRun==null);}}
 void ShowState(string state,string title,string message){if(State!=state||detail.Text!=message)SetState(state,title,message);}
 void ImportHandoff(){
  if(player!=null||handoffRejected||CurrentRun==null||worker==null)return;string path=Path.Combine(CurrentRun,"player-handoff.json");if(!File.Exists(path))return;
  Dictionary<string,object> h;try{h=Common.Read(path);}catch{return;}
  try{
   if(Common.S(h,"token")!=runToken||!String.Equals(Path.GetFullPath(Common.S(h,"run")),CurrentRun,StringComparison.OrdinalIgnoreCase)||Convert.ToInt32(h["workerPid"])!=workerPid||Convert.ToInt64(h["workerCreationFileTime"])!=workerCreation||Convert.ToInt32(h["controllerPid"])!=Process.GetCurrentProcess().Id)throw new InvalidDataException("握手不属于本次run/worker/controller");
   string image=HostResources.ImageFor(CurrentRun);if(!String.Equals(Path.GetFullPath(Common.S(h,"image")),image,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("握手映像路径不符");
   var owned=new HeldPlayer(new IntPtr(Convert.ToInt64(h["remoteHandle"])),Convert.ToInt32(h["pid"]),Convert.ToInt64(h["creationFileTime"]),image,CurrentRun,runToken);
   if(runJob==null||!runJob.Contains(owned.Handle)){owned.Dispose();throw new InvalidDataException("Player is not in our verified job");}
   player=owned;Write("player-job-confirmed",new{proof=owned.Proof,memberOfOwnJob=true});File.WriteAllText(Path.Combine(CurrentRun,"handoff.accepted"),runToken);Write("player-handle-accepted",owned.Proof);
  }catch(Exception ex){handoffRejected=true;Write("handoff-rejected",new{error=ex.ToString(),noPidCleanup=true});}
 }
 void Roles(){
  if(worker!=null&&WorkerRole=="exited"&&player==null&&CurrentRun!=null){string path=Path.Combine(CurrentRun,"result.json");try{if(File.Exists(path)&&!Convert.ToBoolean(Common.Read(path)["playerCreated"]))noPlayerProven=true;}catch{}}
  string w=WorkerRole,p=PlayerRole,key=w+"|"+p;
  lease.Text="worker："+RoleText(w)+"  播放器："+RoleText(p);
  if(State=="stop-timeout")detail.Text="worker："+RoleText(w)+"；播放器："+RoleText(p)+"。仅恢复本次自有run";
  if(key!=lastRoles){lastRoles=key;Write("role-observation",new{workerState=w,playerState=p,playerProof=player==null?null:player.Proof,workerPid=workerPid,playerExitCode=player!=null&&p=="exited"?(object)ProcessProof.ExitCode(player.Handle):null,run=CurrentRun});}
 }
 static string RoleText(string s){switch(s){case "exited":return "已退出";case "alive":return "仍运行";case "unresponsive":return "无响应";case "not-created":return "未创建";case "not-started":return "未启动";default:return "未知";}}
 async Task StartMode(string mode){
#if TEST_BUILD
  ShowState("runtime-unavailable","播放组件尚未准备好","隔离测试版本已禁用预览和桌面设置。");Write("test-build-playback-blocked",new{mode=mode,playerCreated=false,workerCreated=false,desktopChanged=false});return;
#endif
  if(transition||closing||restoring){Write("start-ignored",new{reason="operation-in-progress-or-closing",mode=mode});return;}
  if(!RuntimeProvisioning.Available||runtimeCheckRunning){ShowState("runtime-unavailable","播放组件尚未准备好",RuntimeProvisioning.Message);Write("playback-blocked-runtime-missing",new{mode=mode,playerCreated=false});return;}
  var chosen=selected;
  if(chosen==null){ShowState("selection-unavailable","请先选择壁纸","点击一张已导入的壁纸卡片，再点击预览。");return;}
  if(captureOperation!=null&&captureOperation.Busy)captureOperation.Cancel();
  Poll();
  if(CurrentRun!=null&&WorkerRole=="exited"&&!Clean){ShowState("worker-lost","上次清理尚未确认","请先点击停止并恢复，再启动壁纸。");return;}
  if(!playbackPreparation.TryBegin(20000))return;
  var token=playbackPreparation.Token;
  string source=chosen.SourcePath,confirmation=chosen.PreviewConfirmedSha256,chosenHash=null,attemptRun=null;
  bool builtin=library.IsBuiltin(chosen);
  transition=true;lastAttemptFailure=null;
  ShowState("preparing","正在准备壁纸","正在后台核对文件及组件，最多20秒；可取消启动。");RefreshPlaybackActions();
  Write("playback-preparation-started",new{mode=mode,catalogId=chosen.Id,budgetSeconds=20,stage=playbackPreparation.Stage});
  try{
   var switching=playbackSwitch.Run(
    ()=>playbackPreparation.VerifyAsync(source,mode,confirmation,builtin),
    ()=>!closing&&!IsDisposed&&selected==chosen&&chosen.SourcePath==source&&(mode!="desktop"||chosen.PreviewConfirmedSha256==confirmation),
    verified=>worker!=null&&WorkerRole!="exited"&&observed==mode&&activeRunRequest!=null&&Common.S(activeRunRequest,"expectedSha256")==verified[0]&&Common.S(activeRunRequest,"selectedCatalogId")==chosen.Id,
    ()=>CurrentRun!=null&&!Clean,
    ()=>StopCore("mode-switch",10),
    verified=>{
   chosenHash=verified[0];
   if(player!=null){player.Dispose();player=null;}if(worker!=null){worker.Dispose();worker=null;}
   lastRunFailure=null;activeRunReady=false;LeaseSeconds=0;CurrentRun=Path.Combine(Session,"run-"+DateTime.Now.ToString("HHmmssfff")+"-"+mode+"-"+Guid.NewGuid().ToString("N").Substring(0,6));attemptRun=CurrentRun;Directory.CreateDirectory(CurrentRun);runToken=Guid.NewGuid().ToString("N");noPlayerProven=false;handoffRejected=false;beatSeen=false;beatAge.Restart();lastRoles="";
   Common.SaveNew(Path.Combine(CurrentRun,"request.json"),new{mode=mode,controllerBounds=new[]{Bounds.X,Bounds.Y,Bounds.Width,Bounds.Height},cwp=source,selectedCatalogId=chosen.Id,selectedName=chosen.Name,expectedSha256=chosenHash,previewConfirmedSha256=builtin?Common.CwpHash:confirmation,seconds=LeaseSeconds,playbackPolicy="continuous",audioMuted=settings.Muted,audioVolume=settings.Volume,requested=DateTimeOffset.Now.ToString("o"),controllerPid=Process.GetCurrentProcess().Id,controllerImage=Application.ExecutablePath,controllerCreationFileTime=ProcessProof.Creation(Process.GetCurrentProcess().Handle).ToString(),runToken=runToken});
   InitializeRunSafety();observed=mode;SetState("starting","启动中","正在建立自有播放任务；首帧需用户观察。");
   token.ThrowIfCancellationRequested();
   Write("playback-runtime-rechecked",new{root=verified[1],staticIdentityPassed=true,playerNotYetCreated=true});
   worker=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--worker "+Common.Quote(CurrentRun)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Common.Root});workerPid=worker.Id;workerCreation=ProcessProof.Creation(worker.Handle);BindWorkerJob();
   Write("worker-created",new{pid=workerPid,startedUtc=worker.StartTime.ToUniversalTime().ToString("o"),creationFileTime=workerCreation.ToString(),image=Application.ExecutablePath,handleHeld=true,mode=mode,seconds=LeaseSeconds,run=CurrentRun});
    },token);
   playbackPreparationTask=switching;string outcome=await switching;
   Write("playback-switch-outcome",new{outcome=outcome,chosenId=chosen.Id,activeId=activeRunRequest==null?null:Common.S(activeRunRequest,"selectedCatalogId"),oldStopAttempted=playbackSwitch.OldStopAttempted,newLaunchAttempted=playbackSwitch.LaunchAttempted});
   if(outcome=="same-active")ShowState(activeRunReady?(observed=="desktop"?"mounted":"preview"):"starting","任务保持","所选壁纸已在相同模式运行，未创建第二个任务。");

  }catch(OperationCanceledException){
   if(worker==null)noPlayerProven=true;
   if(!IsDisposed){bool timeout=!playbackPreparation.UserCancelled&&!closing&&token.IsCancellationRequested;Write("playback-preparation-cancelled",new{stage=playbackPreparation.Stage,timeout=timeout,closing=closing,workerCreated=false});
    if(!closing){if(timeout){ShowState("start-failed","准备超时","启动准备超过20秒预算，可以重试；原登记和确认保持。");RecordFault("start","准备壁纸",playbackPreparation.Stage,"启动准备超过20秒预算");}
     else if(Clean)ShowState("start-cancelled","已取消启动","没有启动新的播放任务，可再次选择预览。");else ShowState("switch-cancelled","已取消切换",PlayerRole=="alive"&&!File.Exists(Path.Combine(CurrentRun,"stop.request.json"))?"新任务未启动；原任务“"+activeRunName+"”仍在运行。":"新任务未启动；原任务的停止结果见状态行。");}
   }
  }catch(Exception ex){
   lastAttemptFailure="未能启动“"+chosen.Name+"”："+ex.Message;if(attemptRun!=null){lastRunFailure=ex.Message;if(worker==null)noPlayerProven=true;else Common.RequestStop(attemptRun,"startup-failed");}
   if(!IsDisposed){ShowState("switch-failed","启动或切换未完成",MultiWallpaperPresentation.AttemptFailure(chosen.Name,ex.Message,activeRunName,PlayerRole=="alive"&&attemptRun==null));Write("playback-preparation-failed",new{stage=playbackPreparation.Stage,error=ex.ToString(),newRun=attemptRun,originalRunPreserved=attemptRun==null&&PlayerRole=="alive",chosenId=chosen.Id});RecordFault("start","准备壁纸",playbackPreparation.Stage,ex.ToString());}
  }finally{
   playbackPreparationTask=null;playbackPreparation.Complete();transition=false;
   if(!IsDisposed&&!closing){if(State=="preparing"&&PlayerRole=="alive")ShowState(observed=="desktop"?"mounted":"preview",observed=="desktop"?"桌面壁纸运行中":"预览中","保留当前播放任务");RefreshPlaybackActions();}
  }
 }
 void CancelPlaybackPreparation(string reason){if(!playbackPreparation.Busy)return;playbackPreparation.Cancel();Write("playback-preparation-cancel-requested",new{reason=reason,stage=playbackPreparation.Stage});RefreshPlaybackActions();}
 internal async Task StopManaged(string reason){
  bool cancelOnly=playbackSwitch.CancelKeepsOld(reason);
  CancelPlaybackPreparation(reason);
  if(captureOperation!=null&&captureOperation.Busy)captureOperation.Cancel();
  if(restoring)restoreCancel.Cancel();
  if(transition){Write("stop-deferred",new{reason=reason,cancelNewPreparationOnly=cancelOnly});if(cancelOnly)return;try{if(CurrentRun!=null)Common.RequestStop(CurrentRun,reason);}catch(Exception ex){ShowState("cleanup-unconfirmed","清理未确认","撤下请求写入失败；未声明完成，关闭入口时Job会回收自有进程");Write("stop-request-error",new{error=ex.ToString(),cleanupConfirmed=false});}return;}
  transition=true;try{await StopCore(reason,10);}catch(Exception ex){ShowState("cleanup-unconfirmed","清理未确认","恢复操作失败；worker与播放器状态见状态行，禁止新启动");Write("recovery-operation-error",new{reason=reason,error=ex.ToString(),workerState=WorkerRole,playerState=PlayerRole,cleanupConfirmed=false});}finally{transition=false;}
 }
 async Task<bool> StopCore(string reason,double budget){
  Poll();if(Clean){ConfirmCleanup(reason);return true;}budget=Math.Max(.1,Math.Min(10,budget));var watch=Stopwatch.StartNew();double normal=Math.Min(4,budget*.4),retire=Math.Min(6,budget*.6),force=Math.Min(7,budget*.7);bool fallback=false,workerKilled=false,playerKilled=false;
  ShowState("stopping","撤下中","等待上限约"+budget.ToString("0.0")+"秒；worker与播放器分别核验");Common.RequestStop(CurrentRun,reason);Write("operation-begin",new{reason=reason,budgetSeconds=budget,normalWaitSeconds=normal,workerState=WorkerRole,playerState=PlayerRole});
  while(watch.Elapsed.TotalSeconds<budget){
   Poll();if(Clean){ConfirmCleanup(reason);Write("operation-complete",new{reason=reason,elapsedSeconds=watch.Elapsed.TotalSeconds,workerState=WorkerRole,playerState=PlayerRole});return true;}
   double t=watch.Elapsed.TotalSeconds;
   if(t>=normal&&!fallback){fallback=true;ShowState("stop-timeout","停止超时，正在恢复","worker："+RoleText(WorkerRole)+"；播放器："+RoleText(PlayerRole)+"。仅恢复本次自有run");Write("operation-timeout",new{reason=reason,elapsedSeconds=t,workerState=WorkerRole,playerState=PlayerRole,playerHandleVerified=player!=null});File.WriteAllText(Path.Combine(CurrentRun,"detach.request"),"");}
   // Retire a stalled worker only after its child is positively known to be gone.
   if(t>=retire&&!workerKilled&&(PlayerRole=="exited"||PlayerRole=="not-created")&&(WorkerRole=="alive"||WorkerRole=="unresponsive")){
    ProcessProof.Verify(worker.Handle,workerPid,workerCreation,Application.ExecutablePath);if(!Common.TerminateProcess(worker.Handle,198))Write("worker-retire-failed",new{win32=System.Runtime.InteropServices.Marshal.GetLastWin32Error()});else{workerKilled=true;Write("recovery-worker-terminated",new{pid=workerPid,creationFileTime=workerCreation.ToString(),exactHeldHandle=true,code=198,playerState=PlayerRole});}
   }
   if(t>=force&&!playerKilled&&player!=null&&PlayerRole=="alive"){player.Kill(197);playerKilled=true;Write("recovery-player-terminated",new{proof=player.Proof,code=197,reason="verified-player-stop-timeout"});}
   await Task.Delay(100);
  }
  Poll();if(Clean){ConfirmCleanup(reason);return true;}
  ShowState("cleanup-unconfirmed","清理未确认","worker："+RoleText(WorkerRole)+"；播放器："+RoleText(PlayerRole)+"。禁止新启动；关闭入口时Job会回收自有进程");Write("operation-incomplete",new{reason=reason,elapsedSeconds=watch.Elapsed.TotalSeconds,workerState=WorkerRole,playerState=PlayerRole,noBlindPidCleanup=true});return false;
 }
 string inputCleanupRun;
 void ConfirmCleanup(string reason){
  if(CurrentRun!=null&&Clean&&inputCleanupRun!=CurrentRun){inputCleanupRun=CurrentRun;RuntimeInputs.Maintain(150);Write("runtime-input-maintenance",new{run=CurrentRun,message=RuntimeInputs.LastReport,scope=RuntimeInputs.Scheme});}
  if(!Clean)return;string failure=lastRunFailure??lastAttemptFailure;if(failure==null)ShowState("exited","已退出","播放器与worker分别确认已退出，或明确未创建播放器");else ShowState("failed-cleaned","播放失败，任务已结束",failure+"；"+ResourcePresentation.FailureMaterials(CurrentRun));if(CurrentRun!=null&&!File.Exists(Path.Combine(CurrentRun,"controller-cleanup.json")))Common.SaveNew(Path.Combine(CurrentRun,"controller-cleanup.json"),new{confirmed=true,reason=reason,workerState=WorkerRole,playerState=PlayerRole,local=DateTimeOffset.Now.ToString("o"),playerProof=player==null?null:player.Proof});
 }
 internal void Poll(){
  EmitUiHeartbeat();if(CurrentRun==null)return;ImportHandoff();ObserveWorkerHeartbeat();string events=Path.Combine(CurrentRun,"events.jsonl");
  if(File.Exists(events)){
   foreach(var row in eventTail.Pull()){string phase=Common.S(row,"phase");
    if(phase=="worker-heartbeat")continue;if(phase=="log-warning"||phase=="heartbeat-diagnostics-disabled"||phase=="log-retention-warning"){ShowLogWarning(row["data"]);continue;}if(phase=="control-chain-fault"){lastRunFailure=Common.S(Common.Obj(row["data"]),"message");Write("watchdog-observed",row["data"]);continue;}if(phase!="state")continue;var data=Common.Obj(row["data"]);string s=Common.S(data,"state");
    if(s=="preview"||s=="mounted")activeRunReady=true;else if(s=="failed"||s=="exited")activeRunReady=false;
    // Keep failure evidence even while StopCore/OnClosing owns presentation.
    if(s=="failed"){lastRunFailure=Common.S(data,"message");string original=Common.S(data,"error"),stage=Common.S(data,"failureStage");Write("worker-failure-observed",new{message=lastRunFailure,error=original,failureStage=stage,duringTransition=transition,duringClose=closing});if(original.Length>0)RecordFault(stage=="stop-error"||stage=="detach-failed"?"stop":"start","壁纸运行或停止",stage.Length==0?"worker-runtime":stage,original);}
    if(transition||closing)continue;
    if((s=="preview"||s=="mounted")&&player!=null)ShowState(s,s=="preview"?"正在预览":"已挂载",Common.S(data,"message"));
    else if(s=="engine-started")ShowState(s,"引擎已启动","已持有自有播放器句柄；首帧未自动验证");
    else if(s=="failed"){ShowState("failed","工作进程报告失败",lastRunFailure);}
    else if(s=="starting")ShowState("starting","启动中",Common.S(data,"message"));
   }
  }
  Roles();if(!closing)SyncPreviewControls();if(transition||closing)return;
  if(Clean){ConfirmCleanup("observed-complete");return;}
  if(WorkerRole=="exited")ShowState("worker-lost","工作进程失联","worker已退出；播放器"+RoleText(PlayerRole)+"。请先撤下，禁止新启动");
  else if(WorkerRole=="unresponsive")ShowState("worker-stalled","工作进程无响应","播放器"+RoleText(PlayerRole)+"；撤下、切换和关闭均有界等待");
  RecoverBrokenWorker();
 }
 void OpenLogs(){string target=CurrentRun??Session;Write("open-log-directory",new{path=target});Process.Start(new ProcessStartInfo("explorer.exe",Common.Quote(target)){UseShellExecute=true});}
 async void OnClosing(object sender,FormClosingEventArgs e){
  if(allowClose)return;e.Cancel=true;if(closing)return;closing=true;CancelPlaybackPreparation("controller-close");var watch=Stopwatch.StartNew();bool confirmed=false;Write("controller-close-request",new{maxWaitSeconds=12});
  try{
   await StopFaultHandoff();await StopPreviewCoverCapture();await StopDailyFlow();await StopDiscovery();await StopImport();await StopHomeImages();
   while(transition&&watch.Elapsed.TotalSeconds<10)await Task.Delay(100);
   if(!transition){transition=true;try{confirmed=await StopCore("controller-close",Math.Min(10,12-watch.Elapsed.TotalSeconds));}finally{transition=false;}}
   if(!confirmed){Poll();confirmed=Clean;}
   if(!confirmed){if(CurrentRun!=null)File.WriteAllText(Path.Combine(CurrentRun,"detach.request"),"");ShowState("close-unconfirmed","关闭但清理未确认","可能仍有自有播放器；不声明清理完成，关闭入口时Job会回收自有进程");Write("close-cleanup-unconfirmed",new{workerState=WorkerRole,playerState=PlayerRole,run=CurrentRun});if(watch.Elapsed.TotalSeconds<11)await Task.Delay((int)Math.Min(1000,(12-watch.Elapsed.TotalSeconds)*1000));}
  }catch(Exception ex){Write("close-cleanup-error",new{error=ex.ToString(),cleanupConfirmed=false});}
  finally{poll.Stop();ReleaseJob("controller-close-final");if(!confirmed)confirmed=Clean;if(eventTail!=null)eventTail.Dispose();if(hostTail!=null)hostTail.Dispose();Common.SaveNew(Path.Combine(Session,"controller-close-result.json"),new{cleanupConfirmed=confirmed,workerState=WorkerRole,playerState=PlayerRole,preparationEnded=playbackPreparationTask==null||playbackPreparationTask.IsCompleted,elapsedSeconds=watch.Elapsed.TotalSeconds,closed=DateTimeOffset.Now.ToString("o")});Write("controller-closed",new{cleanupConfirmed=confirmed,elapsedSeconds=watch.Elapsed.TotalSeconds});if(player!=null)player.Dispose();StopTray();Log.Dispose();allowClose=true;Close();}
 }
}
