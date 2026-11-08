#if TEST_BUILD
using System;using System.IO;using System.Diagnostics;using System.Collections.Generic;using System.Threading;using System.Linq;
internal static class RuntimeRetentionRegression {
 static List<string> checks=new List<string>();static List<object> measurements=new List<object>();
 static void Need(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
 static string Source(long size){string dir=Path.Combine(Common.Root,"persistent-library");Directory.CreateDirectory(dir);string p=Path.Combine(dir,"fixture-"+size+".cwp");if(!File.Exists(p))using(var f=File.Create(p)){f.SetLength(size);}return p;}
 static string RunPath(string id){return Path.Combine(Common.LogRoot,"retention-fixture","run-"+id);}
 static void Request(string id,string hash){string run=RunPath(id);Directory.CreateDirectory(run);Common.SaveNew(Path.Combine(run,"request.json"),new{runToken=id,expectedSha256=hash});File.WriteAllText(Path.Combine(run,"fault.txt"),"original failure detail must survive");}
 static string Owned(string id){return Path.Combine(RuntimeInputs.Root,id);}
 static long Bytes(){if(!Directory.Exists(RuntimeInputs.Root))return 0;return Directory.GetFiles(RuntimeInputs.Root,"input.cwp",SearchOption.AllDirectories).Sum(p=>new FileInfo(p).Length);}
 static Process Start(string mode,string id,long size){var si=new ProcessStartInfo(typeof(RuntimeRetentionRegression).Assembly.Location,"child "+mode+" "+id+" "+size){UseShellExecute=false,CreateNoWindow=true};si.EnvironmentVariables["LOCALWALLPAPER_TEST_ROOT"]=Common.AppRoot;return Process.Start(si);}
 static void End(Process p){Need(p.WaitForExit(15000),"child-bounded-normal-end");int code=p.ExitCode;p.Dispose();Need(code==0,"child-exit-zero");}
 static string ChildRun(string mode,long size){string id=Guid.NewGuid().ToString("N");using(var p=Start(mode,id,size))End(p);return id;}
 static void Child(string mode,string id,long size){
  if(mode.StartsWith("worker-")){
   string fixture=Path.Combine(Common.Root,"worker-fixtures");Directory.CreateDirectory(fixture);
   string valid=(string)typeof(MultiWallpaperRegression).GetMethod("Fixture",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,new object[]{fixture,id,(byte)9});
   string run=RunPath(id);Directory.CreateDirectory(run);Common.SaveNew(Path.Combine(run,"request.json"),new{runToken=id,expectedSha256=Common.Hash(valid),mode="preview",cwp=valid,seconds=0,playbackPolicy="continuous"});
   LeaseWorker.TestEndAfterVerifiedCopy=r=>{if(mode=="worker-failed")throw new InvalidOperationException("ORIGINAL-WORKER-COPY-FAILURE");return "controlled-test-no-engine";};
   int exit=LeaseWorker.Run(run);if(exit!=(mode=="worker-failed"?1:0))throw new Exception("actual worker result differs");return;
  }
  string source=Source(size),hash=Common.Hash(source);Request(id,hash);
  using(var lease=RuntimeInputs.Begin(RunPath(id),source,hash,CancellationToken.None)){
   if(mode=="growth"){using(var f=new FileStream(source,FileMode.Open,FileAccess.Write)){f.SetLength(size+1);}bool rejected=false;try{lease.Copy(source,CancellationToken.None);}catch(InvalidDataException){rejected=true;}if(!rejected||File.Exists(lease.Path))throw new Exception("reservation growth allowed");using(var f=new FileStream(source,FileMode.Open,FileAccess.Write)){f.SetLength(size);}lease.End(true,false,"copy-rejected","source-growth");return;}
   if(mode=="reserve"){File.WriteAllText(Path.Combine(Common.Root,"reserved-id.txt"),id);return;}
   if(mode=="cancel"){
    using(var c=new CancellationTokenSource()){c.Cancel();try{lease.Copy(source,c.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){}}
   }else if(mode=="partial"){
    using(var c=new CancellationTokenSource()){
     PlaybackResourceCheck.TestAfterCopyChunk=t=>c.Cancel();try{lease.Copy(source,c.Token);throw new Exception("midcopy cancellation ignored");}catch(OperationCanceledException){}finally{PlaybackResourceCheck.TestAfterCopyChunk=null;}
    }
   }else lease.Copy(source,CancellationToken.None);
   if(mode=="active"){File.WriteAllText(Path.Combine(Common.Root,"active-ready"),id);var w=Stopwatch.StartNew();while(!File.Exists(Path.Combine(Common.Root,"active-release"))&&w.ElapsedMilliseconds<7000)Thread.Sleep(20);}
   if(mode!="unknown")lease.End(true,mode=="failed","test-copy-complete",mode=="failed"?"ORIGINAL-FAILURE":"normal-stop");
  }
 }
 static int Main(string[] args){
  if(args.Length>0&&args[0]=="child"){try{Child(args[1],args[2],Int64.Parse(args[3]));return 0;}catch(Exception e){Console.Error.WriteLine(e);return 2;}}
  string report=args[1];try{
   if(args[0]=="reopen"){
    string id=File.ReadAllText(Path.Combine(Common.Root,"reopen-id.txt"));Need(Directory.Exists(Owned(id)),"fresh-process-sees-owned-pending");RuntimeInputs.Maintain();Need(!Directory.Exists(Owned(id)),"fresh-process-reclaims-ended-copy");Need(File.Exists(Source(2097152)),"fresh-process-persistent-library-kept");
   }else{
    Need(RuntimeInputs.TotalLimit==536870912&&RuntimeInputs.FailureLimit==134217728&&RuntimeInputs.FailureCount==2,"production-policy-512MiB-total-128MiB-two-failures");
    string legacy=Path.Combine(Common.Root,"logs","legacy-input","old.cwp");Directory.CreateDirectory(Path.GetDirectoryName(legacy));File.WriteAllText(legacy,"legacy-protected");
    string workerNormal=ChildRun("worker-normal",0);RuntimeInputs.Maintain();Need(!Directory.Exists(Owned(workerNormal)),"actual-LeaseWorker-normal-copy-result-finalize-cleanup");
    string workerFail=ChildRun("worker-failed",0);RuntimeInputs.Maintain();Need(Directory.Exists(Owned(workerFail)),"actual-LeaseWorker-failure-copy-retained");Need(File.ReadAllText(Path.Combine(RunPath(workerFail),"events.jsonl")).Contains("ORIGINAL-WORKER-COPY-FAILURE"),"actual-worker-original-failure-kept");var workerResult=Common.Read(Path.Combine(RunPath(workerFail),"result.json"));Need(Convert.ToBoolean(workerResult["workerFailed"])&&Convert.ToInt32(workerResult["exitCode"])==1,"actual-worker-cleanup-does-not-rewrite-exit");var fixtureRecord=Common.Read(Path.Combine(Owned(workerFail),"owner.json"));fixtureRecord["failed"]=false;RuntimeInputs.Save(Owned(workerFail),fixtureRecord);RuntimeInputs.Maintain();
    string id=ChildRun("normal",2097152);Need(Bytes()==2097152,"normal-copy-measured-before-cleanup");RuntimeInputs.Maintain();Need(Bytes()==0&&!Directory.Exists(Owned(id)),"normal-copy-reclaimed-after-worker-exit");Need(Common.S(Common.Read(Path.Combine(RunPath(id),"runtime-input-cleanup.json")),"state")=="reclaimed","cleanup-summary-persisted");RuntimeInputs.Maintain();Need(Bytes()==0,"cleanup-idempotent");
    foreach(string mode in new[]{"cancel","partial"}){id=ChildRun(mode,2097152);RuntimeInputs.Maintain();Need(!Directory.Exists(Owned(id)),mode+"-no-partial-input");}
    string active=Guid.NewGuid().ToString("N");using(var p=Start("active",active,2097152)){
     var w=Stopwatch.StartNew();while(!File.Exists(Path.Combine(Common.Root,"active-ready"))&&w.ElapsedMilliseconds<5000)Thread.Sleep(20);
     Need(File.Exists(Path.Combine(Common.Root,"active-ready")),"active-child-ready");RuntimeInputs.Maintain();Need(File.Exists(Path.Combine(Owned(active),"input.cwp")),"live-worker-protected-even-before-terminal");var activeRecord=Common.Read(Path.Combine(Owned(active),"owner.json"));activeRecord["terminal"]=true;RuntimeInputs.Save(Owned(active),activeRecord);RuntimeInputs.Maintain();Need(File.Exists(Path.Combine(Owned(active),"input.cwp")),"live-worker-protected-even-with-terminal-marker");File.WriteAllText(Path.Combine(Common.Root,"active-release"),"");End(p);
    }RuntimeInputs.Maintain();Need(!Directory.Exists(Owned(active)),"active-then-normal-cleanup");
    id=ChildRun("normal",2097152);string input=Path.Combine(Owned(id),"input.cwp");using(var locked=new FileStream(input,FileMode.Open,FileAccess.Read,FileShare.Read)){
     for(int i=0;i<5;i++)RuntimeInputs.Maintain();Need(File.Exists(input),"locked-file-preserved");var r=Common.Read(Path.Combine(Owned(id),"owner.json"));Need(Convert.ToInt32(r["deleteAttempts"])==3,"delete-retries-bounded-at-three");Need(Common.S(Common.Read(Path.Combine(RunPath(id),"runtime-input-cleanup.json")),"state")=="retained-retry-limit","cleanup-failure-not-reported-success");
    }
    // Explicit fixture repair only; production never resets exhausted attempts.
    var repair=Common.Read(Path.Combine(Owned(id),"owner.json"));repair["deleteAttempts"]=0;RuntimeInputs.Save(Owned(id),repair);RuntimeInputs.Maintain();Need(!Directory.Exists(Owned(id)),"unlocked-retry-with-explicit-test-repair");
    string f1=ChildRun("failed",67108864),f2=ChildRun("failed",67108864);RuntimeInputs.Maintain();Need(Bytes()==134217728,"two-failure-inputs-128MiB-retained");string f3=ChildRun("failed",67108864);long peak=Bytes();RuntimeInputs.Maintain();Need(Bytes()==134217728&&!Directory.Exists(Owned(f1)),"third-failure-evicts-oldest-verified-ended-copy");Need(File.ReadAllText(Path.Combine(RunPath(f1),"fault.txt")).Contains("original"),"original-failure-detail-survives-eviction");measurements.Add(new{caseName="failure-retention",peakBytes=peak,retainedBytes=Bytes()});
    // Locked excess failure must block the next preparation rather than growing silently.
    string f4=ChildRun("failed",67108864);using(var locked=new FileStream(Path.Combine(Owned(f2),"input.cwp"),FileMode.Open,FileAccess.Read,FileShare.Read)){
     RuntimeInputs.Maintain();bool refused=false;string next=Guid.NewGuid().ToString("N");Request(next,Common.Hash(Source(2097152)));try{using(var l=RuntimeInputs.Begin(RunPath(next),Source(2097152),Common.Hash(Source(2097152)),CancellationToken.None)){} }catch(IOException){refused=true;}Need(refused,"unreclaimable-failure-budget-refuses-prepare");
    }
    // Repair only the owned test fixture and use normal maintenance to reclaim.
    foreach(string d in Directory.GetDirectories(RuntimeInputs.Root)){var r=Common.Read(Path.Combine(d,"owner.json"));r["failed"]=false;r["deleteAttempts"]=0;RuntimeInputs.Save(d,r);}RuntimeInputs.Maintain();Need(Bytes()==0,"test-failure-fixture-ended-cleanup");
    long cumulative=0;for(int i=0;i<12;i++){id=ChildRun("normal",2097152);cumulative+=2097152;RuntimeInputs.Maintain();Need(Bytes()==0,"continuous-run-"+i+"-zero-retained-input");}measurements.Add(new{caseName="12-normal-runs",copiedBytes=cumulative,retainedBytes=Bytes()});
    id=ChildRun("normal",2097152);File.WriteAllText(Path.Combine(Owned(id),"unknown.bin"),"protected");RuntimeInputs.Maintain();Need(File.Exists(Path.Combine(Owned(id),"input.cwp")),"unknown-file-blocks-delete");File.Delete(Path.Combine(Owned(id),"unknown.bin"));RuntimeInputs.Maintain();
    id=ChildRun("normal",2097152);var wrong=Common.Read(Path.Combine(Owned(id),"owner.json"));wrong["id"]="wrong";RuntimeInputs.Save(Owned(id),wrong);RuntimeInputs.Maintain();Need(File.Exists(Path.Combine(Owned(id),"input.cwp")),"wrong-marker-protected");wrong["id"]=id;RuntimeInputs.Save(Owned(id),wrong);RuntimeInputs.Maintain();
    id=ChildRun("normal",2097152);var outside=Common.Read(Path.Combine(Owned(id),"owner.json"));outside["run"]=Common.Root;RuntimeInputs.Save(Owned(id),outside);RuntimeInputs.Maintain();Need(File.Exists(Path.Combine(Owned(id),"input.cwp")),"outside-log-path-protected");outside["run"]=RunPath(id);RuntimeInputs.Save(Owned(id),outside);RuntimeInputs.Maintain();
    id=ChildRun("normal",2097152);string changed=Path.Combine(Owned(id),"input.cwp"),original=Path.Combine(Common.Root,"original-owned.cwp");File.Move(changed,original);File.WriteAllText(changed,"replacement");RuntimeInputs.Maintain();Need(File.Exists(changed),"replaced-file-identity-protected");File.Delete(changed);File.Move(original,changed);var fixedRecord=Common.Read(Path.Combine(Owned(id),"owner.json"));fixedRecord["deleteAttempts"]=0;RuntimeInputs.Save(Owned(id),fixedRecord);RuntimeInputs.Maintain();
    // Junction created by the invoking test script; never traverse or remove it here.
    string junction=Path.Combine(Common.Root,"junction");bool blocked=false;try{RuntimeInputs.Safe(junction);}catch(IOException){blocked=true;}Need(blocked,"reparse-point-rejected");
    id=ChildRun("growth",2097152);RuntimeInputs.Maintain();Need(!Directory.Exists(Owned(id)),"source-growth-reservation-protected");
    string unknown=ChildRun("unknown",2097152);RuntimeInputs.Maintain();Need(File.Exists(Path.Combine(Owned(unknown),"input.cwp")),"unknown-terminal-state-protected-after-worker-exit");
    // A reservation counts toward the hard limit before any bytes are copied.
    string reserve=ChildRun("reserve",RuntimeInputs.TotalLimit-2097152);bool cap=false;id=Guid.NewGuid().ToString("N");Request(id,Common.Hash(Source(2097152)));try{using(var l=RuntimeInputs.Begin(RunPath(id),Source(2097152),Common.Hash(Source(2097152)),CancellationToken.None)){} }catch(IOException){cap=true;}Need(cap,"hard-cap-includes-incomplete-and-active-reservations");
    Need(File.ReadAllText(legacy)=="legacy-protected"&&new FileInfo(Source(2097152)).Length==2097152,"legacy-and-persistent-resource-unchanged");
    // Resolve only test-owned synthetic unknown records to leave the isolated test clean.
    foreach(string d in Directory.GetDirectories(RuntimeInputs.Root)){var r=Common.Read(Path.Combine(d,"owner.json"));r["terminal"]=true;r["failed"]=false;RuntimeInputs.Save(d,r);}RuntimeInputs.Maintain();Need(Bytes()==0,"all-test-owned-inputs-cleaned");File.Delete(Source(RuntimeInputs.TotalLimit-2097152));File.Delete(Source(67108864));Need(!File.Exists(Path.Combine(Common.Root,"persistent-library","fixture-"+(RuntimeInputs.TotalLimit-2097152)+".cwp")),"large-synthetic-test-source-disposed");
    id=ChildRun("normal",2097152);File.WriteAllText(Path.Combine(Common.Root,"reopen-id.txt"),id);
   }
   Common.SaveNew(report,new{passed=true,checks=checks,measurements=measurements,method="actual RuntimeInputs/PlaybackResourceCheck handlers; real bounded fixture worker subprocesses; no host/engine/window",inputBytes=Bytes()});return 0;
  }catch(Exception e){Common.SaveNew(report,new{passed=false,error=e.ToString(),checks=checks,measurements=measurements});return 1;}
 }
}
#endif
