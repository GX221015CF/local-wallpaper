#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Reflection;
using System.Windows.Forms;
using System.Drawing;

internal static class RefinementRegression {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static;
 static readonly List<object> results=new List<object>();
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 static void Need(bool condition,string message){if(!condition)throw new InvalidDataException(message);}
 static void Check(string name,Action action){try{action();results.Add(new{name=name,passed=true});}catch(Exception error){results.Add(new{name=name,passed=false,error=error.ToString(),hresult=error.HResult,native=error.HResult&65535});}}
 internal static HostResources Fixture(string run,CancellationToken token){
  string input=Path.Combine(Common.Root,"owned-host-inputs");Directory.CreateDirectory(input);
  string host=Path.Combine(input,"host.exe"),contract=Path.Combine(input,"contract.json");
  if(!File.Exists(host)){
   File.WriteAllText(host,"owned inert host fixture; never executed");
   var rows=new List<object>{new{name="Desktop317495Host-refined.exe",sha256=Common.Hash(host)}};
   foreach(string archive in HostResources.Archives){string path=Path.Combine(input,archive);File.WriteAllText(path,new string('x',2048));rows.Add(new{name=archive,sha256=Common.Hash(path)});}
   for(int i=0;i<21;i++)rows.Add(new{name="owned-unused-"+i+".dll",sha256=new string('a',64)});
   Common.SaveNew(contract,new{files=rows});
  }
  Directory.CreateDirectory(run);
  return HostResources.Stage(run,host,contract,Common.Hash(contract),Common.Hash(host),HostResources.Archives.ToDictionary(name=>name,name=>Path.Combine(input,name)),token);
 }
 static void Click(Button button){typeof(Button).GetMethod("OnClick",Flags).Invoke(button,new object[]{EventArgs.Empty});}
 [STAThread]static int Main(string[] args){
  Check("actual-host-stage-survives-short-owned-directory-handle",()=>{
   string run=Path.Combine(Common.LogRoot,"run-host-transient");Task release=null;
   HostResources.TestBeforeCommit=(temporary,final)=>{
    var handle=CreateFile(temporary,0x80,3,IntPtr.Zero,3,0x02000000,IntPtr.Zero);
    Need(!handle.IsInvalid,"owned test directory handle opens");release=Task.Run(()=>{Thread.Sleep(180);handle.Dispose();});
   };
   try{using(var cancel=new CancellationTokenSource(2500)){var staged=Fixture(run,cancel.Token);Need(Directory.Exists(staged.Root)&&staged.Root==Path.Combine(run,"host"),"actual Stage must commit exactly once");staged.Cleanup();}}
   finally{HostResources.TestBeforeCommit=null;if(release!=null)Need(release.Wait(1000),"owned handle release ends");}
  });
  Check("actual-host-stage-transient-native5-at-directory-commit",()=>{
   int attempts=0;HostResources.TestMoveDirectory=(source,target)=>{if(++attempts==1)throw new UnauthorizedAccessException("owned fault model native5 at actual commit seam");Directory.Move(source,target);};
   try{using(var cancellation=new CancellationTokenSource(2500)){var staged=Fixture(Path.Combine(Common.LogRoot,"run-native5"),cancellation.Token);Need(attempts==2,"one recoverable commit failure should retry after ownership checks");staged.Cleanup();}}
   finally{HostResources.TestMoveDirectory=null;}
  });
  Check("permanent-native5-is-bounded-with-original-error-and-cleanup-truth",()=>{
   string run=Path.Combine(Common.LogRoot,"run-native5-fixed");int attempts=0;Exception original=null;var clock=System.Diagnostics.Stopwatch.StartNew();
   HostResources.TestMoveDirectory=(source,target)=>{attempts++;throw new UnauthorizedAccessException("fixed-owned-denial");};
   try{Fixture(run,CancellationToken.None);}catch(UnauthorizedAccessException error){original=error;}finally{HostResources.TestMoveDirectory=null;}
   Need(original!=null&&(original.HResult&65535)==5&&attempts==4&&clock.ElapsedMilliseconds<1600,"fixed refusal cannot become infinite retry or success");
   var report=Common.Read(Path.Combine(run,"host-stage-result.json"));
   Need(!Convert.ToBoolean(report["committed"])&&Convert.ToBoolean(report["stagingCleaned"])&&!Convert.ToBoolean(report["sourceStillExists"]),"failed Stage records owned staging cleanup independently");
   Need(Common.S(report,"firstCommitError").Contains("fixed-owned-denial"),"first raw commit exception retained");
  });
  Check("target-competition-never-overwrites-or-retries-foreign-target",()=>{
   string run=Path.Combine(Common.LogRoot,"run-target-competition");int attempts=0;
   HostResources.TestMoveDirectory=(source,target)=>{attempts++;Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"foreign.txt"),"keep");throw new IOException("target-created",unchecked((int)0x80070020));};
   bool rejected=false;try{Fixture(run,CancellationToken.None);}catch(IOException){rejected=true;}finally{HostResources.TestMoveDirectory=null;}
   Need(rejected&&attempts==1&&File.ReadAllText(Path.Combine(run,"host","foreign.txt"))=="keep","competing target remains untouched");
  });
  Check("changed-source-is-retained-and-commit-is-not-attempted",()=>{
   string run=Path.Combine(Common.LogRoot,"run-source-change");int moves=0;
   HostResources.TestBeforeCommit=(source,target)=>File.AppendAllText(Path.Combine(source,"build","runtime","Desktop317495Host-refined.exe"),"changed");
   HostResources.TestMoveDirectory=(source,target)=>{moves++;Directory.Move(source,target);};
   bool rejected=false;try{Fixture(run,CancellationToken.None);}catch(InvalidDataException){rejected=true;}finally{HostResources.TestBeforeCommit=null;HostResources.TestMoveDirectory=null;}
   var report=Common.Read(Path.Combine(run,"host-stage-result.json"));
   Need(rejected&&moves==0&&Convert.ToBoolean(report["stagingRetained"])&&Directory.Exists(Common.S(report,"temporary")),"changed tree cannot be committed or silently deleted");
  });
  Check("cancel-before-and-during-recovery-keeps-bounded-cleanup",()=>{
   foreach(bool during in new[]{false,true})using(var cancel=new CancellationTokenSource()){
    string run=Path.Combine(Common.LogRoot,during?"run-cancel-retry":"run-cancel-commit");int attempts=0;
    HostResources.TestBeforeCommit=(source,target)=>{if(!during)cancel.Cancel();};
    HostResources.TestMoveDirectory=(source,target)=>{attempts++;cancel.Cancel();throw new IOException("cancel-after-transient",unchecked((int)0x80070020));};
    bool cancelled=false;try{Fixture(run,cancel.Token);}catch(OperationCanceledException){cancelled=true;}finally{HostResources.TestBeforeCommit=null;HostResources.TestMoveDirectory=null;}
    Need(cancelled&&attempts<2,"cancellation stops retry sequence");var report=Common.Read(Path.Combine(run,"host-stage-result.json"));Need(Convert.ToBoolean(report["stagingCleaned"]),"cancelled owned tree is cleaned by actual Stage");
   }
  });
  Check("self-owned-copy-streams-are-closed-before-directory-commit",()=>{
   HostResources.TestBeforeCommit=(source,target)=>{foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.None)){Need(stream.Length>0,"no copied file is held by own reader or writer");}};
   try{var staged=Fixture(Path.Combine(Common.LogRoot,"run-stream-release"),CancellationToken.None);staged.Cleanup();staged.Cleanup();}
   finally{HostResources.TestBeforeCommit=null;}
  });
  Check("nonmodal-rename-cancel-click-ends-edit-without-save",()=>{
   int saved=0;var dialog=new RenameWallpaperDialog("原名",value=>saved++);
   try{dialog.Editor.Input.Text="待取消";Click(dialog.CancelButtonControl);Need(dialog.IsDisposed&&saved==0,"actual cancel Click must close nonmodal editor without saving");}
   finally{dialog.Dispose();}
  });
  Check("rename-Escape-close-and-failed-save-cancellation-share-safe-exit",()=>{
   foreach(string mode in new[]{"escape","close","after-failure","invalid"}){
    int saves=0;var dialog=new RenameWallpaperDialog("最后保存值",value=>{saves++;throw new IOException("owned-store-failure");});
    try{
     if(mode=="after-failure"){dialog.Editor.Input.Text="失败输入";Need(!dialog.TrySave()&&!dialog.IsDisposed,"failed save leaves editor active");}
     if(mode=="invalid")dialog.Editor.Input.Text="";
     if(mode=="escape")typeof(RenameWallpaperDialog).GetMethod("ProcessDialogKey",Flags).Invoke(dialog,new object[]{Keys.Escape});
     else if(mode=="close")dialog.Close();else Click(dialog.CancelButtonControl);
     Need(dialog.IsDisposed&&saves==(mode=="after-failure"?1:0),"cancel/escape/close never commits pending input");
    }finally{dialog.Dispose();}
   }
  });
  Check("settings-underline-page-resize-consistency",()=>{
   using(var tabs=new MaterialTabs()){
    for(int i=0;i<4;i++)tabs.TabPages.Add("分组"+i);tabs.CreateControl();
    foreach(int index in new[]{0,3,1,2,2,0,3,0}){
     tabs.SelectedIndex=index;tabs.Size=new Size(700-index*25,350);tabs.PerformLayout();
     Need(tabs.SelectedTab==tabs.TabPages[index],"actual selected content remains final selected index");
     Need(tabs.Indicator==tabs.TargetIndicator&&!tabs.AnimationRunning,"hidden/resize indicator snaps to current selected page");
    }
   }
  });
  Check("resource-ID-labels-preserve-titles-and-confirmation-semantics",()=>{
   var item=new WallpaperItem{Id="local-guid",ResourceId="395741",Name="本地壁纸 395741",PreviewConfirmedSha256=new string('a',64)};
   Need(ResourcePresentation.Card(item,"窗口已确认 · 桌面未验证")=="已确认 · ID 395741","card removes display-only desktop-unverified suffix and labels ID");
   Need(item.Name=="本地壁纸 395741"&&item.PreviewConfirmedSha256==new string('a',64),"titles and original confirmation remain intact");
   item.ResourceId=null;Need(ResourcePresentation.Id(item)=="本地条目 local-guid","local identifier is never a truncated official ID");
  });
  Check("failure-presentation-separates-ended-task-and-retained-materials",()=>{
   string run=Path.Combine(Common.LogRoot,"run-native5-fixed");
   Common.SaveNew(Path.Combine(run,"runtime-input-cleanup.json"),new{state="retained-failure-budget",bytes=14089328});
   Common.SaveNew(Path.Combine(run,"result.json"),new{hostResourcesCleaned=false});
   string text=ResourcePresentation.FailureMaterials(run);
   Need(text.Contains("播放任务已结束")&&text.Contains("宿主暂存已回收")&&text.Contains("故障运行副本按预算保留"),"failure does not claim all files reclaimed");
  });
  Check("self-owned-dialog-icons-use-independent-selected-icon-clones",()=>{
   using(var expected=AppVisuals.LoadIcon()){
    byte[] expectedBytes;using(var stream=new MemoryStream()){expected.Save(stream);expectedBytes=stream.ToArray();}
    for(int i=0;i<3;i++){
     using(var dialog=new RenameWallpaperDialog("保留名",value=>{}))using(var stream=new MemoryStream()){
      dialog.Icon.Save(stream);Need(stream.ToArray().SequenceEqual(expectedBytes),"rename editor uses chosen blue icon");
      Need(dialog.Icon.Handle!=expected.Handle,"each editor owns its own icon clone");
     }
     using(var dialog=new MaterialDialog())using(var stream=new MemoryStream()){dialog.Icon.Save(stream);Need(stream.ToArray().SequenceEqual(expectedBytes),"settings and other Material dialogs share icon rule");}
    }
    Need(expected.Handle!=IntPtr.Zero,"disposing dialogs cannot dispose another icon clone");
   }
  });
  Check("home-right-has-no-summary-no-scroll-and-all-primary-controls-fit",()=>{
   using(var window=new ControlWindow(null,0,null)){
    var side=(Panel)typeof(ControlWindow).GetField("side",Flags).GetValue(window);var rows=(TableLayoutPanel)typeof(ControlWindow).GetField("sideRows",Flags).GetValue(window);
    Need(!side.AutoScroll&&typeof(ControlWindow).GetField("selectedSummaryToggle",Flags)==null,"obsolete summary and right scrolling removed");
    foreach(float scale in new[]{1f,1.25f,1.5f})foreach(Size work in new[]{new Size(1280,720),new Size(1366,768),new Size(1920,1040)}){
     typeof(ControlWindow).GetField("uiScale",Flags).SetValue(window,scale);
     var client=HomeLayout.Client(work,scale);int available=client.Height-(int)Math.Round(136*scale);
     side.Size=new Size((int)(292*scale),available);side.Padding=new Padding((int)(12*scale));
     rows.Size=side.DisplayRectangle.Size;
     int[] heights={0,40,38,44,36,44,38,26};for(int i=0;i<heights.Length;i++)rows.RowStyles[i].Height=(int)Math.Round(heights[i]*scale);
     foreach(bool confirmationVisible in new[]{false,true}){
     typeof(ControlWindow).GetMethod("LayoutSide",Flags,null,new[]{typeof(bool)},null).Invoke(window,new object[]{confirmationVisible});rows.PerformLayout();
     float total=rows.RowStyles.Cast<RowStyle>().Where(row=>row.SizeType==SizeType.Absolute).Sum(row=>row.Height);
     Need(total<=side.ClientSize.Height-side.Padding.Vertical,"all right-side rows fit work area and scale budget");
     foreach(string field in new[]{"preview","desktop","stop","keepPlaying","renameWallpaper","wallpaperMore"}){
      var button=(Control)typeof(ControlWindow).GetField(field,Flags).GetValue(window);Need(button.TabStop&&button.Height>=20,"primary action remains keyboard reachable and usable height");
      var bounds=button.RectangleToScreen(button.ClientRectangle);var viewport=side.RectangleToScreen(side.ClientRectangle);Need(bounds.Bottom<=viewport.Bottom-side.Padding.Bottom+1,"primary button is geometrically inside right viewport");
     }
     }
    }
    var settings=(Form)typeof(ControlWindow).GetField("settingsWindow",Flags).GetValue(window);settings.Dispose();
   }
  });
  Check("both-actual-rename-entrance-events-can-cancel-and-reopen",()=>{
   using(var window=new ControlWindow(null,0,null)){
    window.TestSuppressRenameShow=true;
    var right=(Button)typeof(ControlWindow).GetField("renameWallpaper",Flags).GetValue(window);
    var cards=(FlowLayoutPanel)typeof(ControlWindow).GetField("cards",Flags).GetValue(window);var card=(WallpaperCard)cards.Controls[0];
    var library=(WallpaperLibrary)typeof(ControlWindow).GetField("library",Flags).GetValue(window);string before=Common.Hash(library.FilePath);
    for(int repeat=0;repeat<2;repeat++){
     Click(right);var editor=(RenameWallpaperDialog)typeof(ControlWindow).GetField("renameWindow",Flags).GetValue(window);Need(editor!=null&&!editor.IsDisposed,"right action enters shared editor");
     editor.Editor.Input.Text="取消未保存";Click(editor.CancelButtonControl);Need(editor.IsDisposed,"right action cancel closes editor");
     typeof(ToolStripItem).GetMethod("OnClick",Flags).Invoke(card.ContextMenuStrip.Items[0],new object[]{EventArgs.Empty});
     editor=(RenameWallpaperDialog)typeof(ControlWindow).GetField("renameWindow",Flags).GetValue(window);Need(editor.Editor.Input.Text==card.Item.Name,"card context reopens last saved name");
     typeof(RenameWallpaperDialog).GetMethod("ProcessDialogKey",Flags).Invoke(editor,new object[]{Keys.Escape});Need(editor.IsDisposed,"card context Escape closes editor");
    }
    Need(Common.Hash(library.FilePath)==before,"both cancelled entrance chains leave catalog unchanged");
    ((Form)typeof(ControlWindow).GetField("settingsWindow",Flags).GetValue(window)).Dispose();
   }
  });
  bool passed=results.All(row=>(bool)row.GetType().GetProperty("passed").GetValue(row,null));
  Common.SaveNew(args[0],new{passed=passed,results=results,scope="Actual HostResources.Stage with24-row owned substitute contract; native5/32 fault models at directory-commit seam; own handle experiment; real cancel Click/escape/close event chains without Show; offscreen tab draw and layout checks; no engine or window automation"});
  return passed?0:1;
 }
}
#endif
