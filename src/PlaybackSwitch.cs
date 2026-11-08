using System;using System.Threading;using System.Threading.Tasks;
// Coordinates the existing verifier, stop handler and worker launch, never a second player.
internal sealed class PlaybackSwitch {
 internal bool Busy{get;private set;}
 internal bool OldStopAttempted{get;private set;}
 internal bool OldStopped{get;private set;}
 internal bool LaunchAttempted{get;private set;}
 internal bool CancelKeepsOld(string reason){return Busy&&!OldStopAttempted&&!LaunchAttempted&&reason=="user-stop";}
 internal async Task<string> Run(Func<Task<string[]>> prepare,Func<bool> stillSelected,Func<string[],bool> alreadyActive,Func<bool> oldActive,Func<Task<bool>> stopOld,Action<string[]> launch,CancellationToken token){
  if(Busy)return "busy";Busy=true;OldStopAttempted=OldStopped=LaunchAttempted=false;
  try{
   token.ThrowIfCancellationRequested();var verified=await prepare();token.ThrowIfCancellationRequested();
   if(!stillSelected())throw new OperationCanceledException("准备期间选择或确认已变化",token);
   if(alreadyActive(verified))return "same-active";
   if(oldActive()){OldStopAttempted=true;if(!await stopOld())return "cleanup-unconfirmed";OldStopped=true;}
   token.ThrowIfCancellationRequested();if(!stillSelected())throw new OperationCanceledException("撤下旧任务期间选择或确认已变化",token);
   LaunchAttempted=true;launch(verified);return "launch-requested";
  }finally{Busy=false;}
 }
}
internal static class MultiWallpaperPresentation {
 internal static string Selection(string selectedId,string activeId,string activeName,bool alive,string reason){
  return alive&&selectedId!=activeId?reason+"\n正在使用："+activeName+"。只选择卡片不会切换。":reason;
 }
 internal static string AttemptFailure(string chosen,string error,string oldName,bool oldAlive){
  return "未能启动“"+chosen+"”："+error+(oldAlive?"；原任务“"+oldName+"”仍在运行。":"；没有启动新壁纸。");
 }
}
