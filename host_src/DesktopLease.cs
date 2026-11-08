using System;using System.Text;using System.Runtime.InteropServices;using System.Diagnostics;using System.Drawing;
internal static class Win {
 public delegate bool EnumProc(IntPtr hwnd,IntPtr param);
 [StructLayout(LayoutKind.Sequential)]public struct RECT{public int L,T,R,B;public Rectangle Rectangle{get{return Rectangle.FromLTRB(L,T,R,B);}}}
 [StructLayout(LayoutKind.Sequential)]public struct POINT{public int X,Y;public POINT(int x,int y){X=x;Y=y;}}
 [DllImport("user32")]public static extern bool EnumWindows(EnumProc p,IntPtr arg);
 [DllImport("user32",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
 [DllImport("user32",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 public static string Class(IntPtr h){var b=new StringBuilder(128);GetClassName(h,b,128);return b.ToString();}
 [DllImport("user32")]public static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32")]public static extern IntPtr GetWindow(IntPtr h,uint cmd);
 [DllImport("user32")]public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 public static uint Pid(IntPtr h){uint p;GetWindowThreadProcessId(h,out p);return p;}
 [DllImport("user32")]public static extern bool IsWindow(IntPtr h);
 [DllImport("user32")]public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32")]public static extern IntPtr GetForegroundWindow();
 [DllImport("user32")]public static extern int GetWindowLongW(IntPtr h,int index);
 [DllImport("user32",SetLastError=true)]public static extern int SetWindowLongW(IntPtr h,int index,int value);
 [DllImport("user32")]public static extern uint GetClassLongW(IntPtr h,int index);
 [DllImport("user32",SetLastError=true)]public static extern bool SetLayeredWindowAttributes(IntPtr h,uint color,byte alpha,uint flags);
 [DllImport("user32",SetLastError=true)]public static extern IntPtr SetParent(IntPtr child,IntPtr parent);
 [DllImport("user32",SetLastError=true)]public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32")]public static extern bool GetWindowRect(IntPtr h,out RECT rect);
 [DllImport("user32")]public static extern bool GetClientRect(IntPtr h,out RECT rect);
 [DllImport("user32")]public static extern int MapWindowPoints(IntPtr from,IntPtr to,ref POINT p,uint count);
 [DllImport("user32")]public static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32")]public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr h);
 [DllImport("user32")]public static extern bool AreDpiAwarenessContextsEqual(IntPtr a,IntPtr b);
 [DllImport("user32")]public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
 [DllImport("user32")]public static extern bool ShowWindowAsync(IntPtr h,int command);
 [DllImport("user32")]public static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
 [DllImport("kernel32")]public static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32")]public static extern bool TerminateProcess(IntPtr h,uint exitCode);
 public static object Geometry(IntPtr h){RECT r,c;GetWindowRect(h,out r);GetClientRect(h,out c);return new{hwnd=h.ToInt64(),pid=Pid(h),parent=GetParent(h).ToInt64(),rect=new[]{r.L,r.T,r.R,r.B},client=new[]{c.R,c.B},dpi=GetDpiForWindow(h),style=GetWindowLongW(h,-16).ToString("x8"),exstyle=GetWindowLongW(h,-20).ToString("x8"),previous=GetWindow(h,3).ToInt64(),next=GetWindow(h,2).ToInt64()};}
}
internal sealed class DesktopLease {
 public IntPtr Root,Icons,Host;public uint ExplorerPid;IntPtr owned,oldParent;int oldStyle,oldEx;bool attached;
 readonly Action<string,object> log;
 public DesktopLease(Action<string,object> logger){log=logger;}
 public void Discover(Rectangle monitor){
  IntPtr root=IntPtr.Zero,icons=IntPtr.Zero,host=IntPtr.Zero;
  Win.EnumWindows(delegate(IntPtr h,IntPtr p){
   string cl=Win.Class(h);if(cl!="Progman"&&cl!="WorkerW")return true;
   var v=Win.FindWindowEx(h,IntPtr.Zero,"SHELLDLL_DefView",null);if(v==IntPtr.Zero)return true;
   // Admit only the actual Windows 11 sibling-backed layout observed here.
   for(IntPtr w=Win.GetWindow(v,2);w!=IntPtr.Zero;w=Win.GetWindow(w,2)){
    Win.RECT r;Win.GetWindowRect(w,out r);
    if(Win.Class(w)=="WorkerW"&&Win.GetParent(w)==h&&Win.IsWindowVisible(w)&&r.Rectangle.Contains(monitor)){
     uint pid=Win.Pid(w);if(Win.Pid(h)!=pid||Win.Pid(v)!=pid)return true;
     if(Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant()!="explorer")return true;
     root=h;icons=v;host=w;return false;
    }
   }return true;
  },IntPtr.Zero);
  if(host==IntPtr.Zero)throw new Exception("No existing verified Explorer desktop container; no creation messages sent");
  Root=root;Icons=icons;Host=host;ExplorerPid=Win.Pid(host);log("desktop-plan",new{root=Win.Geometry(root),icons=Win.Geometry(icons),host=Win.Geometry(host),createsSharedHost=false});
 }
 public void Attach(IntPtr window,Rectangle monitor){
  if(Win.Pid(window)!=(uint)Process.GetCurrentProcess().Id)throw new Exception("Not an owned HWND");
  if(!Win.IsWindow(Host)||Win.Pid(Host)!=ExplorerPid||Win.GetParent(Icons)!=Root||Win.GetParent(Host)!=Root)throw new Exception("Desktop topology changed");
  if(!Win.AreDpiAwarenessContextsEqual(Win.GetWindowDpiAwarenessContext(window),Win.GetWindowDpiAwarenessContext(Root)))throw new Exception("DPI awareness contexts differ; no forced reset attempted");
  if((Win.GetClassLongW(window,-26)&0x60)!=0)throw new Exception("Layered container cannot have CS_OWNDC/CS_CLASSDC");
  owned=window;oldParent=Win.GetParent(window);oldStyle=Win.GetWindowLongW(window,-16);oldEx=Win.GetWindowLongW(window,-20);
  uint style=(uint)oldStyle;style&=~(0x80000000u|0x00c00000u|0x00040000u|0x00080000u|0x00020000u|0x00010000u);style|=0x40000000u|0x02000000u|0x04000000u;
  Win.SetWindowLongW(window,-16,unchecked((int)style));Win.SetWindowLongW(window,-20,(oldEx|0x08080080)&~0x00040000);
  Win.SetParent(window,Root);attached=Win.GetParent(window)==Root;if(!attached){Detach();throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"SetParent failed");}
  if(!Win.SetLayeredWindowAttributes(window,0,255,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Own layered alpha failed");
  var pos=new Win.POINT(monitor.Left,monitor.Top);Win.MapWindowPoints(IntPtr.Zero,Root,ref pos,1);
  if(!Win.SetWindowPos(window,Icons,pos.X,pos.Y,monitor.Width,monitor.Height,0x10|0x20))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"SetWindowPos failed");
  Win.RECT r;Win.GetWindowRect(window,out r);if(r.Rectangle!=monitor)throw new Exception("Desktop bounds mismatch: "+r.Rectangle.ToString());
  log("desktop-attached",new{owned=Win.Geometry(window),host=Root.ToInt64(),backingWorker=Host.ToInt64(),iconRoot=Icons.ToInt64(),strategy="own-layered-Progman-child",foreground=Win.GetForegroundWindow().ToInt64()});
 }
 public bool Valid(){return attached&&Win.IsWindow(Host)&&Win.Pid(Host)==ExplorerPid&&Win.GetParent(owned)==Root&&Win.IsWindow(Icons)&&Win.GetParent(Host)==Root;}
 public void Detach(){
  if(owned==IntPtr.Zero||!Win.IsWindow(owned))return;
  if(Win.Pid(owned)!=(uint)Process.GetCurrentProcess().Id)return;
  Win.ShowWindowAsync(owned,0);Win.SetParent(owned,oldParent);Win.SetWindowLongW(owned,-16,oldStyle);Win.SetWindowLongW(owned,-20,oldEx);attached=false;
  log("desktop-detached",new{owned=Win.Geometry(owned),onlyOwnedWindowModified=true});owned=IntPtr.Zero;
 }
}
