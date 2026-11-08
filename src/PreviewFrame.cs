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
 internal static bool Informative(Bitmap image){double sum=0,squared=0;int count=0,nearBlack=0;for(int y=0;y<image.Height;y+=Math.Max(1,image.Height/50))for(int x=0;x<image.Width;x+=Math.Max(1,image.Width/70)){var p=image.GetPixel(x,y);double l=(p.R*299+p.G*587+p.B*114)/1000.0;sum+=l;squared+=l*l;count++;if(l<4)nearBlack++;}return count>0&&nearBlack<(count*.995)&&squared/count-(sum/count)*(sum/count)>4;}
 internal static void ValidateTarget(PreviewFrameTarget t){
  if(t==null||!LocalImport.Under(t.Run,Path.Combine(Common.Root,"logs"))||!PackageAnalysis.HashText(t.ResourceSha256))throw new InvalidDataException("截图目标不属于本次自有预览");
  LocalImport.NoLinks(t.Run);LocalImport.NoLinks(t.RuntimeCwp);
  if(!LocalImport.Under(t.RuntimeCwp,Path.Combine(t.Run,"input")))RuntimeInputs.VerifyCaptureInput(t.Run,t.RuntimeCwp,t.Token,t.ResourceSha256);
  var req=Common.Read(Path.Combine(t.Run,"request.json"));var load=Common.Read(Path.Combine(t.Run,"load-input.json"));if(Common.S(req,"mode")!="preview"||Common.S(req,"runToken")!=t.Token||Common.S(req,"expectedSha256")!=t.ResourceSha256||Common.S(req,"selectedCatalogId")!=t.CatalogId||Common.S(load,"sourceSha256")!=t.ResourceSha256||Common.S(load,"runtimeSha256")!=t.ResourceSha256||Common.S(load,"runtimeCwp")!=t.RuntimeCwp)throw new InvalidDataException("实际加载资源证据不符");
 }
 internal static object Capture(string folder,PreviewFrameTarget t,Action check){
  ValidateTarget(t);
  IntPtr held=OpenProcess(0x101000,false,t.PlayerPid);if(held==IntPtr.Zero)throw new InvalidDataException("无法核验预览进程");
  try{ProcessProof.Verify(held,t.PlayerPid,t.PlayerCreation,t.PlayerImage);if(Common.Hash(t.PlayerImage)!=Common.HostHash||Common.WaitForSingleObject(held,0)!=258)throw new InvalidDataException("播放器身份或状态不符");check();LocalImport.NoLinks(t.RuntimeCwp);string signature=CoverCache.Signature(t.RuntimeCwp);if(LocalImport.HashCancelable(t.RuntimeCwp,check)!=t.ResourceSha256)throw new InvalidDataException("预览实际加载副本哈希不符");Windows(t);Rect r;if(!GetClientRect(new IntPtr(t.Render),out r)||r.Right<32||r.Bottom<32||r.Right>8192||r.Bottom>8192||(long)r.Right*r.Bottom>12000000)throw new InvalidDataException("预览画面尺寸不在捕获预算内");
   string method="PrintWindow-render-client";using(var raw=new Bitmap(r.Right,r.Bottom,PixelFormat.Format32bppRgb)){
    bool painted;using(var g=Graphics.FromImage(raw)){g.Clear(Color.Black);var dc=g.GetHdc();try{painted=PrintWindow(new IntPtr(t.Render),dc,3);}finally{g.ReleaseHdc(dc);}}
    check();if(!painted||!Informative(raw)){
     if(!Uncovered(t))throw new InvalidDataException("未取得渲染画面；请保持预览窗口完整可见后重试，未保存黑图");method="BitBlt-own-visible-render-client";
     IntPtr source=GetDC(new IntPtr(t.Render));if(source==IntPtr.Zero)throw new InvalidDataException("无法取得自有渲染窗口画面");try{using(var g=Graphics.FromImage(raw)){var dc=g.GetHdc();try{painted=BitBlt(dc,0,0,r.Right,r.Bottom,source,0,0,0x00cc0020);}finally{g.ReleaseHdc(dc);}}}finally{ReleaseDC(new IntPtr(t.Render),source);}if(!Uncovered(t))throw new InvalidDataException("捕获期间预览被遮挡，未保存图片");
    }
    if(!painted||!Informative(raw))throw new InvalidDataException("捕获为空白或黑图，未保存封面");check();Windows(t);Rect finalSize;if(!GetClientRect(new IntPtr(t.Render),out finalSize)||finalSize.Right!=r.Right||finalSize.Bottom!=r.Bottom)throw new InvalidDataException("捕获期间预览尺寸已变化，未保存图片");ProcessProof.Verify(held,t.PlayerPid,t.PlayerCreation,t.PlayerImage);if(Common.WaitForSingleObject(held,0)!=258||CoverCache.Signature(t.RuntimeCwp)!=signature)throw new InvalidDataException("捕获期间预览已停止或资源已变化");
    double scale=Math.Min(1,Math.Min(480.0/r.Right,300.0/r.Bottom));int width=Math.Max(1,(int)Math.Round(r.Right*scale)),height=Math.Max(1,(int)Math.Round(r.Bottom*scale));
    string payload=Path.Combine(folder,"payload");Directory.CreateDirectory(payload);string image=Path.Combine(payload,"preview.jpg");using(var small=new Bitmap(width,height))using(var g=Graphics.FromImage(small)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(raw,new Rectangle(0,0,width,height));check();small.Save(image,ImageFormat.Jpeg);}if(new FileInfo(image).Length>2097152)throw new InvalidDataException("识别图超过2MiB预算");check();return new{target=t,image="preview.jpg",imageSha256=Common.Hash(image),width=width,height=height,sourceWidth=r.Right,sourceHeight=r.Bottom,captureMethod=method,region="owned-render-client-only",blackFrameRejected=true};
   }
  }finally{ProcessProof.CloseHandle(held);}
 }
}
