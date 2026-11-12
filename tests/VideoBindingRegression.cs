#if TEST_BUILD
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Web.Script.Serialization;
internal static class VideoBindingRegression {
 sealed class Fake:IDisposable {
  internal readonly List<string> Calls=new List<string>();internal bool Editor,NullEngine,WrongWindow,StopFail,ReleaseFail;internal string Fail;internal uint Kind,Transport;internal string Root,File;internal IntPtr Window=new IntPtr(0x1234);internal IntPtr Self,Table;readonly List<Delegate> keep=new List<Delegate>();
  internal Fake(){Table=Marshal.AllocHGlobal(0x6c);Self=Marshal.AllocHGlobal(IntPtr.Size);for(int n=0;n<0x6c;n+=4)Marshal.WriteIntPtr(Table,n,IntPtr.Zero);Marshal.WriteIntPtr(Self,Table);
   Put(0,new EngineCore.GetWindow(delegate(IntPtr s,out IntPtr w){Calls.Add("window");w=WrongWindow?new IntPtr(9):Window;return Result("window");}));
   Put(4,new EngineCore.SetPath(delegate(IntPtr s,string p){Calls.Add("root");Root=p;return Result("root");}));
   Put(12,new EngineCore.SetPath(delegate(IntPtr s,string p){Calls.Add("media");File=p;return Result("media");}));
   Put(0x48,new EngineCore.SetNumber(delegate(IntPtr s,uint k){Calls.Add("kind");Kind=k;return Result("kind");}));
   Put(0x4c,new EngineCore.SetNumber(delegate(IntPtr s,uint k){Calls.Add("transport");Transport=k;return Result("transport");}));
   Put(0x18,new EngineCore.SetNumber(delegate(IntPtr s,uint v){Calls.Add("volume");return Result("volume");}));
   Put(0x20,new EngineCore.SetNumber(delegate(IntPtr s,uint v){Calls.Add("fps");return Result("fps");}));
   Put(0x2c,new EngineCore.Start(delegate(IntPtr s,IntPtr cb,IntPtr reserve){Calls.Add("start");return Result("start");}));
   Put(0x30,new EngineCore.Stop(delegate(IntPtr s){Calls.Add("stop");return StopFail?-1:0;}));
   Put(0x3c,new EngineCore.WindowEvent(delegate(IntPtr s,IntPtr window,uint message,IntPtr buttons,IntPtr point){}));
  }
  int Result(string phase){return Fail==phase?-1:0;}
  void Put(int off,Delegate d){keep.Add(d);Marshal.WriteIntPtr(Table,off,Marshal.GetFunctionPointerForDelegate(d));}
  internal IntPtr Create(IntPtr hwnd,IntPtr opts){Calls.Add("create");Editor=opts!=IntPtr.Zero&&Marshal.ReadInt32(opts,4)==2;return NullEngine?IntPtr.Zero:Self;}
  internal void Release(IntPtr s){Calls.Add("release");if(ReleaseFail)throw new IOException("fake-release-failure");}
  public void Dispose(){Marshal.FreeHGlobal(Self);Marshal.FreeHGlobal(Table);GC.KeepAlive(keep);}
 }
 static readonly List<string> checks=new List<string>();static List<string> lastCalls;static void Need(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
 static EngineCore Core(){return new EngineCore((p,o)=>{});}
 static void Normal(string file,bool video){using(var f=new Fake()){var c=Core();try{c.TestInitialize(f.Window,"C:\\隔离 根\\media",file,0,f.Create,f.Release);lastCalls=f.Calls.ToList();Need(!f.Editor,"factory-never-requests-editor-"+file);Need(f.Kind==(video?1u:0u),"independent-video-media-kind-"+file);Need(f.Transport==(video?1u:0u),"known-xor-transport-explicit-"+file);Need(f.Root=="C:\\隔离 根\\media"&&f.File==file,"root-and-basename-correspond-"+file);Need(String.Join(",",f.Calls)==(video?"create,window,kind,root,media,transport,volume,fps,start":"create,window,root,media,volume,fps,start"),"actual-host-initialization-order-"+file);}finally{c.Dispose();c.Dispose();}Need(f.Calls.Count(x=>x=="stop")==1&&f.Calls.Count(x=>x=="release")==1,"stop-release-idempotent-"+file);}}
 static void Fails(string phase){using(var f=new Fake()){f.Fail=phase;var c=Core();bool caught=false;try{c.TestInitialize(f.Window,"C:\\isolated","input.mp4",0,f.Create,f.Release);}catch(Exception e){caught=e.Message.Contains(phase=="media"?"set-video":phase);}finally{c.Dispose();}Need(caught,"propagates-"+phase);Need(phase=="start"||!f.Calls.Contains("start"),"no-false-start-after-"+phase);Need(f.Calls.Last()=="release"&&f.Calls.Count(x=>x=="release")==1,"cleanup-after-"+phase);}}
 static int Main(string[] args){if(args.Length!=1)return 2;var d=new Dictionary<string,object>();int exit=1;try{Normal("input.mp4",true);Normal("input.cwp",false);Normal("INPUT.MP4",true);foreach(string s in new[]{"window","kind","root","media","transport","volume","fps","start"})Fails(s);
   using(var f=new Fake()){f.NullEngine=true;var c=Core();bool caught=false;try{c.TestInitialize(f.Window,"C:\\isolated","input.mp4",0,f.Create,f.Release);}catch(Exception){caught=true;}finally{c.Dispose();}Need(caught&&String.Join(",",f.Calls)=="create","null-factory-rejected-without-start");}
   using(var f=new Fake()){f.WrongWindow=true;var c=Core();bool caught=false;try{c.TestInitialize(f.Window,"C:\\isolated","input.mp4",0,f.Create,f.Release);}catch(Exception){caught=true;}finally{c.Dispose();}Need(caught&&!f.Calls.Contains("start")&&f.Calls.Last()=="release","window-mismatch-rejected-and-released");}
   foreach(bool releaseFail in new[]{false,true})using(var f=new Fake()){f.StopFail=true;f.ReleaseFail=releaseFail;var c=Core();c.TestInitialize(f.Window,"C:\\isolated","input.mp4",0,f.Create,f.Release);bool caught=false;try{c.Dispose();}catch(AggregateException){caught=true;}c.Dispose();Need(caught&&f.Calls.Count(x=>x=="release")==1,"stop-failure-still-release-"+releaseFail);}
   Normal("retry.mp4",true);d["passed"]=true;exit=0;
  }catch(Exception e){d["passed"]=false;d["error"]=e.ToString();}d["checks"]=checks;d["lastInitializationCalls"]=lastCalls;d["evidenceKind"]="TEST_BUILD actual EngineCore.InitializePlayback with managed unmanaged-ABI substitutes";d["thirdPartyLoaded"]=false;d["guiStarted"]=false;d["renderingProven"]=false;File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(d));return exit;}
}
#endif
