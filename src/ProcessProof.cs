using System;using System.IO;using System.Diagnostics;using System.Text;using System.Runtime.InteropServices;using System.Collections.Generic;
internal static class ProcessProof {
 [StructLayout(LayoutKind.Sequential)]internal struct FT{public uint Low,High;public long Value{get{return unchecked((long)(((ulong)High<<32)|Low));}}}
 [DllImport("kernel32",SetLastError=true)]static extern bool GetProcessTimes(IntPtr h,out FT c,out FT e,out FT k,out FT u);
 [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Unicode)]static extern bool QueryFullProcessImageNameW(IntPtr h,uint flags,StringBuilder b,ref uint n);
 [DllImport("kernel32")]static extern uint GetProcessId(IntPtr h);
 [DllImport("kernel32",SetLastError=true)]static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
 [DllImport("kernel32")]internal static extern bool CloseHandle(IntPtr h);
 [DllImport("kernel32")]static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32",SetLastError=true)]static extern bool DuplicateHandle(IntPtr source,IntPtr handle,IntPtr target,out IntPtr result,uint access,bool inherit,uint options);
 [DllImport("kernel32",SetLastError=true)]static extern bool GetExitCodeProcess(IntPtr h,out uint code);
 internal static long Creation(IntPtr h){FT c,e,k,u;if(!GetProcessTimes(h,out c,out e,out k,out u))throw new System.ComponentModel.Win32Exception();return c.Value;}
 internal static string Image(IntPtr h){var b=new StringBuilder(32768);uint n=(uint)b.Capacity;if(!QueryFullProcessImageNameW(h,0,b,ref n))throw new System.ComponentModel.Win32Exception();return Path.GetFullPath(b.ToString());}
 internal static void Verify(IntPtr h,int pid,long creation,string image){if(h==IntPtr.Zero||GetProcessId(h)!=(uint)pid||Creation(h)!=creation||!String.Equals(Image(h),Path.GetFullPath(image),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("进程句柄身份核验失败");}
 internal static IntPtr TransferToController(IntPtr child,Dictionary<string,object> req){
  IntPtr target=OpenProcess(0x1040,false,Convert.ToUInt32(req["controllerPid"]));if(target==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();
  try{Verify(target,Convert.ToInt32(req["controllerPid"]),Convert.ToInt64(req["controllerCreationFileTime"]),Common.S(req,"controllerImage"));IntPtr copy;
   if(!DuplicateHandle(GetCurrentProcess(),child,target,out copy,0x101001,false,0))throw new System.ComponentModel.Win32Exception();return copy;
  }finally{CloseHandle(target);}
 }
 internal static uint ExitCode(IntPtr h){uint code;if(!GetExitCodeProcess(h,out code))throw new System.ComponentModel.Win32Exception();return code;}
}
internal sealed class HeldPlayer:IDisposable {
 internal readonly IntPtr Handle;internal readonly int Pid;internal readonly long Creation;internal readonly string Image,Run,Token;
 internal HeldPlayer(IntPtr h,int pid,long created,string image,string run,string token){ProcessProof.Verify(h,pid,created,image);if(Common.Hash(image)!=Common.HostHash)throw new InvalidDataException("播放器映像哈希不符");Handle=h;Pid=pid;Creation=created;Image=image;Run=run;Token=token;}
 internal string State{get{uint s=Common.WaitForSingleObject(Handle,0);if(s==0)return "exited";if(s==258)return "alive";return "unknown";}}
 internal object Proof{get{return new{pid=Pid,creationFileTime=Creation.ToString(),image=Image,run=Run,token=Token,source="duplicated-direct-child-handle",verifiedThroughHandle=true};}}
 internal void Kill(uint code){ProcessProof.Verify(Handle,Pid,Creation,Image);if(State=="alive"&&!Common.TerminateProcess(Handle,code))throw new System.ComponentModel.Win32Exception();}
 public void Dispose(){ProcessProof.CloseHandle(Handle);}
}
