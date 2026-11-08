using System;
// Formats existing state only. It cannot enable playback or change confirmations.
internal static class RuntimePresentation {
 internal static bool MayRefresh(bool disposed,bool dataFault,bool onlyIfOwned,string visible,string rendered){return !disposed&&!dataFault&&(!onlyIfOwned||visible==rendered);}
 internal static string Runtime(bool available,bool checking,bool cancelling,string checkedAt,string notice,bool playerAlive,string state,string mode){
  string active=null;
  if(playerAlive){
   if(state=="mounted"&&mode=="desktop")active="桌面壁纸正在运行。";
   else if(state=="preview"&&mode=="preview")active="正在窗口预览。";
   else if(state=="engine-started")active="组件已加载，正在准备显示；画面需人工确认。";
   else if(state=="stopping"||state=="stop-timeout")active="正在停止本次播放并清理。";
   else if(state=="starting")active="正在启动本次播放，尚待运行结果。";
   else active="本次播放任务仍在，具体状态见下方。";
  }
  if(checking)return (cancelling?"正在取消本次组件核对…":"正在后台核对本机组件（再次点击可取消）…")+(active==null?"":" "+active);
  if(active!=null)return "播放组件与运行状态："+active;
  if(!available)return String.IsNullOrEmpty(notice)?"播放组件尚未准备好 · 点击选择已有播放组件目录。":notice;
  if(!String.IsNullOrEmpty(notice)&&(notice.Contains("已取消")||notice.Contains("未通过")||notice.Contains("取消检查")))return notice+" 当前未播放。";
  string stamp=String.IsNullOrEmpty(checkedAt)?"":"（"+checkedAt+"）";
  return "本机播放组件已通过身份核验"+stamp+"；当前未播放。";
 }
 internal static string CoverUnavailable(string state,string reason){
  return state=="mounted"?"封面截图仅支持预览窗口；桌面播放不受影响。":"封面截图暂不可用："+(reason??"请先打开并确认预览窗口。");
 }
}
