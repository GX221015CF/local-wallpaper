using System;

internal static class EmptyStateSmoke {
 static void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 internal static int Run(string output){
  try {
   var missing=EmptyStatePresentation.Create(new SourceRootState{Found=false,SavedCustom=false,Error="未发现"},false,false,"隔离根/LocalWallpaper/Data");
   Need(missing.Source.Contains("尚未找到")&&!missing.Source.Contains("播放组件"),"来源缺失状态与播放组件混淆");
   Need(missing.Lead.Contains("CWP")&&missing.Lead.Contains("不会扫描其他磁盘"),"无来源时没有提供有效替代路径或范围说明");
   Need(missing.Data.Contains("隔离根/LocalWallpaper/Data")&&missing.Data.Contains("占用较多空间"),"数据位置或导入空间说明缺失");
   Need(missing.Playback.Contains("尚未准备")&&missing.Playback.Contains("不可用"),"播放组件缺失状态不准确");
   var found=EmptyStatePresentation.Create(new SourceRootState{Found=true,SavedCustom=true,Root="缓存 根",RecordFiles=3},false,false,"data root");
   Need(found.Source.Contains("已找到自选位置")&&found.Source.Contains("缓存 根"),"自选来源状态未显示实际位置");
   Need(found.Lead.Contains("不会自动导入或播放"),"发现提示承诺了自动导入或播放");
   var broken=EmptyStatePresentation.Create(new SourceRootState{Found=false,SavedCustom=true,Root="旧缓存 根",Error="目录缺失"},false,false,"data root");
   Need(broken.Source.Contains("已保存的位置当前不可用")&&broken.Source.Contains("目录缺失"),"失效的自选来源未展示原因");
   var checking=EmptyStatePresentation.Create(null,false,true,"data root");
   Need(checking.Source.Contains("正在检查")&&checking.Playback.Contains("正在核验"),"启动检查阶段未清楚显示");
   var ready=EmptyStatePresentation.Create(new SourceRootState{Found=false,SavedCustom=false},true,false,"data root");
   Need(ready.Playback.Contains("身份已核验")&&ready.Playback.Contains("尚未验证"),"身份核验与实际播放测试的结论混淆");
   Common.SaveNew(output,new{passed=true,scope="empty-list user guidance copy/model only",cases=new[]{"no-known-source","custom-source-found","saved-source-unavailable","source-checking","runtime-missing","runtime-identity-checked"},playerStarted=false,desktopChanged=false,formalDataTouched=false});
   return 0;
  } catch(Exception ex){try{Common.SaveNew(output,new{passed=false,error=FaultSafety.Text(ex.GetType().Name+"："+ex.Message),playerStarted=false,desktopChanged=false,formalDataTouched=false});}catch{}return 1;}
 }
}
