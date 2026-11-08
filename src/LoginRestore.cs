using System;using System.IO;using System.Linq;using System.Text;using System.Diagnostics;using System.Security.Cryptography;using System.Threading;using Microsoft.Win32;
internal sealed class RestoreTarget {public int Version{get;set;}public string CatalogId{get;set;}public string Source{get;set;}public string Sha256{get;set;}public string SelectedAt{get;set;}}
internal sealed class StartupOwner {public int Version{get;set;}public string Command{get;set;}public string EntrySha256{get;set;}public string InstallRoot{get;set;}public string EnabledAt{get;set;}}
internal interface IStartupStore {string Read();void Set(string value);void Remove();}
internal sealed class WindowsStartupStore:IStartupStore {
 internal const string KeyPath=@"Software\Microsoft\Windows\CurrentVersion\Run";internal const string ValueName="GXLocalWallpaper";
 public string Read(){using(var k=Registry.CurrentUser.OpenSubKey(KeyPath,false)){if(k==null)return null;object value=k.GetValue(ValueName,null,RegistryValueOptions.DoNotExpandEnvironmentNames);if(value==null)return null;if(k.GetValueKind(ValueName)!=RegistryValueKind.String)throw new InvalidDataException("同名启动值不是普通字符串，未修改");return value as string;}}
 public void Set(string value){using(var k=Registry.CurrentUser.CreateSubKey(KeyPath)){k.SetValue(ValueName,value,RegistryValueKind.String);}}
 public void Remove(){using(var k=Registry.CurrentUser.OpenSubKey(KeyPath,true)){if(k!=null)k.DeleteValue(ValueName,false);}}
}
internal static class LoginRestore {
 internal static string TargetPath{get{return Path.Combine(Common.Root,"data/last-wallpaper.json");}}
 internal static string OwnerPath{get{return Path.Combine(Common.Root,"data/login-startup-owner.json");}}
 internal static string PlanPath{get{return Path.Combine(Common.Root,"data/login-startup-plan.json");}}
 internal static string Command{get{return Common.Quote(System.Windows.Forms.Application.ExecutablePath)+" --restore-last";}}
 internal static bool IsEnabled(IStartupStore store){try{if(!File.Exists(OwnerPath))return false;var o=Common.Json.Deserialize<StartupOwner>(Common.ReadShared(OwnerPath));return o!=null&&o.Version==1&&o.InstallRoot==Common.Root&&o.Command==Command&&store.Read()==o.Command;}catch{return false;}}
 internal static string Prepare(){var plan=new{version=1,entry=System.Windows.Forms.Application.ExecutablePath,entrySha256=Common.Hash(System.Windows.Forms.Application.ExecutablePath),command=Command,hive="HKEY_CURRENT_USER",key=WindowsStartupStore.KeyPath,valueName=WindowsStartupStore.ValueName,prepared=DateTimeOffset.Now.ToString("o"),systemWritten=false};Common.ReplaceJson(PlanPath,plan);return Common.Hash(PlanPath);}
 // Invoked only by the explicit approval command, never by preparing the UI checkbox.
 internal static void ApplyApprovedPlan(IStartupStore store,string planHash){
  if(!File.Exists(PlanPath)||Common.Hash(PlanPath)!=planHash)throw new InvalidDataException("启用计划指纹不符，请重新准备并确认");var plan=Common.Read(PlanPath);
  if(Common.S(plan,"entry")!=System.Windows.Forms.Application.ExecutablePath||Common.S(plan,"entrySha256")!=Common.Hash(System.Windows.Forms.Application.ExecutablePath)||Common.S(plan,"command")!=Command||Common.S(plan,"key")!=WindowsStartupStore.KeyPath||Common.S(plan,"valueName")!=WindowsStartupStore.ValueName)throw new InvalidDataException("程序或启动计划已变化，未启用");
  string previous=store.Read();if(previous!=null&&!IsEnabled(store))throw new InvalidDataException("已有同名启动项且归属不匹配，未覆盖");
  string oldOwner=File.Exists(OwnerPath)?Common.ReadShared(OwnerPath):null;
  Common.ReplaceJson(OwnerPath,new StartupOwner{Version=1,Command=Command,EntrySha256=Common.S(plan,"entrySha256"),InstallRoot=Common.Root,EnabledAt=DateTimeOffset.Now.ToString("o")});
  try{store.Set(Command);if(!IsEnabled(store))throw new IOException("启动项写后核验失败");}
  catch{try{if(store.Read()==Command){if(previous==null)store.Remove();else store.Set(previous);}}finally{if(oldOwner==null)File.Delete(OwnerPath);else File.WriteAllText(OwnerPath,oldOwner,new UTF8Encoding(false));}throw;}
 }
 internal static void Disable(IStartupStore store){if(!IsEnabled(store))throw new InvalidDataException("启动项归属不匹配，未删除任何配置");store.Remove();if(store.Read()!=null)throw new IOException("启动项撤销未确认");File.Delete(OwnerPath);}
 internal static RestoreTarget ReadTarget(){if(!File.Exists(TargetPath))throw new InvalidDataException("尚未记录可恢复壁纸；请先主动设为桌面壁纸");if(new FileInfo(TargetPath).Length>16384)throw new InvalidDataException("恢复记录过大");var t=Common.Json.Deserialize<RestoreTarget>(Common.ReadShared(TargetPath));if(t==null||t.Version!=1||t.Sha256==null||t.Sha256.Length!=64)throw new InvalidDataException("恢复记录损坏");return t;}
 internal static WallpaperItem Validate(WallpaperLibrary lib,RestoreTarget t,CancellationToken token){
  if(lib.Error!=null)throw new InvalidDataException(lib.Error);var x=lib.Doc.Items.SingleOrDefault(v=>v.Id==t.CatalogId);if(x==null)throw new InvalidDataException("上次壁纸已从列表移除，未换播其他资源");
  if(!String.Equals(Path.GetFullPath(x.SourcePath),Path.GetFullPath(t.Source),StringComparison.OrdinalIgnoreCase)||x.RegisteredSha256!=t.Sha256)throw new InvalidDataException("恢复目标与登记不一致");
  if(!LocalImport.Under(t.Source,Path.Combine(Common.Root,"library")))throw new InvalidDataException("恢复目标不是本版管理副本，请先导入完整本地资源");LocalImport.NoLinks(t.Source);
  bool builtin=lib.IsBuiltin(x)&&t.Sha256==Common.CwpHash;
  if(!builtin&&(x.PreviewConfirmedSha256!=t.Sha256||x.DesktopConfirmedSha256!=t.Sha256))throw new InvalidDataException("目标缺少同完整哈希的窗口或桌面确认");
  string before=CoverCache.Signature(t.Source);var clock=Stopwatch.StartNew();string hash;
  using(var f=new FileStream(t.Source,FileMode.Open,FileAccess.Read,FileShare.Read))using(var h=SHA256.Create()){
   byte[] buffer=new byte[262144];int n;while((n=f.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();if(clock.Elapsed.TotalSeconds>30)throw new TimeoutException("目标校验超过30秒，未播放");h.TransformBlock(buffer,0,n,null,0);}h.TransformFinalBlock(new byte[0],0,0);hash=BitConverter.ToString(h.Hash).Replace("-","").ToLowerInvariant();}
  token.ThrowIfCancellationRequested();if(hash!=t.Sha256||before!=CoverCache.Signature(t.Source))throw new InvalidDataException("目标哈希不符或校验期间变化，未播放");CwpPolicy.Validate(t.Source,hash);return x;
 }
}
