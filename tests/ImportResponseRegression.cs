#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class ImportResponseRegression {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
 static object Field(object owner,string name){return owner.GetType().GetField(name,Flags).GetValue(owner);}
 static object Call(object owner,string name,params object[] values){return owner.GetType().GetMethods(Flags).Single(method=>method.Name==name&&method.GetParameters().Length==values.Length).Invoke(owner,values);}
 static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 static void Layout(Control root){root.PerformLayout();foreach(Control child in root.Controls)Layout(child);}
 static async Task Images(Form dialog){var method=dialog.GetType().GetMethod("WaitForImages",Flags);if(method!=null)await (Task)method.Invoke(dialog,null);}
 static void Prepare(){
  if(File.Exists(Path.Combine(Common.Root,"response-fixture-ready.json")))return;
  Directory.CreateDirectory(Path.Combine(Common.Root,"tests"));string missing=Path.Combine(Common.Root,"tests","no-official-names");Directory.CreateDirectory(missing);File.WriteAllText(Path.Combine(Common.Root,"tests","name-cache-root.txt"),missing);
  for(int index=0;index<61;index++){
   var candidate=(ImportCandidate)typeof(ImportExperienceRegression).GetMethod("Resource",Flags).Invoke(null,new object[]{"response-"+index,(750000+index).ToString(),(byte)(90+index),false});
   CoverCache.WorkFolder=Path.Combine(Common.WorkRoot,"fixture-"+index);Directory.CreateDirectory(CoverCache.WorkFolder);CandidateChecks.Run(candidate,()=>{},null,true);Need(CandidateChecks.Passed(candidate),"owned fixture is structurally checked");
   string image=Path.Combine(Common.Root,"data","response-covers",index+".jpg");Directory.CreateDirectory(Path.GetDirectoryName(image));
   using(var bitmap=new Bitmap(1600,900))using(var graphics=Graphics.FromImage(bitmap)){
    graphics.Clear(Color.FromArgb(80+index,140,210));using(var brush=new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0,0,1600,900),Color.FromArgb(200,230,180),Color.FromArgb(40+index,70,120),45))graphics.FillEllipse(brush,0,0,1600,900);
    bitmap.Save(image,System.Drawing.Imaging.ImageFormat.Jpeg);
   }
   candidate.Name="响应样本 "+index;candidate.Cover=image;candidate.CoverReason="自有性能夹具图片";
   typeof(CandidateChecks).GetMethod("FinishCover",Flags).Invoke(null,new object[]{candidate,candidate.Check});
   typeof(CandidateChecks).GetMethod("SaveResult",Flags).Invoke(null,new object[]{candidate,candidate.Check,new Action(()=>{})});
  }
  Common.SaveNew(Path.Combine(Common.Root,"response-fixture-ready.json"),new{count=61,imageSize="1600x900",owned=true,cacheAllValid=true,sourceHashingOnlyDuringPreparation=true});
 }
 static void ImagePreset(bool compact){
  string folder=Path.Combine(Common.Root,"data","response-covers");
  foreach(string record in Directory.GetFiles(Path.Combine(LocalImport.DataRoot,"cache_info"),"*.dat")){
   var candidate=LocalImport.ReadRecord(record);int index=Int32.Parse(candidate.Id)-750000;
   string image=Path.Combine(folder,index+".jpg"),original=image+".original";if(!File.Exists(original))File.Copy(image,original);
   if(compact){using(var source=Image.FromFile(original))using(var bitmap=new Bitmap(source,new Size(360,203)))bitmap.Save(image,System.Drawing.Imaging.ImageFormat.Jpeg);}
   else File.Copy(original,image,true);
   string cache=(string)typeof(CandidateChecks).GetMethod("Cache",Flags).Invoke(null,new object[]{candidate});var proof=Common.Json.Deserialize<CandidateCheckResult>(Common.ReadShared(cache));
   candidate.Cover=image;candidate.CoverReason="自有性能夹具图片";typeof(CandidateChecks).GetMethod("FinishCover",Flags).Invoke(null,new object[]{candidate,proof});typeof(CandidateChecks).GetMethod("SaveResult",Flags).Invoke(null,new object[]{candidate,proof,new Action(()=>{})});
  }
 }
 sealed class Heartbeat:IDisposable {
  readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=10};readonly Stopwatch clock=Stopwatch.StartNew();long last;internal double Longest;internal int Ticks;
  internal Heartbeat(){last=clock.ElapsedMilliseconds;timer.Tick+=(sender,args)=>{long now=clock.ElapsedMilliseconds;Longest=Math.Max(Longest,now-last);last=now;Ticks++;};timer.Start();}
  internal void Reset(){Longest=0;Ticks=0;last=clock.ElapsedMilliseconds;}
  public void Dispose(){timer.Dispose();}
 }
 static string ReportPath,CurrentStage="prepare";
 static void Stage(string value){CurrentStage=value;File.WriteAllText(ReportPath+".stage",value);}
 static async Task<object> Exercise(){
  var measures=new List<object>();using(var owner=new ControlWindow(null,0,null))using(var heartbeat=new Heartbeat()){
   owner.TestSuppressImportShow=true;typeof(ControlWindow).GetField("sourceState",Flags).SetValue(owner,new SourceRootState{Found=true,Root=LocalImport.DataRoot});
   await Task.Delay(50);heartbeat.Reset();
   for(int pass=0;pass<2;pass++){
    Stage("open-"+pass);var watch=Stopwatch.StartNew();Task open=owner.TestOpenImport();double entryReturn=watch.Elapsed.TotalMilliseconds;
    await open;Stage("layout-"+pass);var dialog=(Form)Field(owner,"importDialog");dialog.ClientSize=new Size(1140,650);Layout(dialog);
    Stage("handle-"+pass);double scanAndText=watch.Elapsed.TotalMilliseconds;var list=(ListView)Field(dialog,"list");IntPtr handle=list.Handle;
    Need(list.Items.Count==61,"61 text rows presented");Stage("images-"+pass);await Images(dialog);double imagesDone=watch.Elapsed.TotalMilliseconds;
    Stage("queries-"+pass);var search=(TextBox)Field(dialog,"candidateSearch");var queryTimes=new List<double>();
    foreach(string query in new[]{"本","本地","7500","75001","不存在","","壁纸","750","75000",""}){
     var queryClock=Stopwatch.StartNew();search.Text=query;queryTimes.Add(queryClock.Elapsed.TotalMilliseconds);await Task.Delay(5);
    }
    Need(list.Items.Count==61,"clear query restores all models");
    Stage("selections-"+pass);var selectionTimes=new List<double>();
    for(int index=0;index<20;index++){
     var selectionClock=Stopwatch.StartNew();if(list.SelectedItems.Count>0)list.SelectedItems[0].Selected=false;list.Items[(index*7)%list.Items.Count].Selected=true;
     selectionTimes.Add(selectionClock.Elapsed.TotalMilliseconds);await Task.Delay(5);
    }
    await Images(dialog);Stage("scroll-"+pass);var scrollClock=Stopwatch.StartNew();foreach(int index in new[]{20,40,10,50,0})list.TopItem=list.Items[index];double scroll=scrollClock.Elapsed.TotalMilliseconds;
    var performance=(ImportPerformance)Field(dialog,"performance");var snapshot=performance.Snapshot();double gap=heartbeat.Longest;int ticks=heartbeat.Ticks;
    Common.SaveNew(ReportPath+".partial-"+pass,new{entrySynchronousMilliseconds=entryReturn,scanAndTextMilliseconds=scanAndText,imagesReadyMilliseconds=imagesDone,maxQueryMilliseconds=queryTimes.Max(),queryTotalMilliseconds=queryTimes.Sum(),maxSelectionMilliseconds=selectionTimes.Max(),selectionTotalMilliseconds=selectionTimes.Sum(),scrollStateMilliseconds=scroll,maxMessageLoopGapMilliseconds=gap,performance=snapshot});Stage("close-"+pass);var closeClock=Stopwatch.StartNew();dialog.Close();await Images(dialog);double close=closeClock.Elapsed.TotalMilliseconds;
    measures.Add(new{pass=pass,entrySynchronousMilliseconds=entryReturn,scanAndTextMilliseconds=scanAndText,imagesReadyMilliseconds=imagesDone,maxQueryMilliseconds=queryTimes.Max(),queryTotalMilliseconds=queryTimes.Sum(),maxSelectionMilliseconds=selectionTimes.Max(),selectionTotalMilliseconds=selectionTimes.Sum(),scrollStateMilliseconds=scroll,closeMilliseconds=close,maxMessageLoopGapMilliseconds=gap,messageTicks=ticks,performance=snapshot});
    await Task.Delay(40);heartbeat.Reset();
   }
  }
  return new{measurements=measures};
 }
 static string Solid(string name,Color color){string path=Path.Combine(Common.Root,"data","response-covers",name+".png");using(var bitmap=new Bitmap(80,50))using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(color);bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png);}return path;}
 static async Task<object> Functional(){
  var checks=new List<string>();
  using(var cache=new ImportImageCache(new ImportPerformance())){
   string image=Solid("cache-mutable",Color.Red);
   using(var first=await cache.GetAsync(image,new Size(80,50),CancellationToken.None))Need(first.GetPixel(10,10).R==255,"initial owned image read");
   using(var hit=await cache.GetAsync(image,new Size(80,50),CancellationToken.None))Need(hit.GetPixel(10,10).R==255,"cached clone remains valid");
   Solid("cache-mutable",Color.Blue);
   using(var changed=await cache.GetAsync(image,new Size(80,50),CancellationToken.None))Need(changed.GetPixel(10,10).B==255,"changed image file receives a new identity");
   for(int index=0;index<110;index++)using(var loaded=await cache.GetAsync(Solid("bounded-"+index,Color.Green),new Size(80,50),CancellationToken.None))Need(loaded!=null,"bounded image loads");
   Need(cache.EntryCount<=96&&cache.RetainedBytes<=8*1048576&&cache.MaximumDecoders<=2,"cache bytes, entries and decoders stay bounded");
   string corrupt=Path.Combine(Common.Root,"data","response-covers","corrupt.png");File.WriteAllText(corrupt,"invalid image");
   using(var bad=await cache.GetAsync(corrupt,new Size(80,50),CancellationToken.None))Need(bad==null,"corrupt image safely falls back");
   using(var missing=await cache.GetAsync(corrupt+".missing",new Size(80,50),CancellationToken.None))Need(missing==null,"missing image safely falls back");
   cache.Dispose();await cache.Cleanup;Need(cache.EntryCount==0&&cache.RetainedBytes==0&&cache.PendingCount==0,"disposed cache reclaims all owned images and tasks");
   checks.Add("cache-reuse-file-change-missing-corrupt-bounds-and-disposal");
  }
  using(var owner=new ControlWindow(null,0,null)){
   owner.TestSuppressImportShow=true;typeof(ControlWindow).GetField("sourceState",Flags).SetValue(owner,new SourceRootState{Found=true,Root=LocalImport.DataRoot});
   Task opening=owner.TestOpenImport();var dialog=(Form)Field(owner,"importDialog");var search=(TextBox)Field(dialog,"candidateSearch");
   search.Text="75002";await opening;await Images(dialog);var list=(ListView)Field(dialog,"list");
   Need(list.Items.Count==10&&list.Items.Cast<ListViewItem>().All(row=>((ImportCandidate)row.Tag).Id.Contains("75002")),"query entered before scan completion applies to every late text batch");
   search.Text="";await Images(dialog);list.TopItem=list.Items[30];var top=list.TopItem;var selected=list.SelectedItems[0];var performance=(ImportPerformance)Field(dialog,"performance");long reads=performance.ImageReads,rebuilds=performance.ListRebuilds,updates=performance.RowUpdates;
   for(int index=0;index<12;index++){Call(dialog,"ApplyFilter");Call(dialog,"UpdateSelection");}await Images(dialog);
   Need(performance.ImageReads==reads&&performance.ListRebuilds==rebuilds&&performance.RowUpdates==updates&&Object.ReferenceEquals(top,list.TopItem)&&Object.ReferenceEquals(selected,list.SelectedItems[0]),"unchanged rows and selection preserve scroll with no image reread or list rebuild");
   checks.Add("late-query-stable-selection-scroll-and-no-redundant-work");
   var rows=(List<ListViewItem>)Field(dialog,"candidateRows");var first=(ImportCandidate)rows[0].Tag;var second=(ImportCandidate)rows[1].Tag;
   first.Cover=Solid("late-red",Color.Red);second.Cover=Solid("late-blue",Color.Blue);
   ImportImageCache.TestBeforeDecode=(path,token)=>{if(path.EndsWith("late-red.png")){token.WaitHandle.WaitOne(80);token.ThrowIfCancellationRequested();}};
   try{
    foreach(ListViewItem row in list.SelectedItems.Cast<ListViewItem>().ToArray())row.Selected=false;rows[0].Selected=true;await Task.Delay(2);rows[0].Selected=false;rows[1].Selected=true;await Images(dialog);
    var picture=(PictureBox)Field(dialog,"cover");Need(picture.Image!=null&&((Bitmap)picture.Image).GetPixel(10,10).B==255&&((Bitmap)picture.Image).GetPixel(10,10).R==0,"late prior image cannot replace the new selected resource");
   }finally{ImportImageCache.TestBeforeDecode=null;}
   checks.Add("rapid-selection-late-image-identity-safe");
   search.Text="不存在";Need(list.Items.Count==0&&((PictureBox)Field(dialog,"cover")).Image==null,"zero-result clears selected detail");
   var names=new Dictionary<string,NameEvidence>{{first.Id,new NameEvidence{Id=first.Id,Name="迟到的官方名称",Kind="official-local-wallpaper-response",SourceSha256=new string('a',64),IndexSha256=new string('b',64),SourceBytes=64,ResponsePath="/pcwallpapercontent/wallpaper/jingling/detailDynamic"}}};
   search.Text="迟到";Call(dialog,"RefreshOfficialNames",names);await Images(dialog);Need(list.Items.Count==1&&((ImportCandidate)list.Items[0].Tag).Id==first.Id,"background names reapply filter to the correct stable model");
   checks.Add("zero-result-and-late-name-update");
   search.Text="";ImportImageCache.TestBeforeDecode=(path,token)=>{token.WaitHandle.WaitOne(150);token.ThrowIfCancellationRequested();};
   try{
    var refresh=(Task)Call(dialog,"Scan");await refresh;dialog.Close();await Images(dialog);Need(dialog.IsDisposed,"close during pending images completes");
    var cache=(ImportImageCache)Field(dialog,"imageCache");Need(cache.EntryCount==0&&cache.PendingCount==0&&cache.RetainedBytes==0,"close cancels pending image work and releases cache");
   }finally{ImportImageCache.TestBeforeDecode=null;}
   checks.Add("close-during-images-no-late-ui-assignment-and-resources-reclaimed");
   var cancelled=owner.TestOpenImport();dialog=(Form)Field(owner,"importDialog");typeof(Control).GetMethod("OnClick",Flags).Invoke(Field(dialog,"cancel"),new object[]{EventArgs.Empty});await cancelled;
   Need(((ImportProgressState)Field(dialog,"progressState")).Phase==ImportProgressPhase.Cancelled,"cancel still reaches the actual scan processor");dialog.Close();await Images(dialog);
   await owner.TestOpenImport();dialog=(Form)Field(owner,"importDialog");await Images(dialog);Need(((ListView)Field(dialog,"list")).Items.Count==61,"reopen recovers the complete candidate model");dialog.Close();await Images(dialog);
   checks.Add("cancel-close-and-normal-reopen");
  }
  await ImportAndLayout(checks);
  return new{checks=checks};
 }
 static async Task ImportAndLayout(List<string> checks){
  using(var owner=new ControlWindow(null,0,null)){
   owner.TestSuppressImportShow=true;
   typeof(ControlWindow).GetField("sourceState",Flags).SetValue(owner,new SourceRootState{Found=true,Root=LocalImport.DataRoot});
   await owner.TestOpenImport();
   var dialog=(Form)Field(owner,"importDialog");
   var list=(ListView)Field(dialog,"list");
   await Images(dialog);
   var candidate=(ImportCandidate)list.SelectedItems[0].Tag;
   string selectedKey=ImportCandidateSearch.Key(candidate);
   await (Task)Call(dialog,"AnalyzeSelected");
   candidate=(ImportCandidate)list.SelectedItems[0].Tag;
   Need(CandidateChecks.Passed(candidate)&&ImportCandidateSearch.Key(candidate)==selectedKey,"targeted real recheck preserves stable selected identity");
   await Images(dialog);
   checks.Add("partial-target-recheck-and-late-image-refresh");
   await (Task)Call(dialog,"ImportSelected");
   var state=(ImportProgressState)Field(dialog,"progressState");
   Need(state.Phase==ImportProgressPhase.Completed&&(state.Text.Contains("已入库")||state.Text.Contains("相同内容")),"actual copy and registration complete with async images");
   var library=(WallpaperLibrary)Field(owner,"library");
   Need(library.Doc.Items.Any(item=>item.RegisteredSha256==candidate.Check.Sha256),"registered import matches selected source identity");
   string failure=Path.Combine(Common.Root,"tests","fail-catalog-save");
   File.WriteAllText(failure,"TEST_BUILD owned save failure");
   try{
    await (Task)Call(dialog,"ImportSelected");
    Need(state.Phase==ImportProgressPhase.Failed,"save failure remains failure with images loaded");
   }finally{File.Delete(failure);}
   string before=Common.Hash(library.FilePath);
   var copying=(Task)Call(dialog,"ImportSelected");
   typeof(Control).GetMethod("OnClick",Flags).Invoke(Field(dialog,"cancel"),new object[]{EventArgs.Empty});
   await copying;
   Need(state.Phase==ImportProgressPhase.Cancelled&&Common.Hash(library.FilePath)==before,"actual cancelled copy preserves saved catalog");
   checks.Add("actual-copy-register-save-failure-cancel-and-identity");
   foreach(float scale in new[]{1f,1.25f,1.5f}){
    dialog.ClientSize=new Size((int)(1140*scale),(int)(650*scale));
    var label=(Label)Field(dialog,"status");
    using(var font=new Font(dialog.Font.FontFamily,9.5f*scale)){
     label.Font=font;state.Checking(1,2,61,new string('长',90));Call(dialog,"PresentProgress");Layout(dialog);
     var bar=(MaterialProgress)Field(dialog,"progress");
     var root=(TableLayoutPanel)Field(dialog,"importLayout");
     Need(label.Height>=HomeTypography.LineHeight(font)+label.Padding.Vertical,"measured status font fits at "+scale);
     Need(label.Bottom<=bar.Top&&bar.Bottom<=root.GetControlFromPosition(0,4).Top,"text, track and buttons do not overlap at "+scale);
     Need(list.Columns.Count==5&&list.Columns.Cast<ColumnHeader>().All(column=>column.Width>0),"all five list columns remain available");
     bar.TestReduceMotion=true;bar.Present(state);Need(!bar.AnimationRunning,"reduced motion stops timer");
    }
   }
   checks.Add("100-125-150-measured-font-layout-five-columns-reduced-motion");
   dialog.Close();await Images(dialog);
   Need(dialog.IsDisposed&&!((MaterialProgress)Field(dialog,"progress")).AnimationRunning,"close releases progress animation");
   Need(((ImportImageCache)Field(dialog,"imageCache")).PendingCount==0,"close leaves no image tasks");
  }
 }
 [STAThread]static int Main(string[] args){
  if(args[0]=="--local-import")return LocalImport.Worker(args[1]);
  ReportPath=args[0];Control.CheckForIllegalCrossThreadCalls=true;Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
  using(var deadline=new System.Threading.Timer(state=>{File.WriteAllText(ReportPath+".timeout",CurrentStage);Environment.Exit(124);},null,45000,System.Threading.Timeout.Infinite)){
  Application.EnableVisualStyles();Prepare();if(args[1]!="functional")ImagePreset(args[1]=="compact");var host=new Control();host.CreateControl();IntPtr handle=host.Handle;
  int exit=1;var context=new ApplicationContext();Application.ThreadException+=(sender,error)=>{if(!File.Exists(ReportPath))Common.SaveNew(ReportPath,new{passed=false,stage=CurrentStage,error=error.Exception.ToString()});context.ExitThread();};host.BeginInvoke(new Action(async()=>{
   try{var result=args[1]=="functional"?await Functional():await Exercise();Common.SaveNew(args[0],new{passed=true,result=result,fixtureCount=61,imagePreset=args[1]=="compact"?"360x203":"1600x900",scope="normal async import entry with TEST_BUILD Show suppression; real Application.Run message loop and owned control state changes; no visible window, input automation, engine or DoEvents"});exit=0;}
   catch(Exception error){Common.SaveNew(args[0],new{passed=false,error=error.ToString()});}
   finally{context.ExitThread();}
  }));
  try{Application.Run(context);}catch(Exception error){if(!File.Exists(ReportPath))Common.SaveNew(ReportPath,new{passed=false,stage=CurrentStage,error=error.ToString()});}
  host.Dispose();context.Dispose();return exit;}
 }
}
#endif
