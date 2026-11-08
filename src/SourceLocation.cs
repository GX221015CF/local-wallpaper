using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

internal sealed class SourceRootState {
 internal bool Found;
 internal bool SavedCustom;
 internal string Condition;
 internal string Root;
 internal string Error;
 internal int RecordFiles=-1; // Counts are produced by the bounded discovery worker, never during window construction.
}
internal sealed class SourceRootConfig {
 public int Version{get;set;}
 public string Root{get;set;}
 public string SavedAt{get;set;}
}

// A source is only a known local cache layout. It is never a search root.
internal static class SourceLocation {
 internal static string ConfigPath {get{return Path.Combine(Common.Root,"data","source-location.json");}}
 internal static string DefaultRoot {get{
#if TEST_BUILD
  string test=Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_DEFAULT_SOURCE");
  if(!String.IsNullOrWhiteSpace(test)){string root=DataLocation.SafePath(test);if(!LocalImport.Under(root,Path.Combine(Common.AppRoot,"tests")))throw new InvalidDataException("测试默认来源必须位于隔离tests目录");return root;}
#endif
  string roaming=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
  if(String.IsNullOrWhiteSpace(roaming)||!Path.IsPathRooted(roaming))throw new InvalidDataException("Windows用户漫游目录不可用");
  return Path.GetFullPath(Path.Combine(roaming,"lxwp","data"));
 }}
 internal static string RecommendedDataParent {get{
#if TEST_BUILD
  string test=Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_LOCALAPPDATA");
  if(!String.IsNullOrWhiteSpace(test)){string root=DataLocation.SafePath(test);if(!LocalImport.Under(root,Path.Combine(Common.AppRoot,"tests")))throw new InvalidDataException("测试推荐目录必须位于隔离tests目录");return root;}
#endif
  string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
  if(!String.IsNullOrWhiteSpace(local)&&Path.IsPathRooted(local)&&Directory.Exists(local))return DataLocation.SafePath(local);
  string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
  if(!String.IsNullOrWhiteSpace(profile)&&Path.IsPathRooted(profile)&&Directory.Exists(profile))return DataLocation.SafePath(Path.Combine(profile,"AppData","Local"));
  throw new InvalidDataException("Windows用户本地数据目录不可用；请通过浏览选择一个本机文件夹。");
 }}
 static string TestOverridePath{get{return Path.Combine(Common.Root,"tests","import-root.txt");}}
 static bool ConfigExists(){try{var attributes=File.GetAttributes(ConfigPath);if((attributes&FileAttributes.Directory)!=0)throw new InvalidDataException("壁纸来源配置路径被目录占用；不会改用默认位置。");return true;}catch(FileNotFoundException){return false;}catch(DirectoryNotFoundException){return false;}}
 static string ValidateRoot(string value,out int recordCount){
  string root=DataLocation.SafePath(value);FileAttributes rootAttributes;try{rootAttributes=File.GetAttributes(root);}catch(DirectoryNotFoundException){throw new DirectoryNotFoundException("所选来源位置不存在。");}catch(FileNotFoundException){throw new DirectoryNotFoundException("所选来源位置不存在。");}if((rootAttributes&FileAttributes.Directory)==0)throw new InvalidDataException("所选来源位置不是文件夹。");LocalImport.NoLinks(root);
#if TEST_BUILD
  if(!LocalImport.Under(root,Path.Combine(Common.AppRoot,"tests")))throw new InvalidDataException("测试壁纸来源必须位于本修订隔离tests目录。");
#endif
  string records=Path.Combine(root,"cache_info"),dynamic=Path.Combine(root,"wallpaper","wpcache","dynamic");
  if(!Directory.Exists(records)||!Directory.Exists(dynamic))throw new InvalidDataException("所选目录不符合已知布局；需要同时包含cache_info和wallpaper\\wpcache\\dynamic。");
  LocalImport.NoLinks(records);LocalImport.NoLinks(dynamic);
  bool testFixture=false;
#if TEST_BUILD
  testFixture=LocalImport.Under(root,Path.Combine(Common.AppRoot,"tests"));
#endif
  if(!testFixture&&!String.IsNullOrWhiteSpace(Common.Root)&&Directory.Exists(Common.Root)&&(DataLocation.Same(root,Common.Root)||LocalImport.Under(root,Common.Root)||LocalImport.Under(Common.Root,root)))throw new InvalidDataException("壁纸来源不能与本软件数据目录重合。");
  if(!testFixture&&!String.IsNullOrWhiteSpace(Common.AppRoot)&&Directory.Exists(Common.AppRoot)&&(DataLocation.Same(root,Common.AppRoot)||LocalImport.Under(root,Common.AppRoot)||LocalImport.Under(Common.AppRoot,root)))throw new InvalidDataException("壁纸来源不能与程序目录重合。");
#if TEST_BUILD
  if(!testFixture)throw new InvalidDataException("测试壁纸来源必须位于本修订隔离tests目录。");
#endif
  // Do not enumerate cache records on the UI thread. ResourceDiscovery performs
  // the bounded, cancellable record scan in its owned worker process.
  recordCount=-1;return root;
 }
 internal static string ValidateRoot(string value){int ignored;return ValidateRoot(value,out ignored);}
 internal static string ResolveRoot(){
#if TEST_BUILD
  if(File.Exists(TestOverridePath)){LocalImport.NoLinks(TestOverridePath);return ValidateRoot(File.ReadAllText(TestOverridePath).Trim());}
#endif
  string path=ConfigPath;if(ConfigExists()){LocalImport.NoLinks(path);if(new FileInfo(path).Length>16384)throw new InvalidDataException("壁纸来源配置超过读取预算");var c=Common.Json.Deserialize<SourceRootConfig>(Common.ReadShared(path));if(c==null||c.Version!=1||String.IsNullOrWhiteSpace(c.Root))throw new InvalidDataException("已保存的壁纸来源配置损坏；不会改用其他目录。");return ValidateRoot(c.Root);}
  return Path.GetFullPath(DefaultRoot);
 }
 internal static SourceRootState GetState(){
  var state=new SourceRootState();string config=ConfigPath;
  bool saved=false;try{saved=ConfigExists();}catch(Exception ex){state.SavedCustom=true;state.Error=ex.Message;state.Condition="saved-config-read-failed";return state;}
  if(saved){state.SavedCustom=true;try{LocalImport.NoLinks(config);if(new FileInfo(config).Length>16384)throw new InvalidDataException("壁纸来源配置超过读取预算");var c=Common.Json.Deserialize<SourceRootConfig>(Common.ReadShared(config));if(c==null||c.Version!=1||String.IsNullOrWhiteSpace(c.Root))throw new InvalidDataException("已保存的壁纸来源配置损坏；不会改用默认位置。");state.Root=ValidateRoot(c.Root,out state.RecordFiles);state.Found=true;state.Condition="saved-layout-ready";}catch(Exception ex){state.Error=ex.Message;state.Condition=ex is UnauthorizedAccessException?"saved-source-access-denied":ex is DirectoryNotFoundException?"saved-source-missing":"saved-source-invalid";}return state;}
  try{state.Root=Path.GetFullPath(DefaultRoot);state.Root=ValidateRoot(state.Root,out state.RecordFiles);state.Found=true;state.Condition="default-layout-ready";}catch(Exception ex){state.Error=ex.Message;state.Condition=ex is UnauthorizedAccessException?"default-source-access-denied":ex is DirectoryNotFoundException?"default-source-missing":"default-source-invalid";}
  return state;
 }
 internal static string SaveRoot(string value){
  string root=ValidateRoot(value),path=ConfigPath,folder=Path.GetDirectoryName(path);DataLocation.SafePath(folder);Directory.CreateDirectory(folder);LocalImport.NoLinks(folder);
  string before=ConfigExists()?Common.Hash(path):null;var config=new SourceRootConfig{Version=1,Root=root,SavedAt=DateTimeOffset.Now.ToString("o")};string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var w=new StreamWriter(f,new System.Text.UTF8Encoding(false))){w.Write(Common.Json.Serialize(config));w.Flush();f.Flush(true);}LocalImport.NoLinks(temp);var check=Common.Json.Deserialize<SourceRootConfig>(Common.ReadShared(temp));if(check==null||check.Version!=1||!DataLocation.Same(ValidateRoot(check.Root),root))throw new InvalidDataException("壁纸来源配置写后核验失败；旧位置保持不变。");if((ConfigExists()?Common.Hash(path):null)!=before)throw new IOException("壁纸来源配置在保存期间发生变化；旧位置保持不变。");if(before==null)File.Move(temp,path);else File.Replace(temp,path,null);if(!DataLocation.Same(ResolveRoot(),root))throw new InvalidDataException("壁纸来源配置提交后回读不一致。");return root;}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
