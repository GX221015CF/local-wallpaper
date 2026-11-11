#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class ImportProgressRegression {
 const BindingFlags Flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static readonly List<object> Results=new List<object>();
 static readonly List<object> CacheMetrics=new List<object>();
 static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 static object Field(object owner,string name){return owner.GetType().GetField(name,Flags).GetValue(owner);}
 static object Call(object owner,string name,params object[] values){return owner.GetType().GetMethods(Flags).Single(method=>method.Name==name&&method.GetParameters().Length==values.Length).Invoke(owner,values);}
 static void Check(string name,Action action){try{action();Results.Add(new{name=name,passed=true});}catch(Exception error){Results.Add(new{name=name,passed=false,error=error.ToString()});}}
 static void Wait(Task task){var clock=System.Diagnostics.Stopwatch.StartNew();while(!task.IsCompleted&&clock.Elapsed.TotalSeconds<20){Application.DoEvents();System.Threading.Thread.Sleep(10);}Need(task.IsCompleted,"owned task exits within 20 seconds");task.GetAwaiter().GetResult();}
 static Form Dialog(ControlWindow owner){return (Form)Activator.CreateInstance(typeof(ControlWindow).GetNestedType("ImportDialog",Flags),Flags,null,new object[]{owner},null);}
 static ImportCandidate Resource(string label,string id,byte seed,bool noCover=false){return (ImportCandidate)typeof(ImportExperienceRegression).GetMethod("Resource",Flags).Invoke(null,new object[]{label,id,seed,noCover});}
 static string Cache(ImportCandidate candidate){return (string)typeof(CandidateChecks).GetMethod("Cache",Flags).Invoke(null,new object[]{candidate});}
 static void Save(ImportCandidate candidate,CandidateCheckResult result){typeof(CandidateChecks).GetMethod("SaveResult",Flags).Invoke(null,new object[]{candidate,result,new Action(()=>{})});}
 static void Click(Control control){typeof(Control).GetMethod("OnClick",Flags).Invoke(control,new object[]{EventArgs.Empty});}
 static ImportProgressState State(Form dialog){return (ImportProgressState)Field(dialog,"progressState");}
 static MaterialProgress Bar(Form dialog){return (MaterialProgress)Field(dialog,"progress");}
 static List<Dictionary<string,object>> Events(ControlWindow owner){return Common.ReadShared(Path.Combine(owner.Session,"controller.jsonl")).Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(line=>Common.Json.Deserialize<Dictionary<string,object>>(line)).Where(row=>Common.S(row,"phase")=="import-progress").Select(row=>Common.Obj(row["data"])).ToList();}
 static string Relative(string path){return path.Substring(Common.Root.TrimEnd('\\').Length+1);}

 static void SavePersistentFixtures(){
  var candidates=new List<ImportCandidate>();
  string[] cases={"valid","source-change","record-change","rules-change","corrupt","cover-change","no-cover","transient","missing-cache"};
  for(int index=0;index<cases.Length;index++){
   var candidate=Resource("persist-"+cases[index],"74444"+index,(byte)(40+index),cases[index]=="no-cover");
   CoverCache.WorkFolder=Path.Combine(Common.WorkRoot,"persist-cover-"+index);Directory.CreateDirectory(CoverCache.WorkFolder);
   CandidateChecks.Run(candidate,()=>{},null,true);Need(CandidateChecks.Passed(candidate),"actual check saves "+cases[index]);
   if(cases[index]!="no-cover"&&candidate.Cover==null){
    string image=Path.Combine(Common.Root,"data","owned-persistent-images",index+".jpg");Directory.CreateDirectory(Path.GetDirectoryName(image));
    using(var bitmap=new Bitmap(48,30))using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.FromArgb(40+index,120,210));bitmap.Save(image,System.Drawing.Imaging.ImageFormat.Jpeg);}
    candidate.Cover=image;candidate.CoverReason="自有小型图片夹具";
    typeof(CandidateChecks).GetMethod("FinishCover",Flags).Invoke(null,new object[]{candidate,candidate.Check});Save(candidate,candidate.Check);
   }
   if(cases[index]=="valid"){candidate.Check.CheckedAt=DateTimeOffset.UtcNow.AddDays(-2).ToString("o");Save(candidate,candidate.Check);}
   CacheMetrics.Add(new{scenario=cases[index],phase="process-A-save",sourceHashBytes=candidate.Check.HashedBytes,coverReads=candidate.Check.CoverReads,coverDecodedBytes=candidate.Check.CoverDecodedBytes,proofOnDisk=File.Exists(Cache(candidate))});
   candidates.Add(candidate);
  }
  Common.SaveNew(Path.Combine(Common.Root,"persistent-fixtures.json"),new{cases=cases,candidates=candidates,processId=System.Diagnostics.Process.GetCurrentProcess().Id});
 }
 static void ReopenPersistentFixtures(){
  var saved=Common.Read(Path.Combine(Common.Root,"persistent-fixtures.json"));
  var cases=Common.Json.Deserialize<string[]>(Common.Json.Serialize(saved["cases"]));
  var candidates=Common.Json.Deserialize<List<ImportCandidate>>(Common.Json.Serialize(saved["candidates"]));
  string rules=PackageAnalysis.RulesPath;byte[] originalRules=File.ReadAllBytes(rules);
  for(int index=0;index<candidates.Count;index++){
   string scenario=cases[index];var candidate=LocalImport.ReadRecord(candidates[index].Record);
   if(scenario=="source-change")File.SetLastWriteTimeUtc(candidate.Source,DateTime.UtcNow);
   if(scenario=="record-change"){
    byte[] record=File.ReadAllBytes(candidate.Record);byte[] trailing={(byte)(' '^0xe7)};File.WriteAllBytes(candidate.Record,record.Concat(trailing).ToArray());candidate=LocalImport.ReadRecord(candidate.Record);
   }
   if(scenario=="rules-change")File.AppendAllText(rules," ");
   if(scenario=="corrupt")File.WriteAllText(Cache(candidate),"invalid owned cache fixture");
   if(scenario=="missing-cache")File.Delete(Cache(candidate));
   if(scenario=="cover-change")File.AppendAllText(Path.Combine(Common.Root,candidates[index].Check.CoverRelative),"changed");
   if(scenario=="transient"){
    var failure=candidates[index].Check;failure.State="failed";failure.FailureKind="io";failure.CheckedAt=DateTimeOffset.UtcNow.AddSeconds(-61).ToString("o");failure.AutoCheckAttempts=1;Save(candidate,failure);
   }
   long sourceBefore=CandidateChecks.SourceHashedBytes;
   bool hit=CandidateChecks.Load(candidate,()=>{});
   string expected=scenario=="source-change"?"source-identity-changed":scenario=="record-change"?"record-changed":scenario=="rules-change"?"rules-changed":scenario=="corrupt"?"cache-corrupt":scenario=="transient"?"transient-check-retry-due":scenario=="missing-cache"?"cache-missing":null;
   if(expected!=null)Need(!hit&&candidate.CacheDisposition==expected,"correct persistent invalidation "+scenario);
   else Need(hit&&candidate.Check.HashedBytes==0,"fresh process reuses saved proof "+scenario);
   Need(CandidateChecks.SourceHashedBytes==sourceBefore,"Load performs no complete resource hashing for "+scenario);
   if(scenario=="cover-change")Need(candidate.Check.CoverRefreshDue&&candidate.Check.CoverRelative==null,"cover invalidation preserves structural proof and schedules cover only");
   if(scenario=="no-cover")Need(!candidate.Check.CoverRefreshDue,"stable absence retains reusable proof");
   CacheMetrics.Add(new{scenario=scenario,phase="process-B-reopen",hit=hit,cacheReason=candidate.CacheDisposition,sourceHashBytes=CandidateChecks.SourceHashedBytes-sourceBefore,coverValidationBytes=hit?candidate.Check.CoverValidationBytes:0,coverRefreshDue=hit&&candidate.Check.CoverRefreshDue});
   if(scenario=="rules-change")File.WriteAllBytes(rules,originalRules);
   if(scenario=="source-change"){
    candidate=LocalImport.ReadRecord(candidate.Record);CoverCache.WorkFolder=Path.Combine(Common.WorkRoot,"changed-recheck");Directory.CreateDirectory(CoverCache.WorkFolder);
    CandidateChecks.Run(candidate,()=>{},null);Need(CandidateChecks.Passed(candidate)&&candidate.Check.HashedBytes==candidate.Bytes,"changed source receives complete targeted recheck");
    CacheMetrics.Add(new{scenario=scenario,phase="targeted-recheck",sourceHashBytes=candidate.Check.HashedBytes,resourceBytes=candidate.Bytes});
   }
  }
 }
 static void ExerciseProgress(string report){
  Check("material-progress-model-and-offscreen-motion",()=>{
   var state=new ImportProgressState();using(var bar=new MaterialProgress()){
    bar.Size=new Size(420,22);bar.TestReduceMotion=true;state.Reading();bar.Present(state);Need(state.Indeterminate&&bar.TrackVisible&&!bar.AnimationRunning,"unknown total stays indeterminate with reduced-motion static segment");
    state.Checking(1,3,61,"检查目录");bar.Present(state);Need(!state.Indeterminate&&state.Fraction==1d/3&&state.Text.Contains("已复用 61"),"known count uses actual denominator");
    state.Checking(1,4,61,"追加待稳定目标");bar.Present(state);Need(state.Fraction==.25,"dynamic denominator updates without fake completion");
    state.Copying("复制",50,200);bar.Present(state);Need(state.Fraction==.25,"byte fraction comes from real quantities");
    state.Registering();bar.Present(state);Need(state.Indeterminate&&state.Phase==ImportProgressPhase.Registering,"registration has separate unknown work");
    state.Cancelling();bar.Present(state);Need(state.Active&&state.Indeterminate,"cancel waits for task completion");
    foreach(var end in new[]{ImportProgressPhase.Completed,ImportProgressPhase.Cancelled,ImportProgressPhase.Failed}){
     state.Finish(end,"任务结果");bar.Present(state);Need(!bar.TrackVisible&&!bar.AnimationRunning&&state.Completed==0,"terminal state clears track and animation");
     state.Reading();bar.Present(state);
    }
    foreach(float scale in new[]{1f,1.25f,1.5f})using(var bitmap=new Bitmap((int)(420*scale),(int)(22*scale)))using(var graphics=Graphics.FromImage(bitmap)){
     bitmap.SetResolution(96*scale,96*scale);bar.Size=bitmap.Size;state.Copying("复制",1,2);bar.Present(state);
     typeof(MaterialProgress).GetMethod("OnPaint",Flags).Invoke(bar,new object[]{new PaintEventArgs(graphics,new Rectangle(Point.Empty,bitmap.Size))});
     Color filled=bitmap.GetPixel(20,bitmap.Height/2);Need(filled.ToArgb()==Theme.Blue.ToArgb(),"actual renderer uses Material blue at "+scale);
     bitmap.Save(Path.Combine(Path.GetDirectoryName(report),"owned-progress-"+scale+".png"));
    }
   }
  });
  Check("actual-scan-partial-all-cache-and-reopen-progress",()=>{
   var first=Resource("queue-first","755551",70);var second=Resource("queue-second","755552",71);
   CoverCache.WorkFolder=Path.Combine(Common.WorkRoot,"queue-first-cover");Directory.CreateDirectory(CoverCache.WorkFolder);CandidateChecks.Run(first,()=>{},null,true);
   using(var owner=new ControlWindow(null,0,null)){
    using(var dialog=Dialog(owner)){
     Wait((Task)Call(dialog,"Scan"));Need(State(dialog).Phase==ImportProgressPhase.Completed&&!Bar(dialog).TrackVisible,"actual scan queue ends with hidden track");
     Need(State(dialog).Text.Contains("已复用 1 项")&&State(dialog).Text.Contains("处理 1 次"),"actual partial scan reports reuse and processing independently");
     var events=Events(owner);Need(Common.S(events[0],"phase")=="Reading"&&events.Any(row=>Common.S(row,"phase")=="Checking"&&Convert.ToInt64(row["total"])==1),"formal scan and queue enter correct states");
    }
    using(var dialog=Dialog(owner)){
     Wait((Task)Call(dialog,"Scan"));Need(State(dialog).Text.Contains("已复用 2 项")&&State(dialog).Text.Contains("处理 0 次")&&!Bar(dialog).TrackVisible,"second window reuses both candidates and hides finished track");
    }
   }
  });
  Check("actual-dynamic-queue-denominator-and-failure",()=>{
   var candidate=Resource("queue-dynamic","755553",72);
   using(var owner=new ControlWindow(null,0,null))using(var dialog=Dialog(owner)){
    Call(dialog,"TestAddCandidate",candidate);int attempts=0;
    dialog.GetType().GetField("TestQueueResult",Flags).SetValue(dialog,new Action<ImportCandidate>(value=>{if(++attempts==1){value.Check.State="waiting";value.Check.Reason="TEST_BUILD owned once-only waiting result";}}));
    Wait((Task)Call(dialog,"TestRunQueue"));var events=Events(owner);
    Need(attempts==2&&events.Any(row=>Common.S(row,"phase")=="Checking"&&Convert.ToInt64(row["completed"])==1&&Convert.ToInt64(row["total"])==2),"actual retry scheduler reports expanded denominator before second attempt");
    Need(State(dialog).Text.Contains("处理 2 次")&&!Bar(dialog).TrackVisible,"dynamic queue final result counts both attempts");
    candidate.Check=null;
    dialog.GetType().GetField("TestQueueResult",Flags).SetValue(dialog,new Action<ImportCandidate>(value=>{value.Check.State="failed";value.Check.Reason="TEST_BUILD owned result failure";}));
    ((ImportCandidate)((List<ListViewItem>)Field(dialog,"candidateRows"))[0].Tag).Check=null;
    Wait((Task)Call(dialog,"TestRunQueue"));Need(State(dialog).Phase==ImportProgressPhase.Failed&&State(dialog).Text.Contains("失败 1 项")&&!Bar(dialog).TrackVisible,"failed queue terminates with accurate failure count");
   }
  });
  Check("actual-scan-cancel-restart-and-close",()=>{
   using(var owner=new ControlWindow(null,0,null))using(var dialog=Dialog(owner)){
    var scan=(Task)Call(dialog,"Scan");Click((Control)Field(dialog,"cancel"));Wait(scan);
    Need(State(dialog).Phase==ImportProgressPhase.Cancelled&&!Bar(dialog).TrackVisible,"actual cancel waits for child and clears track");
    Wait((Task)Call(dialog,"Scan"));Need(State(dialog).Phase==ImportProgressPhase.Completed,"cancelled window can begin a new completed scan");
    dialog.Close();Need(dialog.IsDisposed&&!Bar(dialog).AnimationRunning,"closing releases progress timer");
   }
  });
  Check("actual-close-during-scan-finishes-owned-task",()=>{
   using(var owner=new ControlWindow(null,0,null))using(var dialog=Dialog(owner)){
    var scan=(Task)Call(dialog,"Scan");dialog.Close();Wait(scan);
    var clock=System.Diagnostics.Stopwatch.StartNew();while(!dialog.IsDisposed&&clock.Elapsed.TotalSeconds<3){Application.DoEvents();System.Threading.Thread.Sleep(10);}
    Need(dialog.IsDisposed,"close waits for owned preparation then disposes dialog");
    var operation=(ImportOperation)Field(owner,"importOperation");Need(operation==null||!operation.Busy,"close leaves no busy import process");
   }
  });
  Check("actual-copy-byte-finish-registration-and-save-failure",()=>{
   var candidate=Resource("copy-progress","755554",73);CoverCache.WorkFolder=Path.Combine(Common.WorkRoot,"copy-cover");Directory.CreateDirectory(CoverCache.WorkFolder);CandidateChecks.Run(candidate,()=>{},null,true);
   using(var owner=new ControlWindow(null,0,null))using(var dialog=Dialog(owner)){
    var list=(ListView)Field(dialog,"list");IntPtr handle=list.Handle;Call(dialog,"TestAddCandidate",candidate);
    Wait((Task)Call(dialog,"ImportSelected"));var events=Events(owner);
    int copied=events.FindIndex(row=>Common.S(row,"phase")=="Copying"&&Convert.ToInt64(row["total"])==candidate.Bytes&&Convert.ToInt64(row["completed"])==candidate.Bytes);
    int registering=events.FindIndex(row=>Common.S(row,"phase")=="Registering");int completed=events.FindLastIndex(row=>Common.S(row,"phase")=="Completed");
    Need(copied>=0&&registering>copied&&completed>registering,"actual copied bytes finish before formal registration success");
    Need(State(dialog).Phase==ImportProgressPhase.Completed&&State(dialog).Text.Contains("已入库")&&!Bar(dialog).TrackVisible,"only saved registration ends import successfully");
    string failure=Path.Combine(Common.Root,"tests","fail-catalog-save");File.WriteAllText(failure,"TEST_BUILD owned save failure");
    try{Wait((Task)Call(dialog,"ImportSelected"));Need(State(dialog).Phase==ImportProgressPhase.Failed&&!Bar(dialog).TrackVisible,"formal save failure cannot appear completed");}finally{File.Delete(failure);}
   }
  });
  Check("actual-copy-cancel-clears-track-and-preserves-registration",()=>{
   var candidate=Resource("copy-cancel","755555",74);CoverCache.WorkFolder=Path.Combine(Common.WorkRoot,"copy-cancel-cover");Directory.CreateDirectory(CoverCache.WorkFolder);CandidateChecks.Run(candidate,()=>{},null,true);
   using(var owner=new ControlWindow(null,0,null))using(var dialog=Dialog(owner)){
    var list=(ListView)Field(dialog,"list");IntPtr handle=list.Handle;Call(dialog,"TestAddCandidate",candidate);
    var library=(WallpaperLibrary)Field(owner,"library");string before=Common.Hash(library.FilePath);
    var copying=(Task)Call(dialog,"ImportSelected");Click((Control)Field(dialog,"cancel"));Wait(copying);
    Need(State(dialog).Phase==ImportProgressPhase.Cancelled&&!Bar(dialog).TrackVisible,"cancelled copy clears track after actual child ends");
    Need(Common.Hash(library.FilePath)==before,"cancelled copy preserves prior registration");
   }
  });
  Check("progress-text-track-button-spacing-and-scaled-fonts",()=>{
   using(var owner=new ControlWindow(null,0,null))using(var dialog=Dialog(owner)){
    foreach(float scale in new[]{1f,1.25f,1.5f}){
     dialog.ClientSize=new Size((int)(1140*scale),(int)(650*scale));var label=(Label)Field(dialog,"status");
     using(var font=new Font(dialog.Font.FontFamily,9.5f*scale)){
      label.Font=font;State(dialog).Checking(4,12,61,new string('长',90));Call(dialog,"PresentProgress");
      var root=(TableLayoutPanel)Field(dialog,"importLayout");root.PerformLayout();
      Need(label.Height>=HomeTypography.LineHeight(font)+label.Padding.Vertical,"actual measured status line fits "+scale);
      Need(label.Bottom<=Bar(dialog).Top&&Bar(dialog).Bottom<=root.GetControlFromPosition(0,4).Top,"status/track/buttons do not overlap "+scale);
      Need(((Button)Field(dialog,"cancel")).TabStop,"cancel remains keyboard accessible");
      State(dialog).Finish(ImportProgressPhase.Completed,"结果保留");Call(dialog,"PresentProgress");Need(root.RowStyles[3].Height==0,"terminal layout collapses track row");
     }
    }
   }
  });
 }
 [STAThread]static int Main(string[] args){
  if(args[0]=="--local-import")return LocalImport.Worker(args[1]);
  string mode=args[1];Application.EnableVisualStyles();Directory.CreateDirectory(Path.Combine(Common.Root,"tests"));
  string missing=Path.Combine(Common.Root,"tests","no-official-cache");Directory.CreateDirectory(missing);File.WriteAllText(Path.Combine(Common.Root,"tests","name-cache-root.txt"),missing);
  if(mode=="save")Check("process-A-actual-save",SavePersistentFixtures);
  else if(mode=="reopen")Check("process-B-actual-load-and-invalidation",ReopenPersistentFixtures);
  else ExerciseProgress(args[0]);
  bool passed=Results.All(result=>(bool)result.GetType().GetProperty("passed").GetValue(result,null));
  Common.SaveNew(args[0],new{passed=passed,processId=System.Diagnostics.Process.GetCurrentProcess().Id,mode=mode,results=Results,cacheMetrics=CacheMetrics,scope="actual unshown import processors, actual cache save/load across normal process exits, owned small fixtures and offscreen render; no OS restart or engine"});return passed?0:1;
 }
}
#endif
