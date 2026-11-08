using System;

internal sealed class EmptyStateCopy {
 internal string Lead;
 internal string Source;
 internal string Data;
 internal string Playback;
}

internal static class EmptyStatePresentation {
 internal static EmptyStateCopy Create(SourceRootState source,bool playbackAvailable,bool playbackChecking,string dataRoot) {
  var copy=new EmptyStateCopy();
  if(source==null) {
   copy.Source="壁纸来源：正在检查已知的本机缓存位置。";
   copy.Lead="你也可以先添加已有的 CWP 文件；来源设置不影响本软件数据和播放组件。";
  } else if(source.Found) {
   copy.Source=(source.SavedCustom?"壁纸来源：已找到自选位置 · ":"壁纸来源：已找到本机位置 · ")+(source.Root??"");
   copy.Lead="已找到本机壁纸来源。可查看候选后再决定导入；程序不会自动导入或播放。";
  } else if(source.SavedCustom) {
   copy.Source="壁纸来源：已保存的位置当前不可用；可重新选择。"+(String.IsNullOrWhiteSpace(source.Error)?"":" "+source.Error);
   copy.Lead="来源暂不可用也不影响你添加已有的 CWP 文件；修复来源位置不会改动本软件数据。";
  } else {
   copy.Source="壁纸来源：尚未找到符合已知布局的本机缓存。";
   copy.Lead="可添加已有的 CWP 文件，或稍后浏览本机壁纸来源；这里不会扫描其他磁盘。";
  }
  copy.Data="本软件数据："+(String.IsNullOrWhiteSpace(dataRoot)?"位置尚未确定":dataRoot)+"。导入会复制壁纸文件到此处，可能占用较多空间。";
  copy.Playback=playbackChecking
   ?"播放组件：正在核验本机组件身份；完成前预览和设为桌面不可用。"
   :playbackAvailable
    ?"播放组件：身份已核验；实际加载和播放尚未验证。"
    :"播放组件：尚未准备；仍可管理壁纸列表，预览和设为桌面不可用。";
  return copy;
 }
}
