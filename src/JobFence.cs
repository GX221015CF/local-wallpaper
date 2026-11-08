// Documented Win32 Job APIs; anonymous, non-inheritable ownership handle, held by UI only.
// No job time limit, no breakaway permission, no original Lenovo process is assigned.
using System;using System.Runtime.InteropServices;using Microsoft.Win32.SafeHandles;
internal sealed class JobFence:IDisposable {
 [StructLayout(LayoutKind.Sequential)]struct Basic {
  public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorking,MaxWorking;
  public uint ActiveLimit;public UIntPtr Affinity;public uint Priority,Scheduling;
 }
 [StructLayout(LayoutKind.Sequential)]struct Io {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
 [StructLayout(LayoutKind.Sequential)]struct Extended {public Basic Basic;public Io Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
 sealed class JobHandle:SafeHandleZeroOrMinusOneIsInvalid {
  public JobHandle():base(true){}protected override bool ReleaseHandle(){return ProcessProof.CloseHandle(handle);}
 }
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]static extern JobHandle CreateJobObjectW(IntPtr attributes,string name);
 [DllImport("kernel32",SetLastError=true)]static extern bool SetInformationJobObject(JobHandle job,int info,ref Extended limits,uint size);
 [DllImport("kernel32",SetLastError=true)]static extern bool AssignProcessToJobObject(JobHandle job,IntPtr process);
 [DllImport("kernel32",SetLastError=true)]static extern bool IsProcessInJob(IntPtr process,JobHandle job,out bool yes);
 JobHandle handle;internal readonly int StructureSize=Marshal.SizeOf(typeof(Extended));
 internal JobFence(){
  if(IntPtr.Size!=4||StructureSize!=112)throw new PlatformNotSupportedException("Expected x86 documented job layout (112 bytes)");
  handle=CreateJobObjectW(IntPtr.Zero,null);if(handle.IsInvalid)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
  var limits=new Extended();limits.Basic.Flags=0x2000;
  if(!SetInformationJobObject(handle,9,ref limits,(uint)StructureSize)){int error=Marshal.GetLastWin32Error();handle.Dispose();throw new System.ComponentModel.Win32Exception(error,"Cannot enable kill-on-job-close");}
 }
 internal void Assign(IntPtr process){if(!AssignProcessToJobObject(handle,process))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Cannot bind own worker to job");if(!Contains(process))throw new InvalidOperationException("Own job membership verification failed");}
 internal bool Contains(IntPtr process){bool yes;if(handle==null||handle.IsClosed||!IsProcessInJob(process,handle,out yes))return false;return yes;}
 public void Dispose(){if(handle!=null){handle.Dispose();handle=null;}}
}
