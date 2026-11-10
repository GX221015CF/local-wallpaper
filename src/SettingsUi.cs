using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class SettingsIdentityText {
 internal static string Selection(WallpaperItem selected){return selected==null?"当前选中 · 无":"当前选中 · "+selected.Name+"\n"+ResourcePresentation.Id(selected)+"\n文件 · "+selected.SourcePath;}
 internal static string Playing(WallpaperLibrary library,string activeId,string original,bool alive,string run){var item=library.Doc.Items.FirstOrDefault(row=>row.Id==activeId);return alive?"实际播放 · "+WallpaperNames.ActiveName(library,activeId,original)+"\n"+(item==null?"本地条目 "+activeId:ResourcePresentation.Id(item))+"\n本次任务 · "+run:"实际播放 · 当前未播放\n最近任务 · "+(run??"无");}
}

internal sealed partial class ControlWindow {
 MaterialDialog settingsWindow;
 MaterialTabs settingsTabs;
 readonly Label settingsSelection=new Label(),settingsRunning=new Label(),settingsOperation=new Label();
 readonly LinkLabel homePreparation=new LinkLabel();
 readonly MaterialNotice homeNotice=new MaterialNotice();
 readonly LoadingSkeleton settingsSkeleton=new LoadingSkeleton();
 readonly SoftButton openSettings=new SoftButton();
 readonly Panel preparationHost=new Panel();
 static readonly string[] SettingsGroups={"来源与存储","播放组件","声音与登录","详情与诊断"};

 Control SettingsSection(string title,string explanation){
  var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(22)};
  panel.Controls.Add(new Label{Text=title,Font=new Font(Font.FontFamily,17,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,12)});
  panel.Controls.Add(new Label{Text=explanation,AutoSize=false,Width=600,Height=52,ForeColor=Muted,Margin=new Padding(0,0,0,14)});
  return panel;
 }
 void EnsureSettingsWindow(){
  if(settingsWindow!=null&&!settingsWindow.IsDisposed)return;
  settingsWindow=new MaterialDialog{Text="设置 · 本地壁纸",Name="WallpaperSettings",StartPosition=FormStartPosition.CenterParent,ClientSize=new Size(820,620),MinimumSize=new Size(680,500),ShowInTaskbar=false};
  var outer=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),RowCount=2,ColumnCount=1};outer.RowStyles.Add(new RowStyle(SizeType.Percent,100));outer.RowStyles.Add(new RowStyle(SizeType.Absolute,48));settingsWindow.Controls.Add(outer);
  settingsTabs=new MaterialTabs{Dock=DockStyle.Fill,Font=Font};outer.Controls.Add(settingsTabs,0,0);
  foreach(var name in SettingsGroups)settingsTabs.TabPages.Add(new TabPage(name){BackColor=Theme.Canvas,UseVisualStyleBackColor=false});
  var storage=SettingsSection("来源与存储","来源供发现候选使用；导入会把选中的资源复制到持久个人库。原缓存保持原位置。");settingsTabs.TabPages[0].Controls.Add(storage);
  sourceHint.AutoSize=false;sourceHint.Size=new Size(600,72);sourceHint.Location=Point.Empty;storage.Controls.Add(sourceHint);
  var sourceButton=new SoftButton{Text="更改壁纸来源",Size=new Size(190,40)};sourceButton.Click+=(s,e)=>ChooseSourceLocation();storage.Controls.Add(sourceButton);
  personalLibraryStatus.Dock=DockStyle.None;personalLibraryStatus.AutoSize=false;personalLibraryStatus.Size=new Size(600,95);storage.Controls.Add(personalLibraryStatus);
  var names=new Label{Text="可选名称缓存\n"+OfficialNames.CacheRoot+"\n使用现有固定只读位置补充名称，无需配置。缺失不影响检查和导入；自定义名称优先。",Size=new Size(600,105),ForeColor=Muted};storage.Controls.Add(names);
  var components=SettingsSection("播放组件","只核验保存位置及有依据的标准安装位置。首次采用需明确确认；检查不会加载引擎或播放。");settingsTabs.TabPages[1].Controls.Add(components);
  preparationHost.Size=new Size(620,205);preparationHost.Controls.Add(BuildPreparationArea());components.Controls.Add(preparationHost);
  // Storage information has one home, in the first group. The reused component
  // panel contains only runtime status/actions and its expandable technical note.
  storage.Controls.Add(personalLibraryStatus);personalLibraryStatus.Dock=DockStyle.None;
  settingsSkeleton.Size=new Size(600,46);components.Controls.Add(settingsSkeleton);
  components.Controls.Add(new Label{Text="缺文件、版本变化或配置失效时按上方原因处理。外部组件由你合法安装，软件包不携带它们。",Size=new Size(600,65),ForeColor=Muted});
  var sound=SettingsSection("声音与登录恢复","声音调整沿用现有保存机制。仅打开本页不会修改登录恢复配置。");settingsTabs.TabPages[2].Controls.Add(sound);
  var audio=BuildAudioPanel();audio.Dock=DockStyle.None;audio.Size=new Size(550,85);sound.Controls.Add(audio);
  loginOptions.Text="登录恢复设置";loginOptions.Size=new Size(220,42);loginOptions.Click+=(s,e)=>ShowLoginOptions();sound.Controls.Add(loginOptions);
  var diagnostics=SettingsSection("详情与诊断","所选条目和实际播放任务分别显示。技术记录保留各次运行当时的名称。");settingsTabs.TabPages[3].Controls.Add(diagnostics);
  foreach(var label in new[]{settingsSelection,settingsRunning}){label.Size=new Size(600,100);label.AutoEllipsis=true;label.UseMnemonic=false;diagnostics.Controls.Add(label);}
  var details=new SoftButton{Text="查看所选壁纸详情",Size=new Size(230,42)};details.Click+=(s,e)=>ShowDetails();diagnostics.Controls.Add(details);
  var logsButton=new SoftButton{Text="打开当前会话日志",Size=new Size(230,42)};logsButton.Click+=(s,e)=>OpenLogs();diagnostics.Controls.Add(logsButton);
  handoff.Dock=DockStyle.None;handoff.Size=new Size(230,42);diagnostics.Controls.Add(handoff);diagnostics.Controls.Add(new Label{Text=discoveryHint.Text,Size=new Size(600,38),ForeColor=Muted});
  settingsOperation.Size=new Size(600,72);settingsOperation.ForeColor=Muted;diagnostics.Controls.Add(settingsOperation);
  var result=new Label{Size=new Size(600,65),ForeColor=Muted};result.Text="原“详情与日志”已归入本页。日志属于当前控制会话；运行任务见上方名称与run标识。";diagnostics.Controls.Add(result);
  var close=new SoftButton{Text="完成",Size=new Size(120,38),Anchor=AnchorStyles.Right};close.Click+=(s,e)=>settingsWindow.Hide();outer.Controls.Add(close,0,1);
  settingsWindow.CancelButton=close;
  settingsWindow.FormClosing+=(s,e)=>{if(!closing&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;settingsWindow.Hide();}};
  FormClosed+=(s,e)=>{if(settingsWindow!=null)settingsWindow.Dispose();};
  RefreshSettingsState();
 }
 void ShowSettings(int group=0){
  if(closing)return;EnsureSettingsWindow();settingsTabs.SelectedIndex=Math.Max(0,Math.Min(SettingsGroups.Length-1,group));
  RefreshSettingsState();if(!settingsWindow.Visible)settingsWindow.Show(this);settingsWindow.Activate();
 }
 void RefreshSettingsState(){
  if(settingsWindow==null||settingsWindow.IsDisposed)return;
  string activeId=activeRunRequest==null?null:Common.S(activeRunRequest,"selectedCatalogId");
  settingsSelection.Text=SettingsIdentityText.Selection(selected);
  settingsRunning.Text=SettingsIdentityText.Playing(library,activeId,activeRunName,PlayerRole=="alive",CurrentRun);
  settingsOperation.Text="最近操作提示 · "+libraryHint.Text;
  settingsSkeleton.Visible=runtimeCheckRunning&&runtimeDiscovery==null&&!RuntimeProvisioning.Available;
 }
 void RefreshHomePreparation(){
  string message=runtimeCheckRunning?"正在检查播放条件…":!RuntimeProvisioning.Available?"播放组件需要处理 · 打开设置":sourceState!=null&&!sourceState.Found?"壁纸来源需要处理 · 打开设置":"播放条件已就绪";
  homePreparation.Text=message;homePreparation.LinkArea=RuntimeProvisioning.Available&&sourceState!=null&&sourceState.Found?new LinkArea(0,0):new LinkArea(0,message.Length);
  if(lastFault!=null&&!lastFault.StatusOnly&&libraryHint.Text!=runtimeRenderedNotice){message="有一项操作需要处理 · 查看设置";homePreparation.Text=message;homePreparation.LinkArea=new LinkArea(0,message.Length);}
  homePreparation.AccessibleDescription=message;RefreshSettingsState();
 }
}
