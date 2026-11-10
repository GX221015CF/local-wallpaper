using System;
using System.IO;

internal static class ResourcePresentation {
 internal static string Id(string resourceId,string catalogId){return OfficialNameStore.Id(resourceId)?"ID "+resourceId:"本地条目 "+(catalogId??"未登记");}
 internal static string Id(WallpaperItem item){return item==null?"尚未选择":Id(item.ResourceId??(item.NameSource==null?null:item.NameSource.Id),item.Id);}
 internal static string Card(WallpaperItem item,string status){
  string compact=(status??"").Replace(" · 桌面未验证","");
  return compact+" · "+Id(item);
 }
 internal static string FailureMaterials(string run){
  string host="宿主材料状态待确认",input="运行副本状态待确认";
  try{
   string path=Path.Combine(run,"host-stage-result.json");
   if(File.Exists(path)){
    var stage=Common.Read(path);
    host=stage.ContainsKey("stagingCleaned")&&Convert.ToBoolean(stage["stagingCleaned"])?"宿主暂存已回收":stage.ContainsKey("stagingRetained")&&Convert.ToBoolean(stage["stagingRetained"])?"宿主暂存仍保留，待核对":"宿主提交状态见诊断";
   }
   string resultPath=Path.Combine(run,"result.json");
   if(File.Exists(resultPath)&&Convert.ToBoolean(Common.Read(resultPath)["hostResourcesCleaned"]))host="宿主运行材料已回收";
  }catch{host="宿主材料清理待确认";}
  try{
   string path=Path.Combine(run,"runtime-input-cleanup.json");
   if(File.Exists(path)){
    string state=Common.S(Common.Read(path),"state");
    input=state=="reclaimed"?"运行副本已回收":state=="retained-failure-budget"?"故障运行副本按预算保留":state.StartsWith("retained")?"运行副本仍保留，原因见诊断":"运行副本回收状态待确认";
   }
  }catch{input="运行副本清理待确认";}
  return "播放任务已结束；"+host+"；"+input+"。";
 }
}
