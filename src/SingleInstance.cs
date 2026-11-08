// One existing controller owns UI, heartbeats and Jobs even when the UI is hidden.
// The only IPC command is SHOW. The pipe is restricted to the current Windows SID.
using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Security.AccessControl;using System.Security.Principal;using System.Security.Cryptography;using System.Diagnostics;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows.Forms;
internal sealed class InstanceChannel:IDisposable {
 const string Command="SHOW-v1";readonly ControlWindow ui;NamedPipeServerStream waiting;bool disposed;
 [DllImport("kernel32",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint pid);
 [DllImport("kernel32",SetLastError=true)]static extern bool GetNamedPipeServerProcessId(IntPtr pipe,out uint pid);
 [DllImport("user32")]static extern bool AllowSetForegroundWindow(uint pid);
 static string PipeName{get{string s=WindowsIdentity.GetCurrent().User.Value+"|"+Path.GetFullPath(Application.ExecutablePath).ToUpperInvariant();using(var h=SHA256.Create())return "GXLocalWallpaper-"+BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(s))).Replace("-","").Substring(0,32);}}
 internal InstanceChannel(ControlWindow owner){ui=owner;}
 static object Peer(IntPtr pipe,bool client){uint pid;if(!(client?GetNamedPipeClientProcessId(pipe,out pid):GetNamedPipeServerProcessId(pipe,out pid)))throw new IOException("无法核验自有通信进程");using(var p=Process.GetProcessById((int)pid)){long created=ProcessProof.Creation(p.Handle);ProcessProof.Verify(p.Handle,(int)pid,created,Application.ExecutablePath);return new{pid=pid,creation=created,image=Application.ExecutablePath};}}
 internal static bool TryShow(out string reason){reason=null;try{using(var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous)){pipe.Connect(2500);Peer(pipe.SafePipeHandle.DangerousGetHandle(),false);uint serverPid;if(GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out serverPid))AllowSetForegroundWindow(serverPid);byte[] b=Encoding.ASCII.GetBytes(Command);pipe.Write(b,0,b.Length);pipe.Flush();byte[] ack=new byte[2];var task=Task<int>.Factory.FromAsync(pipe.BeginRead,pipe.EndRead,ack,0,2,null);if(!task.Wait(3000)||task.Result!=2||Encoding.ASCII.GetString(ack)!="OK")throw new IOException("已有实例尚未确认界面已恢复");return true;}}catch(Exception ex){reason=ex.Message;return false;}}
 internal async void Start(){
  while(!disposed){NamedPipeServerStream pipe=null;try{
   var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
   pipe=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,256,256,security);waiting=pipe;
   await Task.Factory.FromAsync(pipe.BeginWaitForConnection,pipe.EndWaitForConnection,null);if(disposed)break;object peer=Peer(pipe.SafePipeHandle.DangerousGetHandle(),true);
   byte[] bytes=new byte[Command.Length];int read=0;var deadline=Stopwatch.StartNew();while(read<bytes.Length){var task=Task<int>.Factory.FromAsync(pipe.BeginRead,pipe.EndRead,bytes,read,bytes.Length-read,null);if(await Task.WhenAny(task,Task.Delay(Math.Max(1,1500-(int)deadline.ElapsedMilliseconds)))!=task)throw new IOException("通信读取超时");int n=await task;if(n==0)throw new IOException("通信不完整");read+=n;}
   if(Encoding.ASCII.GetString(bytes)!=Command)throw new InvalidDataException("拒绝未知命令");
   if(ui.RestoreInterface()){ui.Write("instance-reactivated",new{peer=peer,command=Command,sameController=true,noPlaybackRequested=true});byte[] ack=Encoding.ASCII.GetBytes("OK");await pipe.WriteAsync(ack,0,ack.Length);}
  }catch(Exception ex){if(!disposed)ui.Write("instance-channel-rejected",new{reason=ex.GetType().Name});}
  finally{if(waiting==pipe)waiting=null;if(pipe!=null)pipe.Dispose();}
  }
 }
 public void Dispose(){disposed=true;if(waiting!=null){waiting.Dispose();waiting=null;}}
}
