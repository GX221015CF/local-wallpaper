#if TEST_BUILD
using System;
using System.IO;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

internal static class StabilityRegression {
 static readonly List<object> results=new List<object>();
 static void Run(string name,Action test){try{test();results.Add(new{name=name,passed=true});}catch(Exception error){results.Add(new{name=name,passed=false,error=error.ToString(),hresult=error.HResult,nativeCode=error.HResult&65535});}}
 static PreviewFrame.CapturedFrame Frame(bool valid){
  var bitmap=new Bitmap(96,64);
  using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.Black);if(valid){graphics.FillRectangle(Brushes.Coral,0,0,48,64);graphics.FillRectangle(Brushes.SkyBlue,48,0,48,64);}}
  return new PreviewFrame.CapturedFrame{Image=bitmap,Painted=true,Method="owned-synthetic-frame"};
 }
 static void Need(bool value,string message){if(!value)throw new InvalidDataException(message);}
 static string LastFailureRun;
 static int Child(string[] args){
  try{
   string id=args[2],source=args[3],run=Path.Combine(Common.LogRoot,"run-"+id);Directory.CreateDirectory(run);
   string hash=Common.Hash(source);
   Common.SaveNew(Path.Combine(run,"request.json"),new{runToken=id,expectedSha256=hash,cwp=source,mode="preview",seconds=0,playbackPolicy="continuous"});
   using(var lease=RuntimeInputs.Begin(run,source,hash,CancellationToken.None)){
    if(args[1]=="failed"){
     Exception original=null;
     using(var held=new FileStream(Path.Combine(lease.Directory,"owner.json"),FileMode.Open,FileAccess.Read,FileShare.Read)){
      try{lease.Copy(source,CancellationToken.None);}catch(IOException error){original=error;}
     }
     Need(original!=null&&original.Data.Contains("atomicTarget"),"forced held-reader failure preserves target and native error");
     lease.End(true,true,"private-resource-copy",original.Message);
    }else{lease.Copy(source,CancellationToken.None);lease.End(true,false,"copy-complete","normal-end");}
   }
   return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 2;}
 }
 static string ChildLease(string mode,string source){
  string id=Guid.NewGuid().ToString("N");
  using(var process=Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,"child "+mode+" "+id+" "+Common.Quote(source)){
   UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){
   Need(process.WaitForExit(7000)&&process.ExitCode==0,"owned child ends normally within budget");
  }
  return Path.Combine(Common.LogRoot,"run-"+id);
 }
 static int Main(string[] args){
  if(args.Length>0&&args[0]=="write-json"){
   for(int index=0;index<12;index++)Common.ReplaceJson(args[1],new{writer=Process.GetCurrentProcess().Id,sequence=index});
   return 0;
  }
  if(args.Length>0&&args[0]=="child")return Child(args);
  if(args.Length>1&&args[1]=="reopen"){
   string run=File.ReadAllText(Path.Combine(Common.Root,"retained-failure-run.txt"));
   var request=Common.Read(Path.Combine(run,"request.json"));
   string directory=Path.Combine(RuntimeInputs.Root,Common.S(request,"runToken"));var record=Common.Read(Path.Combine(directory,"owner.json"));
   Need(Convert.ToBoolean(record["terminal"])&&Convert.ToBoolean(record["failed"]),"independent process reads retained failure truth");
   Need(Common.Hash(Path.Combine(directory,"input.cwp"))==Common.S(record,"sha256"),"independent process checks retained resource identity");
   Common.SaveNew(args[0],new{passed=true,results=new[]{new{name="independent-retained-record-and-resource-readback",passed=true}},engineLoaded=false});return 0;
  }
  Run("transient-reader-must-not-fail-runtime-registration",()=>{
   string folder=Path.Combine(Common.Root,"owned-input-test");Directory.CreateDirectory(folder);
   string path=Path.Combine(folder,"owner.json");Common.SaveNew(path,new{version=1});
   using(var held=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
    var released=Task.Run(()=>{Thread.Sleep(180);held.Dispose();});
    try{RuntimeInputs.Save(folder,new Dictionary<string,object>{{"version",2}});}finally{released.Wait(1000);}
   }
   Need(Convert.ToInt32(Common.Read(path)["version"])==2,"runtime owner update must survive a short read handle");
  });
  Run("temporarily-black-then-valid-frame-must-be-selected",()=>{
   int attempts=0;
   using(var frame=PreviewFrame.AcquireReadyFrame(()=>Frame(++attempts>=3),()=>{},null)){
    Need(PreviewFrame.Informative(frame.Image)&&attempts==3,"must wait for an informative owned frame");
   }
  });
  Run("persistent-black-is-bounded-and-not-saved",()=>{
   int attempts=0;bool rejected=false;var clock=Stopwatch.StartNew();
   try{using(var frame=PreviewFrame.AcquireReadyFrame(()=>{attempts++;return Frame(false);},()=>{},null)){} }
   catch(InvalidDataException){rejected=true;}
   Need(rejected&&attempts==6&&clock.ElapsedMilliseconds<1800,"black frames cannot become false success or infinite retry");
  });
  Run("flat-or-failed-capture-is-not-accepted",()=>{
   using(var plain=new Bitmap(64,64)){using(var g=Graphics.FromImage(plain))g.Clear(Color.White);Need(!PreviewFrame.Informative(plain),"unchanged variance threshold rejects flat white");}
   bool rejected=false;try{using(var frame=PreviewFrame.AcquireReadyFrame(()=>{var f=Frame(true);f.Painted=false;return f;},()=>{},null)){} }catch(InvalidDataException){rejected=true;}
   Need(rejected,"API failure must not accept informative leftover pixels");
  });
  Run("window-resize-retries-same-target-and-invalid-target-stops",()=>{
   int attempts=0;using(var frame=PreviewFrame.AcquireReadyFrame(()=>{if(++attempts==1)throw new PreviewFrame.FrameChangedException("same-window-resized");return Frame(true);},()=>{},null))Need(attempts==2,"same valid window can stabilize");
   attempts=0;bool rejected=false;try{using(var frame=PreviewFrame.AcquireReadyFrame(()=>{attempts++;throw new InvalidDataException("target-identity-changed");},()=>{},null)){} }catch(InvalidDataException){rejected=true;}
   Need(rejected&&attempts==1,"identity failure cannot be retried against another window");
  });
  Run("capture-cancellation-stops-readiness-wait",()=>{
   using(var cancellation=new CancellationTokenSource()){
    int attempts=0;bool cancelled=false;
    try{using(var frame=PreviewFrame.AcquireReadyFrame(()=>{attempts++;cancellation.Cancel();return Frame(false);},()=>cancellation.Token.ThrowIfCancellationRequested(),null)){} }catch(OperationCanceledException){cancelled=true;}
    Need(cancelled&&attempts==1,"cancelled capture does not retry or save");
   }
  });
  Run("permanent-reader-preserves-original-and-native-error",()=>{
   string p=Path.Combine(Common.Root,"blocked.json");Common.SaveNew(p,new{original=true});string before=Common.Hash(p);bool rejected=false;var clock=Stopwatch.StartNew();
   using(var held=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.Read)){
    try{Common.ReplaceJson(p,new{original=false});}catch(IOException error){rejected=true;Need((error.HResult&65535)==32&&error.Data.Contains("atomicFirstHResult"),"preserve native sharing error and first HRESULT");}
   }
   Need(rejected&&clock.ElapsedMilliseconds<900&&Common.Hash(p)==before,"bounded replace cannot delete original registration");
  });
  Run("shared-delete-readers-and-concurrent-writers-remain-atomic",()=>{
   string p=Path.Combine(Common.Root,"concurrent.json");Common.SaveNew(p,new{writer=-1});
   using(var held=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))Common.ReplaceJson(p,new{writer=0});
   var tasks=Enumerable.Range(1,12).Select(index=>Task.Run(()=>Common.ReplaceJson(p,new{writer=index,payload=new string('x',1000)}))).ToArray();
   Need(Task.WaitAll(tasks,3000),"all independent temp writes finish");
   Need(Convert.ToInt32(Common.Read(p)["writer"])>=1,"target remains one complete JSON document");
   Need(!Directory.GetFiles(Common.Root,"j-*.tmp").Any(),"no transaction temporary leaked");
   var children=new List<Process>();
   using(var stopRead=new CancellationTokenSource()){
    var reader=Task.Run(()=>{while(!stopRead.IsCancellationRequested){var document=Common.Read(p);Need(document.ContainsKey("writer"),"reader sees only complete snapshots");Thread.Sleep(2);}});
    try{
     for(int child=0;child<3;child++)children.Add(Process.Start(new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath,"write-json "+Common.Quote(p)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}));
     foreach(var process in children)Need(process.WaitForExit(5000)&&process.ExitCode==0,"cross-process writer finishes normally");
    }finally{stopRead.Cancel();Need(reader.Wait(2000),"parallel reader ends normally");foreach(var process in children)process.Dispose();}
   }
   Need(Common.Read(p).ContainsKey("sequence"),"cross-process serialized updates stay readable");
  });
  Run("cancelled-registration-keeps-old-state",()=>{
   string dir=Path.Combine(Common.Root,"cancelled-owner");Directory.CreateDirectory(dir);string p=Path.Combine(dir,"owner.json");Common.SaveNew(p,new{old=true});string before=Common.Hash(p);bool cancelled=false;
   using(var held=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.Read))using(var token=new CancellationTokenSource(40)){
    try{RuntimeInputs.Save(dir,new Dictionary<string,object>{{"old",false}},token.Token);}catch(OperationCanceledException){cancelled=true;}
   }
   Need(cancelled&&before==Common.Hash(p),"cancel before commit preserves previous owner bytes");
  });
  Run("unable-to-remove-replaced-error-is-bounded-and-recorded",()=>{
   string p=Path.Combine(Common.Root,"native-1175.json");Common.SaveNew(p,new{old=true});
   Common.TestBeforeAtomicCommit=(target,attempt)=>{if(target==p&&attempt==1)throw new IOException("fixture-ERROR_UNABLE_TO_REMOVE_REPLACED",unchecked((int)0x80070497));};
   try{
    var report=Common.ReplaceJsonChecked(p,new{old=false},()=>{},Common.Root);
    Need(report.Attempts==2&&(report.FirstHResult&65535)==1175&&!Convert.ToBoolean(Common.Read(p)["old"]),"known OS replacement condition retries once and retains first code");
   }finally{Common.TestBeforeAtomicCommit=null;}
  });
  Run("cover-publish-replace-cancel-and-confirmation-remain-independent",()=>{
   string fixture=Path.Combine(Common.Root,"cover-fixture");Directory.CreateDirectory(fixture);
   string source=(string)typeof(MultiWallpaperRegression).GetMethod("Fixture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{fixture,"cover-resource",(byte)7});
   var library=new WallpaperLibrary();library.Doc.Items.Clear();library.Doc.SelectedId=null;library.Save();bool duplicate;
   var item=library.Add(source,out duplicate);string hash=item.RegisteredSha256;
   var target=new PreviewFrameTarget{Run=Path.Combine(Common.LogRoot,"run-cover"),ResourceSha256=hash,CatalogId=item.Id,Token=Guid.NewGuid().ToString("N"),PlayerPid=1,PlayerCreation=2,Window=3,Render=4};
   Directory.CreateDirectory(target.Run);
   string image=Path.Combine(fixture,"frame.jpg");using(var frame=Frame(true))frame.Image.Save(image,System.Drawing.Imaging.ImageFormat.Jpeg);
   string imageHash=Common.Hash(image);
   var first=PreviewCovers.Publish(image,target,imageHash,96,64,"synthetic-storage-test","TEST_BUILD",false,()=>{});
   Need(PreviewCovers.Resolve(hash)!=null&&item.PreviewConfirmedSha256==null,"cover publication never creates playback confirmation");
   string firstPath=PreviewCovers.ImagePath(first);bool cancelled=false;
   try{PreviewCovers.Publish(image,target,imageHash,96,64,"synthetic-storage-test","TEST_BUILD",true,()=>{throw new OperationCanceledException("preview-closed");});}catch(OperationCanceledException){cancelled=true;}
   Need(cancelled&&PreviewCovers.Resolve(hash).ImageRelative==first.ImageRelative&&File.Exists(firstPath),"close cancellation preserves old cover");
   var replacement=PreviewCovers.Publish(image,target,imageHash,96,64,"synthetic-storage-test","TEST_BUILD",true,()=>{});
   Need(replacement.ImageRelative!=first.ImageRelative&&PreviewCovers.Resolve(hash).ImageRelative==replacement.ImageRelative,"explicit replacement refreshes same hash binding");
   library.ConfirmPreview(item,hash,target.Run,"TEST_BUILD-storage-handler");
   Need(library.CanMountExact(item,hash)&&item.PreviewConfirmedSha256==hash,"same resource confirmation remains a separate gate");
   var request=new Dictionary<string,object>{{"version",1},{"action","capture-cover"},{"token",target.Token},{"run",target.Run},{"resourceSha256",hash},{"playerPid",1},{"playerCreation",2L},{"window",3L},{"render",4L}};
   Need(PreviewControlRequests.Matches(request,target),"preview request belongs to exact current target");request["token"]=Guid.NewGuid().ToString("N");
   Need(!PreviewControlRequests.Matches(request,target),"stale preview request cannot affect new run");
  });
  Run("preview-to-desktop-switch-orders-stop-and-rejects-unconfirmed-cleanup",()=>{
   var coordinator=new PlaybackSwitch();var order=new List<string>();
   string result=coordinator.Run(()=>Task.FromResult(new[]{"same-hash"}),()=>true,data=>false,()=>true,
    ()=>{order.Add("old-preview-ended");return Task.FromResult(true);},data=>order.Add("new-desktop-requested"),CancellationToken.None).GetAwaiter().GetResult();
   Need(result=="launch-requested"&&String.Join(",",order)=="old-preview-ended,new-desktop-requested","new desktop request follows old preview end");
   order.Clear();result=coordinator.Run(()=>Task.FromResult(new[]{"same-hash"}),()=>true,data=>false,()=>true,
    ()=>Task.FromResult(false),data=>order.Add("must-not-launch"),CancellationToken.None).GetAwaiter().GetResult();
   Need(result=="cleanup-unconfirmed"&&order.Count==0&&!coordinator.Busy,"unconfirmed stop cannot become a desktop success");
  });
  Run("actual-input-lifecycle-failure-next-operation-and-repeat-cleanup",()=>{
   string fixture=Path.Combine(Common.Root,"fixture");Directory.CreateDirectory(fixture);
   string source=(string)typeof(MultiWallpaperRegression).GetMethod("Fixture",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{fixture,"resource",(byte)9});
   string hash=Common.Hash(source);LastFailureRun=ChildLease("failed",source);RuntimeInputs.Maintain();
   Need(Common.S(Common.Read(Path.Combine(LastFailureRun,"runtime-input-cleanup.json")),"state")=="retained-failure-budget","original failure retained by actual maintenance policy");
   File.WriteAllText(Path.Combine(Common.Root,"retained-failure-run.txt"),LastFailureRun);
   for(int iteration=0;iteration<3;iteration++){
    string run=ChildLease("normal",source);RuntimeInputs.Maintain();RuntimeInputs.Maintain();
    var request=Common.Read(Path.Combine(run,"request.json"));Need(!Directory.Exists(Path.Combine(RuntimeInputs.Root,Common.S(request,"runToken"))),"ended child copy is actually reclaimed");
    Need(Common.S(Common.Read(Path.Combine(run,"runtime-input-cleanup.json")),"state")=="reclaimed","cleanup has explicit matching result");
   }
   Need(File.Exists(source)&&Common.Hash(source)==hash,"persistent source survives failed and repeated normal runs");
   string previousApp=Common.AppRoot;
   try{
    Common.AppRoot=Path.Combine(previousApp,"next-version");
    string id=Guid.NewGuid().ToString("N"),nextRun=Path.Combine(Common.LogRoot,"run-"+id);Directory.CreateDirectory(nextRun);
    Common.SaveNew(Path.Combine(nextRun,"request.json"),new{runToken=id,expectedSha256=hash,cwp=source,mode="preview",seconds=0,playbackPolicy="continuous"});
    using(var lease=RuntimeInputs.Begin(nextRun,source,hash,CancellationToken.None)){
     lease.Copy(source,CancellationToken.None);lease.End(true,false,"copy-complete","same-store-next-version");
    }
    Need(File.Exists(Path.Combine(LastFailureRun,"request.json")),"next version preserves original failure evidence");
   }finally{Common.AppRoot=previousApp;}
  });
  bool passed=true;foreach(var row in results)if(!(bool)row.GetType().GetProperty("passed").GetValue(row,null))passed=false;
  Common.SaveNew(args[0],new{passed=passed,results=results,evidenceKind="TEST_BUILD actual registry writer with real isolated sharing conflict; actual frame-readiness policy with owned synthetic images; no live window/engine"});
  return passed?0:1;
 }
}
#endif
