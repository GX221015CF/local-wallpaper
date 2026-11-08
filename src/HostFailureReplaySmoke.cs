#if TEST_BUILD
using System;using System.IO;using System.Diagnostics;
internal static class HostFailureReplaySmoke {
 internal static int Run(string suite){
  suite=Path.GetFullPath(suite);if(!LocalImport.Under(suite,Path.Combine(Common.AppRoot,"tests")))return 2;
  try{
   var failure=PlaybackFailure.ReadHost(suite);
   if(failure==null||failure.Stage!="engine-failed"||!failure.Error.Contains("运行组件契约数量不符")||!failure.Message.Contains("运行组件契约数量不符"))throw new InvalidDataException("真实R18失败正文或阶段未传递");
   var fault=FaultSafety.Sanitize(new FaultRecord{SchemaVersion=1,Id="diag-20261106-000000-000000000019",Category=FaultSafety.Category("start"),Operation="启动壁纸",Stage=failure.Stage,Reason=failure.Error});
   if(!fault.Reason.Contains("EngineCore.VerifyExternal")||fault.Stage!="engine-failed")throw new InvalidDataException("诊断丢失具体阶段或调用位置");
   if(!PlaybackFailure.KeepPrimary(fault,true,"failed-cleaned")||PlaybackFailure.KeepPrimary(fault,false,"failed-cleaned")||PlaybackFailure.KeepPrimary(fault,true,"preview"))throw new InvalidDataException("核心故障与非阻断补充事件的优先级不正确");
   Common.SaveNew(Path.Combine(suite,"replay-result.json"),new{passed=true,method="captured R18 host.jsonl through production PlaybackFailure and FaultSafety processors",primaryFailurePriorityPassed=true,stage=failure.Stage,message=failure.Message,error=fault.Reason,pid=Process.GetCurrentProcess().Id,guiCreated=false,nativeOpenCalled=false,playerStarted=false});return 0;
  }catch(Exception ex){Common.SaveNew(Path.Combine(suite,"replay-failed.json"),new{passed=false,error=ex.ToString()});return 1;}
 }
}
#endif
