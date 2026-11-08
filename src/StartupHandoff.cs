using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;

internal static class StartupHandoff {
 internal static string CurrentStage="启动初始化";
#if TEST_BUILD
 internal static bool TestFailFallbackWrite;
#endif
 internal static void SetStage(string stage){CurrentStage=FaultSafety.Text(stage,140);}

 static string ProgramVersion(){
  try{
   string path=Path.Combine(Common.AppRoot,"build","release.json");
   if(!File.Exists(path))return "版本清单缺失";
   var d=Common.Json.Deserialize<Dictionary<string,object>>(Common.ReadShared(path));
   return FaultSafety.Text(Common.S(d,"Version"),100);
  }catch(Exception ex){return "版本未知（"+FaultSafety.Text(ex.GetType().Name,40)+"）";}
 }

 internal static FaultRecord Capture(Exception error,string stage){
  if(error==null)error=new InvalidOperationException("启动失败，但异常对象不可用");
  if(!DataLocation.Ready)try{DataLocation.Emergency();}catch{}
  string state=DataLocation.Ready?"正式数据位置已通过校验；本次启动后续状态需核对":DataLocation.Current!=null?"数据位置配置已读取，但正式数据验证未完成；未建立空库":"数据位置尚未确认；未建立空库";
  string dataStatus=DataLocation.Ready?"verified":DataLocation.Current!=null?"configured-but-not-validated":"unknown";
  string diagnosticId="diag-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,12);
  return FaultSafety.Sanitize(new FaultRecord{
   SchemaVersion=1,
   Id=diagnosticId,IncidentId=diagnosticId,
   EventTime=DateTimeOffset.Now.ToString("o"),Category=FaultSafety.Category("environment"),
   Operation="打开本地壁纸",Stage=FaultSafety.Text(stage??CurrentStage,140),
   Reason=FaultSafety.Text(error.GetType().FullName+"："+error.Message,1600),
   ExceptionType=FaultSafety.Text(error.GetType().FullName,160),
   ProgramVersion=ProgramVersion(),EntryPath=FaultSafety.Text(DataLocation.EntryPath,600),
   ProcessImage=FaultSafety.Text(Application.ExecutablePath,600),DataLocationStatus=dataStatus,
   StartupFailure=true,UserSymptom="用户尚未补充",DataState=state,
   CleanupState="主界面建立未完成；启动器没有请求播放，其他清理状态未推断。",
   DesktopState="本次启动没有请求播放或桌面切换；当前桌面状态未核查。",
   WorkerState="unknown",PlayerState="unknown",Events=new List<Dictionary<string,object>>()
  });
 }

 internal static string RecordDiagnosticFailure(FaultRecord fault,Exception diagnosticError){
  string temp=null;
  try{
#if TEST_BUILD
   if(TestFailFallbackWrite)return null;
#endif
   string root=DataLocation.SafePath(DataLocation.EmergencyRoot);
   if(DataLocation.Current!=null){string data=DataLocation.SafePath(DataLocation.Current.DataRoot);if(DataLocation.Same(root,data)||LocalImport.Under(root,data)||LocalImport.Under(data,root))throw new InvalidDataException("诊断备用位置与正式数据重叠，拒绝写入");}
   Directory.CreateDirectory(root);LocalImport.NoLinks(root);
   string folder=Path.Combine(root,"startup-failures");Directory.CreateDirectory(folder);LocalImport.NoLinks(folder);
   string target=Path.Combine(folder,fault.Id+"-"+Guid.NewGuid().ToString("N").Substring(0,8)+".json");
   temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
   var record=new{
    schemaVersion=1,diagnosticId=fault.Id,incidentId=fault.IncidentId,recordedAt=DateTimeOffset.Now.ToString("o"),
    programVersion=fault.ProgramVersion,entryPath=fault.EntryPath,actualProcessImage=fault.ProcessImage,
    startupStage=fault.Stage,originalExceptionType=fault.ExceptionType,originalException=fault.Reason,
    dataLocationStatus=fault.DataLocationStatus,dataLocation=DataLocation.Snapshot(),
    diagnosticFailureType=diagnosticError==null?"unknown":FaultSafety.Text(diagnosticError.GetType().FullName,160),
    diagnosticFailure=diagnosticError==null?"诊断包生成失败":FaultSafety.Text(diagnosticError.Message,800),
    packageCreated=false,automaticRepair=false
   };
   byte[] bytes=new UTF8Encoding(false).GetBytes(Common.Json.Serialize(record));if(bytes.Length>131072)throw new InvalidDataException("启动备用记录超过128KiB预算");
   using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
   File.Move(temp,target);temp=null;return target;
  }catch{return null;}
  finally{if(temp!=null)try{if(File.Exists(temp))File.Delete(temp);}catch{}}
 }

 internal static DiagnosticResult BuildEvidence(string operation,FaultRecord fault,Action cancelled,bool injectFailure=false){
  try{
#if TEST_BUILD
   if(injectFailure)throw new IOException("TEST_BUILD隔离注入：诊断包生成阶段失败");
#endif
   return DiagnosticBundle.Build(operation,fault,cancelled);
  }catch(OperationCanceledException){throw;}catch(Exception ex){fault.DiagnosticFailurePath=RecordDiagnosticFailure(fault,ex);throw;}
 }

 internal static int Show(Exception error,string stage=null){
  FaultRecord fault=null;
  try{
   fault=Capture(error,stage??CurrentStage);
   Application.EnableVisualStyles();
   using(var window=new FaultHandoffForm(fault,null)){
#if TEST_BUILD
    if(Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_STARTUP_AUTOCLOSE")=="1"){
     window.TestSkipClipboard=true;
     window.Shown+=async(s,e)=>{
      await window.TestCopy();
      Directory.CreateDirectory(Path.Combine(Common.Root,"evidence"));
      Common.SaveNew(Path.Combine(Common.Root,"evidence/startup-diagnostic-result.json"),new{
       passed=window.TestPackage!=null,diagnosticId=fault.Id,stage=fault.Stage,reason=fault.Reason,
       fallbackRecord=fault.DiagnosticFailurePath,package=window.TestPackage,playerStarted=false,userClicked=false
      });
      window.Close();
     };
    }
#endif
    window.ShowDialog();
   }
  }catch(Exception reportingError){
   string record=fault==null?null:RecordDiagnosticFailure(fault,reportingError);
   string original=fault==null?FaultSafety.Text(error==null?"启动异常未知":error.GetType().FullName+"："+error.Message,800):fault.Reason;
   string secondary=FaultSafety.Text(reportingError.GetType().FullName+"："+reportingError.Message,400);
   string status=record==null?"原始启动异常仍保留在本提示中；备用记录也未能保存。":"原始启动异常已保留；诊断包准备失败，备用记录："+record;
   try{MessageBox.Show("本地壁纸没有打开。\n阶段："+(stage??CurrentStage)+"\n原始错误："+original+"\n交接准备错误："+secondary+"\n"+status+"\n没有自动修复或启动播放。","本地壁纸");}catch{}
  }
  return 1;
 }
}
