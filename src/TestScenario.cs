// Optional integration harness. All actions use the same visible Button.PerformClick handlers.
// No external input injection, Explorer automation or private Lenovo protocol.
using System;using System.IO;using System.Threading.Tasks;using System.Collections;using System.Diagnostics;
internal static class TestScenario {
 internal static async Task Run(ControlWindow ui,string path){
  Exception failure=null;try{if(Common.Read(path).ContainsKey("dataHome")){await ui.RunDataHomeUiSmoke();return;}if(Common.Read(path).ContainsKey("submit")){await ui.RunSubmitSmoke();return;}if(Common.Read(path).ContainsKey("diagnosticTimeout")){await ui.RunDiagnosticTimeoutSmoke();return;}if(Common.Read(path).ContainsKey("diagnostic")){await ui.RunDiagnosticSmoke();return;}
   if(Common.Read(path).ContainsKey("nameObserve")){await ui.RunNameObserveSmoke();return;}if(Common.Read(path).ContainsKey("nameFlow")){await ui.RunNameFlowSmoke();return;}
   var workflowSpec=Common.Read(path);if(workflowSpec.ContainsKey("previewCandidate")){await ui.RunPreviewCandidateSmoke();return;}if(workflowSpec.ContainsKey("previewControls")){await ui.RunPreviewControlsSmoke();return;}if(workflowSpec.ContainsKey("previewCoverStore")){await ui.RunPreviewCoverStoreSmoke();return;}if(workflowSpec.ContainsKey("previewCover")){await ui.RunPreviewCoverSmoke();return;}if(workflowSpec.ContainsKey("workflowUi")){await ui.RunWorkflowUi();return;}
   var spec=Common.Read(path);if(spec.ContainsKey("dailyFlow")){await ui.RunDailyFlowSmoke();return;}if(spec.ContainsKey("discovery")){await ui.RunDiscoverySmoke(Common.S(spec,"discovery"));return;}if(spec.ContainsKey("dailyPolish")){await ui.RunDailyPolishSmoke(Common.S(spec,"dailyPolish"));return;}if(spec.ContainsKey("names")){await ui.RunNameSmoke(Common.S(spec,"names"));return;}if(spec.ContainsKey("cover366396")){await ui.RunCover366Smoke(Common.S(spec,"cover366396"));return;}if(spec.ContainsKey("covers")){await ui.RunCoverSmoke(Common.S(spec,"covers"));return;}if(spec.ContainsKey("localImport")){await ui.RunImportSmoke(Common.S(spec,"localImport"));return;}if(spec.ContainsKey("movieIntegration")){await ui.RunMovieIntegration();return;}if(spec.ContainsKey("movieDesktop")){await ui.RunMovieDesktop();return;}if(spec.ContainsKey("offline")){await ui.RunOfflineCheck();return;}ui.Write("scenario-start",new{path=path});
   foreach(object item in (IEnumerable)spec["steps"]){var step=Common.Obj(item);if(step.ContainsKey("register"))ui.AcceptAddedFile(Common.S(step,"register"));if(step.ContainsKey("cover"))ui.AcceptCoverFile(Common.S(step,"cover"));if(step.ContainsKey("selectPath"))ui.SelectRegisteredPath(Common.S(step,"selectPath"));if(step.ContainsKey("observe"))ui.ObserveGallery(Common.S(step,"observe"));if(step.ContainsKey("guardAttempt"))await ui.TestStartGuard(Common.S(step,"guardAttempt"));if(step.ContainsKey("action"))ui.Trigger(Common.S(step,"action"));if(step.ContainsKey("delayMs"))await Task.Delay(Convert.ToInt32(step["delayMs"]));
    #if DEBUG_FAULTS
    if(step.ContainsKey("debugUiStallMs")){ui.Write("debug-ui-stall",new{milliseconds=Convert.ToInt32(step["debugUiStallMs"]),debugBuildOnly=true});System.Threading.Thread.Sleep(Convert.ToInt32(step["debugUiStallMs"]));}
#endif
    if(step.ContainsKey("until")){string state=Common.S(step,"until");var sw=Stopwatch.StartNew();int timeout=step.ContainsKey("timeoutMs")?Convert.ToInt32(step["timeoutMs"]):15000;while(ui.State!=state){if(sw.ElapsedMilliseconds>timeout||ui.State=="failed"&&state!="failed")throw new Exception("Expected "+state+", observed "+ui.State);await Task.Delay(100);}}
   }
   ui.Write("scenario-passed",new{path=path});ui.Trigger("exit");
  }catch(Exception ex){failure=ex;}
  if(failure!=null){ui.Write("scenario-failed",new{path=path,error=failure.ToString()});ui.Trigger("stop");await Task.Delay(1500);ui.Trigger("exit");}
 }
}
