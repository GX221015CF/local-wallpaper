using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Security.Cryptography;

internal sealed class RuntimeLocationRecord { public int SchemaVersion{get;set;} public string Root{get;set;} public string SavedAt{get;set;} }
internal static class RuntimeProvisioning {
 internal static bool Available {get;private set;}
 internal static string Root {get;private set;}
 internal static string Message {get;private set;}
 internal static string CheckedAt {get;private set;}
 static long componentFilesHashed;
 internal static long ComponentFilesHashed {get{return System.Threading.Interlocked.Read(ref componentFilesHashed);}}
 static readonly object SaveGate=new object();
 static RuntimeProvisioning(){Message="播放组件尚未准备好；预览和桌面功能不可用。点击此处选择本机已有组件目录并检查。";}
 internal static bool HasSavedLocation { get { return File.Exists(ConfigPath) || Directory.Exists(ConfigPath); } }
 static string ConfigPath {get{
#if TEST_BUILD
  string fixture=Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_RUNTIME_CONFIG");if(fixture!=null){fixture=Path.GetFullPath(fixture);string isolation=Path.GetFullPath(Path.Combine(Common.AppRoot,"..","tests","isolation"));if(!LocalImport.Under(fixture,isolation)||Path.GetFileName(fixture)!="runtime-location.json")throw new InvalidDataException("测试组件配置必须位于tests/isolation");return fixture;}
#endif
  return Path.Combine(Common.Root,"data","runtime-location.json");}}
 const string HostName="Desktop317495Host-refined.exe";
 static string ContractPath(){string p=Path.Combine(Common.AppRoot,"rules","runtime-components.json");
#if TEST_BUILD
  if(!File.Exists(p)){string parent=Path.GetFullPath(Path.Combine(Common.AppRoot,".."));string candidate=Path.Combine(parent,"rules","runtime-components.json");if(!LocalImport.Under(candidate,parent))throw new InvalidDataException("测试组件契约路径越界");p=candidate;}
#endif
  return p;}
 #if TEST_BUILD
 internal static Dictionary<string,string> TestContractRows;
#endif
 static Dictionary<string,string> ContractRows(){
#if TEST_BUILD
  if(TestContractRows!=null)return new Dictionary<string,string>(TestContractRows,StringComparer.OrdinalIgnoreCase);
#endif
  string p=ContractPath();LocalImport.NoLinks(p);
  if(Common.Hash(p)!=RuntimeDelivery.ContractHash)throw new InvalidDataException("程序内组件契约身份不符");
  var doc=Common.Read(p);var rows=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(object obj in (System.Collections.IEnumerable)doc["files"]){var row=Common.Obj(obj);string name=Common.S(row,"name"),hash=Common.S(row,"sha256");if(String.IsNullOrWhiteSpace(name)||Path.GetFileName(name)!=name||name.Contains(":")||!PackageAnalysis.HashText(hash)||rows.ContainsKey(name))throw new InvalidDataException("组件契约含无效文件名或哈希");rows.Add(name,hash);}
  if(rows.Count!=24||!rows.ContainsKey(HostName)||rows[HostName]!=Common.HostHash)throw new InvalidDataException("组件契约数量或自有宿主身份不符");rows.Remove(HostName);return rows;
 }
 static string SafeRoot(string value){string root=DataLocation.SafePath(value);if(!Directory.Exists(root))throw new DirectoryNotFoundException("所选目录不存在。");LocalImport.NoLinks(root);if(LocalImport.Under(root,Common.AppRoot)||LocalImport.Under(Common.AppRoot,root)||LocalImport.Under(root,Common.Root)||LocalImport.Under(Common.Root,root))throw new InvalidDataException("组件目录不能与程序目录或用户数据目录重叠。");string dep=Path.Combine(root,"depdlls");if(Directory.Exists(dep))LocalImport.NoLinks(dep);return root;}
 static string HashCancelable(string path,CancellationToken token){using(var sha=SHA256.Create())using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.SequentialScan)){byte[] buffer=new byte[1024*1024];int read;long bytes=0;while((read=stream.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();sha.TransformBlock(buffer,0,read,buffer,0);bytes+=read;}token.ThrowIfCancellationRequested();sha.TransformFinalBlock(new byte[0],0,0);System.Threading.Interlocked.Increment(ref componentFilesHashed);System.Threading.Interlocked.Add(ref Common.HashBytes,bytes);return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();}}
 internal static Dictionary<string,string> VerifyRoot(string selected,CancellationToken token){
  token.ThrowIfCancellationRequested();string root=SafeRoot(selected);var rows=ContractRows();var resolved=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(var row in rows){token.ThrowIfCancellationRequested();string direct=Path.Combine(root,row.Key),nested=Path.Combine(root,"depdlls",row.Key);bool hasDirect=File.Exists(direct),hasNested=File.Exists(nested);if(hasDirect&&hasNested)throw new InvalidDataException("组件在根目录和depdlls中重复，拒绝选择："+row.Key);if(!hasDirect&&!hasNested)throw new FileNotFoundException("缺少必需组件："+row.Key,row.Key);string path=hasDirect?direct:nested;LocalImport.NoLinks(path);if(HashCancelable(path,token)!=row.Value)throw new InvalidDataException("组件SHA-256与已审查契约不符："+row.Key);resolved.Add(row.Key,path);}
  return resolved;
 }
 internal static void Initialize(){Initialize(CancellationToken.None);}
 internal static void Initialize(CancellationToken token){Available=false;Root=null;try{token.ThrowIfCancellationRequested();if(Directory.Exists(ConfigPath))throw new InvalidDataException("组件位置配置被目录占用，请检查配置位置；未采用其他组件。");if(!File.Exists(ConfigPath)){Message="播放组件尚未准备好；预览和桌面功能不可用。点击此处选择本机已有组件目录并检查。";CheckedAt=DateTimeOffset.Now.ToString("o");return;}LocalImport.NoLinks(ConfigPath);if(new FileInfo(ConfigPath).Length>8192)throw new InvalidDataException("组件位置配置超出大小限制。");var cfg=Common.Json.Deserialize<RuntimeLocationRecord>(Common.ReadShared(ConfigPath));if(cfg==null||cfg.SchemaVersion!=1)throw new InvalidDataException("组件位置配置格式不支持；未启动播放器。");VerifyRoot(cfg.Root,token);Root=SafeRoot(cfg.Root);Available=true;CheckedAt=DateTimeOffset.Now.ToString("o");Message="播放组件已按本机完整SHA-256契约检查；实际加载和播放仍未验证。";}catch(OperationCanceledException){Available=false;Root=null;CheckedAt=DateTimeOffset.Now.ToString("o");Message="本次组件检查已取消。";throw;}catch(Exception ex){Available=false;Root=null;CheckedAt=DateTimeOffset.Now.ToString("o");Message="播放组件检查未通过，预览和桌面功能已禁用："+ex.Message;}}
 internal static string SelectAndSave(string selected,CancellationToken token){VerifyRoot(selected,token);string root=SafeRoot(selected);string folder=Path.GetDirectoryName(ConfigPath);Directory.CreateDirectory(folder);LocalImport.NoLinks(folder);string temp=ConfigPath+"."+Guid.NewGuid().ToString("N")+".tmp";bool unchanged=false,backupCleanupPending=false;try{using(var fs=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var sw=new StreamWriter(fs,new System.Text.UTF8Encoding(false))){sw.Write(Common.Json.Serialize(new RuntimeLocationRecord{SchemaVersion=1,Root=root,SavedAt=DateTimeOffset.Now.ToString("o")}));sw.Flush();fs.Flush(true);}LocalImport.NoLinks(temp);var verify=Common.Json.Deserialize<RuntimeLocationRecord>(Common.ReadShared(temp));if(verify==null||verify.SchemaVersion!=1||!DataLocation.Same(verify.Root,root))throw new InvalidDataException("组件位置配置写后核验失败。");lock(SaveGate){token.ThrowIfCancellationRequested();if(File.Exists(ConfigPath)){LocalImport.NoLinks(ConfigPath);RuntimeLocationRecord current=null;try{current=Common.Json.Deserialize<RuntimeLocationRecord>(Common.ReadShared(ConfigPath));}catch{}if(current!=null&&current.SchemaVersion==1&&DataLocation.Same(current.Root,root)){unchanged=true;}else{string backup=temp+".bak";try{
#if TEST_BUILD
if(Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_CONFIG_REPLACE_FAIL")=="1")throw new IOException("隔离测试注入：配置替换失败");
#endif
File.Replace(temp,ConfigPath,backup);}catch(Exception replaceError){if(File.Exists(backup)){if(!File.Exists(ConfigPath)){try{File.Move(backup,ConfigPath);}catch(Exception restoreError){throw new IOException("组件位置保存失败，原配置备份仍保留："+backup,new AggregateException(replaceError,restoreError));}}else throw new IOException("组件位置保存失败，原配置与恢复备份均保留："+backup,replaceError);}throw;}if(File.Exists(backup)){try{File.Delete(backup);}catch{backupCleanupPending=true;}}}}else File.Move(temp,ConfigPath);}}finally{if(File.Exists(temp))File.Delete(temp);}Root=root;Available=true;CheckedAt=DateTimeOffset.Now.ToString("o");Message=unchanged?"本机组件身份已复核；位置未变化，无需重复保存；播放加载仍未验证。":"本机组件身份已检查并保存；播放加载仍未验证。";if(backupCleanupPending)Message+="旧配置备份尚未清理，请交给Agent检查。";return Message;}
 internal static string SelectAndSave(string selected){return SelectAndSave(selected,CancellationToken.None);}
 internal static string RequireForStart(){return RequireForStart(CancellationToken.None);}
 internal static string RequireForStart(CancellationToken token){
  try{
   token.ThrowIfCancellationRequested();
   if(!File.Exists(ConfigPath))throw new FileNotFoundException("尚未保存播放组件位置，请先选择本机已有组件目录。");
   LocalImport.NoLinks(ConfigPath);
   if(new FileInfo(ConfigPath).Length>8192)throw new InvalidDataException("组件位置配置超出大小限制。");
   string before=Common.ReadShared(ConfigPath);
   var current=Common.Json.Deserialize<RuntimeLocationRecord>(before);
   if(current==null||current.SchemaVersion!=1)throw new InvalidDataException("组件位置配置格式不支持。");
   if(Available&&!String.IsNullOrEmpty(Root)&&!DataLocation.Same(current.Root,Root))throw new InvalidDataException("组件位置配置已变化，请重新选择并检查。");
   VerifyRoot(current.Root,token);
   token.ThrowIfCancellationRequested();
   if(Common.ReadShared(ConfigPath)!=before)throw new InvalidDataException("组件位置配置在复核期间变化，请重试。");
   Root=SafeRoot(current.Root);Available=true;CheckedAt=DateTimeOffset.Now.ToString("o");
   return Root;
  }catch(OperationCanceledException){throw;}
  catch(Exception ex){Available=false;Root=null;CheckedAt=DateTimeOffset.Now.ToString("o");Message="播放组件在启动前复核未通过，预览和桌面功能已禁用："+ex.Message;throw new InvalidOperationException(Message,ex);}
 }
}
