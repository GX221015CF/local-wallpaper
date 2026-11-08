using System;using System.IO;using System.Drawing;using System.Collections.Generic;using System.Linq;using System.Text;
internal sealed class WallpaperItem {
 public NameEvidence NameSource{get;set;}public string LatestImportRecordPath{get;set;}public string Id{get;set;}public string Name{get;set;}public string SourcePath{get;set;}public string CoverPath{get;set;}public string Added{get;set;}public string RegisteredSha256{get;set;}public string PreviewConfirmedSha256{get;set;}public string PreviewConfirmationRun{get;set;}public string PreviewConfirmationTime{get;set;}public string PreviewConfirmationMethod{get;set;}public string DesktopConfirmedSha256{get;set;}public string ResourceId{get;set;}public string OriginKind{get;set;}public string SourceRecordPath{get;set;}public string PreviewConfirmationSource{get;set;}public string DesktopVerificationRun{get;set;}public string DesktopVerificationTime{get;set;}public string DesktopVerificationMethod{get;set;}public string DesktopVerificationRecord{get;set;}
}
internal sealed class LibraryDocument {public int Version{get;set;}public string SelectedId{get;set;}public List<WallpaperItem> Items{get;set;}}
internal sealed class WallpaperLibrary {
 internal readonly string Data=Path.Combine(Common.Root,"data");internal string Error;internal LibraryDocument Doc;sealed class Hint{internal long Bytes,Ticks;internal string Hash,PolicyError,Signature,RuleStamp;}readonly Dictionary<string,Hint> hints=new Dictionary<string,Hint>(StringComparer.OrdinalIgnoreCase);internal long HintHits;
 readonly Dictionary<string,Hint> persistedHints=new Dictionary<string,Hint>(StringComparer.OrdinalIgnoreCase);
 string StatusCachePath{get{return Path.Combine(Data,"status-cache.json");}}
 void LoadStatusCache(){try{if(File.Exists(StatusCachePath)&&new FileInfo(StatusCachePath).Length<1048576){var rows=Common.Json.Deserialize<Dictionary<string,string[]>>(Common.ReadShared(StatusCachePath));foreach(var row in rows)if(row.Value.Length==2&&row.Value[1].Length==64)persistedHints[row.Key]=new Hint{Signature=row.Value[0],Hash=row.Value[1]};}}catch{}}
 void SaveStatusCache(){try{var rows=new Dictionary<string,string[]>();foreach(var row in persistedHints)rows[row.Key]=new[]{row.Value.Signature,row.Value.Hash};Common.ReplaceJson(StatusCachePath,rows);}catch{}}
 internal string DisplayHash(WallpaperItem x){
  var f=new FileInfo(x.SourcePath);string sig=CoverCache.Signature(x.SourcePath);Hint h;
  string rules=PackageAnalysis.RuleStamp();bool inMemory=hints.TryGetValue(x.SourcePath,out h)&&h.Signature==sig;
  if(inMemory&&h.RuleStamp==rules){HintHits++;PreviewCovers.NoteIdentity(x.SourcePath,h.Hash,sig);return h.Hash;}
  string value;if(inMemory)value=h.Hash;else if(persistedHints.TryGetValue(x.SourcePath,out h)&&h.Signature==sig){HintHits++;value=h.Hash;}else value=Common.Hash(x.SourcePath);
  string error=null;try{CwpPolicy.Validate(x.SourcePath,value);}catch(Exception ex){error=ex.Message;}
  h=new Hint{Bytes=f.Length,Ticks=f.LastWriteTimeUtc.Ticks,Signature=sig,Hash=value,PolicyError=error,RuleStamp=rules};hints[x.SourcePath]=h;persistedHints[x.SourcePath]=h;SaveStatusCache();PreviewCovers.NoteIdentity(x.SourcePath,value,sig);return value;
 }
 internal void SeedImportedHash(WallpaperItem x,string hash){var f=new FileInfo(x.SourcePath);string error=null;try{CwpPolicy.Validate(x.SourcePath,hash);}catch(Exception ex){error=ex.Message;}var h=new Hint{Bytes=f.Length,Ticks=f.LastWriteTimeUtc.Ticks,Signature=CoverCache.Signature(x.SourcePath),Hash=hash,PolicyError=error};hints[x.SourcePath]=h;persistedHints[x.SourcePath]=h;SaveStatusCache();PreviewCovers.NoteIdentity(x.SourcePath,hash,h.Signature);}
 internal bool CanMountHint(WallpaperItem x){if(!CanPreview(x))return false;try{return CanMountExact(x,DisplayHash(x));}catch{return false;}}
 internal bool CanMountExact(WallpaperItem x,string hash){return !String.IsNullOrEmpty(hash)&&CanPreview(x)&&(IsBuiltin(x)&&hash==Common.CwpHash||x.PreviewConfirmedSha256==hash);}
 internal string FilePath{get{return Path.Combine(Data,"catalog.json");}}
 internal WallpaperLibrary(){
  Directory.CreateDirectory(Data);LoadStatusCache();Directory.CreateDirectory(Path.Combine(Data,"covers"));
  if(File.Exists(FilePath)){try{Doc=Common.Json.Deserialize<LibraryDocument>(Common.ReadShared(FilePath));if(Doc==null||Doc.Items==null||Doc.Version!=1)throw new InvalidDataException("登记数据格式不受支持");foreach(var x in Doc.Items){x.SourcePath=Path.GetFullPath(x.SourcePath);if(String.IsNullOrEmpty(x.Id))throw new InvalidDataException("条目缺少标识");}}catch(Exception ex){Doc=null;Error="本地登记数据读取失败，原文件未覆盖："+ex.Message;}}
  if(Doc==null&&DataLocation.Ready)throw new InvalidDataException(Error??"正式登记缺失，未创建空库");
  if(Doc==null){Doc=new LibraryDocument{Version=1,SelectedId="builtin-317495",Items=new List<WallpaperItem>{new WallpaperItem{Id="builtin-317495",Name="雨中静默少女",SourcePath=Common.DefaultCwp,CoverPath=Path.Combine(Common.Root,"assets","rain317495-cover.jpg"),Added=DateTimeOffset.Now.ToString("o"),RegisteredSha256=Common.CwpHash}}};if(Error==null)Save();}
 }
 internal WallpaperItem Selected{get{return Doc.Items.FirstOrDefault(x=>x.Id==Doc.SelectedId)??Doc.Items.FirstOrDefault();}}
 internal bool IsBuiltin(WallpaperItem x){return x!=null&&x.Id=="builtin-317495"&&(String.Equals(Path.GetFullPath(x.SourcePath),Common.DefaultCwp,StringComparison.OrdinalIgnoreCase)||String.Equals(Path.GetFullPath(x.SourcePath),Path.Combine(Common.Root,"library","317495",Common.CwpHash,"317495.cwp"),StringComparison.OrdinalIgnoreCase));}
 internal string Status(WallpaperItem x){
  if(x==null)return "未选择";if(!File.Exists(x.SourcePath))return "文件不存在";
  try{string h=DisplayHash(x);if(hints[x.SourcePath].PolicyError!=null)return "格式暂不支持";return IsBuiltin(x)?(h==Common.CwpHash?"已验证":"播放待确认"):(x.PreviewConfirmedSha256==h?(x.DesktopConfirmedSha256==h?"窗口已确认 · 桌面已验证":"窗口已确认 · 桌面未验证"):(x.OriginKind=="official-local-import"?"已导入 · 播放未验证":"播放待确认"));}catch{return "无法读取";}
 }
 internal bool CanPlay(WallpaperItem x){return CanPreview(x);}
 internal string FormatError(WallpaperItem x){try{DisplayHash(x);return hints[x.SourcePath].PolicyError;}catch(Exception ex){return ex.Message;}}
 internal bool IsPreviewConfirmedHint(WallpaperItem x){if(x==null)return false;try{string h=DisplayHash(x);return IsBuiltin(x)&&h==Common.CwpHash||x.PreviewConfirmedSha256==h;}catch{return false;}}
 internal bool CanPreview(WallpaperItem x){return x!=null&&File.Exists(x.SourcePath)&&(String.Equals(Path.GetExtension(x.SourcePath),".cwp",StringComparison.OrdinalIgnoreCase)||VideoPackage.Extension(x.SourcePath))&&Error==null&&FormatError(x)==null;}
 internal bool CanMount(WallpaperItem x){if(!CanPreview(x))return false;try{string h=Common.Hash(x.SourcePath);return IsBuiltin(x)&&h==Common.CwpHash||x.PreviewConfirmedSha256==h;}catch{return false;}}
 internal void ConfirmPreview(WallpaperItem x,string hash,string run,string method){
  if(x==null||!Doc.Items.Contains(x)||x.RegisteredSha256!=hash)throw new InvalidDataException("确认不属于库内该条目及已登记资源，未保存");
  if(Common.Hash(x.SourcePath)!=hash)throw new InvalidDataException("源文件已变化，本次确认失效");
  string old=x.PreviewConfirmedSha256,oldRun=x.PreviewConfirmationRun,oldTime=x.PreviewConfirmationTime,oldMethod=x.PreviewConfirmationMethod;
  x.PreviewConfirmedSha256=hash;x.PreviewConfirmationRun=run;x.PreviewConfirmationTime=DateTimeOffset.Now.ToString("o");x.PreviewConfirmationMethod=method;
  try{Save();}catch{x.PreviewConfirmedSha256=old;x.PreviewConfirmationRun=oldRun;x.PreviewConfirmationTime=oldTime;x.PreviewConfirmationMethod=oldMethod;throw;}
 }
 internal WallpaperItem Add(string path,out bool duplicate){
  if(Error!=null)throw new InvalidOperationException(Error);path=Path.GetFullPath(path);if(!File.Exists(path))throw new FileNotFoundException("所选文件不存在",path);if(!String.Equals(Path.GetExtension(path),".cwp",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("本轮只登记本地CWP文件");
  var old=Doc.Items.FirstOrDefault(x=>String.Equals(x.SourcePath,path,StringComparison.OrdinalIgnoreCase));duplicate=old!=null;if(duplicate){Select(old);return old;}
  var item=new WallpaperItem{Id=Guid.NewGuid().ToString("N"),Name=Path.GetFileNameWithoutExtension(path),SourcePath=path,Added=DateTimeOffset.Now.ToString("o"),CoverPath=null,RegisteredSha256=Common.Hash(path)};string hints=Path.Combine(Data,"known-metadata.json");if(File.Exists(hints)){var known=Common.Read(hints);if(known.ContainsKey(item.RegisteredSha256)){var info=Common.Obj(known[item.RegisteredSha256]);item.Name=Common.S(info,"name");item.CoverPath=Common.S(info,"cover");}}
  string previous=Doc.SelectedId;Doc.Items.Add(item);Doc.SelectedId=item.Id;try{Save();}catch{Doc.Items.Remove(item);Doc.SelectedId=previous;throw;}return item;
 }
 internal void SetCover(WallpaperItem item,string source){
  if(Error!=null)throw new InvalidOperationException(Error);if(new FileInfo(source).Length>20971520)throw new InvalidDataException("封面请小于20MB");
  using(var im=Image.FromFile(source)){if((long)im.Width*im.Height>40000000)throw new InvalidDataException("封面像素过大");}
  string ext=Path.GetExtension(source).ToLowerInvariant();if(!new[]{".jpg",".jpeg",".png",".bmp"}.Contains(ext))throw new InvalidDataException("请选择JPG、PNG或BMP封面");
  string copy=Path.Combine(Data,"covers",Guid.NewGuid().ToString("N")+ext);File.Copy(source,copy,false);item.CoverPath=copy;Save();
 }
 internal string Remove(WallpaperItem item){
  if(Error!=null)throw new InvalidOperationException(Error);if(item==null||!Doc.Items.Contains(item))throw new InvalidOperationException("所选壁纸已不在列表中");
  int index=Doc.Items.IndexOf(item);string priorSelected=Doc.SelectedId;string archive=Path.Combine(Data,"removed-items");Directory.CreateDirectory(archive);string record=Path.Combine(archive,Guid.NewGuid().ToString("N")+".json");
  Common.SaveNew(record,new{removedAt=DateTimeOffset.Now.ToString("o"),scope="catalog-entry-only; all resources and historical confirmations retained",item=item});
  Doc.Items.RemoveAt(index);if(Doc.SelectedId==item.Id)Doc.SelectedId=Doc.Items.Count==0?null:Doc.Items[Math.Min(index,Doc.Items.Count-1)].Id;
  try{Save();}catch{Doc.Items.Insert(index,item);Doc.SelectedId=priorSelected;throw;}return record;
 }
 internal void Select(WallpaperItem x){if(Error!=null)throw new InvalidOperationException(Error);if(x==null||!Doc.Items.Contains(x))throw new InvalidDataException("所选壁纸不属于当前登记");string old=Doc.SelectedId;Doc.SelectedId=x.Id;try{Save();}catch{Doc.SelectedId=old;throw;}}
 internal void Save(){
#if TEST_BUILD
  if(File.Exists(Path.Combine(Common.Root,"tests","fail-catalog-save")))throw new IOException("TEST_BUILD隔离故障注入：catalog保存失败");
#endif
  string temp=Path.Combine(Data,"catalog-"+Guid.NewGuid().ToString("N")+".tmp");try{File.WriteAllText(temp,Common.Json.Serialize(Doc),new UTF8Encoding(false));if(File.Exists(FilePath))File.Replace(temp,FilePath,Path.Combine(Data,"catalog.backup.json"),true);else File.Move(temp,FilePath);}finally{if(File.Exists(temp))File.Delete(temp);}}
 internal static Image LoadCover(WallpaperItem x){try{if(x==null)return null;string path=x.CoverPath;if(String.IsNullOrEmpty(path)||!File.Exists(path)){path=CachedCoverPath(x);}if(String.IsNullOrEmpty(path)||!File.Exists(path)){var screenshot=PreviewCovers.ForItem(x);path=screenshot==null?null:PreviewCovers.ImagePath(screenshot);}if(String.IsNullOrEmpty(path)||!File.Exists(path))path=VideoCovers.ForItem(x);if(String.IsNullOrEmpty(path)||!File.Exists(path))return null;using(var s=File.OpenRead(path))using(var image=Image.FromStream(s))return new Bitmap(image);}catch{return null;}}
 static string CachedCoverPath(WallpaperItem x){try{string hash=x.RegisteredSha256;if(String.IsNullOrEmpty(hash)||hash.Length!=64||hash.Any(c=>!Uri.IsHexDigit(c))||String.IsNullOrEmpty(x.SourcePath)||!File.Exists(x.SourcePath))return null;if(!PreviewCovers.IdentityMatches(x))return null;string folder=Path.Combine(Common.Root,"data","cover-cache",hash);string meta=Path.Combine(folder,"cover.json");string thumb=Path.Combine(folder,"thumbnail.jpg");if(!File.Exists(meta)||new FileInfo(meta).Length>65536||!File.Exists(thumb)||new FileInfo(thumb).Length>1048576)return null;CoverBinding b=Common.Json.Deserialize<CoverBinding>(Common.ReadShared(meta));if(b==null||b.Version!=1||!String.Equals(b.Sha256,hash,StringComparison.OrdinalIgnoreCase)||String.IsNullOrEmpty(b.ThumbnailSha256)||!String.Equals(Common.Hash(thumb),b.ThumbnailSha256,StringComparison.OrdinalIgnoreCase))return null;return thumb;}catch{return null;}}
}
