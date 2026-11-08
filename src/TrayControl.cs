using System;using System.Threading.Tasks;
// The controller routes to the existing instance and stop/close lifecycle. No process creation.
internal sealed class TrayState {
 internal string Text;internal bool Stop,Open,Exit;
 internal static TrayState For(bool closing,bool busy,bool active,bool recovery,string mode,string runningName){
  string status=closing?"正在退出":busy?"正在处理播放任务":active?(mode=="desktop"?"桌面运行中":"预览中"):recovery?"停止清理待确认":"未播放";
  string name=String.IsNullOrWhiteSpace(runningName)?"未命名壁纸":runningName.Replace('\r',' ').Replace('\n',' ');
  string text="本地壁纸 · "+status+(active?" · "+name:"");if(text.Length>63)text=text.Substring(0,62)+"…";
  return new TrayState{Text=text,Open=!closing,Stop=!closing&&!busy&&(active||recovery),Exit=!closing};
 }
}
internal sealed class TrayCommands {
 readonly Func<bool> open;readonly Func<Task> stop;readonly Action exit;bool stopping;
 internal TrayCommands(Func<bool> show,Func<Task> stopPlayback,Action close){open=show;stop=stopPlayback;exit=close;}
 internal bool Open(TrayState state){return state.Open&&open();}
 internal async Task Stop(TrayState state){if(!state.Stop||stopping)return;stopping=true;try{await stop();}finally{stopping=false;}}
 internal void Exit(TrayState state){if(state.Exit)exit();}
}
