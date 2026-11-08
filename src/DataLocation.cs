using System;using System.IO;using System.Linq;using System.Text;using System.Collections.Generic;using System.Threading;
internal sealed class Deployment {
 public int SchemaVersion{get;set;}public int DataFormat{get;set;}public string StoreId{get;set;}public string DataRoot{get;set;}public string ActiveRoot{get;set;}public string ReleaseSha256{get;set;}public string PreviousRoot{get;set;}public string PreviousReleaseSha256{get;set;}public string MigrationRecord{get;set;}public string CommittedAt{get;set;}
}
internal sealed class StoreMarker {public int SchemaVersion{get;set;}public int DataFormat{get;set;}public string StoreId{get;set;}}
internal sealed class LocationBinding {public int SchemaVersion{get;set;}public string ControlRoot{get;set;}public string DataRoot{get;set;}public string StoreId{get;set;}}
internal static class DataLocation {
 internal static Deployment Current;internal static MigrationRecord InProgress;internal static string Failure;internal static bool Ready;internal static string Phase="定位数据";
 internal static string ControlRoot {get {
#if TEST_BUILD
  string configuredBinding=Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_BINDING");if(!String.IsNullOrWhiteSpace(configuredBinding)&&File.Exists(configuredBinding)){var binding=ReadBinding(SafePath(configuredBinding));string bound=SafePath(binding.ControlRoot);if(!LocalImport.Under(bound,Path.Combine(Common.AppRoot,"tests")))throw new InvalidDataException("测试绑定的配置位置必须在本修订tests内");return bound;}
  string p=Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_CONTROL");if(p!=null){p=SafePath(p);if(!LocalImport.Under(p,Path.Combine(Common.AppRoot,"tests")))throw new InvalidDataException("测试配置必须在本修订tests内");return p;}
  throw new InvalidDataException("测试构建必须显式指定隔离配置，拒绝使用正式库");
#else
  return ReadBinding(BindingPath).ControlRoot;
#endif
 }}
 internal static string BindingPath{get{
#if TEST_BUILD
  string test=Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_BINDING");if(test!=null){test=SafePath(test);string isolation=SafePath(Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_ROOT"));string app=SafePath(Common.AppRoot);string allowed=Path.Combine(app,"build");if((!DataLocation.Same(app,isolation)&&!LocalImport.Under(app,isolation))||!LocalImport.Under(test,allowed)||Path.GetFileName(test)!="data-location.json")throw new InvalidDataException("测试绑定必须位于隔离根下当前程序版本的build目录");return test;}
#endif
  return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data-location.json");}}
 internal static string ConfigPath{get{return Path.Combine(ControlRoot,"deployment.json");}}
 internal static string EmergencyRoot{get{try{return Path.Combine(ControlRoot,"recovery-handoff");}catch{return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"recovery-handoff");}}}
 internal static string SafePath(string value){if(String.IsNullOrWhiteSpace(value)||value.IndexOfAny(new[]{'\r','\n','\0','"'})>=0||!Path.IsPathRooted(value)||value.StartsWith(@"\\")||value.Contains("..")||value.Substring(2).Contains(":"))throw new InvalidDataException("数据位置不是有效本地绝对路径");string p=Path.GetFullPath(value).TrimEnd('\\');if(p.Length<4||p.Length>220)throw new InvalidDataException("数据位置不能是磁盘根或过长路径");for(string n=p;!String.IsNullOrEmpty(n);n=Path.GetDirectoryName(n))if(File.Exists(n)||Directory.Exists(n)){if((File.GetAttributes(n)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("位置包含链接或重解析点");}return p;}
 internal static bool Same(string a,string b){return String.Equals(Path.GetFullPath(a).TrimEnd('\\'),Path.GetFullPath(b).TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}
 internal static void DataPath(string root,string program){root=SafePath(root);program=SafePath(program);
#if TEST_BUILD
 if(!LocalImport.Under(root,Path.Combine(Common.AppRoot,"tests")))throw new InvalidDataException("测试数据必须在本修订tests内，拒绝正式库");
#endif
 bool overlap=Same(root,program)||LocalImport.Under(root,program)||LocalImport.Under(program,root);
#if TEST_BUILD
 if(Same(program,Common.AppRoot)&&LocalImport.Under(root,Path.Combine(Common.AppRoot,"tests")))overlap=false;
#endif
 if(overlap)throw new InvalidDataException("用户数据与程序版本目录必须分离");foreach(string part in root.Split('\\'))if(new[]{"build","runtime","rules"}.Contains(part.ToLowerInvariant()))throw new InvalidDataException("数据位置不能指向运行组件目录");}
 internal static T ReadSmall<T>(string p,int max=2097152){LocalImport.NoLinks(p);if(new FileInfo(p).Length>max)throw new InvalidDataException("配置超过读取预算");return Common.Json.Deserialize<T>(Common.ReadShared(p));}
 internal static LocationBinding ReadBinding(string path){var b=ReadSmall<LocationBinding>(path,4096);if(b==null||b.SchemaVersion!=1||!System.Text.RegularExpressions.Regex.IsMatch(b.StoreId??"","^[a-f0-9]{32}$"))throw new InvalidDataException("缺少或损坏数据位置绑定，未猜选库");b.ControlRoot=SafePath(b.ControlRoot);b.DataRoot=SafePath(b.DataRoot);if(Same(b.ControlRoot,b.DataRoot)||LocalImport.Under(b.ControlRoot,b.DataRoot)||LocalImport.Under(b.DataRoot,b.ControlRoot))throw new InvalidDataException("控制记录与正式数据必须分离");foreach(string part in b.ControlRoot.Split('\\'))if(new[]{"build","runtime","rules"}.Contains(part.ToLowerInvariant()))throw new InvalidDataException("位置绑定不能指向运行组件目录");return b;}
 internal static Deployment ValidateDeployment(Deployment d){if(d==null||d.SchemaVersion!=1||d.DataFormat!=1||String.IsNullOrEmpty(d.StoreId)||!PackageAnalysis.HashText(d.ReleaseSha256))throw new InvalidDataException("位置配置损坏或格式不支持，未选择其他库");DataPath(d.DataRoot,d.ActiveRoot);d.DataRoot=SafePath(d.DataRoot);d.ActiveRoot=SafePath(d.ActiveRoot);return d;}
 internal static Deployment LoadBound(string path){var binding=ReadBinding(path);var d=ValidateDeployment(ReadSmall<Deployment>(Path.Combine(binding.ControlRoot,"deployment.json"),16384));if(d.StoreId!=binding.StoreId||!Same(d.DataRoot,binding.DataRoot))throw new InvalidDataException("位置绑定与登记身份不符，未切换或恢复备份");return d;}
 internal static Deployment Load(){
#if TEST_BUILD
 if(File.Exists(BindingPath))return LoadBound(BindingPath);
 return ValidateDeployment(ReadSmall<Deployment>(ConfigPath,16384));
#else
 return LoadBound(BindingPath);
#endif
 }
 internal static void ValidateStore(Deployment d,bool writable){DataPath(d.DataRoot,d.ActiveRoot);var m=ReadSmall<StoreMarker>(Path.Combine(d.DataRoot,"store.json"),16384);if(m==null||m.SchemaVersion!=1||m.DataFormat!=d.DataFormat||m.StoreId!=d.StoreId)throw new InvalidDataException("数据根身份或格式不符");string p=Path.Combine(d.DataRoot,"data","catalog.json");var doc=ReadSmall<LibraryDocument>(p);if(doc==null||doc.Version!=1||doc.Items==null||doc.Items.Any(x=>String.IsNullOrEmpty(x.Id)||!PackageAnalysis.HashText(x.RegisteredSha256)))throw new InvalidDataException("登记损坏或格式不支持，未创建空库");var sound=ReadSmall<AppSettings>(Path.Combine(d.DataRoot,"data","settings.json"),16384);if(sound==null||sound.Version!=1||sound.Volume<0||sound.Volume>100)throw new InvalidDataException("设置损坏，未使用默认值覆盖");if(writable){string probe=Path.Combine(d.DataRoot,"data",".write-"+Guid.NewGuid().ToString("N"));try{using(var f=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None))f.Flush(true);}finally{if(File.Exists(probe))File.Delete(probe);}}}
 internal static void Initialize(){try{Current=Load();if(!Same(Current.ActiveRoot,Common.AppRoot)){
#if TEST_BUILD
 string isolated=SafePath(Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_ROOT"));string allowed=Path.Combine(isolated,"tests");if(!LocalImport.Under(Current.ActiveRoot,allowed)||!LocalImport.Under(Current.DataRoot,allowed))throw new InvalidDataException("测试版本关联或数据位置超出隔离根");
#else
 throw new InvalidDataException("此版本不是当前日常版本，请从固定入口打开；未写入旧库");
#endif
 }VersionStore.VerifyRelease(Current.ActiveRoot,Current.ReleaseSha256,Current.DataFormat);ValidateStore(Current,false);Common.Root=Current.DataRoot;Ready=true;Phase="日常运行";}catch(Exception ex){Failure=ex.Message;throw new InvalidDataException("无法确定或使用当前数据位置，原数据未迁移或恢复。请提交给Agent。\n"+ex.Message,ex);}}
 internal static void ForTool(string explicitRoot){if(explicitRoot==null){Initialize();return;}var d=Load();string p=SafePath(explicitRoot);if(!Same(p,d.DataRoot))throw new InvalidDataException("本地工具只能写位置配置指定的同一正式库；隔离测试请使用测试构建");Initialize();}
 internal static string EntryPath{get{return Current==null&&!Ready?System.Windows.Forms.Application.ExecutablePath:Path.Combine(Current==null?Common.AppRoot:Current.ActiveRoot,"build","LocalWallpaper.exe");}}
 internal static object Snapshot(){string config;try{config=ConfigPath;}catch{config="尚未解析；位置绑定："+BindingPath;}MigrationRecord m=InProgress;string record=Current==null?null:Current.MigrationRecord;try{if(m==null&&record!=null&&LocalImport.Under(record,Path.Combine(ControlRoot,"m")))m=ReadSmall<MigrationRecord>(record,65536);}catch{}object migration=m==null?null:new{diagnosticHistoryOnly=true,id=m.Id,sourceRoot=m.SourceRoot,sourceFormat=m.SourceFormat,targetFormat=m.TargetFormat,sourceFingerprint=m.SourceFingerprint,backup=m.Backup,backupFingerprint=m.BackupFingerprint,status=m.Status,committed=m.Committed,preparedAt=m.PreparedAt,committedAt=m.CommittedAt,backupNotRestored=true};return new{schemaVersion=1,programRoot=Common.AppRoot,entry=EntryPath,actualProcessImage=System.Windows.Forms.Application.ExecutablePath,configuredReleaseSha256=Current==null?null:Current.ReleaseSha256,dataRoot=Ready?Common.Root:null,configuredDataRoot=Current==null?null:Current.DataRoot,dataFormat=Current==null?0:Current.DataFormat,storeId=Current==null?null:Current.StoreId,sessionRoot=Common.LogRoot,taskRoot=Common.WorkRoot,phase=Phase,configuration=config,bindingFile=BindingPath,migrationRecord=record,migration=migration,committedAt=Current==null?null:Current.CommittedAt,previousProgram=Current==null?null:Current.PreviousRoot,failure=Failure,historicalEvidence=true,recheckBeforeAction=true};}
 internal static void Emergency(){Common.Root=EmergencyRoot;Phase="数据定位失败，诊断专用位置；不是正式库";Directory.CreateDirectory(Common.WorkRoot);}
}
internal sealed class DataLease:IDisposable {
 FileStream file;internal DataLease(string root){DataLocation.SafePath(root);LocalImport.NoLinks(root);string p=Path.Combine(root,".writer.lock");if(File.Exists(p))LocalImport.NoLinks(p);file=new FileStream(p,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);}
 public void Dispose(){if(file!=null){file.Dispose();file=null;}}
}
// Same legacy GUI mutex, held for controller/tool/transaction lifetime. Never kills an owner.
internal sealed class UpgradeGate:IDisposable {
 Mutex mutex;bool held;internal UpgradeGate(){
#if TEST_BUILD
 string name="Local\\LocalWallpaperUpgrade-Test-"+PackageAnalysis.Digest(Encoding.UTF8.GetBytes(DataLocation.ControlRoot)).Substring(0,24);
#else
 string name="Local\\Local317495ControlWindow-v1";
#endif
 mutex=new Mutex(false,name);try{try{held=mutex.WaitOne(0);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("已有本地壁纸实例或切换任务，请先正常退出原窗口；未停止任何实例");}catch{mutex.Dispose();throw;}}
 public void Dispose(){if(mutex!=null){if(held)mutex.ReleaseMutex();mutex.Dispose();mutex=null;}}
}
