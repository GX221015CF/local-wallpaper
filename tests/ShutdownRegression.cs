#if TEST_BUILD
using System;using System.IO;using System.Reflection;using System.Collections.Generic;using System.Runtime.InteropServices;
internal static class ShutdownRegression {
 static int stops,releases,stopResult;static bool releaseThrows;static Delegate stopDelegate,releaseDelegate;
 static void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
 public static int StopStub(IntPtr self){stops++;return stopResult;}
 public static void ReleaseStub(IntPtr self){releases++;if(releaseThrows)throw new InvalidOperationException("fixture-release-failure");}
 static void Lifetime(bool stopFails,bool releaseFails,bool loggingFails=false){
  stops=releases=0;stopResult=stopFails?unchecked((int)0x80004005):0;releaseThrows=releaseFails;
  var core=new EngineCore((phase,data)=>{if(loggingFails)throw new IOException("fixture-dispose-log");});var type=typeof(EngineCore);
  var stopType=type.GetNestedType("Stop",BindingFlags.NonPublic);var releaseType=type.GetNestedType("Release",BindingFlags.NonPublic);
  stopDelegate=Delegate.CreateDelegate(stopType,typeof(ShutdownRegression).GetMethod("StopStub"));
  releaseDelegate=Delegate.CreateDelegate(releaseType,typeof(ShutdownRegression).GetMethod("ReleaseStub"));
  IntPtr table=Marshal.AllocHGlobal(0x40),instance=Marshal.AllocHGlobal(IntPtr.Size);
  try{
   for(int i=0;i<0x40;i++)Marshal.WriteByte(table,i,0);
   Marshal.WriteIntPtr(table,0x30,Marshal.GetFunctionPointerForDelegate(stopDelegate));Marshal.WriteIntPtr(instance,table);
   type.GetField("self",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(core,instance);
   type.GetField("release",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(core,releaseDelegate);
   Exception fault=null;try{core.Dispose();}catch(Exception ex){fault=ex;}
   Assert(stops==1,"stop must be attempted once");
   Assert(releases==1,"release must run even after engine-stop returns a failing HRESULT; actual release count="+releases);
   Assert((fault!=null)==(stopFails||releaseFails||loggingFails),"cleanup error must be preserved");
   if(stopFails)Assert(fault.ToString().Contains("engine-stop=80004005"),"original stop error lost");
   if(releaseFails)Assert(fault.ToString().Contains("fixture-release-failure"),"release error lost");
   core.Dispose();Assert(stops==1&&releases==1,"Dispose retry must not stop or release same instance twice");
  }finally{Marshal.FreeHGlobal(instance);Marshal.FreeHGlobal(table);GC.KeepAlive(stopDelegate);GC.KeepAlive(releaseDelegate);}
 }
 static void Row(string run,string phase,string error,int seq){File.AppendAllText(Path.Combine(run,"host.jsonl"),Common.Json.Serialize(new{seq=seq,phase=phase,data=new{error=error}})+"\n");}
 static int Main(string[] args){
  if(args.Length!=2)return 2;string run=Path.GetFullPath(args[0]),report=Path.GetFullPath(args[1]);var cases=new List<string>();
  try{
   Directory.CreateDirectory(run);Lifetime(false,false);cases.Add("actual-dispose-normal-and-idempotent");
   Lifetime(true,false);cases.Add("actual-dispose-stop-failure-still-releases");
   Lifetime(false,true);cases.Add("actual-dispose-release-failure-is-idempotent");
   Lifetime(true,true);cases.Add("actual-dispose-keeps-stop-and-release-errors");
#if !LEGACY_R20
   Lifetime(true,false,true);cases.Add("actual-dispose-log-failure-still-stops-and-releases");
   var cookies=new List<long>();EngineCore.TestRemoveCookie=p=>{cookies.Add(p.ToInt64());if(p.ToInt64()==11)throw new IOException("fixture-dep-cookie");return true;};
   try{var cookieCore=new EngineCore((p,d)=>{});typeof(EngineCore).GetField("depdllCookie",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(cookieCore,new IntPtr(11));typeof(EngineCore).GetField("rootCookie",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(cookieCore,new IntPtr(22));Exception cookieError=null;try{cookieCore.Dispose();}catch(Exception ex){cookieError=ex;}Assert(cookieError!=null&&cookieError.ToString().Contains("fixture-dep-cookie")&&cookies.Count==2&&cookies[1]==22,"root cleanup skipped after dep cleanup failure");cookieCore.Dispose();Assert(cookies.Count==2,"cookie cleanup repeated after failure");}finally{EngineCore.TestRemoveCookie=null;}
   cases.Add("actual-dispose-independent-cookie-cleanup-and-retry");
   var unopened=new EngineCore((p,d)=>{});unopened.Dispose();unopened.Dispose();cases.Add("cancel-before-engine-created-cleanup-noop");
   foreach(bool failStop in new[]{false,true})foreach(bool failDetach in new[]{false,true}){
    int detached=0;var events=new List<string>();
    var outcome=HostShutdown.Complete(()=>{if(failStop)throw new Exception("fixture-stop");},()=>{detached++;if(failDetach)throw new Exception("fixture-detach");},(p,d)=>events.Add(p));
    Assert(detached==1,"detach skipped after stop failure");Assert(outcome.Failed==(failStop||failDetach),"host cleanup outcome wrong");
    Assert(HostShutdown.ExitCode("window-close",outcome.Failed)==(outcome.Failed?4:0),"host exit code misreports cleanup");
    Assert(HostShutdown.ExitCode("engine-failed",outcome.Failed)==3,"start failure must retain precedence");
    Assert(events.Contains("stop-error")==failStop&&events.Contains("detach-failed")==failDetach,"cleanup phase lost");
   }cases.Add("actual-host-shutdown-stop-detach-matrix");
   int afterLogFailure=0;var badLog=HostShutdown.Complete(()=>{throw new Exception("original-stop");},()=>{afterLogFailure++;},(p,d)=>{throw new IOException("fixture-log-failure");});
   Assert(afterLogFailure==1&&badLog.Failed&&badLog.Errors.Count==2,"diagnostic failure masked original or skipped detach");cases.Add("host-diagnostic-failure-still-detaches");
   var failed=Path.Combine(run,"start-and-stop");Directory.CreateDirectory(failed);Row(failed,"engine-failed","original-open-failure",1);Row(failed,"stop-error","secondary-stop-failure",2);Row(failed,"closed","",3);
   var failure=PlaybackFailure.ReadHost(failed);Assert(failure!=null&&failure.Stage=="engine-failed"&&failure.Error.Contains("original-open-failure")&&failure.Error.Contains("secondary-stop-failure"),"primary and cleanup failure must both survive closed record");
   Assert(PlaybackFailure.Failed(0,failure),"zero exit cannot turn logged host failure into success");cases.Add("actual-host-failure-parser-preserves-primary-and-stop");
   var stopped=Path.Combine(run,"stop-only");Directory.CreateDirectory(stopped);Row(stopped,"stop-error","fixture-stop-only",1);
   failure=PlaybackFailure.ReadHost(stopped);Assert(failure!=null&&failure.Stage=="stop-error"&&PlaybackFailure.Failed(0,failure),"stop-only fault missing");cases.Add("stop-only-error-rejected-with-zero-exit");
   var empty=Path.Combine(run,"normal");Directory.CreateDirectory(empty);Row(empty,"closed","",1);
   Assert(PlaybackFailure.ReadHost(empty)==null&&!PlaybackFailure.Failed(0,null)&&PlaybackFailure.Failed(4,null),"normal or nonzero result classified incorrectly");cases.Add("normal-close-not-falsely-failed");
   var detach=Path.Combine(run,"detach");Directory.CreateDirectory(detach);Row(detach,"detach-failed","fixture-detach-only",1);Assert(PlaybackFailure.ReadHost(detach).Stage=="detach-failed","detach failure missing");cases.Add("detach-error-retained");
#endif
   File.WriteAllText(report,Common.Json.Serialize(new{passed=true,cases=cases,actualEngineDisposeInvoked=true,actualManagedAbiStub=true,openCalled=false,nativeEngineLoaded=false,guiCreated=false,desktopChanged=false}));return 0;
  }catch(Exception ex){File.WriteAllText(report,Common.Json.Serialize(new{passed=false,cases=cases,error=ex.ToString(),actualEngineDisposeInvoked=true,openCalled=false,nativeEngineLoaded=false,guiCreated=false}));return 1;}
 }
}
#endif
