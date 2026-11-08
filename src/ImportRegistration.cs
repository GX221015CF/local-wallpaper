using System;using System.IO;using System.Collections.Generic;
internal sealed partial class ControlWindow {
 WallpaperItem RegisterImport(ImportOperation operation,Dictionary<string,object> result,out bool duplicate){
  try{
   var receipt=Common.Json.Deserialize<ImportReceipt>(Common.Json.Serialize(result["receipt"]));
   Action check=()=>{if(closing||operation.Cancelled)throw new OperationCanceledException();};
   var x=ManagedCatalog.Commit(library,receipt,operation.Folder,result.ContainsKey("existingTargetVerified")&&Convert.ToBoolean(result["existingTargetVerified"]),check,out duplicate);
   LocalImport.CleanPayload(operation.Folder);SelectWallpaper(x);bool selectionSaved=selected==x;RefreshCards();libraryHint.Text=(duplicate?"相同内容已在列表中，未重复建卡。":"已导入。")+(selectionSaved?"已选中该条目，当前播放未改变。":"条目已保存，但选择未保存；保留原选择，当前播放未改变。");
   Write("local-import-registered",new{id=x.Id,sourceId=receipt.Candidate.Id,hash=receipt.Sha256,duplicate=duplicate,source=x.SourcePath,autoPlayed=false});return x;
  }catch(Exception original){try{LocalImport.CleanPayload(operation.Folder);}catch(Exception ex){throw new IOException("临时文件清理未确认："+ex.Message,original);}throw;}
 }
}
