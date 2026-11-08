using System;using System.Drawing;using System.Windows.Forms;
internal sealed partial class ControlWindow {
 NotifyIcon tray;ContextMenuStrip trayMenu;ToolStripMenuItem trayStatus,trayOpen,trayChoose,trayStop,trayExit;TrayCommands trayCommands;bool trayFailed;
 TrayState CurrentTrayState(){return TrayState.For(closing,transition||restoring||playbackPreparation.Busy,PlayerRole=="alive",CurrentRun!=null&&!Clean,activeRunRequest==null?null:Common.S(activeRunRequest,"mode"),activeRunName);}
 void StartTray(){
  if(tray!=null||closing)return;
  try{
   trayCommands=new TrayCommands(()=>RestoreInterface(),()=>StopManaged("tray-stop"),()=>Close());
   trayMenu=new ContextMenuStrip();trayStatus=new ToolStripMenuItem{Enabled=false};trayMenu.Items.Add(trayStatus);trayMenu.Items.Add(new ToolStripSeparator());
   trayOpen=new ToolStripMenuItem("打开主界面");trayOpen.Click+=(s,e)=>trayCommands.Open(CurrentTrayState());trayMenu.Items.Add(trayOpen);
   trayChoose=new ToolStripMenuItem("选择 / 更换壁纸");trayChoose.Click+=(s,e)=>{if(trayCommands.Open(CurrentTrayState())){cards.Focus();libraryHint.Text="请点击需要的壁纸卡片，再预览或设为桌面；选择卡片本身不会切换播放。";}};trayMenu.Items.Add(trayChoose);
   trayStop=new ToolStripMenuItem("停止播放并恢复原桌面");trayStop.Click+=async(s,e)=>{try{await trayCommands.Stop(CurrentTrayState());}catch(Exception ex){RestoreInterface();Write("tray-stop-error",new{error=ex.ToString()});libraryHint.Text="停止未完成，请查看状态："+ex.Message;}finally{RefreshTray();}};trayMenu.Items.Add(trayStop);
   trayMenu.Items.Add(new ToolStripSeparator());trayExit=new ToolStripMenuItem("彻底退出");trayExit.Click+=(s,e)=>trayCommands.Exit(CurrentTrayState());trayMenu.Items.Add(trayExit);
   tray=new NotifyIcon{Icon=Icon,ContextMenuStrip=trayMenu};tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Left)trayCommands.Open(CurrentTrayState());};tray.DoubleClick+=(s,e)=>trayCommands.Open(CurrentTrayState());
   // WinForms NotifyIcon's native window handles Explorer's TaskbarCreated message.
   RefreshTray();tray.Visible=true;Write("tray-ready",new{oneIcon=true,existingControllerOnly=true,windowsMayCollapseIcon=true});
  }catch(Exception ex){StopTray();trayFailed=true;libraryHint.Text="托盘入口未能建立，保留主界面："+ex.Message;Write("tray-start-failed",new{error=ex.ToString(),hideDisabled=true});}
 }
 void RefreshTray(){if(tray==null)return;var s=CurrentTrayState();try{tray.Text=s.Text;trayStatus.Text=s.Text;trayOpen.Enabled=s.Open;trayChoose.Enabled=s.Open;trayStop.Enabled=s.Stop;trayExit.Enabled=s.Exit;}catch(Exception ex){if(!trayFailed){trayFailed=true;Write("tray-update-failed",new{error=ex.ToString()});}if(interfaceHidden&&!closing)RestoreInterface();}}
 void StopTray(){if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}if(trayMenu!=null){trayMenu.Dispose();trayMenu=null;}trayCommands=null;}
}
