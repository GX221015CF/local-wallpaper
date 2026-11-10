using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed partial class ControlWindow {
 void RefreshEmptyState() {
  if(emptyState.IsDisposed)return;
  bool show=library==null||library.Doc==null||library.Doc.Items.Count==0;
  emptyState.Visible=show;
  if(!show)return;
  var copy=EmptyStatePresentation.Create(sourceState,RuntimeProvisioning.Available,runtimeCheckRunning,Common.Root);
  emptyLead.Text="查看本机已下载的壁纸，检查通过后选择导入。准备问题请打开设置。";
  emptySource.Text=copy.Source;
  emptyData.Text=copy.Data;
  emptyPlayback.Text=copy.Playback;
  emptySource.ForeColor=sourceState!=null&&sourceState.Found?Color.FromArgb(20,125,91):Muted;
  emptyPlayback.ForeColor=RuntimeProvisioning.Available?Muted:Color.FromArgb(157,82,28);
  emptyData.ForeColor=Muted;
  emptyImport.Enabled=!closing&&!transition;
  emptyAdd.Enabled=!closing&&!transition;
  emptyChooseSource.Enabled=!closing&&!transition;
  emptyImport.Text=sourceState!=null&&sourceState.Found?"查看并导入本机候选":"导入本机已下载壁纸";
  string[] descriptions={copy.Source,copy.Data,copy.Playback};
  emptySource.AccessibleDescription=descriptions[0];
  emptyData.AccessibleDescription=descriptions[1];
  emptyPlayback.AccessibleDescription=descriptions[2];
 }
}
