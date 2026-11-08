// Reuse only the proven CWP-v2 directory/AES-CBC/raw-DEFLATE rule.
// Unknown v3 is deliberately not parsed. No engine loading, network, movie decode or package writes.
using System;using System.IO;using System.IO.Compression;using System.Drawing;using System.Drawing.Imaging;using System.Drawing.Drawing2D;using System.Text;using System.Linq;using System.Diagnostics;using System.Security.Cryptography;using System.Collections.Generic;using System.Runtime.InteropServices;
internal sealed class CoverBinding {
 public string CheckedAt{get;set;}public int Version{get;set;}public string Source{get;set;}public string SourceSignature{get;set;}public string Md5{get;set;}public string Sha256{get;set;}public string Thumbnail{get;set;}public string ThumbnailSha256{get;set;}public string Member{get;set;}public string MemberSha256{get;set;}public string Method{get;set;}public string Reason{get;set;}public int Width{get;set;}public int Height{get;set;}
}
internal static class CoverCache {
 const int MiB=1048576,MaxImage=8*MiB,MaxTable=MiB,MaxPixels=16000000;
 internal static string WorkFolder;internal static long HashedBytes,PreviewBytes;internal static int Hits,Reads;
#if TEST_BUILD
 internal static int TestDelay;
#endif
 static string Root{get{return Path.Combine(Common.Root,"data","cover-cache");}}
 [StructLayout(LayoutKind.Sequential)]struct Basic {public long Creation,Access,Write,Change;public uint Attributes;}
 [StructLayout(LayoutKind.Sequential)]struct FileId {public uint Attributes;public ProcessProof.FT Creation,Access,Write;public uint Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;}
 [DllImport("kernel32",SetLastError=true)]static extern bool GetFileInformationByHandleEx(IntPtr h,int info,out Basic basic,uint size);
 [DllImport("kernel32",SetLastError=true)]static extern bool GetFileInformationByHandle(IntPtr h,out FileId info);
 internal static string Signature(string path){using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){Basic b;FileId id;if(!GetFileInformationByHandleEx(f.SafeFileHandle.DangerousGetHandle(),0,out b,40)||!GetFileInformationByHandle(f.SafeFileHandle.DangerousGetHandle(),out id))throw new IOException("无法核验源文件变化身份");return String.Join(":",new[]{f.Length.ToString(),b.Creation.ToString(),b.Write.ToString(),b.Change.ToString(),id.Volume.ToString(),id.IndexHigh.ToString(),id.IndexLow.ToString()});}}
 static string Hex(byte[] b){return BitConverter.ToString(b).Replace("-","").ToLowerInvariant();}
 static string Digest(byte[] b){using(var h=SHA256.Create())return Hex(h.ComputeHash(b));}
 static string MapPath(ImportCandidate c){return Path.Combine(Root,"sources",Digest(Encoding.UTF8.GetBytes(Path.GetFullPath(c.Source).ToUpperInvariant()+"|"+c.Md5))+".json");}
 static void Publish(string p,object value){Directory.CreateDirectory(Path.GetDirectoryName(p));Directory.CreateDirectory(WorkFolder);string t=Path.Combine(WorkFolder,Guid.NewGuid().ToString("N")+".tmp");try{Common.SaveNew(t,value);if(File.Exists(p))File.Replace(t,p,null);else File.Move(t,p);}finally{if(File.Exists(t))File.Delete(t);}}
 static void Apply(ImportCandidate c,CoverBinding b,bool hit){c.Cover=b.Thumbnail;c.CoverReason=b.Reason;c.CoverSourceSha256=b.Sha256;c.CoverMember=b.Member;c.CoverCacheHit=hit;}
 static bool ThumbnailValid(CoverBinding b){return b.Thumbnail==null||LocalImport.Under(b.Thumbnail,Root)&&File.Exists(b.Thumbnail)&&new FileInfo(b.Thumbnail).Length<=MiB&&Common.Hash(b.Thumbnail)==b.ThumbnailSha256;}
 internal static void Ensure(ImportCandidate c,Action taskCheck){try{if(VideoPackage.IsVideo(LocalImport.CheckHeader(c.Source))){c.Cover=null;c.CoverReason="视频没有静态封面；可导入后从预览生成识别图";return;}if(LocalImport.CheckHeader(c.Source)=="CWP-v3"){AnalyzeCandidate(c,c.Source,null,taskCheck);return;}Resolve(c,c.Source,null,null,taskCheck);}catch(OperationCanceledException){throw;}catch(Exception ex){c.Cover=null;c.CoverReason="读取未完成："+ex.Message;c.AnalysisStatus="文件未稳定或读取失败";}}
 internal static string ForImport(ImportCandidate c,string privateSource,string sha,string md5,Action taskCheck){if(VideoPackage.IsVideo(LocalImport.CheckHeader(privateSource))){VideoCovers.Ensure(c,privateSource,sha,taskCheck);return null;}if(LocalImport.CheckHeader(privateSource)=="CWP-v3")return AnalyzeCandidate(c,privateSource,sha,taskCheck);return Resolve(c,privateSource,sha,md5,taskCheck).Thumbnail;}
 static string AnalyzeCandidate(ImportCandidate c,string path,string hash,Action check,bool force=false,Action<string> phase=null){
  c.Analysis=null;c.AnalysisError=null;c.AnalysisStatus=null;c.AnalysisStage="准备分析文件";
  Action<string> reportPhase=name=>{c.AnalysisStage=name;if(phase!=null)phase(name);};
  try{var r=PackageAnalysis.Analyze(path,check,force,hash,reportPhase);c.Analysis=r;c.AnalysisStatus=r.StructureRecognized?"结构已识别 · 播放须单独确认":"容器结构未验证 · "+r.Reason;c.Cover=PackageAnalysis.CoverPath(r);c.CoverReason=r.CoverReason??r.Reason;c.CoverSourceSha256=r.Sha256;c.CoverMember=r.CoverMember;c.CoverCacheHit=r.CacheHit;return c.Cover;}
  catch(OperationCanceledException){throw;}catch(Exception ex){c.Cover=null;c.AnalysisError=ex.Message;c.CoverReason="分析未完成："+ex.Message;c.AnalysisStatus="检查失败："+ex.Message+"（中断阶段："+c.AnalysisStage+"） · 未导入；点击“重新分析此文件”可重试";return null;}
 }
 internal static void AnalyzeForUser(ImportCandidate c,Action check,Action<string> phase){if(LocalImport.CheckHeader(c.Source)!="CWP-v3"){Ensure(c,check);return;}AnalyzeCandidate(c,c.Source,c.DiscoveredSha256,check,true,phase);}
 static CoverBinding Resolve(ImportCandidate c,string readPath,string suppliedSha,string suppliedMd5,Action taskCheck){
  c.Cover=null;c.CoverReason=null;string sig=null,map=MapPath(c);var clock=Stopwatch.StartNew();Action check=()=>{taskCheck();if(clock.Elapsed.TotalSeconds>10)throw new TimeoutException("封面处理超过10秒预算");};
  var b=new CoverBinding{Version=1,CheckedAt=DateTimeOffset.UtcNow.ToString("o"),Source=c.Source,Md5=c.Md5};
  try{
   check();LocalImport.NoLinks(readPath);sig=Signature(c.Source);b.SourceSignature=sig;
   if(File.Exists(map)){try{var old=Common.Json.Deserialize<CoverBinding>(Common.ReadShared(map));if(old.Version==1&&old.Source==c.Source&&old.SourceSignature==sig&&old.Md5==c.Md5&&(suppliedSha==null||old.Sha256==suppliedSha)){
     // A revision may relocate an otherwise valid hash-bound cache. Never trust its old absolute path.
     if(old.Thumbnail!=null&&!ThumbnailValid(old)&&old.Sha256!=null&&old.Sha256.Length==64&&old.Sha256.All(Uri.IsHexDigit)){
      string local=Path.Combine(Root,old.Sha256,"thumbnail.jpg");
      if(File.Exists(local)&&new FileInfo(local).Length<=MiB&&Common.Hash(local)==old.ThumbnailSha256)old.Thumbnail=local;
     }
     if(ThumbnailValid(old)&&(old.Thumbnail!=null||old.CheckedAt!=null&&DateTimeOffset.UtcNow-DateTimeOffset.Parse(old.CheckedAt)<TimeSpan.FromSeconds(5))){Hits++;Apply(c,old,true);return old;}
     // Invalid images are rebuilt below within the existing task/cancellation budget, rather than cached as a permanent failure.
    }}catch(IOException){}catch(ArgumentException){}}
    string format=LocalImport.CheckHeader(readPath);string known=LocalImport.FindCover(c.Md5,suppliedSha);
    // v3 candidates are parsed only by the bounded V3Package reader below;
    // other container versions still require an exact, previously evidenced cover.
    if(format!="CWP-v2"&&format!="CWP-v3"&&known==null)throw new NotSupportedException("此格式的预览图布局尚未核验");
   string hash=suppliedSha,md5=suppliedMd5;
   if(hash==null){using(var f=new FileStream(readPath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var h=SHA256.Create())using(var m=MD5.Create()){if(f.Length>256L*MiB)throw new InvalidDataException("源文件超过256MiB封面读取预算");byte[] buf=new byte[262144];int n;while((n=f.Read(buf,0,buf.Length))>0){check();h.TransformBlock(buf,0,n,null,0);m.TransformBlock(buf,0,n,null,0);HashedBytes+=n;
#if TEST_BUILD
     if(TestDelay>0)System.Threading.Thread.Sleep(TestDelay);
#endif
    }h.TransformFinalBlock(new byte[0],0,0);m.TransformFinalBlock(new byte[0],0,0);hash=Hex(h.Hash);md5=Hex(m.Hash);}}
   b.Sha256=hash;if(md5!=c.Md5)throw new InvalidDataException("源文件MD5与本地记录不符");
   byte[] image=null;known=LocalImport.FindCover(md5,hash);
   if(known!=null){if(new FileInfo(known).Length>MaxImage)throw new InvalidDataException("已有封面超过8MiB预算");image=File.ReadAllBytes(known);b.Method="migrated-real-cover-exact-source-hash";b.Member="previously-evidenced-preview";}
   else if(format=="CWP-v2"){string member;image=ReadV2Preview(readPath,check,out member);b.Member=member;b.Method="proven-v2-table-and-one-preview-member";} else if(format=="CWP-v3"){string member;image=V3Package.ReadPreview(readPath,check,out member);b.Member=member;b.Method="proven-v3-table-and-one-preview-member";}
   else throw new NotSupportedException("仅沿用已审查v3的完整哈希封面，未解析其他v3");
   check();b.MemberSha256=Digest(image);Size dimensions=ImageDimensions(image);b.Width=dimensions.Width;b.Height=dimensions.Height;
   using(var stream=new MemoryStream(image,false))using(var im=Image.FromStream(stream,true,true)){
    if(im.Width!=dimensions.Width||im.Height!=dimensions.Height)throw new InvalidDataException("封面解码尺寸不符");
    double scale=Math.Min(1,Math.Min(360.0/im.Width,240.0/im.Height));int w=Math.Max(1,(int)Math.Round(im.Width*scale)),h=Math.Max(1,(int)Math.Round(im.Height*scale));
    using(var thumb=new Bitmap(w,h))using(var g=Graphics.FromImage(thumb)){g.Clear(Color.White);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(im,0,0,w,h);check();
     if(Signature(c.Source)!=sig)throw new InvalidDataException("源文件读取中变化，请重新检查");string dir=Path.Combine(Root,hash);Directory.CreateDirectory(dir);Directory.CreateDirectory(WorkFolder);string target=Path.Combine(dir,"thumbnail.jpg"),temp=Path.Combine(WorkFolder,Guid.NewGuid().ToString("N")+".tmp");
     try{thumb.Save(temp,ImageFormat.Jpeg);check();if(File.Exists(target))File.Replace(temp,target,null);else File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}b.Thumbnail=target;b.ThumbnailSha256=Common.Hash(target);
    }
   }
   b.Reason="真实封面 · 仅代表预览图已读取";Publish(Path.Combine(Root,hash,"cover.json"),b);Publish(map,b);Apply(c,b,false);return b;
  }catch(OperationCanceledException){throw;}catch(Exception ex){taskCheck();b.Thumbnail=null;b.Reason=ex.Message;try{if(sig!=null&&Signature(c.Source)==sig)Publish(map,b);}catch(IOException){}Apply(c,b,false);return b;}
 }
 static byte[] Exact(BinaryReader r,int count){byte[] data=r.ReadBytes(count);if(data.Length!=count)throw new InvalidDataException("目录或预览图成员被截断");return data;}
 static byte[] Decrypt(byte[] data){if(data.Length==0||data.Length%16!=0)throw new InvalidDataException("不符合已证实的加密块结构");byte[] password=Encoding.ASCII.GetBytes("123"),s,key,iv,m;using(var h=SHA256.Create())s=h.ComputeHash(password);using(var h=MD5.Create()){m=h.ComputeHash(password);key=h.ComputeHash(s.Concat(m).ToArray());iv=h.ComputeHash(s);}using(var aes=Aes.Create()){aes.Key=key;aes.IV=iv;aes.Mode=CipherMode.CBC;aes.Padding=PaddingMode.PKCS7;using(var d=aes.CreateDecryptor())return d.TransformFinalBlock(data,0,data.Length);}}
 internal static void ValidateV2Directory(string p,Action check){string member;ReadV2Preview(p,check,out member,true);}
 internal static void EnsureVerified(ImportCandidate c,string sha,string md5,Action check){Resolve(c,c.Source,sha,md5,check);}
 static byte[] ReadV2Preview(string p,Action check,out string member,bool directoryOnly=false){
  member=null;long previewStart=0;int previewSize=0;Reads++;
  using(var stream=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var r=new BinaryReader(stream)){
   if(r.ReadUInt32()!=0x02505743)throw new NotSupportedException("不是已支持的v2封面结构");uint declared=r.ReadUInt32(),tableSize=r.ReadUInt32(),bias=r.ReadUInt32();if(declared!=stream.Length||tableSize==0||tableSize>MaxTable||16L+tableSize>stream.Length)throw new InvalidDataException("目录长度超出已支持范围");
   check();byte[] table=Decrypt(Exact(r,(int)tableSize));long previous=16L+tableSize;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   using(var memory=new MemoryStream(table,false))using(var t=new BinaryReader(memory,new UTF8Encoding(false,true))){uint count=t.ReadUInt32();if(count==0||count>20480)throw new InvalidDataException("目录成员数超出预算");
    for(int i=0;i<count;i++){check();if(memory.Length-memory.Position<16)throw new InvalidDataException("目录项不完整");uint offset=t.ReadUInt32(),size=t.ReadUInt32(),flags=t.ReadUInt32(),length=t.ReadUInt32();if(length==0||length>1024||length>memory.Length-memory.Position)throw new InvalidDataException("成员名称长度异常");string name=new UTF8Encoding(false,true).GetString(Exact(t,(int)length)).Replace('\\','/');
     if(name.StartsWith("/")||name.Split('/').Any(x=>x==".."||x=="."||x.Length==0)||name.Any(c=>c<32||c==':')||!names.Add(name))throw new InvalidDataException("成员路径不符合已支持规则");
     long start=(long)offset+bias;if(flags!=0||start!=previous||size==0||start+size>stream.Length)throw new InvalidDataException("目录标志或成员范围不符合已证实v2规则");previous=start+size;
     if(name.Equals("preview.jpg",StringComparison.OrdinalIgnoreCase)||name.Equals("preview.png",StringComparison.OrdinalIgnoreCase)){if(member!=null||size>MaxImage)throw new InvalidDataException("预览成员重复或超过8MiB预算");member=name;previewStart=start;previewSize=(int)size;}
    }if(memory.Position!=memory.Length||previous!=stream.Length)throw new InvalidDataException("目录或数据范围未完整对应");
   }
   if(directoryOnly)return new byte[0];
   if(member==null)throw new NotSupportedException("包内没有已支持的根目录preview.jpg/preview.png");stream.Position=previewStart;check();byte[] compressed=Decrypt(Exact(r,previewSize));
   using(var input=new MemoryStream(compressed,false))using(var deflate=new DeflateStream(input,CompressionMode.Decompress))using(var output=new MemoryStream()){byte[] buffer=new byte[32768];int n;while((n=deflate.Read(buffer,0,buffer.Length))>0){check();if(output.Length+n>MaxImage)throw new InvalidDataException("预览图解压超过8MiB预算");output.Write(buffer,0,n);}PreviewBytes+=output.Length;return output.ToArray();}
  }
 }
 static int Be32(byte[] b,int p){return checked((int)((uint)b[p]<<24|(uint)b[p+1]<<16|(uint)b[p+2]<<8|b[p+3]));}
 internal static Size ImageDimensions(byte[] b){
  if(b.Length<24||b.Length>MaxImage)throw new InvalidDataException("封面图片长度异常");int w=0,h=0;
  if(b.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})){if(Encoding.ASCII.GetString(b,12,4)!="IHDR")throw new InvalidDataException("PNG头不完整");w=Be32(b,16);h=Be32(b,20);}
  else if(b[0]==255&&b[1]==216){int p=2;while(p<b.Length){if(b[p++]!=255)throw new InvalidDataException("JPEG标记损坏");while(p<b.Length&&b[p]==255)p++;if(p>=b.Length)break;int marker=b[p++];if(marker==217||marker==218)break;if(marker==1||marker>=208&&marker<=215)continue;if(p+2>b.Length)break;int length=b[p]*256+b[p+1];if(length<2||p+length>b.Length)throw new InvalidDataException("JPEG段长度异常");if(new[]{192,193,194,195,197,198,199,201,202,203,205,206,207}.Contains(marker)){if(length<8)throw new InvalidDataException("JPEG尺寸段损坏");h=b[p+3]*256+b[p+4];w=b[p+5]*256+b[p+6];break;}p+=length;}}
  else throw new InvalidDataException("预览成员不是受支持的JPEG/PNG图片");
  if(w<=0||h<=0||w>8192||h>8192||(long)w*h>MaxPixels)throw new InvalidDataException("封面尺寸超过8192边长或1600万像素预算");return new Size(w,h);
 }
}
