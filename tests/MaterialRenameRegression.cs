#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class MaterialRenameRegression {
 static readonly List<string> checks=new List<string>();
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static;
 static void Need(bool value,string name){if(!value)throw new InvalidDataException(name);checks.Add(name);}
 static void Rejected(Action action,string name){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Need(rejected,name);}
 static NameEvidence Evidence(string id){return new NameEvidence{Id=id,Name="官方新名称",Kind="official-local-wallpaper-response",SourceSha256=new string('a',64),IndexSha256=new string('b',64),SourceBytes=10,ResponsePath="/pcwallpapercontent/wallpaper/jingling/detailDynamic"};}
 static object Field(object obj,string name){return obj.GetType().GetField(name,Flags).GetValue(obj);}
 static IEnumerable<Control> Descendants(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
 static string Fixture(string name,byte seed){string folder=Path.Combine(Common.Root,"fixture");Directory.CreateDirectory(folder);return (string)typeof(MultiWallpaperRegression).GetMethod("Fixture",Flags).Invoke(null,new object[]{folder,name,seed});}
 static void RenameChecks(){
  var library=new WallpaperLibrary();library.Doc.Items.Clear();library.Doc.SelectedId=null;library.Save();bool duplicate;
  var first=library.Add(Fixture("first",3),out duplicate);first.ResourceId="12345";var second=library.Add(Fixture("second",8),out duplicate);second.ResourceId="23456";
  first.Name="原有名称";first.NameSource=Evidence("12345");first.PreviewConfirmedSha256=first.RegisteredSha256;library.Save();
  string path=first.SourcePath,hash=first.RegisteredSha256,cover=first.CoverPath;var provenance=first.NameSource;
  WallpaperNames.Rename(library,first,"  中文，自定义！  ");
  Need(first.Name=="中文，自定义！"&&first.CustomName==first.Name&&first.NameBeforeCustom=="原有名称","rename-trims-and-preserves-original-name");
  Need(first.SourcePath==path&&first.RegisteredSha256==hash&&first.CoverPath==cover&&first.NameSource==provenance&&first.PreviewConfirmedSha256==hash,"rename-preserves-identity-cover-confirmation-and-name-provenance");
  WallpaperNames.Rename(library,first,"本地壁纸 12345");
  Need(!OfficialNameStore.Update(first,Evidence("12345"),null)&&first.Name=="本地壁纸 12345","explicit-placeholder-looking-custom-name-wins-over-official-refresh");
  WallpaperNames.Rename(library,second,first.Name);Need(second.Name==first.Name&&second.Id!=first.Id,"same-display-name-retains-distinct-resource-identities");
  Need(!ControlWindow.RegisteredNameQueryIds(library.Doc.Items).Any(),"custom-names-excluded-from-background-placeholder-query");
  foreach(string invalid in new[]{"","  ","bad\nname","bad\tname",new string('x',81)})Rejected(()=>WallpaperNames.Rename(library,first,invalid),"invalid-name-rejected-"+checks.Count);
  string before=Common.Hash(library.FilePath),oldName=first.Name;
  Directory.CreateDirectory(Path.Combine(Common.Root,"tests"));string fail=Path.Combine(Common.Root,"tests","fail-catalog-save");File.WriteAllText(fail,"owned-save-failure");
  try{bool rejected=false;try{WallpaperNames.Rename(library,first,"不得覆盖");}catch(IOException){rejected=true;}Need(rejected&&first.Name==oldName&&Common.Hash(library.FilePath)==before,"save-failure-rolls-back-model-and-preserves-catalog");}
  finally{File.Delete(fail);}
  var candidate=new ImportCandidate{Id="12345",Name="后续登记名称",Source=path,Bytes=new FileInfo(path).Length};
  string operation=Path.Combine(Common.WorkRoot,"duplicate");Directory.CreateDirectory(Path.Combine(operation,"payload"));
  // Existing matching content in the catalog is reused by the actual commit.
  string dir=Path.Combine(Common.Root,"library",candidate.Id,hash);Directory.CreateDirectory(dir);File.Copy(path,Path.Combine(dir,candidate.Id+".cwp"));
  ManagedCatalog.Commit(library,new ImportReceipt{Candidate=candidate,Sha256=hash,Bytes=candidate.Bytes,Format="CWP-v2",ImportedAt=DateTimeOffset.Now.ToString("o")},operation,true,()=>{},out duplicate);
  LocalImport.CleanPayload(operation);Need(duplicate&&first.Name==oldName&&first.CustomName==oldName,"duplicate-registration-does-not-replace-custom-name");
  var originalRequest=new Dictionary<string,object>{{"selectedCatalogId",first.Id},{"selectedName","运行时原名"}};
  WallpaperNames.Rename(library,first,"运行中改名");
  Need(WallpaperNames.ActiveName(library,first.Id,"旧名")=="运行中改名"&&(string)originalRequest["selectedName"]=="运行时原名","active-display-updates-without-mutating-original-request");
  Need(SettingsIdentityText.Selection(second).Contains(second.SourcePath)&&SettingsIdentityText.Playing(library,first.Id,"旧名",true,"runA").Contains(first.Name),"settings-selected-B-and-running-A-bind-to-distinct-ids");
  library.Select(first);Common.SaveNew(Path.Combine(Common.Root,"expected-id.json"),new{id=first.Id,name=first.Name});
  var legacy=new WallpaperItem{Id="legacy",Name="历史仅Name",SourcePath=path,RegisteredSha256=hash};library.Doc.Items.Add(legacy);library.Save();
  Need(legacy.CustomName==null&&legacy.NameBeforeCustom==null,"legacy-name-origin-is-not-guessed");
 }
 static void Controls(){
  using(var dialog=new RenameWallpaperDialog("预填原名",name=>{throw new IOException("fixture-store-busy");})){
   Need(dialog.Editor.Input.Text=="预填原名","rename-dialog-prefills-current-name");dialog.Editor.Input.Text="输入保留";
   Need(!dialog.TrySave()&&dialog.Editor.Input.Text=="输入保留"&&dialog.Validation.Text.Contains("未保存"),"dialog-save-failure-retains-input-with-inline-error");
   Need(dialog.CancelButton==dialog.CancelButtonControl,"rename-cancel-is-explicit-and-escape-accessible");
  }
  using(var window=new ControlWindow(null,0,null)){
   var settings=(Form)Field(window,"settingsWindow");var tabs=(MaterialTabs)Field(window,"settingsTabs");
   Need(tabs.TabPages.Count==4,"settings-contains-all-four-existing-feature-groups");
   Need(!settings.Visible&&!window.Visible,"control-construction-never-shows-a-window");
   foreach(string field in new[]{"sourceHint","libraryHint","libraryPathRow","loginOptions","mute"}){
    var control=(Control)Field(window,field);Need(Descendants(settings).Contains(control),"technical-control-moved-to-settings-"+field);
   }
   Need(Descendants(window).Any(control=>control.Name=="OpenSettings"),"fixed-settings-entry-is-on-home");
   Need(!Descendants(window).Any(control=>control==Field(window,"sourceHint")||control==Field(window,"libraryHint")),"home-has-no-technical-preparation-controls");
   var method=typeof(ControlWindow).GetMethod("EnsureSettingsWindow",Flags);for(int i=0;i<8;i++)method.Invoke(window,null);
   Need(Object.ReferenceEquals(settings,Field(window,"settingsWindow")),"repeat-settings-entry-reuses-one-window-and-controls");
   tabs.SelectedIndex=1;tabs.SelectedIndex=0;Need(tabs.SelectedIndex==0,"settings-switch-return-keeps-controls-usable");
   Need(Descendants(settings).Any(control=>control.Text=="查看所选壁纸详情")&&Descendants(settings).Any(control=>control.Text=="打开当前会话日志"),"selected-details-and-actual-session-log-entries-preserved");
   Need(Descendants(settings).Contains((Control)Field(window,"handoff"))&&!Descendants(window).Contains((Control)Field(window,"handoff")),"diagnostic-handoff-keeps-home-status-unobstructed");
   string catalog=Path.Combine(Common.Root,"data","catalog.json"),catalogHash=Common.Hash(catalog);
   typeof(ControlWindow).GetMethod("RefreshSettingsState",Flags).Invoke(window,null);
   Need(Common.Hash(catalog)==catalogHash&&!File.Exists(Path.Combine(Common.Root,"data","runtime-location.json")),"opening-and-refreshing-settings-does-not-adopt-components-or-write-catalog");
   var token=new CancellationTokenSource();typeof(ControlWindow).GetField("runtimeCheck",Flags).SetValue(window,token);typeof(ControlWindow).GetField("runtimeCheckRunning",Flags).SetValue(window,true);
   typeof(ControlWindow).GetMethod("UpdatePreparationActions",Flags).Invoke(window,null);
   Need(((Button)Field(window,"checkComponents")).Text=="取消组件检查"&&!((Button)Field(window,"chooseComponents")).Enabled,"settings-actions-follow-real-checking-state");
   typeof(ControlWindow).GetMethod("CancelRuntimeCheck",Flags).Invoke(window,null);
   Need(token.IsCancellationRequested&&((Button)Field(window,"checkComponents")).Text=="正在取消…","settings-cancel-keeps-existing-cancellation-chain");
   typeof(ControlWindow).GetField("runtimeCheck",Flags).SetValue(window,null);typeof(ControlWindow).GetField("runtimeCheckRunning",Flags).SetValue(window,false);token.Dispose();
   settings.Hide();typeof(ControlWindow).GetMethod("RefreshSettingsState",Flags).Invoke(window,null);
   Need(!settings.IsDisposed,"hidden-settings-remains-refreshable-for-async-completion");
   for(int width=900;width<=1180;width+=140){window.ClientSize=new Size(width,680);window.PerformLayout();Need(((Control)Field(window,"openSettings")).Width>50,"settings-entry-retains-usable-width-at-"+width);}
   settings.Dispose();
  }
  foreach(float scale in new[]{1f,1.25f,1.5f})foreach(Size screen in new[]{new Size(1280,720),new Size(1366,768),new Size(1920,1080)}){
   Size size=HomeLayout.Client(screen,scale);Need(size.Width<=screen.Width&&size.Height<=screen.Height,"home-client-fits-screen-scale-"+screen.Width+"-"+scale);
   Need(HomeLayout.CardColumns(Math.Max(100,size.Width-(int)(360*scale)),scale)>=1,"card-columns-remain-positive-"+screen.Width+"-"+scale);
  }
 }
 [STAThread] static int Main(string[] args){try{
  if(args.Length>1&&args[1]=="reopen"){
   var expected=Common.Read(Path.Combine(Common.Root,"expected-id.json"));var library=new WallpaperLibrary();var item=library.Doc.Items.Single(x=>x.Id==Common.S(expected,"id"));
   Need(item.Name==Common.S(expected,"name")&&item.CustomName==item.Name,"independent-process-reloads-custom-name");
   Need(!OfficialNameStore.Update(item,Evidence("12345"),null),"independent-reopen-custom-name-survives-official-refresh");
   Need(library.Doc.Items.Single(x=>x.Id=="legacy").CustomName==null,"legacy-data-remains-compatible-on-reopen");
  }else{RenameChecks();Controls();}
  Common.SaveNew(args[0],new{passed=true,checks=checks,evidenceKind="TEST_BUILD real handlers, constructed owned controls without Show or input injection, layout computations; no real GUI appearance claim"});return 0;
 }catch(Exception error){Common.SaveNew(args[0],new{passed=false,checks=checks,error=error.ToString()});return 1;}}
}
#endif
