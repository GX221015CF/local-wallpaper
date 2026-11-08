using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.IO.Compression;
using System.Collections.Generic;

internal static class StartupDiagnosticSmoke {
 internal static int Run(string controlRoot,string scenario){
  var clock=Stopwatch.StartNew();string fallback=null,packagePath=null;FaultRecord fault=null;Exception packageFailure=null;bool passed=false,createdEmptyStore=false,formalDataTouched=false,playerStarted=false;
  try{
   string tests=Path.Combine(Common.AppRoot,"tests");
   controlRoot=DataLocation.SafePath(controlRoot);
   if(!LocalImport.Under(controlRoot,tests))throw new InvalidDataException("测试控制目录不在本轮隔离根内");
   Directory.CreateDirectory(controlRoot);LocalImport.NoLinks(controlRoot);
   Environment.SetEnvironmentVariable("LOCALWALLPAPER_TEST_CONTROL",controlRoot);
   DataLocation.Current=null;DataLocation.Ready=false;DataLocation.Failure="TEST_BUILD隔离夹具：未提供正式数据位置";DataLocation.Phase="隔离启动故障夹具";
   DataLocation.Emergency();
   string stage;Exception original;
   if(scenario=="stage-first-use"){stage="检查首次使用数据位置";original=new FileNotFoundException("ISOLATED_STARTUP_FAULT_BINDING_NOT_FOUND");}
   else if(scenario=="stage-data-validation"){stage="验证数据位置与登记";original=new InvalidDataException("ISOLATED_STARTUP_FAULT_DATA_VALIDATION");}
   else if(scenario=="stage-main-window"){stage="创建主界面";original=new InvalidOperationException("ISOLATED_STARTUP_FAULT_MAIN_WINDOW");}
   else if(scenario=="diagnostic-package-failure"||scenario=="fallback-write-failure"){stage="读取程序启动状态";original=new IOException("ISOLATED_ORIGINAL_STARTUP_EXCEPTION_PRESERVED");}
   else throw new ArgumentException("Unknown startup diagnostic scenario");
   StartupHandoff.SetStage(stage);fault=StartupHandoff.Capture(original,stage);
   if(fault.Id!=fault.IncidentId||fault.ExceptionType!=original.GetType().FullName||!fault.Reason.Contains(original.Message)||fault.ProcessImage!=System.Windows.Forms.Application.ExecutablePath||String.IsNullOrEmpty(fault.ProgramVersion)||fault.DataLocationStatus!="unknown")throw new InvalidDataException("Startup capture fields do not match the caught exception.");
   if(File.Exists(DataLocation.BindingPath)||File.Exists(Path.Combine(controlRoot,"deployment.json"))||File.Exists(Path.Combine(controlRoot,"data","catalog.json")))createdEmptyStore=true;
   string operation=Path.Combine(Common.WorkRoot,"startup-diagnostic-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(operation);LocalImport.NoLinks(operation);
   if(scenario=="diagnostic-package-failure"||scenario=="fallback-write-failure"){
    StartupHandoff.TestFailFallbackWrite=scenario=="fallback-write-failure";
    try{StartupHandoff.BuildEvidence(operation,fault,()=>{},true);throw new InvalidDataException("Expected isolated diagnostic package failure was not raised.");}
    catch(IOException ex){packageFailure=ex;fallback=fault.DiagnosticFailurePath;}
    finally{StartupHandoff.TestFailFallbackWrite=false;DiagnosticBundle.CleanOperation(operation);}
    if(scenario=="diagnostic-package-failure"){
     if(String.IsNullOrEmpty(fallback)||!File.Exists(fallback))throw new InvalidDataException("Original startup error fallback record missing.");
     LocalImport.NoLinks(fallback);var saved=Common.Json.Deserialize<Dictionary<string,object>>(Common.ReadShared(fallback));
     if(Common.S(saved,"diagnosticId")!=fault.Id||Common.S(saved,"startupStage")!=stage||Common.S(saved,"originalException")!=fault.Reason||!Common.S(saved,"diagnosticFailure").Contains("诊断包生成阶段失败"))throw new InvalidDataException("Fallback record lost the original failure or secondary diagnostic failure.");
    }else if(!String.IsNullOrEmpty(fallback)||!fault.Reason.Contains("ISOLATED_ORIGINAL_STARTUP_EXCEPTION_PRESERVED"))throw new InvalidDataException("Fallback write failure changed or hid the original startup exception.");
   }else{
    DiagnosticResult result=StartupHandoff.BuildEvidence(operation,fault,()=>{});packagePath=Path.GetFullPath(Path.Combine(Common.Root,result.PackageRelative));
    if(result.Id!=fault.Id||result.IncidentId!=fault.Id||!File.Exists(packagePath)||Common.Hash(packagePath)!=result.PackageSha256)throw new InvalidDataException("Diagnostic id, ZIP location, or digest mismatch.");
    DiagnosticBundle.Validate(packagePath,fault.Id);
    if(!result.ClipboardText.Contains(fault.Id)||!result.ClipboardText.Contains(stage)||!result.ClipboardText.Contains(original.Message)||!result.ClipboardText.Contains(fault.ProcessImage))throw new InvalidDataException("Handoff text does not include the startup failure facts.");
    using(var zip=ZipFile.OpenRead(packagePath)){
     var entry=zip.GetEntry("diagnostic.json");if(entry==null)throw new InvalidDataException("Diagnostic main record missing.");
     Dictionary<string,object> main;using(var reader=new StreamReader(entry.Open(),Encoding.UTF8))main=Common.Json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
     if(Common.S(main,"diagnosticId")!=fault.Id)throw new InvalidDataException("ZIP main record ID mismatch.");
     var capture=Common.Obj(main["startupCapture"]);
     if(Common.S(capture,"programVersion")!=fault.ProgramVersion||Common.S(capture,"entryPath")!=fault.EntryPath||Common.S(capture,"actualProcessImage")!=fault.ProcessImage||Common.S(capture,"startupStage")!=stage||Common.S(capture,"exceptionType")!=fault.ExceptionType||!Common.S(capture,"exception").Contains(original.Message)||Common.S(capture,"dataLocationStatus")!="unknown")throw new InvalidDataException("Startup diagnostic payload mismatch.");
     var manifestEntry=zip.GetEntry("manifest.json");Dictionary<string,object> manifest;using(var reader=new StreamReader(manifestEntry.Open(),Encoding.UTF8))manifest=Common.Json.Deserialize<Dictionary<string,object>>(reader.ReadToEnd());
     if(Common.S(manifest,"diagnosticId")!=fault.Id)throw new InvalidDataException("ZIP manifest ID mismatch.");
    }
    DiagnosticBundle.CleanOperation(operation);
   }
   if(File.Exists(DataLocation.BindingPath)||File.Exists(Path.Combine(controlRoot,"deployment.json"))||File.Exists(Path.Combine(controlRoot,"data","catalog.json")))createdEmptyStore=true;
   formalDataTouched=false;playerStarted=false;
   passed=!createdEmptyStore&&!formalDataTouched&&!playerStarted;
  }catch(Exception ex){packageFailure=ex;}
  finally{
   var result=new{
    schemaVersion=1,passed=passed,classification="TEST_BUILD headless StartupHandoff/DiagnosticBundle processor; no window, user interaction, player, or desktop action",
    scenario=scenario,diagnosticId=fault==null?null:fault.Id,incidentId=fault==null?null:fault.IncidentId,
    programVersion=fault==null?null:fault.ProgramVersion,entryPath=fault==null?null:fault.EntryPath,actualProcessImage=fault==null?System.Windows.Forms.Application.ExecutablePath:fault.ProcessImage,
    startupStage=fault==null?StartupHandoff.CurrentStage:fault.Stage,exceptionType=fault==null?null:fault.ExceptionType,originalException=fault==null?null:fault.Reason,
    dataLocationStatus=fault==null?null:fault.DataLocationStatus,fallbackRecord=fallback,packagePath=packagePath,
    diagnosticFailureType=packageFailure==null?null:packageFailure.GetType().FullName,diagnosticFailure=packageFailure==null?null:FaultSafety.Text(packageFailure.Message,800),
    emptyStoreCreated=createdEmptyStore,formalDataTouched=formalDataTouched,playerStarted=playerStarted,desktopChanged=false,
    elapsedMilliseconds=clock.ElapsedMilliseconds,normalProcessExitExpected=true
   };
   try{Directory.CreateDirectory(controlRoot);string output=Path.Combine(controlRoot,"startup-diagnostic-result.json");File.WriteAllText(output,Common.Json.Serialize(result),new UTF8Encoding(false));}catch{passed=false;}
  }
  return passed?0:2;
 }
}
