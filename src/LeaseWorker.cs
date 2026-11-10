using System;using System.IO;using System.Diagnostics;using System.Threading;using System.Collections.Generic;using System.Text;
internal static class LeaseWorker {
#if TEST_BUILD
 internal static Func<string,string> TestEndAfterVerifiedCopy;
#endif
 static string LeaseName(){
#if TEST_BUILD
  return "Local\\R25-HeadlessPlaybackLease-"+PackageAnalysis.Digest(Encoding.UTF8.GetBytes(Path.GetFullPath(Common.Root)));
#else
  return "Local\\Local317495ControlPlaybackLease-v1";
#endif
 }
 internal static int Run(string run){
  run=Path.GetFullPath(run);string logs=Path.Combine(Common.Root,"logs")+Path.DirectorySeparatorChar;
  if(!run.StartsWith(logs,StringComparison.OrdinalIgnoreCase)||!File.Exists(Path.Combine(run,"request.json")))return 2;
  using(var j=new Journal(Path.Combine(run,"events.jsonl"))){
   RuntimeInputLease runtimeInput=null;Process child=null;StreamWriter stdout=null,stderr=null;IntPtr childHandle=IntPtr.Zero;bool leaseHeld=false;var watch=Stopwatch.StartNew();PlaybackFailure hostFailure=null;string startStage="worker-entry";string reason="unknown";int exit=-1;bool forced=false,workerFailed=false;int? playerExit=null;
   HostResources stagedHost=null;bool hostResourcesCleaned=false;string hostResourceCleanupError=null;
   CancellationTokenSource preparationCancel=null;System.Threading.Timer preparationPoll=null;
   using(var lease=new Mutex(false,LeaseName())){
    try{
     try{leaseHeld=lease.WaitOne(0);}catch(AbandonedMutexException){leaseHeld=true;}
     if(!leaseHeld)throw new InvalidOperationException("已有本入口管理的播放器尚未退出，请从原入口撤下。");
     preparationCancel=new CancellationTokenSource(20000);
     var startToken=preparationCancel.Token;
     if(File.Exists(Path.Combine(run,"stop.request.json")))preparationCancel.Cancel();
     preparationPoll=new System.Threading.Timer(_=>{if(File.Exists(Path.Combine(run,"stop.request.json")))preparationCancel.Cancel();},null,0,50);
     var req=Common.Read(Path.Combine(run,"request.json"));string mode=Common.S(req,"mode"),source=Path.GetFullPath(Common.S(req,"cwp"));int seconds=Convert.ToInt32(req["seconds"]);
     if((mode!="preview"&&mode!="desktop")||seconds!=0||Common.S(req,"playbackPolicy")!="continuous")throw new ArgumentException("需要明确的continuous播放策略；不设正常播放期限。");
     j.Write("state",new{state="starting",message="启动中：校验资源，尚未创建播放器"});
     if(!File.Exists(source))throw new FileNotFoundException("CWP 路径不存在，未启动播放器。",source);
     startStage="resource-hash";string hash=PlaybackResourceCheck.Hash(source,startToken);if(hash!=Common.S(req,"expectedSha256"))throw new InvalidDataException("源CWP内容已变化，拒绝加载；请重新选择并试播。");
     if(!String.Equals(Path.GetExtension(source),".cwp",StringComparison.OrdinalIgnoreCase)&&!VideoPackage.Extension(source))throw new InvalidDataException("需要本地CWP文件");
     startStage="resource-policy";string formatPolicy=CwpPolicy.Validate(source,hash,()=>startToken.ThrowIfCancellationRequested());j.Write("cwp-policy-accepted",new{policy=formatPolicy,sha256=hash,pathIndependent=true,compatibilityClaimed=false});
     if(mode=="desktop"&&hash!=Common.CwpHash&&Common.S(req,"previewConfirmedSha256")!=hash)throw new InvalidDataException("必须先确认该哈希的窗口画面，才允许桌面试验。");
     string host=Path.Combine(Common.Runtime,"Desktop317495Host-refined.exe");
     if(Common.Hash(host)!=Common.HostHash)throw new InvalidDataException("播放器哈希不符，拒绝运行。");
     runtimeInput=RuntimeInputs.Begin(run,source,hash,startToken);string input=runtimeInput.Directory;string copy=runtimeInput.Path;
     startStage="private-resource-copy";runtimeInput.Copy(source,startToken);if(PlaybackResourceCheck.Hash(copy,startToken)!=hash||PlaybackResourceCheck.Hash(source,startToken)!=hash)throw new InvalidDataException("源与运行副本哈希不符，拒绝运行。");
#if TEST_BUILD
     if(TestEndAfterVerifiedCopy!=null){reason=TestEndAfterVerifiedCopy(run);exit=0;return exit;}
#endif
     RuntimeDelivery.Verify(Common.AppRoot);
     startStage="component-identity";string externalRuntimeRoot=RuntimeProvisioning.RequireForStart(startToken);
     startStage="host-resource-stage";var verifiedFiles=RuntimeProvisioning.VerifyRoot(externalRuntimeRoot,startToken);
     stagedHost=HostResources.Stage(run,host,Path.Combine(Common.AppRoot,"rules","runtime-components.json"),RuntimeDelivery.ContractHash,Common.HostHash,verifiedFiles,startToken);host=stagedHost.Image;
     j.Write("host-resources-staged",new{image=host,contract=stagedHost.Contract,archives=HostResources.Archives,externalDllsCopied=false});
     Common.SaveNew(Path.Combine(run,"load-input.json"),new{sourceCwp=source,sourceSha256=hash,formatPolicy=formatPolicy,runtimeCwp=copy,runtimeSha256=hash,hostDirectory=Path.GetDirectoryName(host),engineDirectory=externalRuntimeRoot,engineWorkingDirectory=externalRuntimeRoot,resourceRoot=input,cwpFile=Path.GetFileName(copy),externalSourceDirectory=Path.GetDirectoryName(source),externalSourceDirectoryAddedToSearch=false,externalComponentsCopied=true,externalDllsCopied=false,resourceArchives=HostResources.Archives,cacheDirectory=Path.Combine(run,"private"),fallbackAllowed=false});
     // Read-only overlap guard; never take over or stop an existing legacy test host.
     foreach(var p in Process.GetProcesses())using(p){bool candidate=false;try{candidate=p.ProcessName.StartsWith("Desktop317495Host",StringComparison.OrdinalIgnoreCase);if(!candidate)continue;string path=p.MainModule.FileName;if(path.StartsWith(Path.GetDirectoryName(Common.Root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("工作区内已有自有播放器，请先从它原来的入口撤下。");}catch(System.ComponentModel.Win32Exception ex){if(candidate)throw new InvalidOperationException("发现同名宿主但无法核验身份，拒绝新启动。",ex);}catch(InvalidOperationException ex){if(ex.Message.StartsWith("工作区内"))throw;}}
     var info=new ProcessStartInfo(host,Common.Quote(run)+" "+mode+" continuous"+" "+Common.Quote(input)+" "+Common.Quote(Path.GetFileName(copy))+" "+Common.Quote(externalRuntimeRoot)+" "+RuntimeDelivery.ContractHash+" "+Common.HostHash){UseShellExecute=false,WorkingDirectory=externalRuntimeRoot,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Normal,RedirectStandardOutput=true,RedirectStandardError=true};
     foreach(string k in new[]{"APPDATA","LOCALAPPDATA","TEMP","TMP","USERPROFILE"}){string d=Path.Combine(run,"private",k);Directory.CreateDirectory(d);info.EnvironmentVariables[k]=d;}
     info.EnvironmentVariables["PATH"]=externalRuntimeRoot+";"+Path.Combine(externalRuntimeRoot,"depdlls")+";"+Path.GetDirectoryName(host)+";"+Environment.GetFolderPath(Environment.SpecialFolder.Windows)+"\\System32;"+Environment.GetFolderPath(Environment.SpecialFolder.Windows);
     stdout=new StreamWriter(Path.Combine(run,"native-stdout.log"),false,new UTF8Encoding(false)){AutoFlush=true};stderr=new StreamWriter(Path.Combine(run,"native-stderr.log"),false,new UTF8Encoding(false)){AutoFlush=true};
     child=new Process{StartInfo=info};object outputGate=new object();
     child.OutputDataReceived+=(s,e)=>{if(e.Data!=null)lock(outputGate)stdout.WriteLine(e.Data);};child.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(outputGate)stderr.WriteLine(e.Data);};
     // Worker cannot launch native code until UI has bound this exact worker to its job.
     startStage="job-grant";var grantWait=Stopwatch.StartNew();string grantPath=Path.Combine(run,"job-ready.json");
     while(!File.Exists(grantPath)&&grantWait.ElapsedMilliseconds<4000){if(File.Exists(Path.Combine(run,"stop.request.json")))throw new OperationCanceledException("Stopped before job launch grant");startToken.ThrowIfCancellationRequested();Thread.Sleep(50);}
     if(!File.Exists(grantPath))throw new TimeoutException("No verified job launch grant in4seconds; no player created");
     var grant=Common.Read(grantPath);if(Common.S(grant,"token")!=Common.S(req,"runToken")||Convert.ToInt32(grant["workerPid"])!=Process.GetCurrentProcess().Id||Convert.ToInt64(grant["workerCreationFileTime"])!=ProcessProof.Creation(Process.GetCurrentProcess().Handle)||Convert.ToInt32(grant["controllerPid"])!=Convert.ToInt32(req["controllerPid"]))throw new InvalidDataException("Job grant identity mismatch");
     j.Write("job-launch-granted",new{workerPid=Process.GetCurrentProcess().Id,token=Common.S(req,"runToken"),normalTimeLimit=false});
     startToken.ThrowIfCancellationRequested();preparationPoll.Dispose();preparationPoll=null;
#if TEST_BUILD
     throw new InvalidOperationException("TEST_BUILD禁止启动播放器");
#else
     child.Start();
#endif
     childHandle=child.Handle;child.BeginOutputReadLine();child.BeginErrorReadLine();
     string token=Common.S(req,"runToken");long childCreated=ProcessProof.Creation(childHandle);
     Common.SaveNew(Path.Combine(run,"child-identity.json"),new{pid=child.Id,creationFileTime=childCreated.ToString(),image=host,run=run,token=token,workerPid=Process.GetCurrentProcess().Id});
     IntPtr remote=ProcessProof.TransferToController(childHandle,req);
     Common.SaveNew(Path.Combine(run,"player-handoff.json"),new{remoteHandle=remote.ToInt64(),pid=child.Id,creationFileTime=childCreated.ToString(),image=host,run=run,token=token,workerPid=Process.GetCurrentProcess().Id,workerCreationFileTime=ProcessProof.Creation(Process.GetCurrentProcess().Handle).ToString(),controllerPid=Convert.ToInt32(req["controllerPid"])});
     j.Write("player-handoff",new{token=token,pid=child.Id,creationFileTime=childCreated.ToString(),targetController=Convert.ToInt32(req["controllerPid"])});
     var ackWait=Stopwatch.StartNew();while(!File.Exists(Path.Combine(run,"handoff.accepted"))&&ackWait.ElapsedMilliseconds<3000)Thread.Sleep(50);
     if(!File.Exists(Path.Combine(run,"handoff.accepted")))throw new TimeoutException("控制窗口未在3秒内确认播放器句柄，撤下本次播放器");
     Common.SaveNew(Path.Combine(run,"identity.json"),new{pid=child.Id,startUtc=child.StartTime.ToUniversalTime().ToString("o"),exe=host,exeSha256=Common.HostHash,sourceCwp=source,sourceSha256=hash,runtimeCwp=copy,runtimeSha256=hash,resourceRoot=input,cwpFile=Path.GetFileName(copy),hostDirectory=Path.GetDirectoryName(host),engineDirectory=externalRuntimeRoot,externalComponentsCopied=true,externalDllsCopied=false,resourceArchives=HostResources.Archives,mode=mode,seconds=seconds,workerPid=Process.GetCurrentProcess().Id,environmentIsSecuritySandbox=false});
     j.Write("player-created",new{pid=child.Id,started=child.StartTime.ToString("o"),handleHeld=true,mode=mode,seconds=seconds,cwp=source,sha256=hash});
     var uiAge=Stopwatch.StartNew();long lastUiSequence=-1;var hostTail=new IncrementalLines(Path.Combine(run,"host.jsonl"));bool stop=false,engine=false,announced=false;double stopAt=0;double lastBeat=-1,lastUiRead=-1;long beatSequence=0;int ownWorkerPid=Process.GetCurrentProcess().Id;long ownWorkerCreated=ProcessProof.Creation(Process.GetCurrentProcess().Handle);Dictionary<string,object> hostData=null;
     // This process is independent of UI responsiveness; the exact child handle is retained.
     while(Common.WaitForSingleObject(childHandle,0)==258){
      if(watch.Elapsed.TotalSeconds-lastBeat>=1){lastBeat=watch.Elapsed.TotalSeconds;Common.ReplaceJson(Path.Combine(run,"worker-heartbeat.json"),new{token=token,workerPid=ownWorkerPid,workerCreationFileTime=ownWorkerCreated.ToString(),sequence=++beatSequence,elapsedSeconds=watch.Elapsed.TotalSeconds});j.Write("worker-heartbeat",new{elapsedSeconds=watch.Elapsed.TotalSeconds});}
#if DEBUG_FAULTS
      if(File.Exists(Path.Combine(run,"debug-stall.request"))){j.Write("debug-worker-stall",new{seconds=30,debugBuildOnly=true});Thread.Sleep(30000);File.Delete(Path.Combine(run,"debug-stall.request"));}
#endif
      if(watch.Elapsed.TotalSeconds-lastUiRead>=.5){lastUiRead=watch.Elapsed.TotalSeconds;try{var heartbeat=Common.Read(Path.Combine(run,"ui-heartbeat.json"));long seq=Convert.ToInt64(heartbeat["sequence"]);if(Common.S(heartbeat,"token")==Common.S(req,"runToken")&&seq>lastUiSequence){lastUiSequence=seq;uiAge.Restart();}}catch{}}
      if(!stop&&uiAge.Elapsed.TotalSeconds>8){stop=true;stopAt=watch.Elapsed.TotalSeconds;reason="controller-ui-heartbeat-stale";j.Write("control-chain-fault",new{reason=reason,message="控制界面超过8秒未响应，worker已自动撤下",normalPlaybackDeadline=false});File.WriteAllText(Path.Combine(run,"detach.request"),"");}
      string stopPath=Path.Combine(run,"stop.request.json");
      if(!stop&&File.Exists(stopPath)){stop=true;stopAt=watch.Elapsed.TotalSeconds;try{reason=Common.S(Common.Read(stopPath),"reason");}catch{reason="stop-request";}j.Write("state",new{state="stopping",message="撤下中",reason=reason});File.WriteAllText(Path.Combine(run,"detach.request"),"");}
      string hostLog=Path.Combine(run,"host.jsonl");
      if(File.Exists(hostLog)){
       foreach(var row in hostTail.Pull()){
        string phase=Common.S(row,"phase");var data=row["data"]as Dictionary<string,object>;if(data==null)continue;
        if(phase=="host")hostData=data;
        if(phase=="engine-start"){engine=true;j.Write("state",new{state="engine-started",message="引擎已启动；尚未验证首帧",firstFrameVerified=false});}
        if(phase=="heartbeat-diagnostics-disabled"||phase=="log-retention-warning")j.Write("log-warning",row);
        if(phase=="engine-failed"||phase=="attach-failed"||phase=="stop-error"||phase=="detach-failed")j.Write("host-error",row);
        if(phase=="withdraw-begin"&&!stop){reason=Common.S(data,"reason");j.Write("state",new{state="stopping",message="撤下中",reason=reason});}
        if(phase=="closed"&&!stop)reason=Common.S(data,"reason");
       }
      }
      if(engine&&hostData!=null&&!announced&&!stop){
       var geo=Common.Obj(hostData["window"]);IntPtr hwnd=new IntPtr(Convert.ToInt64(geo["hwnd"]));uint pid;
       if(Common.IsWindow(hwnd)&&Common.GetWindowThreadProcessId(hwnd,out pid)!=0&&pid==(uint)child.Id&&(mode!="desktop"||Common.GetParent(hwnd)!=IntPtr.Zero)){
        if(mode=="preview")Common.ShowWindowAsync(hwnd,9);announced=true;j.Write("state",new{state=mode=="desktop"?"mounted":"preview",message=mode=="desktop"?"已挂载：结构已确认，首帧未自动验证":"正在预览：窗口已建立，首帧未自动验证",firstFrameVerified=false,hwnd=hwnd.ToInt64(),pid=child.Id});
       }
      }
      if(stop&&watch.Elapsed.TotalSeconds-stopAt>8){forced=true;reason=reason+"-stop-timeout";j.Write("owned-process-emergency",new{reason=reason,pid=child.Id,exactHandle=true});Common.TerminateProcess(childHandle,124);}
      Thread.Sleep(100);
     }
     hostTail.Dispose();child.WaitForExit();exit=child.ExitCode;playerExit=exit;
     if(reason=="unknown"){using(var finalTail=new IncrementalLines(Path.Combine(run,"host.jsonl"))){foreach(var row in finalTail.Pull())if(Common.S(row,"phase")=="closed")reason=Common.S(Common.Obj(row["data"]),"reason");}}
     if(reason=="unknown")reason=exit==0?"host-exit":"host-failure";
     try{hostFailure=PlaybackFailure.ReadHost(run);}catch(Exception detailError){j.Write("host-failure-read-error",new{error=detailError.ToString()});}
     if(PlaybackFailure.Failed(exit,hostFailure)){workerFailed=true;if(exit==0)exit=1;}
     j.Write("state",new{state=workerFailed?"failed":"exited",message=!workerFailed?"已退出":hostFailure==null?"播放器失败（退出码"+exit+"）":hostFailure.Message,failureStage=hostFailure==null?null:hostFailure.Stage,error=hostFailure==null?null:hostFailure.Error,reason=reason,exitCode=exit,playerExitCode=playerExit,firstFrameVerified=false});
    }catch(OperationCanceledException ex){bool requested=File.Exists(Path.Combine(run,"stop.request.json"));reason=requested?"start-cancelled-before-player":"start-preparation-timeout";exit=requested?0:1;workerFailed=!requested;j.Write("state",new{state=requested?"exited":"failed",message=requested?"已取消启动准备":"启动准备超出20秒预算",error=ex.ToString(),playerCreated=childHandle!=IntPtr.Zero});}
    catch(Exception ex){workerFailed=true;reason="validation-start-or-monitor-failure";j.Write("state",new{state="failed",message=ex.Message,error=ex.ToString(),hresult=ex.HResult,nativeError=ex.HResult&65535,registryTarget=ex.Data["atomicTarget"],registryAttempts=ex.Data["atomicAttempts"],registryFirstHResult=ex.Data["atomicFirstHResult"],failureStage=startStage,reason=reason});exit=1;}
    finally{
     if(preparationPoll!=null){using(var done=new ManualResetEvent(false)){preparationPoll.Dispose(done);done.WaitOne(1000);}}if(preparationCancel!=null)preparationCancel.Dispose();
     if(childHandle!=IntPtr.Zero&&Common.WaitForSingleObject(childHandle,0)==258){
      File.WriteAllText(Path.Combine(run,"detach.request"),"");if(!child.WaitForExit(8000)){forced=true;Common.TerminateProcess(childHandle,124);child.WaitForExit(5000);}playerExit=child.HasExited?child.ExitCode:124;if(!workerFailed)exit=playerExit.Value;
     }
     if(stdout!=null)stdout.Dispose();if(stderr!=null)stderr.Dispose();
     if(stagedHost!=null){bool childExited=childHandle==IntPtr.Zero||Common.WaitForSingleObject(childHandle,0)==0;try{if(!childExited)throw new IOException("宿主仍在运行，保留资源目录");stagedHost.Cleanup();hostResourcesCleaned=true;j.Write("host-resources-cleaned",new{root=stagedHost.Root,childExited=true});}catch(Exception cleanupError){hostResourceCleanupError=cleanupError.ToString();j.Write("host-resource-cleanup-failed",new{error=hostResourceCleanupError,root=stagedHost.Root});if(!workerFailed){workerFailed=true;exit=1;reason="host-resource-cleanup-failed";}}}
     string requestedStopReason=null;try{if(File.Exists(Path.Combine(run,"stop.request.json")))requestedStopReason=Common.S(Common.Read(Path.Combine(run,"stop.request.json")),"reason");}catch{}
     Common.SaveNew(Path.Combine(run,"result.json"),new{exitCode=exit,playerExitCode=playerExit,hostResourcesCleaned=hostResourcesCleaned,hostResourceCleanupError=hostResourceCleanupError,preparationStage=startStage,failureStage=hostFailure==null?null:hostFailure.Stage,failureDetail=hostFailure==null?null:hostFailure.Error,workerFailed=workerFailed,reason=reason,requestedStopReason=requestedStopReason,forcedOwnProcess=forced,elapsedSeconds=watch.Elapsed.TotalSeconds,ended=DateTimeOffset.Now.ToString("o"),playerCreated=childHandle!=IntPtr.Zero});
     try{if(runtimeInput!=null)runtimeInput.End(childHandle==IntPtr.Zero||Common.WaitForSingleObject(childHandle,0)==0,workerFailed||forced||exit!=0,startStage,reason);}catch(Exception retentionError){j.Write("runtime-input-finalize-failed",new{error=retentionError.ToString(),originalExit=exit,originalReason=reason});}finally{if(runtimeInput!=null)runtimeInput.Dispose();}
     j.Write("worker-exit",new{exitCode=exit,reason=reason,forcedOwnProcess=forced});if(child!=null)child.Dispose();if(leaseHeld)lease.ReleaseMutex();
    }
   }return exit;
  }
 }
}
