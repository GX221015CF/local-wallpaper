#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Forms;

internal static class SourceLocationSmoke {
 static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 static string InTests(string path){string full=Path.GetFullPath(path),root=Path.GetFullPath(Path.Combine(Common.AppRoot,"tests"));if(!LocalImport.Under(full,root))throw new InvalidDataException("来源测试夹具必须位于隔离tests目录");return full;}
 static void MakeSource(string root){Directory.CreateDirectory(Path.Combine(root,"cache_info"));Directory.CreateDirectory(Path.Combine(root,"wallpaper","wpcache","dynamic"));}
 static void AddRecord(string root){
  string dynamic=Path.Combine(root,"wallpaper","wpcache","dynamic");byte[] cwp=new byte[32];Array.Copy(BitConverter.GetBytes(0x01505743u),cwp,4);Array.Copy(BitConverter.GetBytes((uint)cwp.Length),0,cwp,4,4);for(int i=8;i<cwp.Length;i++)cwp[i]=(byte)(i*7);
  string md5;using(var hash=MD5.Create())md5=BitConverter.ToString(hash.ComputeHash(cwp)).Replace("-","").ToLowerInvariant();string file=Path.Combine(dynamic,md5+".cwp");File.WriteAllBytes(file,cwp);
  byte[] record=Encoding.UTF8.GetBytes("0000"+Common.Json.Serialize(new{wpId=730011,wpName=md5+".cwp",wpMd5=md5,wpType=2,action=4}));for(int i=0;i<record.Length;i++)record[i]^=0xe7;File.WriteAllBytes(Path.Combine(root,"cache_info","730011_fixture.dat"),record);
 }
 internal static int Run(string suite){
  string child=null;try{
   suite=InTests(suite);Directory.CreateDirectory(suite);Directory.CreateDirectory(Path.Combine(Common.Root,"data"));
   string absent=Path.Combine(Common.AppRoot,"tests","cache-not-installed"),def=InTests(Path.Combine(Common.AppRoot,"tests","known-default")),custom=InTests(Path.Combine(Common.AppRoot,"tests","chosen-source")),local=InTests(Path.Combine(Common.AppRoot,"tests","local-appdata"));
   MakeSource(def);MakeSource(custom);Directory.CreateDirectory(local);Environment.SetEnvironmentVariable("LOCALWALLPAPER_TEST_LOCALAPPDATA",local);Environment.SetEnvironmentVariable("LOCALWALLPAPER_TEST_DEFAULT_SOURCE",absent);
   var missing=SourceLocation.GetState();Need(!missing.Found&&!missing.SavedCustom&&missing.Condition=="default-source-missing","没有来源时应表现为未找到，不能猜测其他位置");Need(SourceLocation.RecommendedDataParent==local,"TEST_BUILD推荐数据位置未来自隔离的LocalAppData夹具");
   Environment.SetEnvironmentVariable("LOCALWALLPAPER_TEST_DEFAULT_SOURCE",def);var found=SourceLocation.GetState();Need(found.Found&&!found.SavedCustom&&found.Condition=="default-layout-ready"&&found.RecordFiles==-1,"已知默认缓存布局未被快速识别或在窗口线程枚举记录");var emptyScan=ResourceDiscovery.Scan(false,()=>{},new string[0]);Need(emptyScan.Outcome=="no-records"&&DiscoveryPresentation.Text(emptyScan).Contains("没有可导入记录"),"空缓存未给出准确状态和下一步");
   custom=InTests(Path.Combine(Common.AppRoot,"tests","chosen source 中文 空格"));MakeSource(custom);AddRecord(custom);string config=SourceLocation.SaveRoot(custom);Need(DataLocation.Same(config,custom)&&File.Exists(SourceLocation.ConfigPath),"自选来源未保存");Need(DataLocation.Same(LocalImport.DataRoot,custom),"实际发现器数据根没有读取自选配置");
   var discovered=ResourceDiscovery.Scan(false,()=>{},new string[0]);Need(discovered.Candidates.Count==1&&discovered.Candidates[0].Id=="730011"&&DataLocation.Same(Path.GetDirectoryName(Path.GetDirectoryName(discovered.Candidates[0].Record)),custom),"发现器未从自选的已知缓存布局读取候选");
   string before=Common.Hash(SourceLocation.ConfigPath),wrong=InTests(Path.Combine(Common.AppRoot,"tests","wrong-layout"));Directory.CreateDirectory(wrong);bool rejected=false;try{SourceLocation.SaveRoot(wrong);}catch(InvalidDataException){rejected=true;}Need(rejected&&Common.Hash(SourceLocation.ConfigPath)==before&&DataLocation.Same(LocalImport.DataRoot,custom),"错误目录覆盖了有效来源配置");
   string malformed=Path.Combine(custom,"cache_info","777777_bad.dat");File.WriteAllBytes(malformed,new byte[]{1,2,3});var malformedScan=ResourceDiscovery.Scan(false,()=>{},new string[0]);Need(malformedScan.CorruptRecords==1&&DiscoveryPresentation.Text(malformedScan).Contains("记录损坏"),"损坏记录未单独分类");File.Delete(malformed);
   var customWithoutRecords=ResourceDiscovery.Scan(false,()=>{},new string[0]);Need(customWithoutRecords.Candidates.Count==1&&customWithoutRecords.Outcome=="records-found","自选有效缓存未从隔离中文空格路径发现");
   child=Path.Combine(suite,"child-result.json");var start=new ProcessStartInfo(Application.ExecutablePath,"--source-location-reopen "+Common.Quote(suite)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Common.Root};using(var process=Process.Start(start)){if(!process.WaitForExit(20000))throw new TimeoutException("新进程来源回读测试超过20秒；没有强制结束测试进程");Need(process.ExitCode==0,"新进程未能读取并使用已保存的来源配置");}
   Need(File.Exists(child),"新进程回读证据缺失");var result=Common.Json.Deserialize<SourceLocationSmokeResult>(Common.ReadShared(child));Need(result.Passed&&DataLocation.Same(result.Root,custom)&&result.CandidateCount==1,"新进程读取的来源或候选不一致");
   Common.SaveNew(Path.Combine(suite,"source-location-regression.json"),new{passed=true,scope="TEST_BUILD actual source configuration and discovery processor chain; child was a separate TEST_BUILD process",missingDefaultShownAsUnavailable=true,knownDefaultLayoutFound=true,recordEnumerationOnUiThread=false,customChineseSpacePathSaved=true,discoveryUsedCustomRoot=true,invalidLayoutRejectedWithoutOverwrite=true,corruptRecordReported=true,newProcessReadSameRoot=true,newProcessCandidateCount=result.CandidateCount,recordCountEnumeratedSynchronously=false,sourceCacheCopied=false,formalDataReadOrWritten=false,playerStarted=false,desktopChanged=false});return 0;
  }catch(Exception ex){try{if(!String.IsNullOrEmpty(suite))Common.SaveNew(Path.Combine(suite,"source-location-regression-failed.json"),new{passed=false,error=ex.ToString(),formalDataReadOrWritten=false,playerStarted=false,desktopChanged=false});}catch{}return 1;}
 }
 static void CopyTree(string source,string target){Directory.CreateDirectory(target);foreach(string name in new[]{"build","rules"}){string from=Path.Combine(source,name);if(!Directory.Exists(from))continue;string to=Path.Combine(target,name);Directory.CreateDirectory(to);foreach(string dir in Directory.GetDirectories(from,"*",SearchOption.AllDirectories)){Directory.CreateDirectory(Path.Combine(to,dir.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)));}foreach(string file in Directory.GetFiles(from,"*",SearchOption.AllDirectories)){string dest=Path.Combine(to,file.Substring(from.Length).TrimStart(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(dest));File.Copy(file,dest,false);}}}
 internal static int RunBatch(string revision,string suite){int result=Run(suite);if(result!=0)return result;string app=Common.AppRoot,package=InTests(Path.Combine(app,"tests","p"));CopyTree(Path.GetFullPath(revision),package);Common.AppRoot=package;return FirstUseRegression.Run(Path.GetFullPath(revision),Path.Combine(Path.GetFullPath(suite),"first-use-regression.json"));}
 internal static int Reopen(string suite){try{suite=InTests(suite);var state=SourceLocation.GetState();Need(state.Found&&state.SavedCustom,"新进程未回读自选来源");Need(DataLocation.Same(LocalImport.DataRoot,state.Root),"新进程发现器根不等于持久配置");var scan=ResourceDiscovery.Scan(false,()=>{},new string[0]);Need(scan.Candidates.Count==1&&scan.Candidates[0].Id=="730011","新进程发现器未使用持久来源");Common.SaveNew(Path.Combine(suite,"child-result.json"),new SourceLocationSmokeResult{Passed=true,Root=LocalImport.DataRoot,CandidateCount=scan.Candidates.Count});return 0;}catch(Exception ex){try{Common.SaveNew(Path.Combine(suite,"child-failed.json"),new{passed=false,error=ex.ToString()});}catch{}return 1;}}
}
internal sealed class SourceLocationSmokeResult{public bool Passed{get;set;}public string Root{get;set;}public int CandidateCount{get;set;}}
#endif
