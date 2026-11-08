#if TEST_BUILD
using System;using System.IO;using System.Linq;using System.Threading.Tasks;
using System.Drawing;using System.Windows.Forms;using System.Diagnostics;
internal sealed partial class ControlWindow {
 WallpaperItem MovieItem(){return library.Doc.Items.Single(x=>x.RegisteredSha256==CwpPolicy.Movie395647Hash);}
 internal async Task RunMovieIntegration(){try{
  SelectWallpaper(MovieItem());ObserveGallery("integration-initial");OfflineShot("integration-initial-ui");
  if(library.Doc.Items.Count!=3||!File.Exists(selected.CoverPath)||settings.Muted!=true||settings.Volume!=30)throw new Exception("Migration or cover/settings mismatch");
  SelectWallpaper(library.Doc.Items.Find(x=>x.Id=="builtin-317495"));await StartMode("preview");await WaitOfflineState("preview");await Task.Delay(1200);
  string previous=CurrentRun;SelectWallpaper(MovieItem());await Task.Delay(600);
  if(activeRunRequest==null||Common.S(activeRunRequest,"selectedCatalogId")==selected.Id)throw new Exception("Selection unexpectedly changed playback");
  ObserveGallery("selected-movie-old-still-playing");
  await StartMode("preview");await WaitOfflineState("preview");
  if(!File.Exists(Path.Combine(previous,"controller-cleanup.json"))||!Convert.ToBoolean(Common.Read(Path.Combine(previous,"controller-cleanup.json"))["confirmed"]))throw new Exception("Previous run cleanup not confirmed before replacement");
  await Task.Delay(2200);OfflineShot("integration-preview-ui");
  Common.SaveNew(Path.Combine(Common.Root,"evidence/integration-preview-ready.json"),new{run=CurrentRun,previousRun=previous,mode="preview",sha256=CwpPolicy.Movie395647Hash});
  await Task.Delay(5500);await StopManaged("integration-active-stop");if(!Clean)throw new Exception("Active stop cleanup unconfirmed");
  OfflineShot("integration-stopped-ui");Write("movie-integration-passed",new{oldPreview=true,moviePreview=true,sourceHash=CwpPolicy.Movie395647Hash,switchConfirmed=true});Close();return;
 }catch(Exception ex){Write("movie-integration-failed",new{error=ex.ToString()});}await StopManaged("integration-failed");Close();}
 internal async Task RunMovieDesktop(){Form layer=null;try{
  SelectWallpaper(MovieItem());if(selected.PreviewConfirmedSha256!=Common.Hash(selected.SourcePath))throw new Exception("Explicit user window confirmation required; no desktop attempted");
  await StartMode("desktop");await WaitOfflineState("mounted");await Task.Delay(2000);
  string run=CurrentRun;SelectWallpaper(library.Doc.Items.Find(x=>x.Id=="builtin-317495"));await Task.Delay(500);
  if(Common.S(activeRunRequest,"expectedSha256")!=CwpPolicy.Movie395647Hash)throw new Exception("Card selection changed actual desktop");
  OfflineShot("desktop-playing-ui");ObserveGallery("desktop-selection-separate");
  WindowState=FormWindowState.Minimized;
  layer=new Form{Text="本地壁纸 · 层级验证",ClientSize=new Size(340,90),StartPosition=FormStartPosition.Manual,Location=new Point(930,650),BackColor=Color.White,ShowInTaskbar=false};
  layer.Controls.Add(new Label{Text="普通窗口在壁纸之上\n本轮测试结束后将自动退出",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=Font});layer.Show();
  Common.SaveNew(Path.Combine(Common.Root,"evidence/movie-desktop-ready.json"),new{run=run,ordinaryWindow=layer.Handle.ToInt64(),controllerWindow=Handle.ToInt64(),sha256=CwpPolicy.Movie395647Hash});
  await Task.Delay(10000);layer.Close();layer.Dispose();layer=null;WindowState=FormWindowState.Normal;
  Write("movie-desktop-smoke-ended",new{run=run,automaticCompatibilityPromotion=false,reason="close-active-desktop"});
  Close();return;
 }catch(Exception ex){Write("movie-desktop-failed",new{error=ex.ToString()});}
 finally{if(layer!=null){layer.Close();layer.Dispose();}}
 await StopManaged("desktop-smoke-failed");Close();}
}
#endif
