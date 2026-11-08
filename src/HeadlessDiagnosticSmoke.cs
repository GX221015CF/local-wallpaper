#if TEST_BUILD
using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.IO.Compression;

internal static class HeadlessDiagnosticSmoke {
 static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 static string ReadText(ZipArchive archive,string name){using(var reader=new StreamReader(archive.GetEntry(name).Open(),Encoding.UTF8))return reader.ReadToEnd();}
 internal static int ReloadBinding(string binding,string output){
  try{
   binding=Path.GetFullPath(binding);output=Path.GetFullPath(output);string app=Path.GetFullPath(Common.AppRoot);string allowed=Path.Combine(app,"build");
   if(!LocalImport.Under(binding,allowed)||Path.GetFileName(binding)!="data-location.json"||!LocalImport.Under(output,Path.Combine(app,"tests")))throw new InvalidDataException("Reopen fixture paths must remain inside package build/tests.");
   Environment.SetEnvironmentVariable("LOCALWALLPAPER_TEST_BINDING",binding);Environment.SetEnvironmentVariable("LOCALWALLPAPER_TEST_CONTROL",Path.Combine(Path.GetDirectoryName(binding),"control"));
   DataLocation.Initialize();DataLocation.ValidateStore(DataLocation.Current,false);
   var catalog=DataLocation.ReadSmall<LibraryDocument>(Path.Combine(DataLocation.Current.DataRoot,"data","catalog.json"));
   Common.SaveNew(output,new{passed=true,classification="A separate newly started TEST_BUILD process read the persisted isolated binding and store",bindingPath=binding,dataRoot=DataLocation.Current.DataRoot,dataFormat=DataLocation.Current.DataFormat,storeId=DataLocation.Current.StoreId,catalogItems=catalog.Items.Count,processId=System.Diagnostics.Process.GetCurrentProcess().Id,formalDataReadOrWritten=false,playerStarted=false,desktopChanged=false,userClicked=false});
   return 0;
  }catch(Exception ex){try{Directory.CreateDirectory(Path.GetDirectoryName(output));Common.SaveNew(output,new{passed=false,error=FaultSafety.Text(ex.GetType().Name+"："+ex.Message),formalDataReadOrWritten=false,playerStarted=false,desktopChanged=false});}catch{}return 1;}
 }
 internal static int Run(string output){
  string operation=Path.Combine(Common.WorkRoot,"diagnostic-regression");
  try{
   if(!LocalImport.Under(operation,Common.WorkRoot))throw new InvalidDataException("Diagnostic operation escaped isolated task root");
   RuntimeProvisioning.Initialize();bool missingUnavailable=!RuntimeProvisioning.Available&&RuntimeProvisioning.Message.Contains("尚未准备好");bool startRefused=false;try{RuntimeProvisioning.RequireForStart();}catch(InvalidOperationException){startRefused=true;}
   Require(missingUnavailable&&startRefused,"Missing playback components must remain unavailable and reject playback start");
   Directory.CreateDirectory(operation);
   var fault=new FaultRecord{SchemaVersion=1,Id="diag-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,12),IncidentId=null,EventTime=DateTimeOffset.Now.ToString("o"),Category=FaultSafety.Category("environment"),Operation="隔离组件检查",Stage="TEST_BUILD诊断生成",Reason="隔离夹具：播放组件尚未准备好",UserSymptom="未补充",DataState="仅隔离数据与程序位置；正式数据未读取。",CleanupState="未启动播放器；无播放任务。",DesktopState="未请求桌面操作。",WorkerState="unknown",PlayerState="unknown",Events=new List<Dictionary<string,object>>()};
   fault.IncidentId=fault.Id;
   var result=DiagnosticBundle.Build(operation,fault,()=>{});
   string full=Path.GetFullPath(Path.Combine(Common.Root,result.PackageRelative));
   Require(File.Exists(full),"Diagnostic ZIP missing");
   Require(result.Id==fault.Id&&result.IncidentId==fault.Id,"Diagnostic identity changed");
   Require(Common.Hash(full)==result.PackageSha256,"Diagnostic ZIP hash differs from result");
   DiagnosticBundle.Validate(full,result.Id);
   bool packageId=false,faultId=false,textId=false,textPath=false;int manifestFiles=0;
   using(var archive=ZipFile.OpenRead(full)){
    var manifest=Common.Json.Deserialize<Dictionary<string,object>>(ReadText(archive,"manifest.json"));
    var diagnostic=Common.Json.Deserialize<Dictionary<string,object>>(ReadText(archive,"diagnostic.json"));
    var snapshot=Common.Obj(diagnostic["eventSnapshot"]);var faultJson=Common.Obj(snapshot["fault"]);
    string handoff=ReadText(archive,"交给Agent.txt");
    packageId=Common.S(manifest,"diagnosticId")==result.Id&&Common.S(diagnostic,"diagnosticId")==result.Id;
    faultId=Common.S(faultJson,"Id")==result.Id;
    textId=handoff.Contains(result.Id)&&result.ClipboardText==handoff;
    textPath=handoff.Contains(full);
    foreach(object unused in (System.Collections.IEnumerable)manifest["files"])manifestFiles++;
   }
   Require(packageId&&faultId&&textId&&textPath,"Diagnostic text, ID, or ZIP manifest identity mismatch");
   DiagnosticBundle.CleanOperation(operation);
   bool tempClean=!Directory.Exists(Path.Combine(operation,"diagnostic-temp"))&&!File.Exists(Path.Combine(operation,"diagnostic-package.tmp"));
   Require(tempClean,"Diagnostic temporary files remain");
   Directory.CreateDirectory(Path.GetDirectoryName(output));
   Common.SaveNew(output,new{passed=true,classification="TEST_BUILD processor only; no GUI, clipboard API, player, or desktop action",playbackComponentsMissingReported=missingUnavailable,playbackStartRejected=startRefused,diagnosticId=result.Id,incidentId=result.IncidentId,packageRelative=result.PackageRelative,packageSha256=result.PackageSha256,packageBytes=result.Bytes,manifestFileCount=manifestFiles,manifestDiagnosticIdMatched=packageId,eventFaultIdMatched=faultId,handoffTextContainsSameId=textId,handoffTextContainsSamePackagePath=textPath,temporaryFilesClean=tempClean,formalDataReadOrWritten=false,externalRuntimeLoaded=false,playerStarted=false,desktopChanged=false,userClicked=false});
   return 0;
  }catch(Exception ex){try{Directory.CreateDirectory(Path.GetDirectoryName(output));Common.SaveNew(output,new{passed=false,classification="TEST_BUILD processor only",error=FaultSafety.Text(ex.GetType().Name+"："+ex.Message),formalDataReadOrWritten=false,playerStarted=false,desktopChanged=false});}catch{}return 1;}
  finally{try{DiagnosticBundle.CleanOperation(operation);}catch{}}
 }
}
#endif
