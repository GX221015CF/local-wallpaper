using System;using System.IO;using System.Drawing;using System.Threading.Tasks;using System.Windows.Forms;
internal sealed partial class ControlWindow {
 readonly Button removeWallpaper=new SoftButton();bool removing;
 void UpdateRemoveButton(){removeWallpaper.Enabled=selected!=null&&!closing&&!transition&&!removing&&library.Error==null&&(importOperation==null||!importOperation.Busy);}
 async Task RemoveChosenWallpaper(bool askUser=true){
  var target=selected;if(target==null||closing||transition||removing||(importOperation!=null&&importOperation.Busy))return;
  if(askUser){string message="将“"+target.Name+"”从本软件列表移除？\n\n不删除官方缓存、原始CWP或已保存副本。\n如果它正在播放，会先停止并恢复原桌面。\n需要时可以重新导入。";if(MessageBox.Show(this,message,"移除壁纸",MessageBoxButtons.OKCancel,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2)!=DialogResult.OK){Write("wallpaper-remove-cancelled",new{id=target.Id});return;}}
  removing=true;UpdateRemoveButton();
  try{
   bool active=activeRunRequest!=null&&Common.S(activeRunRequest,"selectedCatalogId")==target.Id&&CurrentRun!=null&&!Clean;
   if(active){await StopManaged("remove-active-wallpaper");if(!Clean){libraryHint.Text="清理未确认，未移除壁纸。请先恢复原桌面或正常退出。";libraryHint.ForeColor=Color.Firebrick;Write("wallpaper-remove-blocked",new{id=target.Id,reason="cleanup-unconfirmed"});return;}}
   if(closing)return;string archive=library.Remove(target);if(detailsWindow!=null&&!detailsWindow.IsDisposed)detailsWindow.Close();selected=library.Selected;Cwp=selected==null?Common.DefaultCwp:selected.SourcePath;RefreshCards();libraryHint.ForeColor=Muted;libraryHint.Text="已从列表移除，原文件和已保存副本均保留；需要时可重新导入。";
   Write("wallpaper-removed",new{id=target.Id,sourceHash=target.RegisteredSha256,source=target.SourcePath,archive=archive,scope="catalog-only",stoppedOwnActiveWallpaper=active,remaining=library.Doc.Items.Count,noResourceDeleted=true});
  }catch(Exception ex){libraryHint.Text="未能移除："+ex.Message;libraryHint.ForeColor=Color.Firebrick;Write("wallpaper-remove-failed",new{id=target.Id,error=ex.Message});}
  finally{removing=false;UpdateRemoveButton();}
 }
}
