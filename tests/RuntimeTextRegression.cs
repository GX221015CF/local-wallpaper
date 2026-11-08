#if TEST_BUILD
using System;using System.IO;using System.Collections.Generic;using System.Web.Script.Serialization;
internal static class RuntimeTextRegression {
 static readonly List<object> results=new List<object>();static bool passed=true;
 static void Check(string name,bool ok,string actual){results.Add(new{name=name,passed=ok,actual=actual});if(!ok)passed=false;}
 static int Main(string[] args){
  if(args.Length!=1)return 2;
  try{
   const string stale="本机播放组件已核验；仅组件身份已检查，尚未加载或播放。";
   string text=RuntimePresentation.Runtime(true,false,false,"checked",stale,true,"mounted","desktop");Check("desktop-live-must-not-say-not-loaded",text.Contains("桌面")&&!text.Contains("尚未加载或播放"),text);
   text=RuntimePresentation.CoverUnavailable("mounted","legacy");Check("cover-action-scope-must-not-deny-desktop",text.Contains("封面")&&!text.Contains("桌面模式不可用"),text);
   text=RuntimePresentation.Runtime(true,false,false,"checked",stale,true,"preview","preview");Check("preview-current-state",text.Contains("窗口预览")&&!text.Contains("尚未加载"),text);
   text=RuntimePresentation.Runtime(true,false,false,"checked",stale,true,"engine-started","preview");Check("engine-start-not-automatic-frame-proof",text.Contains("人工确认"),text);
   text=RuntimePresentation.Runtime(true,false,false,"checked",stale,true,"stopping","desktop");Check("stopping-status",text.Contains("停止")&&!text.Contains("尚未加载"),text);
   text=RuntimePresentation.Runtime(true,false,false,"checked",stale,false,"exited","desktop");Check("stopped-not-historical-never-loaded",text.Contains("当前未播放")&&!text.Contains("尚未加载"),text);
   text=RuntimePresentation.Runtime(true,false,false,"checked",null,false,"idle",null);Check("identity-only-idle",text.Contains("身份核验")&&text.Contains("当前未播放"),text);
   text=RuntimePresentation.Runtime(false,false,false,null,null,false,"idle",null);Check("missing-components",text.Contains("尚未准备好"),text);
   text=RuntimePresentation.Runtime(true,true,false,"checked",stale,true,"mounted","desktop");Check("checking-with-live-desktop",text.Contains("核对")&&text.Contains("桌面"),text);
   text=RuntimePresentation.Runtime(true,true,true,"checked",stale,true,"preview","preview");Check("cancel-check-does-not-hide-playback",text.Contains("取消")&&text.Contains("预览"),text);
   text=RuntimePresentation.Runtime(true,false,false,"checked","已取消本次检查；保留配置",false,"idle",null);Check("cancelled-selection-notice-preserved",text.Contains("已取消")&&text.Contains("保留配置"),text);
   text=RuntimePresentation.CoverUnavailable("idle","未建立预览");Check("no-preview-cover-reason-scoped",text.Contains("封面截图")&&text.Contains("未建立预览"),text);
   text=RuntimePresentation.Runtime(true,false,false,"checked",stale,true,"starting","desktop");Check("starting-not-falsely-running",text.Contains("正在启动")&&!text.Contains("正在运行"),text);
   Check("unrelated-diagnostic-not-overwritten",!RuntimePresentation.MayRefresh(false,false,true,"导入失败","组件就绪"),"refresh ownership guard");
   Check("data-fault-not-overwritten",!RuntimePresentation.MayRefresh(false,true,false,"数据损坏","组件就绪"),"data error precedence");
   Check("periodic-owned-status-refreshes",RuntimePresentation.MayRefresh(false,false,true,stale,stale),"owned status refresh");
   Check("explicit-refresh-claims-status",RuntimePresentation.MayRefresh(false,false,false,"other","old"),"explicit refresh");
   Check("disposed-control-not-refreshed",!RuntimePresentation.MayRefresh(true,false,false,"old","old"),"disposed guard");
  }catch(Exception ex){passed=false;results.Add(new{error=ex.ToString()});}
  File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(new{passed=passed,cases=results,scope="actual presentation formatter called by UI; no Form constructed; does not prove pixels or live refresh",guiCreated=false,engineLoaded=false}));return passed?0:1;
 }
}
#endif
