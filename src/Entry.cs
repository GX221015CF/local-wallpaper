using System;using System.IO;using System.Threading;using System.Diagnostics;using System.Windows.Forms;
internal static class Entry {
 [STAThread]static int Main(string[] args){StartupHandoff.SetStage("初始化Windows界面环境");try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);return RunMain(args);}catch(Exception ex){if(args.Length>0&&(args[0]=="--local-import"||args[0]=="--worker"))return 1;return StartupHandoff.Show(ex,StartupHandoff.CurrentStage);}}
 static int RunMain(string[] args){
  StartupHandoff.SetStage("检查启动参数");
#if TEST_BUILD
  if(args.Length==2&&args[0]=="--host-failure-replay")return HostFailureReplaySmoke.Run(args[1]);
  if(args.Length==2&&args[0]=="--core-playback-smoke")return CorePlaybackSmoke.Run(args[1]);
  if(args.Length==2&&args[0]=="--core-playback-reopen")return CorePlaybackSmoke.Reopen(args[1]);
  if(args.Length==2&&args[0]=="--cover-path-regression")return CoverPathSmoke.Run(Path.GetFullPath(args[1]));
  if(args.Length==2&&args[0]=="--local-import"){
   TestImportIsolation.SetDataRoot();Directory.CreateDirectory(Common.LogRoot);return LocalImport.Worker(Path.GetFullPath(args[1]));
  }
  if(args.Length==2&&args[0]=="--import-flow-smoke")return ImportFlowSmoke.Run(Path.GetFullPath(args[1]));
  if(args.Length==2&&args[0]=="--import-flow-reopen")return ImportFlowSmoke.Reopen(Path.GetFullPath(args[1]));
  if(args.Length>0&&(args[0]=="--worker"||args[0]=="--restore-last"||args[0]=="--apply-login-restore"))return 2;
#endif
  if(args.Length==2&&args[0]=="--local-import"){DataLocation.Initialize();Directory.CreateDirectory(Common.LogRoot);return LocalImport.Worker(Path.GetFullPath(args[1]));}
  if(args.Length==2&&args[0]=="--worker"){DataLocation.Initialize();Directory.CreateDirectory(Common.LogRoot);return LeaseWorker.Run(args[1]);}
#if TEST_BUILD
  if(args.Length==3&&args[0]=="--first-use-regression")return FirstUseRegression.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));
  if(args.Length==3&&args[0]=="--startup-diagnostic-regression")return StartupDiagnosticSmoke.Run(Path.GetFullPath(args[1]),args[2]);
  if(args.Length==2&&args[0]=="--diagnostic-bundle-regression")return HeadlessDiagnosticSmoke.Run(Path.GetFullPath(args[1]));
  if(args.Length==3&&args[0]=="--binding-reload-regression")return HeadlessDiagnosticSmoke.ReloadBinding(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));
  if(args.Length==4&&args[0]=="--runtime-provisioning-smoke")return RuntimeProvisioningSmoke.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]),Path.GetFullPath(args[3]));
  if(args.Length==2&&args[0]=="--source-location-regression")return SourceLocationSmoke.Run(Path.GetFullPath(args[1]));
  if(args.Length==2&&args[0]=="--source-recovery-regression")return SourceRecoverySmoke.Run(Path.GetFullPath(args[1]));
  if(args.Length==2&&args[0]=="--source-location-reopen")return SourceLocationSmoke.Reopen(Path.GetFullPath(args[1]));
  if(args.Length==3&&args[0]=="--first-use-source-batch")return SourceLocationSmoke.RunBatch(Path.GetFullPath(args[1]),Path.GetFullPath(args[2]));
  if(args.Length==2&&args[0]=="--empty-state-smoke")return EmptyStateSmoke.Run(Path.GetFullPath(args[1]));
  if(args.Length==2&&args[0]=="--names-diagnostics-smoke")return NameDiagnosticsSmoke.Run(Common.Root,args[1]);
  if(args.Length==1&&args[0]=="--startup-diagnostic-smoke")throw new InvalidDataException("隔离启动失败夹具；不是正式程序发生故障");
#endif
  if(args.Length==0&&!File.Exists(DataLocation.BindingPath)){StartupHandoff.SetStage("检查首次使用数据位置");int first=FirstUseSetup.RunInteractive();if(first==0)return 0;if(first<0)return 1;}
  StartupHandoff.SetStage("读取并验证数据位置");
  DataLocation.Initialize();
  StartupHandoff.SetStage("准备程序运行日志");
  Directory.CreateDirectory(Common.LogRoot);
  if(args.Length==2&&args[0]=="--apply-login-restore"){try{using(var gate=new UpgradeGate())using(var lease=new DataLease(Common.Root)){DataLocation.ValidateStore(DataLocation.Current,true);LoginRestore.ApplyApprovedPlan(new WindowsStartupStore(),args[1]);return 0;}}catch(Exception ex){MessageBox.Show("登录恢复未启用："+ex.Message,"本地壁纸");return 4;}}
  bool restoreRequested=args.Length==1&&args[0]=="--restore-last";
  string cwp=Common.DefaultCwp,scenario=null;int seconds=0;
#if TEST_BUILD
  for(int i=0;i<args.Length&&!restoreRequested;i++){if(args[i]=="--scenario"&&i+1<args.Length)scenario=Path.GetFullPath(args[++i]);else return 2;}
#else
  if(args.Length!=0&&!restoreRequested)return 2;
#endif
  // Continuous only; playback has no duration ceiling.
  string mutexName;
#if TEST_BUILD
  mutexName="Local\\GXLocalWallpaper-Test-local_trial_startup_evidence_r9_retry_20261104-v1";
#else
  mutexName="Local\\Local317495ControlWindow-v1";
#endif
  StartupHandoff.SetStage("取得单实例并验证当前版本");bool held=false;using(var mutex=new Mutex(false,mutexName)){
   try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held){string reason;if(InstanceChannel.TryShow(out reason))return 0;MessageBox.Show("已有本地壁纸实例正在运行。若是同版本，请稍后重试打开；若是其他版本，请先正常退出那个本地壁纸窗口，再打开此版本。\n没有停止原实例或新建播放器。\n"+reason,"本地壁纸");return 3;}
    DataLocation.Initialize();using(var dataLease=new DataLease(Common.Root)){DataLocation.ValidateStore(DataLocation.Current,true);
    StartupHandoff.SetStage("创建主界面");Common.SetProcessDpiAwarenessContext(new IntPtr(-4));var window=new ControlWindow(cwp,seconds,scenario,restoreRequested);StartupHandoff.SetStage("运行主界面消息循环");Application.Run(window);return 0;}
   }catch(Exception ex){return StartupHandoff.Show(ex,StartupHandoff.CurrentStage);}
   finally{if(held)mutex.ReleaseMutex();}
  }
 }
}
