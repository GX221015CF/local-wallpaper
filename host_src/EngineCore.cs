// Same x86 playback ABI verified by native_host_20261027; no launcher or IPC.
using System;using System.IO;using System.Diagnostics;using System.Runtime.InteropServices;using System.Security.Cryptography;using System.Collections.Generic;using System.Web.Script.Serialization;
internal sealed class EngineCore:IDisposable {
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryW(string path);
 [DllImport("kernel32",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr m,string n);
 [DllImport("kernel32",SetLastError=true)]static extern bool SetDefaultDllDirectories(uint flags);
 [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr AddDllDirectory(string path);
 [DllImport("kernel32",SetLastError=true)]static extern bool RemoveDllDirectory(IntPtr cookie);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate IntPtr Factory(IntPtr hwnd,IntPtr options);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)]internal delegate void Release(IntPtr self);
 [UnmanagedFunctionPointer(CallingConvention.StdCall,CharSet=CharSet.Unicode)]internal delegate int SetPath(IntPtr self,[MarshalAs(UnmanagedType.LPWStr)]string path);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate int SetNumber(IntPtr self,uint value);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate int Start(IntPtr self,IntPtr callback,IntPtr reserved);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate int Stop(IntPtr self);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate int GetWindow(IntPtr self,out IntPtr window);
 [UnmanagedFunctionPointer(CallingConvention.StdCall)]internal delegate void WindowEvent(IntPtr self,IntPtr window,uint message,IntPtr buttons,IntPtr coordinates);
 IntPtr inputWindow;WindowEvent windowEvent;bool playbackStarted;
 internal bool CanForwardPointer{get{return playbackStarted&&self!=IntPtr.Zero&&windowEvent!=null;}}
 IntPtr module,self,rootCookie,depdllCookie;Release release;readonly Action<string,object> log;string engineRoot;
 public EngineCore(Action<string,object> logger){log=logger;}
 T Method<T>(int offset)where T:class{return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self),offset),typeof(T))as T;}
 void Check(string name,int hr){log(name,new{result=hr.ToString("x8")});if(hr<0)throw new Exception(name+"="+hr.ToString("x8"));}
 static string Hash(string path){using(var input=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
 static string ContractValue(Dictionary<string,object> row,string name){return row.ContainsKey(name)?Convert.ToString(row[name]):"";}
 static void NoLinks(string path){string full=Path.GetFullPath(path);for(string p=full;!String.IsNullOrEmpty(p);p=Path.GetDirectoryName(p)){if(File.Exists(p)||Directory.Exists(p))if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("组件目录或文件包含重解析点");if(Path.GetDirectoryName(p)==p)break;}}
 void VerifyExternal(string supplied,string contractHash,string hostHash){
  engineRoot=Path.GetFullPath(supplied);NoLinks(engineRoot);if(!Directory.Exists(engineRoot))throw new DirectoryNotFoundException("外部组件目录不存在");
  string program=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",".."));string contract=Path.Combine(program,"rules","runtime-components.json");NoLinks(contract);
  if(Hash(contract)!=contractHash)throw new InvalidDataException("运行组件契约身份不符");var doc=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(contract));var entries=doc["files"] as System.Collections.IList;if(entries==null)throw new InvalidDataException("运行组件契约files必须是数组");if(entries.Count!=24)throw new InvalidDataException("运行组件契约数量不符，要求24项，实际"+entries.Count);int count=0;bool hostSeen=false;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(object entry in entries){var row=entry as Dictionary<string,object>;if(row==null)throw new InvalidDataException("运行组件契约格式无效");string name=ContractValue(row,"name"),expected=ContractValue(row,"sha256");if(String.IsNullOrWhiteSpace(name)||!names.Add(name))throw new InvalidDataException("运行组件契约含空名称或重复组件："+name);if(name=="Desktop317495Host-refined.exe"){hostSeen=true;string own=Process.GetCurrentProcess().MainModule.FileName;if(expected!=hostHash||Hash(own)!=hostHash)throw new InvalidDataException("自有宿主与已验证契约身份不符");continue;}if(Path.GetFileName(name)!=name||name.Contains(":")||expected.Length!=64)throw new InvalidDataException("运行组件契约路径无效");string direct=Path.Combine(engineRoot,name),nested=Path.Combine(engineRoot,"depdlls",name);bool hasDirect=File.Exists(direct),hasNested=File.Exists(nested);if(hasDirect==hasNested)throw new InvalidDataException("外部组件缺失或重复："+name);string path=hasDirect?direct:nested;NoLinks(path);if(Hash(path)!=expected)throw new InvalidDataException("外部组件在宿主复核时身份变化："+name);count++;}
  if(count!=23||!hostSeen)throw new InvalidDataException("外部组件契约应为23项且含自有宿主身份");
 }
 public void Open(IntPtr hwnd,string cwpRoot,string cwpFile,string externalRoot,string contractHash,string hostHash,uint volume){
#if TEST_BUILD
  if(TestOpenFactory!=null){release=TestOpenRelease;InitializePlayback(hwnd,cwpRoot,cwpFile,volume,TestOpenFactory,"managed-ABI-substitute");return;}
#endif
  VerifyExternal(externalRoot,contractHash,hostHash);Directory.SetCurrentDirectory(engineRoot);
  // Restrict native DLL lookup to system locations and the two verified install folders.
  const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS=0x1000,LOAD_LIBRARY_SEARCH_USER_DIRS=0x400;
  if(!SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS|LOAD_LIBRARY_SEARCH_USER_DIRS))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"SetDefaultDllDirectories");
  rootCookie=AddDllDirectory(engineRoot);if(rootCookie==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"AddDllDirectory install root");
  string dep=Path.Combine(engineRoot,"depdlls");if(Directory.Exists(dep)){NoLinks(dep);depdllCookie=AddDllDirectory(dep);if(depdllCookie==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"AddDllDirectory depdlls");}
  string enginePath=Path.Combine(engineRoot,"jlwpengine.dll");NoLinks(enginePath);module=LoadLibraryW(enginePath);if(module==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"LoadLibraryW verified jlwpengine.dll");
  var create=(Factory)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module,"CreateCFDynWallpaperEngine"),typeof(Factory));
  release=(Release)Marshal.GetDelegateForFunctionPointer(GetProcAddress(module,"ReleaseCFDynWallpaperEngine"),typeof(Release));
  InitializePlayback(hwnd,cwpRoot,cwpFile,volume,create,enginePath);
 }
 void InitializePlayback(IntPtr hwnd,string cwpRoot,string cwpFile,uint volume,Factory create,string enginePath){
  // Factory option kind 2 constructs CFDynWallpaperEngineEditor, not a video player.
  // The pinned official caller uses the default engine and public slot 0x48 for media kind.
  bool video=String.Equals(Path.GetExtension(cwpFile),".mp4",StringComparison.OrdinalIgnoreCase);self=create(hwnd,IntPtr.Zero);if(self==IntPtr.Zero)throw new Exception("Null engine");IntPtr result;
  Check("get-engine-window",Method<GetWindow>(0)(self,out result));if(result!=hwnd)throw new Exception("Engine HWND mismatch");
  if(video)Check("set-media-kind-video",Method<SetNumber>(0x48)(self,1));
  log("engine-input",new{engineDirectory=engineRoot,workingDirectory=Directory.GetCurrentDirectory(),resourceRoot=cwpRoot,cwpFile=cwpFile,actualCwp=Path.Combine(cwpRoot,cwpFile),externalComponentsRechecked=true,loadedModulePath=enginePath});
  Check("set-root",Method<SetPath>(4)(self,cwpRoot));Check(video?"set-video":"set-cwp",Method<SetPath>(12)(self,cwpFile));
  // Only the pinned LX-video-mp4-xor16 format reaches this branch through playback policy.
  // Keep the transport bytes intact and select the official encoded-input reader explicitly.
  if(video)Check("set-video-transport",Method<SetNumber>(0x4c)(self,1));
  SetVolume(volume);Check("request-fps",Method<SetNumber>(0x20)(self,30));
  Check("engine-start",Method<Start>(0x2c)(self,IntPtr.Zero,IntPtr.Zero));
  // Pinned official lxwplayer forwards render-window messages through slot 0x3c.
  // WM_MOUSEMOVE reaches GameSceneEventReceiver and mouse-bound particle control points.
  if(!video){windowEvent=Method<WindowEvent>(0x3c);inputWindow=hwnd;}
  playbackStarted=true;
 }
 internal bool ForwardMouseMove(int x,int y){
  if(!CanForwardPointer||x<0||y<0||x>Int16.MaxValue||y>Int16.MaxValue)return false;
  int packed=unchecked((int)(((uint)(ushort)y<<16)|(ushort)x));
  windowEvent(self,inputWindow,0x200,IntPtr.Zero,new IntPtr(packed));return true;
 }
#if TEST_BUILD
 internal static Factory TestOpenFactory;internal static Release TestOpenRelease;
 internal void TestInitialize(IntPtr hwnd,string root,string file,uint volume,Factory factory,Release destroy){release=destroy;InitializePlayback(hwnd,root,file,volume,factory,"managed-ABI-substitute");}
#endif

 public void SetVolume(uint volume){if(self==IntPtr.Zero)throw new InvalidOperationException("Engine not created");Check("set-volume",Method<SetNumber>(0x18)(self,Math.Min(100u,volume)));}
#if TEST_BUILD
 internal static Func<IntPtr,bool> TestRemoveCookie;
#endif
 static bool RemoveCookie(IntPtr cookie){
#if TEST_BUILD
  if(TestRemoveCookie!=null)return TestRemoveCookie(cookie);
#endif
  return RemoveDllDirectory(cookie);
 }
 public void Dispose(){
  playbackStarted=false;windowEvent=null;inputWindow=IntPtr.Zero;
  var errors=new List<Exception>();
  if(self!=IntPtr.Zero){
   IntPtr target=self;Stop stop=null;
   try{stop=Method<Stop>(0x30);}catch(Exception ex){errors.Add(ex);}
   var releaseTarget=release;self=IntPtr.Zero;release=null;
   try{log("engine-stop-begin",new{});}catch(Exception ex){errors.Add(ex);}
   try{if(stop!=null){int hr=stop(target);if(hr<0)errors.Add(new Exception("engine-stop="+hr.ToString("x8")));try{log("engine-stop",new{result=hr.ToString("x8")});}catch(Exception ex){errors.Add(ex);}}}catch(Exception ex){errors.Add(ex);}
   try{if(releaseTarget==null)throw new InvalidOperationException("Engine release callback unavailable");releaseTarget(target);log("engine-released",new{});}catch(Exception ex){errors.Add(ex);}
  }
  IntPtr dep=depdllCookie;depdllCookie=IntPtr.Zero;
  try{if(dep!=IntPtr.Zero&&!RemoveCookie(dep))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"RemoveDllDirectory depdlls");}catch(Exception ex){errors.Add(ex);}
  IntPtr root=rootCookie;rootCookie=IntPtr.Zero;
  try{if(root!=IntPtr.Zero&&!RemoveCookie(root))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"RemoveDllDirectory root");}catch(Exception ex){errors.Add(ex);}
  if(errors.Count>0)throw new AggregateException("引擎停止或释放失败；已独立尝试释放与目录清理，详情保留",errors);
 }
}
