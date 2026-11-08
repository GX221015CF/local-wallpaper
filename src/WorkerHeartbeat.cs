using System;using System.IO;using System.Diagnostics;
internal sealed partial class ControlWindow {
 long workerBeatSequence;readonly Stopwatch workerBeatReadClock=Stopwatch.StartNew();
 void ObserveWorkerHeartbeat(){
  if(CurrentRun==null||worker==null||workerBeatReadClock.ElapsedMilliseconds<500)return;workerBeatReadClock.Restart();
  try{var h=Common.Read(Path.Combine(CurrentRun,"worker-heartbeat.json"));long n=Convert.ToInt64(h["sequence"]);if(Common.S(h,"token")==runToken&&Convert.ToInt32(h["workerPid"])==workerPid&&Convert.ToInt64(h["workerCreationFileTime"])==workerCreation&&n>workerBeatSequence){workerBeatSequence=n;beatSeen=true;beatAge.Restart();}}catch{}
 }
 void ShowLogWarning(object data){libraryHint.Text="心跳诊断日志保存异常；控制心跳独立，播放仍可停止。详情见日志。";libraryHint.ForeColor=System.Drawing.Color.Firebrick;Write("log-warning-observed",data);}
}
