using System;using System.IO;using System.Collections.Generic;using System.Threading;using System.Security.Cryptography;using System.Web.Script.Serialization;
// No native calls. Only pinned resource archives and our own host are staged.
internal sealed partial class HostResources {
#if TEST_BUILD
 internal static Action<string,string> TestBeforeCommit;
 internal static Action<string,string> TestMoveDirectory;
#endif
 static void MoveDirectory(string source,string destination){
#if TEST_BUILD
  if(TestMoveDirectory!=null){TestMoveDirectory(source,destination);return;}
#endif
  Directory.Move(source,destination);
 }
 internal static readonly string[] Archives={"jlwpengine.resc","jlwpengine2.resc"};
 internal string Root{get;private set;} internal string Image{get;private set;} internal string Contract{get;private set;}
 const string HostName="Desktop317495Host-refined.exe";
 static void PathBudget(string root){
  // Check strings before Path.GetDirectoryName invokes legacy MAX_PATH normalization.
  foreach(string file in new[]{Path.Combine(root,"build","runtime",HostName),Path.Combine(root,"rules","runtime-components.json"),Path.Combine(root,"build","runtime",Archives[0]),Path.Combine(root,"build","runtime",Archives[1])})
   if(file.Length>=260)throw new PathTooLongException("运行目录过长，无法安全准备播放宿主（最长文件路径"+file.Length+"字符，要求少于260）。请使用较短的软件数据位置；尚未创建播放器。");
  foreach(string dir in new[]{root,Path.Combine(root,"build","runtime"),Path.Combine(root,"rules")})
   if(dir.Length>=248)throw new PathTooLongException("运行目录过长，无法安全准备播放宿主（目录路径要求少于248字符）；尚未创建播放器。");
 }
 internal static string ImageFor(string run){return Path.Combine(Path.GetFullPath(run),"host","build","runtime",HostName);}
 static void NoLinks(string path){for(string p=Path.GetFullPath(path);!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)){if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("宿主资源路径包含重解析点");if(Path.GetDirectoryName(p)==p)break;}}
 static string Hash(string file,CancellationToken token){NoLinks(file);using(var input=File.OpenRead(file))using(var sha=SHA256.Create()){byte[] b=new byte[65536];int n;while((n=input.Read(b,0,b.Length))>0){token.ThrowIfCancellationRequested();sha.TransformBlock(b,0,n,b,0);}token.ThrowIfCancellationRequested();sha.TransformFinalBlock(b,0,0);return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();}}
 static void Copy(string source,string target,string expected,CancellationToken token){NoLinks(source);if(Hash(source,token)!=expected)throw new InvalidDataException("暂存前资源身份变化："+Path.GetFileName(source));using(var input=File.OpenRead(source))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] b=new byte[65536];int n;while((n=input.Read(b,0,b.Length))>0){token.ThrowIfCancellationRequested();output.Write(b,0,n);}output.Flush(true);}if(Hash(target,token)!=expected||Hash(source,token)!=expected)throw new InvalidDataException("暂存资源身份变化："+Path.GetFileName(source));}
 static Dictionary<string,string> ReadContract(string file,string expected,string hostHash,CancellationToken token){if(Hash(file,token)!=expected)throw new InvalidDataException("资源暂存契约身份不符");if(new FileInfo(file).Length>65536)throw new InvalidDataException("资源契约过大");var doc=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));object value;var rows=doc.TryGetValue("files",out value)?value as System.Collections.IList:null;if(rows==null||rows.Count!=24)throw new InvalidDataException("资源契约数量不符");var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(object obj in rows){var row=obj as Dictionary<string,object>;if(row==null)throw new InvalidDataException("资源契约格式错误");string name=Convert.ToString(row["name"]),hash=Convert.ToString(row["sha256"]);if(String.IsNullOrWhiteSpace(name)||name=="."||name==".."||Path.GetFileName(name)!=name||name.Contains(":")||hash.Length!=64||result.ContainsKey(name))throw new InvalidDataException("资源契约名称或身份错误");foreach(char c in hash)if(!Uri.IsHexDigit(c))throw new InvalidDataException("资源契约哈希错误");result.Add(name,hash);}if(!result.ContainsKey(HostName)||result[HostName]!=hostHash)throw new InvalidDataException("资源契约宿主身份不符");foreach(string archive in Archives)if(!result.ContainsKey(archive))throw new InvalidDataException("资源契约缺少归档："+archive);return result;}
 internal static HostResources Stage(string run,string host,string contract,string contractHash,string hostHash,IDictionary<string,string> resolved,CancellationToken token){
  token.ThrowIfCancellationRequested();run=Path.GetFullPath(run);NoLinks(run);if(!Directory.Exists(run))throw new DirectoryNotFoundException("本次运行目录不存在");string final=Path.Combine(run,"host");PathBudget(final);if(Directory.Exists(final)||File.Exists(final))throw new IOException("本次宿主资源目录已存在，保留现场");
  // A run has one owner. Keep a random sibling name, but do not add 39 characters
  // compared with final/host; the exact R25 observed run is already 187 characters.
  string temporary=Path.Combine(run,"s"+Guid.NewGuid().ToString("N").Substring(0,7));PathBudget(temporary);NoLinks(temporary);if(Directory.Exists(temporary)||File.Exists(temporary))throw new IOException("本次宿主暂存目录已被占用，保留原内容，请重试");
  var rows=ReadContract(contract,contractHash,hostHash,token);if(Hash(host,token)!=hostHash)throw new InvalidDataException("待暂存宿主身份不符");foreach(string name in Archives)if(!resolved.ContainsKey(name))throw new InvalidDataException("缺少已核验资源："+name);
  var staged=new HostResources{Root=temporary,Image=Path.Combine(temporary,"build","runtime",HostName),Contract=Path.Combine(temporary,"rules","runtime-components.json")};
  var attempts=new List<object>();string identity=null;bool cleaned=false,committed=false;Exception failure=null,cleanupFailure=null;
  try{
   Directory.CreateDirectory(Path.GetDirectoryName(staged.Image));Directory.CreateDirectory(Path.GetDirectoryName(staged.Contract));
   Copy(host,staged.Image,hostHash,token);Copy(contract,staged.Contract,contractHash,token);
   foreach(string name in Archives)Copy(resolved[name],Path.Combine(Path.GetDirectoryName(staged.Image),name),rows[name],token);
   token.ThrowIfCancellationRequested();identity=DirectoryIdentity(temporary);
   var expected=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{Path.Combine("build","runtime",HostName),hostHash},{Path.Combine("rules","runtime-components.json"),contractHash}};
   foreach(string name in Archives)expected.Add(Path.Combine("build","runtime",name),rows[name]);
#if TEST_BUILD
   if(TestBeforeCommit!=null)TestBeforeCommit(temporary,final);
#endif
   Commit(staged,final,identity,expected,token,attempts);committed=true;return staged;
  }catch(Exception original){
   failure=original;
   try{
    if(original.Data.Contains("hostRetainUntrusted")||identity!=null&&Directory.Exists(staged.Root)&&DirectoryIdentity(staged.Root)!=identity)throw new IOException("宿主暂存归属变化或提交状态不明，保留现场供核对");
    staged.Cleanup();cleaned=true;
   }catch(Exception clean){cleanupFailure=clean;original.Data["hostStageCleanupError"]=clean.ToString();}
   throw;
  }finally{
   try{
    Common.SaveNew(Path.Combine(run,"host-stage-result.json"),new{
     committed=committed,temporary=temporary,target=final,directoryIdentity=identity,attempts=attempts,
     firstCommitError=staged.FirstCommitError,originalError=failure==null?null:failure.ToString(),
     hresult=failure==null?0:failure.HResult,nativeError=failure==null?0:failure.HResult&65535,
     stagingCleaned=cleaned,stagingRetained=!committed&&!cleaned,cleanupError=cleanupFailure==null?null:cleanupFailure.ToString(),
     sourceStillExists=Directory.Exists(temporary),targetExists=Directory.Exists(final),playerCreated=false});
   }catch(Exception diagnostic){if(failure!=null)failure.Data["hostStageDiagnosticError"]=diagnostic.ToString();}
  }
 }
 internal void Cleanup(){if(!Directory.Exists(Root))return;NoLinks(Root);var expectedFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Image,Contract};foreach(string name in Archives)expectedFiles.Add(Path.Combine(Path.GetDirectoryName(Image),name));var expectedDirs=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.Combine(Root,"build"),Path.Combine(Root,"build","runtime"),Path.Combine(Root,"rules")};
  var directories=new List<string>{Root};var allFiles=new List<string>();for(int i=0;i<directories.Count;i++){string dir=directories[i];NoLinks(dir);foreach(string f in Directory.GetFiles(dir)){NoLinks(f);if(!expectedFiles.Contains(f))throw new IOException("宿主目录含未知文件，保留全部现场："+f);allFiles.Add(f);}foreach(string sub in Directory.GetDirectories(dir)){NoLinks(sub);if(!expectedDirs.Contains(sub))throw new IOException("宿主目录含未知目录，保留全部现场："+sub);directories.Add(sub);}}
  foreach(string f in allFiles)File.Delete(f);directories.Sort((a,b)=>b.Length.CompareTo(a.Length));foreach(string dir in directories)Directory.Delete(dir,false);
 }
}
