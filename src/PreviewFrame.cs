using System;using System.IO;using System.Linq;using System.Drawing;using System.Drawing.Imaging;using System.Drawing.Drawing2D;using System.Runtime.InteropServices;using System.Collections.Generic;
internal sealed class PreviewFrameTarget {
 public string Run{get;set;}public string Token{get;set;}public string ResourceSha256{get;set;}public string RuntimeCwp{get;set;}public string CatalogId{get;set;}public int PlayerPid{get;set;}public long PlayerCreation{get;set;}public string PlayerImage{get;set;}public long Window{get;set;}public long Render{get;set;}
}
// Public Win32 window capture only. No engine ABI, injection, screen DC or process memory access.
internal static class PreviewFrame {
 [StructLayout(LayoutKind.Sequential)]internal struct Rect{public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
 [DllImport("user32")]static extern bool IsWindow(IntPtr h);
 [DllImport("user32")]static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32")]static extern bool IsIconic(IntPtr h);
 [DllImport("user32")]static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32")]static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32")]static extern bool GetClientRect(IntPtr h,out Rect r);
 [DllImport("user32")]static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32")]static extern bool ClientToScreen(IntPtr h,ref Point p);
 [DllImport("user32")]static extern IntPtr GetForegroundWindow();
 internal static bool ForegroundIs(long hwnd){return GetForegroundWindow()==new IntPtr(hwnd);}
 [DllImport("user32")]internal static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32")]static extern IntPtr GetTopWindow(IntPtr h);
 [DllImport("user32")]static extern IntPtr GetWindow(IntPtr h,uint cmd);
 [DllImport("user32")]static extern IntPtr GetDC(IntPtr h);
 [DllImport("user32")]static extern int ReleaseDC(IntPtr h,IntPtr dc);
 [DllImport("user32")]static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("gdi32")]static extern bool BitBlt(IntPtr to,int x,int y,int w,int h,IntPtr from,int sx,int sy,uint op);
 [DllImport("kernel32")]static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 internal static void Windows(PreviewFrameTarget t){uint topPid,renderPid;var top=new IntPtr(t.Window);var render=new IntPtr(t.Render);GetWindowThreadProcessId(top,out topPid);GetWindowThreadProcessId(render,out renderPid);if(!IsWindow(top)||!IsWindow(render)||topPid!=t.PlayerPid||renderPid!=t.PlayerPid||GetParent(render)!=top||GetParent(top)!=IntPtr.Zero||!IsWindowVisible(top)||!IsWindowVisible(render)||IsIconic(top))throw new InvalidDataException("预览窗口已失效、最小化或身份不符");}
 static bool Uncovered(PreviewFrameTarget t){Windows(t);var top=new IntPtr(t.Window);if(GetForegroundWindow()!=top)return false;Rect client;if(!GetClientRect(new IntPtr(t.Render),out client))return false;var origin=new Point();if(!ClientToScreen(new IntPtr(t.Render),ref origin))return false;var target=new Rectangle(origin.X,origin.Y,client.Right,client.Bottom);int visited=0;for(var h=GetTopWindow(IntPtr.Zero);h!=IntPtr.Zero&&h!=top;h=GetWindow(h,2)){if(++visited>1024)return false;if(!IsWindowVisible(h)||IsIconic(h))continue;Rect r;if(GetWindowRect(h,out r)&&new Rectangle(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top).IntersectsWith(target))return false;}return true;}
 internal sealed class FrameAssessment {
  public int Samples{get;set;}public int NearBlack{get;set;}public double Variance{get;set;}public bool Informative{get;set;}
 }
 internal static FrameAssessment Assess(Bitmap image){
  double sum=0,squared=0;int count=0,nearBlack=0;
  for(int y=0;y<image.Height;y+=Math.Max(1,image.Height/50))for(int x=0;x<image.Width;x+=Math.Max(1,image.Width/70)){
   var pixel=image.GetPixel(x,y);double luminance=(pixel.R*299+pixel.G*587+pixel.B*114)/1000.0;
   sum+=luminance;squared+=luminance*luminance;count++;if(luminance<4)nearBlack++;
  }
  double variance=count==0?0:squared/count-(sum/count)*(sum/count);
  return new FrameAssessment{Samples=count,NearBlack=nearBlack,Variance=variance,Informative=count>0&&nearBlack<count*.995&&variance>4};
 }
 internal static bool Informative(Bitmap image){return Assess(image).Informative;}
 internal static void ValidateTarget(PreviewFrameTarget t){
  if(t==null||!LocalImport.Under(t.Run,Path.Combine(Common.Root,"logs"))||!PackageAnalysis.HashText(t.ResourceSha256))throw new InvalidDataException("截图目标不属于本次自有预览");
  LocalImport.NoLinks(t.Run);LocalImport.NoLinks(t.RuntimeCwp);
  if(!LocalImport.Under(t.RuntimeCwp,Path.Combine(t.Run,"input")))RuntimeInputs.VerifyCaptureInput(t.Run,t.RuntimeCwp,t.Token,t.ResourceSha256);
  var req=Common.Read(Path.Combine(t.Run,"request.json"));var load=Common.Read(Path.Combine(t.Run,"load-input.json"));if(Common.S(req,"mode")!="preview"||Common.S(req,"runToken")!=t.Token||Common.S(req,"expectedSha256")!=t.ResourceSha256||Common.S(req,"selectedCatalogId")!=t.CatalogId||Common.S(load,"sourceSha256")!=t.ResourceSha256||Common.S(load,"runtimeSha256")!=t.ResourceSha256||Common.S(load,"runtimeCwp")!=t.RuntimeCwp)throw new InvalidDataException("实际加载资源证据不符");
 }

 internal sealed class CapturedFrame : IDisposable {
  internal Bitmap Image;internal bool Painted;internal string Method;internal int Attempts;
  internal bool PrintSucceeded,BitBltSucceeded;
  public void Dispose(){if(Image!=null)Image.Dispose();}
 }
 internal sealed class FrameChangedException : IOException {internal FrameChangedException(string message):base(message){}}
 internal static CapturedFrame AcquireReadyFrame(Func<CapturedFrame> capture,Action check,Action<string> phase){
  return AcquireReadyFrame(capture,check,phase,null);
 }
 internal static CapturedFrame AcquireReadyFrame(Func<CapturedFrame> capture,Action check,Action<string> phase,Action<int,object> observe){
  var clock=System.Diagnostics.Stopwatch.StartNew();string last="尚未取得画面";
  for(int attempt=1;attempt<=6;attempt++){
   check();if(phase!=null)phase("capture-frame-acquire");
   CapturedFrame frame=null;
   try{
    frame=capture();check();var assessment=Assess(frame.Image);
    if(observe!=null)observe(attempt,new{method=frame.Method,printWindow=frame.PrintSucceeded,bitBlt=frame.BitBltSucceeded,painted=frame.Painted,
     width=frame.Image.Width,height=frame.Image.Height,assessment=assessment,elapsedMilliseconds=clock.ElapsedMilliseconds});
    if(frame.Painted&&assessment.Informative){frame.Attempts=attempt;var accepted=frame;frame=null;return accepted;}
    last="捕获接口未返回有效内容，空白或黑图已拒绝";
   }catch(FrameChangedException error){
    last=error.Message;if(observe!=null)observe(attempt,new{reason="window-size-changed",error=last,elapsedMilliseconds=clock.ElapsedMilliseconds});
   }finally{if(frame!=null)frame.Dispose();}
   if(attempt==6||clock.ElapsedMilliseconds>=1500)break;
   if(phase!=null)phase("capture-frame-readiness-wait");
   int delay=Math.Min(180,60*attempt);
   for(int waited=0;waited<delay;waited+=20){check();if(clock.ElapsedMilliseconds>=1500)break;System.Threading.Thread.Sleep(Math.Min(20,delay-waited));}
   if(clock.ElapsedMilliseconds>=1500)break;
  }
  if(phase!=null)phase("capture-frame-validity");
  throw new InvalidDataException(last+"；有限取帧预算已结束，未保存封面。");
 }
 static CapturedFrame CaptureOnce(PreviewFrameTarget target,Action check,Action<string> phase){
  Windows(target);Rect bounds;
  if(!GetClientRect(new IntPtr(target.Render),out bounds)||bounds.Right<32||bounds.Bottom<32||bounds.Right>8192||bounds.Bottom>8192||(long)bounds.Right*bounds.Bottom>12000000)
   throw new InvalidDataException("预览画面尺寸不在捕获预算内");
  var frame=new CapturedFrame{Image=new Bitmap(bounds.Right,bounds.Bottom,PixelFormat.Format32bppRgb),Method="PrintWindow-render-client"};
  try{
   if(phase!=null)phase("capture-render-printwindow");
   using(var graphics=Graphics.FromImage(frame.Image)){
    graphics.Clear(Color.Black);var dc=graphics.GetHdc();
    try{frame.Painted=frame.PrintSucceeded=PrintWindow(new IntPtr(target.Render),dc,3);}finally{graphics.ReleaseHdc(dc);}
   }
   check();
   if(!frame.Painted||!Informative(frame.Image)){
    if(!Uncovered(target))throw new InvalidDataException("未取得渲染画面；请保持预览窗口完整可见，未保存黑图");
    frame.Method="BitBlt-own-visible-render-client";
    if(phase!=null)phase("capture-visible-render-bitblt");
    var source=GetDC(new IntPtr(target.Render));
    if(source==IntPtr.Zero)throw new InvalidDataException("无法取得自有渲染窗口画面");
    try{using(var graphics=Graphics.FromImage(frame.Image)){
     var dc=graphics.GetHdc();
     try{frame.Painted=frame.BitBltSucceeded=BitBlt(dc,0,0,bounds.Right,bounds.Bottom,source,0,0,0x00cc0020);}finally{graphics.ReleaseHdc(dc);}
    }}finally{ReleaseDC(new IntPtr(target.Render),source);}
    if(!Uncovered(target))throw new InvalidDataException("捕获期间预览被遮挡，未保存图片");
   }
   Windows(target);Rect finalSize;
   if(!GetClientRect(new IntPtr(target.Render),out finalSize)||finalSize.Right!=bounds.Right||finalSize.Bottom!=bounds.Bottom)
    throw new FrameChangedException("捕获期间预览尺寸已变化，等待同一窗口稳定");
   return frame;
  }catch{frame.Dispose();throw;}
 }
 internal static object Capture(string folder,PreviewFrameTarget target,Action check){return Capture(folder,target,check,null);}
 internal static object Capture(string folder,PreviewFrameTarget target,Action check,Action<string> phase){
  if(phase!=null)phase("capture-target-validation");
  ValidateTarget(target);
  IntPtr held=OpenProcess(0x101000,false,target.PlayerPid);
  if(held==IntPtr.Zero)throw new InvalidDataException("无法核验预览进程");
  try{
   ProcessProof.Verify(held,target.PlayerPid,target.PlayerCreation,target.PlayerImage);
   if(Common.Hash(target.PlayerImage)!=Common.HostHash||Common.WaitForSingleObject(held,0)!=258)throw new InvalidDataException("播放器身份或状态不符");
   check();LocalImport.NoLinks(target.RuntimeCwp);string signature=CoverCache.Signature(target.RuntimeCwp);
   if(LocalImport.HashCancelable(target.RuntimeCwp,check)!=target.ResourceSha256)throw new InvalidDataException("预览实际加载副本哈希不符");
   using(var frame=AcquireReadyFrame(()=>CaptureOnce(target,check,phase),check,phase,
    (attempt,observation)=>Common.SaveNew(Path.Combine(folder,"frame-attempt-"+attempt.ToString("00")+".json"),
     new{run=target.Run,resourceSha256=target.ResourceSha256,window=target.Window,render=target.Render,observation=observation}))){
    check();Windows(target);Rect acceptedSize;if(!GetClientRect(new IntPtr(target.Render),out acceptedSize)||acceptedSize.Right!=frame.Image.Width||acceptedSize.Bottom!=frame.Image.Height)throw new InvalidDataException("有效帧取得后窗口尺寸再次变化，未保存图片");ProcessProof.Verify(held,target.PlayerPid,target.PlayerCreation,target.PlayerImage);
    if(Common.WaitForSingleObject(held,0)!=258||CoverCache.Signature(target.RuntimeCwp)!=signature)throw new InvalidDataException("捕获期间预览已停止或资源已变化");
    var raw=frame.Image;
    double scale=Math.Min(1,Math.Min(480.0/raw.Width,300.0/raw.Height));
    int width=Math.Max(1,(int)Math.Round(raw.Width*scale)),height=Math.Max(1,(int)Math.Round(raw.Height*scale));
    if(phase!=null)phase("capture-image-publish");
    string payload=Path.Combine(folder,"payload");Directory.CreateDirectory(payload);string image=Path.Combine(payload,"preview.jpg");
    using(var small=new Bitmap(width,height))using(var graphics=Graphics.FromImage(small)){
     graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.DrawImage(raw,new Rectangle(0,0,width,height));check();small.Save(image,ImageFormat.Jpeg);
    }
    if(new FileInfo(image).Length>2097152)throw new InvalidDataException("识别图超过2MiB预算");
    check();return new{target=target,image="preview.jpg",imageSha256=Common.Hash(image),width=width,height=height,sourceWidth=raw.Width,sourceHeight=raw.Height,captureMethod=frame.Method,captureAttempts=frame.Attempts,region="owned-render-client-only",blackFrameRejected=true};
   }
  }finally{ProcessProof.CloseHandle(held);}
 }
}
