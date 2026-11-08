using System;using System.IO;using System.Text;using System.Security.Cryptography;using System.Collections.Generic;using System.Web.Script.Serialization;using System.Runtime.InteropServices;
internal static class Common {
 internal const string CwpHash="ddcd609eb1708a3ea0a5909c4766efe946a15cb28ac35ed7a4dd87a5c5d85e48";
 internal const string HostHash="25541b6288a0ace82a75bf65aa73ab910e4e44668929348fd926ce9839786fb7";
 #if TEST_BUILD
 internal static string Root=ResolveTestRoot();
 static string ResolveTestRoot(){
  string configured=Environment.GetEnvironmentVariable("LOCALWALLPAPER_TEST_ROOT");
  if(String.IsNullOrWhiteSpace(configured))return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",".."));
  string root=Path.GetFullPath(configured).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
  string marker=Path.Combine(root,".localwallpaper-test-root");
  if(!Directory.Exists(root)||(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0||!File.Exists(marker)||(File.GetAttributes(marker)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("TEST_BUILD需要本修订tests/isolation中的显式隔离根标记");
  DirectoryInfo cursor=new DirectoryInfo(root),isolation=null;while(cursor!=null){if(cursor.Name=="isolation"&&cursor.Parent!=null&&cursor.Parent.Name=="tests"){isolation=cursor;break;}cursor=cursor.Parent;}
  if(isolation==null||isolation.Parent.Parent==null||root.Equals(isolation.FullName,StringComparison.OrdinalIgnoreCase)||!root.StartsWith(isolation.FullName.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("TEST_BUILD隔离根必须位于本修订tests/isolation下");
  string expected="wallpaper-small-trial-20261103|"+isolation.Parent.Parent.Name;
  if(!String.Equals(File.ReadAllText(marker).Trim(),expected,StringComparison.Ordinal))throw new InvalidDataException("TEST_BUILD隔离根身份不匹配，拒绝运行");
  string expectedBase=Path.GetFullPath(Path.Combine(root,"build")).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!AppDomain.CurrentDomain.BaseDirectory.StartsWith(expectedBase,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("TEST_BUILD实际程序位置不属于登记的隔离根");
  return root;
 }
#else
 internal static string Root=Directory.GetParent(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)).FullName;
#endif
 internal static string AppRoot=Root;
 internal static string Runtime{get{return Path.Combine(AppRoot,"build","runtime");}}
 internal static string BuildKey{get{return Path.GetFileName(AppRoot);}}
 internal static string LogRoot{get{return Path.Combine(Root,"logs",BuildKey);}}
 internal static string WorkRoot{get{return Path.Combine(Root,"sessions",BuildKey,"tasks");}}
 internal static string DefaultCwp{get{return Path.GetFullPath(Path.Combine(AppRoot,"..","exports","2026-10-26","current_wallpaper","雨中静默少女_317495.cwp"));}}
 internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=8388608};
 internal static long HashCalls,HashBytes,HashTicks;
 internal static string Hash(string p){using(var s=File.OpenRead(p))using(var h=SHA256.Create()){long t=System.Diagnostics.Stopwatch.GetTimestamp();string hash=BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();System.Threading.Interlocked.Increment(ref HashCalls);System.Threading.Interlocked.Add(ref HashBytes,s.Length);System.Threading.Interlocked.Add(ref HashTicks,System.Diagnostics.Stopwatch.GetTimestamp()-t);return hash;}}
 internal static string Quote(string p){if(p.Contains("\""))throw new ArgumentException("Quote in path");return "\""+p+"\"";}
 internal static string ReadShared(string p){using(var s=new FileStream(p,FileMode.Open,FileAccess.Read,(FileShare.ReadWrite|FileShare.Delete)))using(var r=new StreamReader(s,Encoding.UTF8))return r.ReadToEnd();}
 internal static Dictionary<string,object> Read(string p){return Json.Deserialize<Dictionary<string,object>>(ReadShared(p));}
 internal static Dictionary<string,object> Obj(object x){return (Dictionary<string,object>)x;}
 internal static string S(Dictionary<string,object> d,string k){return d.ContainsKey(k)?Convert.ToString(d[k]):"";}
 internal static void SaveNew(string p,object data){using(var f=new StreamWriter(new FileStream(p,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false)))f.Write(Json.Serialize(data));}
 internal static void ReplaceJson(string p,object value){string temp=p+".tmp";File.WriteAllText(temp,Json.Serialize(value),new UTF8Encoding(false));if(File.Exists(p))File.Replace(temp,p,null);else File.Move(temp,p);}
 internal static string ShortSiblingTempPath(string target){string dir=Path.GetDirectoryName(Path.GetFullPath(target));string temp=Path.Combine(dir,".t"+Guid.NewGuid().ToString("N").Substring(0,16)+".tmp");if(temp.Length>=260)throw new PathTooLongException("目标目录过深，无法在传统Windows路径预算内安全写入临时文件；原文件保持不变。");return temp;}
 internal static void RequestStop(string run,string reason){string p=Path.Combine(run,"stop.request.json");try{SaveNew(p,new{reason=reason,local=DateTimeOffset.Now.ToString("o")});}catch(IOException){if(!File.Exists(p))throw;}}
 [DllImport("user32")]internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32")]internal static extern bool IsWindow(IntPtr h);
 [DllImport("user32")]internal static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32")]internal static extern bool ShowWindowAsync(IntPtr h,int cmd);
 [DllImport("kernel32",SetLastError=true)]internal static extern bool TerminateProcess(IntPtr handle,uint code);
 [DllImport("kernel32")]internal static extern uint WaitForSingleObject(IntPtr handle,uint ms);
 [DllImport("user32")]internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
internal sealed class Journal:IDisposable {
 readonly DailyLog writer;internal Journal(string p){writer=new DailyLog(p);}internal void Write(string phase,object data){writer.Write(phase,data);}public void Dispose(){writer.Dispose();}
}
