using System;using System.IO;using System.Diagnostics;using System.Collections.Generic;
internal sealed partial class ControlWindow {
 JobFence runJob;IncrementalLines eventTail,hostTail;long uiSequence;readonly Stopwatch uiBeat=Stopwatch.StartNew();bool faultRecoveryQueued;Dictionary<string,object> lastHostBeat;
 void InitializeRunSafety(){
  ReleaseJob("previous-run-confirmed-clean");if(eventTail!=null)eventTail.Dispose();if(hostTail!=null)hostTail.Dispose();
  eventTail=new IncrementalLines(Path.Combine(CurrentRun,"events.jsonl"));hostTail=new IncrementalLines(Path.Combine(CurrentRun,"host.jsonl"));
  activeRunRequest=Common.Read(Path.Combine(CurrentRun,"request.json"));activeRunName=Common.S(activeRunRequest,"selectedName");workerBeatSequence=0;lastHostBeat=null;faultRecoveryQueued=false;uiSequence=0;uiBeat.Restart();runJob=new JobFence();EmitUiHeartbeat(true);
 }
 void EmitUiHeartbeat(bool force=false){
  if(CurrentRun==null||!force&&uiBeat.ElapsedMilliseconds<1000)return;uiBeat.Restart();
  string target=Path.Combine(CurrentRun,"ui-heartbeat.json"),temp=target+".tmp";
  try{File.WriteAllText(temp,Common.Json.Serialize(new{token=runToken,sequence=++uiSequence,local=DateTimeOffset.Now.ToString("o")}));if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}
  catch(Exception ex){Write("ui-heartbeat-write-error",new{error=ex.Message});}
 }
 void BindWorkerJob(){
  ProcessProof.Verify(worker.Handle,workerPid,workerCreation,System.Windows.Forms.Application.ExecutablePath);runJob.Assign(worker.Handle);
  Common.SaveNew(Path.Combine(CurrentRun,"job-ready.json"),new{token=runToken,workerPid=workerPid,workerCreationFileTime=workerCreation.ToString(),controllerPid=Process.GetCurrentProcess().Id,killOnOwnerExit=true,jobTimeLimit=false});
  Write("job-bound",new{workerPid=workerPid,workerCreationFileTime=workerCreation.ToString(),anonymous=true,inheritableHandle=false,structureBytes=runJob.StructureSize,limitFlags="00002000",normalTimeLimit=false});
 }
 void ReleaseJob(string reason){if(runJob!=null){Write("job-close",new{reason=reason,scope="only this run's assigned worker and inherited descendants",killOnClose=true});runJob.Dispose();runJob=null;}}
 void RecoverBrokenWorker(){
  if(faultRecoveryQueued||closing||transition||Clean||player==null)return;
  if(WorkerRole!="exited"&&WorkerRole!="unresponsive")return;
  faultRecoveryQueued=true;lastRunFailure="工作进程失联，已自动启动本次自有播放器撤下";Write("worker-fault-auto-withdraw",new{workerState=WorkerRole,playerState=PlayerRole,noPlaybackDeadlineDependency=true});
  BeginInvoke((Action)(async()=>await StopManaged("worker-fault-auto-withdraw")));
 }
 void UpdateContinuousPresentation(){
  if(hostTail!=null)foreach(var row in hostTail.Pull())if(Common.S(row,"phase")=="heartbeat")lastHostBeat=Common.Obj(row["data"]);
  string text=PlayerRole=="alive"?(lastHostBeat==null?"持续运行":"持续运行 · "+Common.S(lastHostBeat,"elapsedSeconds")+" 秒"):"未在播放";
  if(countdown.Text!=text)countdown.Text=text;
 }
}
