using System;using System.IO;using System.Linq;using System.Text;using System.Threading;using System.Collections.Generic;
internal sealed class NameRefreshResult {
 public Dictionary<string,NameEvidence> Names{get;set;}public string Reason{get;set;}public string[] Missing{get;set;}public int BodiesRead{get;set;}public NameReadDiagnostics Diagnostics{get;set;}
}
// Persist only the existing, allowlisted NameEvidence fields. No cache bodies/URLs/headers.
internal static class OfficialNameStore {
 internal static string FilePath{get{return Path.Combine(Common.Root,"data","official-names.json");}}
 static void Save(Dictionary<string,NameEvidence> names){string temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";try{Common.SaveNew(temp,names);if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);}finally{if(File.Exists(temp))File.Delete(temp);}}
 internal static bool Id(string s){return !String.IsNullOrEmpty(s)&&s.Length<=12&&s.All(c=>c>='0'&&c<='9');}
 internal static bool Valid(NameEvidence e,string id){return e!=null&&Id(id)&&e.Id==id&&OfficialNames.Clean(e.Name)==e.Name&&e.Kind=="official-local-wallpaper-response"&&PackageAnalysis.HashText(e.SourceSha256)&&PackageAnalysis.HashText(e.IndexSha256)&&e.SourceBytes>0&&e.SourceBytes<=2097152&&(e.ResponsePath=="/pcwallpapercontent/wallpaper/jingling/wallpaperListDynamic"||e.ResponsePath=="/pcwallpapercontent/wallpaper/jingling/queryCarouselWallpaperListByPage"||e.ResponsePath=="/pcwallpapercontent/wallpaper/jingling/detailDynamic");}
 internal static Dictionary<string,NameEvidence> Load(){var result=new Dictionary<string,NameEvidence>();if(!File.Exists(FilePath))return result;LocalImport.NoLinks(FilePath);if(new FileInfo(FilePath).Length>2097152)throw new InvalidDataException("已保存名称记录超预算，保留原文件");var saved=Common.Json.Deserialize<Dictionary<string,NameEvidence>>(Common.ReadShared(FilePath));if(saved==null||saved.Count>2048)throw new InvalidDataException("已保存名称记录不可用，保留原文件");foreach(var row in saved)if(Valid(row.Value,row.Key))result[row.Key]=row.Value;return result;}
 internal static NameRefreshResult Refresh(IEnumerable<string> ids,Action check){
  string[] wanted=ids.Where(Id).Distinct().Take(128).ToArray();check();if(wanted.Length==0)return new NameRefreshResult{Names=new Dictionary<string,NameEvidence>(),Missing=new string[0]};var saved=Load();var fresh=OfficialNames.ReadNames(wanted,check);string reason=OfficialNames.LastReason;int bodies=OfficialNames.BodiesRead;
  check();if(fresh.Count>0){string name="Local\\WallpaperOfficialNames-"+PackageAnalysis.Digest(Encoding.UTF8.GetBytes(Common.Root.ToUpperInvariant())).Substring(0,24);using(var mutex=new Mutex(false,name)){bool held=false;try{for(int i=0;i<20&&!held;i++){check();try{held=mutex.WaitOne(50);}catch(AbandonedMutexException){held=true;}}if(!held)throw new IOException("名称保存正忙，本轮保留原名，稍后重试");saved=Load();bool changed=false;foreach(var row in fresh){if(!Valid(row.Value,row.Key))continue;NameEvidence old;if(saved.TryGetValue(row.Key,out old)){if(old.Name!=row.Value.Name)reason="新旧官方名称冲突，保留已保存名称";continue;}if(saved.Count>=2048){reason="名称持久记录达到2048项预算";break;}saved[row.Key]=row.Value;changed=true;}if(changed){check();Directory.CreateDirectory(Path.GetDirectoryName(FilePath));Save(saved);}}finally{if(held)mutex.ReleaseMutex();}}}
  var names=saved.Where(x=>wanted.Contains(x.Key)).ToDictionary(x=>x.Key,x=>x.Value);return new NameRefreshResult{Names=names,Missing=wanted.Where(x=>!names.ContainsKey(x)).ToArray(),Reason=reason,BodiesRead=bodies,Diagnostics=OfficialNames.LastDiagnostics};
 }
 internal static void ApplySaved(List<ImportCandidate> list){try{Apply(list,Load());}catch{ /* Names must not change a completed structural check to failed. */ }}
 internal static void Apply(List<ImportCandidate> list,Dictionary<string,NameEvidence> names){foreach(var c in list){NameEvidence e;if(names.TryGetValue(c.Id,out e)&&Valid(e,c.Id)){if(c.NameSource!=null&&c.NameSource.Name!=e.Name)continue;c.Name=e.Name;c.NameSource=e;}}}
 internal static bool Placeholder(string name,string id,string hash,out string suffix){suffix="";if(PackageAnalysis.HashText(hash)&&name!=null){string s=" · "+hash.Substring(0,8);if(name.EndsWith(s,StringComparison.Ordinal)){suffix=s;name=name.Substring(0,name.Length-s.Length);}}return name=="本地壁纸 "+id||name=="本地场景 "+id;}
 internal static string ItemId(WallpaperItem item){return Id(item.ResourceId)?item.ResourceId:item.NameSource!=null&&Valid(item.NameSource,item.NameSource.Id)?item.NameSource.Id:null;}
 internal static bool Update(WallpaperItem item,NameEvidence e,Action<string,object> log){
  if(!Valid(e,e==null?null:e.Id)||!PackageAnalysis.HashText(item.RegisteredSha256))return false;string id=ItemId(item);if(id!=null&&id!=e.Id)return false;string suffix;
  if(!Placeholder(item.Name,e.Id,item.RegisteredSha256,out suffix)){if(log!=null)log("official-name-kept",new{catalogId=item.Id,resourceId=e.Id,reason=item.Name==e.Name?"existing meaningful name already matches":"existing meaningful or user-defined name preserved"});return false;}
  item.Name=e.Name+suffix;item.NameSource=e;return true;
 }
 internal static int ApplyCards(WallpaperLibrary library,Dictionary<string,NameEvidence> names,List<ImportCandidate> candidates,Action<string,object> log){
  var changed=new List<Tuple<WallpaperItem,string,NameEvidence>>();try{foreach(var item in library.Doc.Items){NameEvidence evidence=null;string id=ItemId(item);if(id!=null)names.TryGetValue(id,out evidence);
    if(evidence==null&&candidates!=null){var match=candidates.FirstOrDefault(c=>c.NameSource!=null&&Valid(c.NameSource,c.Id)&&(id==c.Id||id==null&&PackageAnalysis.HashText(item.RegisteredSha256)&&item.RegisteredSha256==(c.Check==null?c.CoverSourceSha256:c.Check.Sha256)));if(match!=null)evidence=match.NameSource;}
    if(evidence==null)continue;string oldName=item.Name;var oldSource=item.NameSource;if(Update(item,evidence,log))changed.Add(Tuple.Create(item,oldName,oldSource));}
   if(changed.Count>0)library.Save();
  }catch{foreach(var old in changed){old.Item1.Name=old.Item2;old.Item1.NameSource=old.Item3;}throw;}
  foreach(var row in changed)if(log!=null)log("official-name-updated",new{catalogId=row.Item1.Id,resourceId=row.Item1.NameSource.Id,name=row.Item1.Name,sourceFingerprint=row.Item1.NameSource.SourceSha256,sha256=row.Item1.RegisteredSha256,compatibilityUnchanged=true});return changed.Count;
 }
}
