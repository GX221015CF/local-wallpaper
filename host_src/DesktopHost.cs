using System;using System.IO;using System.Drawing;using System.Diagnostics;using System.Runtime.InteropServices;using System.Windows.Forms;using System.Web.Script.Serialization;
internal sealed class Surface:Panel{protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ClassStyle|=0x20;return p;}}}
internal sealed partial class DesktopHost:Form {
 readonly string run,mode,cwpRoot,cwpFile,externalRuntimeRoot,contractHash,hostHash;readonly DailyLog log;readonly Stopwatch elapsed=Stopwatch.StartNew();
 readonly Surface surface=new Surface();readonly Timer tick=new Timer();EngineCore engine;DesktopLease lease;bool prepared,closed,modulesWritten,cleanupFailed,cleanupAttempted;volatile bool finished;string exitReason="window-close";IntPtr window;System.Threading.Timer safety;
 void Write(string phase,object data){log.Write(phase,data,elapsed.ElapsedMilliseconds);}
 public DesktopHost(string path,string requestedMode,string root,string file,string runtimeRoot,string runtimeContractHash,string expectedHostHash){
  cwpRoot=Path.GetFullPath(root);cwpFile=file;externalRuntimeRoot=Path.GetFullPath(runtimeRoot);contractHash=runtimeContractHash;hostHash=expectedHostHash;if(Path.GetFileName(file)!=file||!File.Exists(Path.Combine(cwpRoot,file)))throw new ArgumentException("Invalid CWP root/file");
  run=Path.GetFullPath(path);mode=requestedMode;log=new DailyLog(Path.Combine(run,"host.jsonl"));
  Icon=AppVisuals.LoadIcon();AutoScaleMode=AutoScaleMode.None;Text="Desktop317495-"+mode+"-"+Process.GetCurrentProcess().Id;BackColor=Color.Black;
  if(mode=="desktop"){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;Bounds=Screen.PrimaryScreen.Bounds;}else{ClientSize=new Size(1280,800);StartPosition=FormStartPosition.CenterScreen;MaximizeBox=false;FormBorderStyle=FormBorderStyle.FixedSingle;}
  surface.BackColor=Color.Black;surface.Dock=DockStyle.Fill;Controls.Add(surface);if(mode=="preview")SetupPreviewControls();
  Shown+=Start;FormClosing+=Closing;tick.Interval=250;tick.Tick+=Tick;
 }
 protected override bool ShowWithoutActivation{get{return mode=="desktop";}}
 protected override CreateParams CreateParams{get{var p=base.CreateParams;if(mode=="desktop")p.ExStyle|=0x08000080;return p;}}
 protected override void SetVisibleCore(bool value){
  if(value&&!prepared&&mode=="desktop"){
   try{window=Handle;lease=new DesktopLease(Write);var bounds=Screen.PrimaryScreen.Bounds;lease.Discover(bounds);lease.Attach(window,bounds);prepared=true;}
   catch(Exception e){Write("attach-failed",new{error=e.ToString()});if(lease!=null)lease.Detach();throw;}
  }
  base.SetVisibleCore(value);
 }
 protected override void WndProc(ref Message m){if(mode=="desktop"&&m.Msg==0x21){m.Result=new IntPtr(4);return;}base.WndProc(ref m);}
 void Start(object s,EventArgs e){
  window=Handle;Write("host",new{pid=Process.GetCurrentProcess().Id,mode=mode,window=Win.Geometry(Handle),render=Win.Geometry(surface.Handle),foreground=Win.GetForegroundWindow().ToInt64(),playbackPolicy="continuous",normalDeadline=false,requestedFps=30,externalRuntimeRoot=externalRuntimeRoot});
  if(mode=="preview")Write("preview-layout",previewLayout);
  try{engine=new EngineCore(Write);var req=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(Path.Combine(run,"request.json")));audioToken=Convert.ToString(req["runToken"]);uint volume=req.ContainsKey("audioMuted")&&!Convert.ToBoolean(req["audioMuted"])?(uint)Math.Max(0,Math.Min(100,Convert.ToInt32(req["audioVolume"]))):0;engine.Open(surface.Handle,cwpRoot,cwpFile,externalRuntimeRoot,contractHash,hostHash,volume);Write("audio-initial",new{effectiveVolume=volume});tick.Start();}
  catch(Exception x){Write("engine-failed",new{error=x.ToString()});exitReason="engine-failed";BeginInvoke((Action)(()=>Close()));}
 }
 string audioToken;long audioSeq;
 void ReadAudio(){string path=Path.Combine(run,"audio-request.json");if(!File.Exists(path))return;try{var d=new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(path));long seq=Convert.ToInt64(d["sequence"]);if(Convert.ToString(d["token"])!=audioToken||seq<=audioSeq)return;int volume=Convert.ToInt32(d["volume"]);if(volume<0||volume>100)return;uint effective=Convert.ToBoolean(d["muted"])?0u:(uint)volume;engine.SetVolume(effective);audioSeq=seq;Write("audio-applied",new{sequence=seq,volume=volume,muted=Convert.ToBoolean(d["muted"]),effectiveVolume=effective});}catch(IOException){}catch(Exception x){Write("audio-error",new{error=x.Message});}}
 int lastSecond=-1;
 void Tick(object s,EventArgs e){
  if(File.Exists(Path.Combine(run,"detach.request"))||File.Exists(Path.Combine(run,"close.request"))){exitReason="explicit-withdraw";Close();return;}
  if(mode=="desktop"&&!lease.Valid()){exitReason="desktop-host-lost";Close();return;}
  ReadAudio();ReadPreviewStatus();int sec=(int)elapsed.Elapsed.TotalSeconds;if(sec<lastSecond+2)return;lastSecond=sec;
  using(var p=Process.GetCurrentProcess()){Write("heartbeat",new{cpuMs=p.TotalProcessorTime.TotalMilliseconds,workingSet=p.WorkingSet64,elapsedSeconds=sec});
  if(sec>=4&&!modulesWritten){modulesWritten=true;var list=new System.Collections.Generic.List<object>();foreach(ProcessModule m in p.Modules)list.Add(new{name=m.ModuleName,path=m.FileName});Write("modules",list);}}
 }
 void CleanupPlayback(){if(cleanupAttempted)return;cleanupAttempted=true;var outcome=HostShutdown.Complete(()=>{if(engine!=null)engine.Dispose();},()=>{if(lease!=null)lease.Detach();},Write);cleanupFailed=outcome.Failed;}
 void Closing(object s,FormClosingEventArgs e){
  if(closed)return;if(mode=="preview"&&exitReason!="engine-failed"&&!File.Exists(Path.Combine(run,"detach.request"))&&!File.Exists(Path.Combine(run,"close.request"))){e.Cancel=true;SendPreviewAction("close");return;}closed=true;
  safety=new System.Threading.Timer(delegate{if(!finished){Win.ShowWindowAsync(window,0);Win.TerminateProcess(Win.GetCurrentProcess(),124);}},null,6000,System.Threading.Timeout.Infinite);
  tick.Stop();Win.ShowWindowAsync(Handle,0);Write("withdraw-begin",new{reason=exitReason});
  try{CleanupPlayback();}
  finally{finished=true;if(safety!=null)safety.Dispose();Write("closed",new{reason=exitReason,cleanupSucceeded=!cleanupFailed,exitCode=HostShutdown.ExitCode(exitReason,cleanupFailed),elapsedSeconds=elapsed.Elapsed.TotalSeconds});log.Dispose();}
 }
 [STAThread]static int Main(string[] args){
  if(args.Length!=8||IntPtr.Size!=4)return 2;if(args[2]!="continuous"||!(args[1]=="preview"||args[1]=="desktop"))return 2;
  Win.SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  DesktopHost app=null;try{app=new DesktopHost(args[0],args[1],args[3],args[4],args[5],args[6],args[7]);Application.Run(app);app.CleanupPlayback();return HostShutdown.ExitCode(app.exitReason,app.cleanupFailed);}
  catch(Exception x){File.WriteAllText(Path.Combine(args[0],"fatal.txt"),x.ToString());return 1;}
  finally{if(app!=null){app.CleanupPlayback();app.Dispose();}}
 }
}
