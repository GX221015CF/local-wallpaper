using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal sealed partial class HostResources {
 internal string FirstCommitError;
 [StructLayout(LayoutKind.Sequential)]struct NativeIdentity {public uint Attributes;public System.Runtime.InteropServices.ComTypes.FILETIME Created,Accessed,Written;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]static extern SafeFileHandle CreateFile(string path,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32",SetLastError=true)]static extern bool GetFileInformationByHandle(SafeFileHandle file,out NativeIdentity info);
 static string DirectoryIdentity(string path){
  NoLinks(path);using(var handle=CreateFile(path,0x80,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)){
   NativeIdentity info;if(handle.IsInvalid||!GetFileInformationByHandle(handle,out info))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
   if((info.Attributes&0x10)==0||(info.Attributes&0x400)!=0)throw new InvalidDataException("宿主目录身份不是普通目录");
   return info.Volume+":"+info.IndexHigh+":"+info.IndexLow+":"+info.Created.dwHighDateTime+":"+info.Created.dwLowDateTime;
  }
 }
 static void ValidateTree(HostResources staged,string identity,IDictionary<string,string> expected,CancellationToken token){
  if(DirectoryIdentity(staged.Root)!=identity)throw new InvalidDataException("宿主暂存目录身份已变化，保留现场");
  var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var dirs=new List<string>{staged.Root};
  var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase){Path.Combine(staged.Root,"build"),Path.Combine(staged.Root,"build","runtime"),Path.Combine(staged.Root,"rules")};
  for(int i=0;i<dirs.Count;i++){
   token.ThrowIfCancellationRequested();
   foreach(string dir in Directory.GetDirectories(dirs[i])){NoLinks(dir);if(!allowed.Contains(dir))throw new InvalidDataException("宿主提交发现未知目录，保留现场");dirs.Add(dir);}
   foreach(string file in Directory.GetFiles(dirs[i])){NoLinks(file);string relative=file.Substring(staged.Root.Length+1),hash;if(!expected.TryGetValue(relative,out hash)||Hash(file,token)!=hash)throw new InvalidDataException("宿主提交文件身份已变化，保留现场");files.Add(relative);}
  }
  if(files.Count!=expected.Count||dirs.Count!=4)throw new InvalidDataException("宿主提交材料不完整，保留现场");
 }
 static void Commit(HostResources staged,string final,string identity,IDictionary<string,string> expected,CancellationToken token,List<object> attempts){
  var clock=Stopwatch.StartNew();Exception first=null;
  for(int attempt=1;attempt<=4;attempt++){
   token.ThrowIfCancellationRequested();
   try{
    NoLinks(final);if(Directory.Exists(final)||File.Exists(final))throw new IOException("宿主提交目标已被占用，未覆盖");
    ValidateTree(staged,identity,expected,token);
   }catch(OperationCanceledException){throw;}
   catch(Exception validation){validation.Data["hostRetainUntrusted"]=true;if(first!=null)validation.Data["hostFirstCommitError"]=first.ToString();throw;}
   if(first!=null&&clock.ElapsedMilliseconds>=800){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();throw new IOException("宿主恢复预算结束");}
   try{
    MoveDirectory(staged.Root,final);
    staged.Root=final;staged.Image=Path.Combine(final,"build","runtime",HostName);staged.Contract=Path.Combine(final,"rules","runtime-components.json");
    ValidateTree(staged,identity,expected,token);
    attempts.Add(new{attempt=attempt,outcome="committed",elapsedMilliseconds=clock.ElapsedMilliseconds});return;
   }catch(OperationCanceledException){throw;}
   catch(Exception error){
    if(first==null){first=error;staged.FirstCommitError=error.ToString();}
    int native=error.HResult&65535;
    attempts.Add(new{attempt=attempt,outcome="failed",nativeError=native,hresult=error.HResult,error=error.ToString(),source=staged.Root,target=final,elapsedMilliseconds=clock.ElapsedMilliseconds});
    if(!Directory.Exists(staged.Root)&&Directory.Exists(final)){
     try{
      var committed=new HostResources{Root=final,Image=Path.Combine(final,"build","runtime",HostName),Contract=Path.Combine(final,"rules","runtime-components.json")};
      ValidateTree(committed,identity,expected,token);staged.Root=committed.Root;staged.Image=committed.Image;staged.Contract=committed.Contract;
      attempts.Add(new{attempt=attempt,outcome="verified-committed-after-os-error",elapsedMilliseconds=clock.ElapsedMilliseconds});return;
     }catch{error.Data["hostRetainUntrusted"]=true;throw;}
    }
    if(staged.Root==final){error.Data["hostRetainUntrusted"]=true;throw;}
    bool recoverable=(error is IOException||error is UnauthorizedAccessException)&&(native==5||native==32||native==33);
    if(!recoverable||Directory.Exists(final)||File.Exists(final)||attempt==4||clock.ElapsedMilliseconds>=800){
     first.Data["hostCommitTarget"]=final;first.Data["hostCommitAttempts"]=attempt;first.Data["hostCommitElapsedMilliseconds"]=clock.ElapsedMilliseconds;
     System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();throw;
    }
    for(int waited=0;waited<60*attempt;waited+=15){token.ThrowIfCancellationRequested();if(clock.ElapsedMilliseconds>=800)break;Thread.Sleep(15);}
    if(clock.ElapsedMilliseconds>=800){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first).Throw();throw;}
   }
  }
 }
}
