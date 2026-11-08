using System;using System.IO;using System.Linq;using System.Drawing;using System.Drawing.Imaging;
internal static class ManagedCatalog {
 internal static WallpaperItem Commit(WallpaperLibrary library,ImportReceipt receipt,string operation,bool existingVerified,Action check,out bool duplicate){
  check();if(library.Error!=null)throw new InvalidOperationException(library.Error);var c=receipt.Candidate;
  if(!PackageAnalysis.HashText(receipt.Sha256)||c.Id==null||c.Id.Length>80||c.Id.Any(ch=>!Char.IsLetterOrDigit(ch)&&ch!='-'))throw new InvalidDataException("入库标识无效");
  string directory=Path.Combine(Common.Root,"library",c.Id,receipt.Sha256),target=Path.Combine(directory,c.Id+VideoPackage.ResourceExtension(receipt.Format)),payload=Path.Combine(operation,"payload");
  if(!LocalImport.Under(directory,Path.Combine(Common.Root,"library"))||!LocalImport.Under(operation,Common.WorkRoot))throw new InvalidDataException("入库目录边界无效");
  var old=library.Doc.Items.FirstOrDefault(entry=>entry.RegisteredSha256==receipt.Sha256);duplicate=old!=null;
  var x=old??new WallpaperItem{Id="import-"+c.Id+"-"+receipt.Sha256,Name=CleanName(c.Name,c.Id)+(library.Doc.Items.Any(i=>i.ResourceId==c.Id)?" · "+receipt.Sha256.Substring(0,8):""),NameSource=c.NameSource,Added=receipt.ImportedAt,RegisteredSha256=receipt.Sha256,OriginKind=c.Record==null?"local-tool-import":"official-local-import",ResourceId=c.Id};
  string source=x.SourcePath,cover=x.CoverPath,record=x.SourceRecordPath,latest=x.LatestImportRecordPath;bool moved=false,addedCover=false,wroteRegistration=false;
  try{
   if(!Directory.Exists(directory)){check();File.Move(Path.Combine(payload,"resource"+VideoPackage.ResourceExtension(receipt.Format)),Path.Combine(payload,c.Id+VideoPackage.ResourceExtension(receipt.Format)));Directory.CreateDirectory(Path.GetDirectoryName(directory));Directory.Move(payload,directory);moved=true;}
   else {
    if(!existingVerified||!File.Exists(target))throw new IOException("已有管理副本未核验，拒绝覆盖");
    // A formerly missing cover can be filled, but an existing user's cover is never replaced.
    string incoming=Path.Combine(payload,"cover.jpg"),owned=Path.Combine(directory,"cover.jpg");
    if(File.Exists(incoming)&&!File.Exists(owned)){File.Copy(incoming,owned,false);addedCover=true;}
   }
   check();x.SourcePath=target;x.SourceRecordPath=Path.Combine(directory,"source.json");if(File.Exists(Path.Combine(directory,"cover.jpg")))x.CoverPath=Path.Combine(directory,"cover.jpg");
   x.LatestImportRecordPath=Path.Combine(operation,"registration.json");Common.SaveNew(x.LatestImportRecordPath,new{itemId=x.Id,sourceId=c.Id,originalSource=c.Source,sha256=receipt.Sha256,bytes=receipt.Bytes,duplicate=duplicate,managedFile=target,autoPlayed=false,dependenciesVerified=false});
   wroteRegistration=true;if(old==null)library.Doc.Items.Add(x);library.Save();
  }catch{
   if(wroteRegistration)File.Delete(Path.Combine(operation,"registration.json"));x.SourcePath=source;x.CoverPath=cover;x.SourceRecordPath=record;x.LatestImportRecordPath=latest;if(old==null)library.Doc.Items.Remove(x);
   if(moved&&LocalImport.Under(directory,Path.Combine(Common.Root,"library")))Directory.Delete(directory,true);else if(addedCover)File.Delete(Path.Combine(directory,"cover.jpg"));throw;
  }
  library.SeedImportedHash(x,receipt.Sha256);return x;
 }
 internal static string CleanName(string name,string id){if(String.IsNullOrWhiteSpace(name))return "本地壁纸 "+id;string value=new string(name.Where(c=>!Char.IsControl(c)).ToArray());value=String.Join(" ",value.Split((char[])null,StringSplitOptions.RemoveEmptyEntries));return value.Length>120?value.Substring(0,120):value;}
 internal static WallpaperItem ImportLocal(string path,AnalysisReport report,string requestedId,string name,bool allowUnknown,Action check,NameEvidence nameSource,out bool duplicate){
  if(!report.ContainerValidated&&!allowUnknown)throw new InvalidDataException("容器结构未验证；如确需仅保存文件，明确使用 --allow-unverified");
  string id=String.IsNullOrEmpty(requestedId)?"file-"+report.Sha256.Substring(0,16):requestedId;
  if(id.Length>80||id.Any(c=>!Char.IsLetterOrDigit(c)&&c!='-'))throw new ArgumentException("资源ID只允许文字数字和短横线");
  string op=Path.Combine(Common.WorkRoot,"cli-"+Guid.NewGuid().ToString("N")),payload=Path.Combine(op,"payload");Directory.CreateDirectory(payload);
  try{
   string before=CoverCache.Signature(path);if(before!=report.SourceSignature)throw new InvalidDataException("分析后文件已经变化");
   string temp=Path.Combine(payload,"resource.cwp");using(var src=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var dst=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] buf=new byte[262144];int n;while((n=src.Read(buf,0,buf.Length))>0){check();if(dst.Length+n>report.Bytes)throw new InvalidDataException("复制中源文件增长，未入库");dst.Write(buf,0,n);}dst.Flush(true);}
   check();if(V3Package.HashFile(temp,check)!=report.Sha256||V3Package.HashFile(path,check)!=report.Sha256||CoverCache.Signature(path)!=before)throw new InvalidDataException("复制后源文件或临时文件哈希不符");
   if(report.ContainerValidated){var copy=PackageAnalysis.Analyze(temp,check,true,report.Sha256);if(!copy.ContainerValidated||copy.Family!=report.Family)throw new InvalidDataException("临时副本结构复核不符");}
   string cp=PackageAnalysis.CoverPath(report);if(cp!=null){if(Common.Hash(cp)!=report.CoverSha256)throw new InvalidDataException("封面缓存校验不符");using(var im=Image.FromFile(cp))im.Save(Path.Combine(payload,"cover.jpg"),ImageFormat.Jpeg);}
   var candidate=new ImportCandidate{Id=id,Name=CleanName(name,id),NameSource=nameSource,Source=path,Bytes=report.Bytes,Status=report.ContainerValidated?"可导入 · 结构校验通过":"容器结构未验证"};
   var receipt=new ImportReceipt{Candidate=candidate,Sha256=report.Sha256,Bytes=report.Bytes,Format=report.Family??"容器结构未验证",ImportedAt=DateTimeOffset.Now.ToString("o"),Cover=cp==null?null:"cover.jpg"};
   Common.SaveNew(Path.Combine(payload,"source.json"),receipt);PackageAnalysis.Reports(report,Path.Combine(payload,"analysis"));
   string existing=Path.Combine(Common.Root,"library",id,report.Sha256,id+".cwp");bool verified=File.Exists(existing)&&V3Package.HashFile(existing,check)==report.Sha256;
   string catalog=Path.Combine(Common.Root,"data","catalog.json");if(!File.Exists(catalog))Common.SaveNew(catalog,new LibraryDocument{Version=1,Items=new System.Collections.Generic.List<WallpaperItem>()});
   var library=new WallpaperLibrary();return Commit(library,receipt,op,verified,check,out duplicate);
  }finally{LocalImport.CleanPayload(op);}
 }
}
