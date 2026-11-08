using System;using System.IO;using System.Text;using System.Linq;using System.Collections.Generic;
// Read-only view of the known local video transport. Stored bytes are never rewritten.
// Fixed table and absolute-offset modulo-16 match the identity-pinned engine's reader.
internal static class VideoPackage {
 internal const string Format="LX-video-mp4-xor16";
 static readonly byte[] Key={0x64,0x6a,0x6b,0x38,0x26,0x24,0x64,0x66,0x60,0x32,0x32,0x33,0x6a,0x73,0x6a,0x68};
 internal static bool IsVideo(string format){return format==Format;}
 internal static bool Extension(string p){return String.Equals(Path.GetExtension(p),".mp4",StringComparison.OrdinalIgnoreCase);}
 internal static string ResourceExtension(string format){return IsVideo(format)?".mp4":".cwp";}
 internal static void DecodeForCover(string source,string target,string expectedHash,Action check){
  LocalImport.NoLinks(source);
  if(!LocalImport.Under(target,CoverCache.WorkFolder))throw new InvalidDataException("视频取帧输出目录越界");
  using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))
  using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
  using(var hash=System.Security.Cryptography.SHA256.Create()){
   try{
   if(input.Length>256L*1048576)throw new InvalidDataException("视频超过256MiB取帧预算");
   byte[] buffer=new byte[262144];long offset=0;int count;
   while((count=input.Read(buffer,0,buffer.Length))>0){
    check();hash.TransformBlock(buffer,0,count,null,0);
    for(int index=0;index<count;index++)buffer[index]^=Key[(offset+index)%16];
    output.Write(buffer,0,count);offset+=count;
   }
   hash.TransformFinalBlock(new byte[0],0,0);
   if(BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant()!=expectedHash)
    throw new InvalidDataException("视频取帧读取内容与检查身份不符");
   check();
   }catch{
    output.Dispose();File.Delete(target);throw;
   }
  }
 }
 static byte[] Read(FileStream f,long offset,int count){if(offset<0||count<0||offset>f.Length-count)throw new InvalidDataException("视频区间越界");f.Position=offset;byte[] b=new byte[count];int n=0,k;while(n<count&&(k=f.Read(b,n,count-n))>0)n+=k;if(n!=count)throw new EndOfStreamException("视频读取中变化");for(int i=0;i<count;i++)b[i]^=Key[(offset+i)%16];return b;}
 static uint U(byte[] b,int p){if(p<0||p>b.Length-4)throw new InvalidDataException("视频字段不完整");return ((uint)b[p]<<24)|((uint)b[p+1]<<16)|((uint)b[p+2]<<8)|b[p+3];}
 static long Q(byte[] b,int p){ulong n=((ulong)U(b,p)<<32)|U(b,p+4);if(n>Int64.MaxValue)throw new InvalidDataException("视频长度溢出");return (long)n;}
 static string Text(byte[] b,int p){return Encoding.ASCII.GetString(b,p,4);}
 internal static bool Recognize(FileStream f){if(f.Length<32)return false;var b=Read(f,0,16);return U(b,0)==32&&Text(b,4)=="ftyp"&&Text(b,8)=="isom";}
 internal static bool Recognize(string p){using(var f=new FileStream(p,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))return Recognize(f);}
 sealed class Box{internal string Type;internal int Start,Body,End;}
 sealed class Track{internal string Handler,Codec;internal uint Samples,DefaultSize;internal uint[] Sizes,Offsets;internal uint[,] Map;internal long TimedSamples;internal bool LocalData,AvcConfig;}
 sealed class Parser {
  internal readonly byte[] Data;internal readonly Action Check;internal int Count;
  internal Parser(byte[] b,Action check){Data=b;Check=check;}
  internal List<Box> Boxes(int start,int end){var rows=new List<Box>();while(start<end){Check();if(++Count>4096||end-start<8)throw new InvalidDataException("视频目录超过预算或不完整");long size=U(Data,start);int h=8;if(size==1){size=Q(Data,start+8);h=16;}if(size<h||size>end-start)throw new InvalidDataException("视频box范围无效");rows.Add(new Box{Start=start,Body=start+h,End=start+(int)size,Type=Text(Data,start+4)});start+=(int)size;}return rows;}
  internal void Walk(int a,int z,Track t,int depth){if(depth>8)throw new InvalidDataException("视频目录嵌套过深");foreach(var b in Boxes(a,z)){
   int p=b.Body;int n=b.End-p;
   if(b.Type=="mdia"||b.Type=="minf"||b.Type=="stbl"||b.Type=="dinf")Walk(p,b.End,t,depth+1);
   else if(b.Type=="hdlr"){if(n<24||t.Handler!=null)throw new InvalidDataException("视频轨道类型无效");t.Handler=Text(Data,p+8);}
   else if(b.Type=="dref"){if(n<20||U(Data,p+4)!=1)throw new InvalidDataException("仅支持单个本地视频数据引用");var refs=Boxes(p+8,b.End);if(refs.Count!=1||refs[0].Type!="url "||refs[0].End-refs[0].Body!=4||U(Data,refs[0].Body)!=1)throw new InvalidDataException("拒绝视频外部数据引用");t.LocalData=true;}
   else if(b.Type=="stsd"){
    if(n<16||U(Data,p+4)!=1||t.Codec!=null)throw new InvalidDataException("仅支持单编码描述视频");var entries=Boxes(p+8,b.End);if(entries.Count!=1)throw new InvalidDataException("视频编码描述无效");var e=entries[0];t.Codec=e.Type;
    if(e.End-e.Body<8||Data[e.Body+6]!=0||Data[e.Body+7]!=1)throw new InvalidDataException("视频编码数据引用无效");
    if(t.Codec=="avc1"){
     if(e.End-e.Start<86)throw new InvalidDataException("AVC视频描述过短");var extra=Boxes(e.Start+86,e.End);var config=extra.SingleOrDefault(v=>v.Type=="avcC");
     if(config==null||config.End-config.Body<7||Data[config.Body]!=1)throw new InvalidDataException("缺少已支持的AVC配置");t.AvcConfig=true;
    }else if(t.Codec!="mp4a")throw new InvalidDataException("暂不支持的视频编码："+t.Codec);
   }else if(b.Type=="stsz"){
    if(n<12||t.Sizes!=null)throw new InvalidDataException("视频样本尺寸表无效");t.DefaultSize=U(Data,p+4);t.Samples=U(Data,p+8);if(t.Samples==0||t.Samples>1000000)throw new InvalidDataException("视频样本数量超预算");
    if(n!=12+(t.DefaultSize==0?(long)t.Samples*4:0))throw new InvalidDataException("视频样本尺寸表未完整消费");t.Sizes=new uint[t.DefaultSize==0?t.Samples:0];for(int i=0;i<t.Sizes.Length;i++)t.Sizes[i]=U(Data,p+12+i*4);
   }else if(b.Type=="stco"){
    if(n<8||t.Offsets!=null)throw new InvalidDataException("视频偏移表无效");uint total=U(Data,p+4);if(total==0||total>1000000||n!=8+(long)total*4)throw new InvalidDataException("视频偏移表超预算或不完整");t.Offsets=new uint[total];for(int i=0;i<t.Offsets.Length;i++)t.Offsets[i]=U(Data,p+8+i*4);
   }else if(b.Type=="stsc"){
    if(n<8||t.Map!=null)throw new InvalidDataException("视频chunk映射重复");uint total=U(Data,p+4);if(total==0||total>100000||n!=8+(long)total*12)throw new InvalidDataException("视频chunk映射无效");t.Map=new uint[total,2];uint last=0;for(int i=0;i<total;i++){uint first=U(Data,p+8+i*12),samples=U(Data,p+12+i*12);if(first<=last||samples==0||samples>1000000||U(Data,p+16+i*12)!=1)throw new InvalidDataException("视频chunk引用无效");t.Map[i,0]=first;t.Map[i,1]=samples;last=first;}if(t.Map[0,0]!=1)throw new InvalidDataException("视频chunk起点无效");
   }else if(b.Type=="stts"){
    if(n<8||t.TimedSamples!=0)throw new InvalidDataException("视频时间表无效");uint total=U(Data,p+4);if(total>1000000||n!=8+(long)total*8)throw new InvalidDataException("视频时间表不完整");for(int i=0;i<total;i++){Check();t.TimedSamples+=U(Data,p+8+i*8);if(U(Data,p+12+i*8)==0)throw new InvalidDataException("视频时间步长为零");}
   }else if(b.Type=="co64"||b.Type=="stz2"||b.Type=="sinf"||b.Type=="encv"||b.Type=="enca")throw new InvalidDataException("视频包含尚未支持的偏移或受保护轨道结构");
  }}
 }
 internal static string Validate(string path,Action check){
  check();LocalImport.NoLinks(path);using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
   if(!Recognize(f)||f.Length>256L*1048576)throw new InvalidDataException("不是已支持的本地视频封装或超过256MiB预算");
   long pos=0,mdatStart=0,mdatEnd=0;byte[] moov=null;int boxes=0;
   while(pos<f.Length){check();if(++boxes>128||f.Length-pos<8)throw new InvalidDataException("视频顶层目录不完整");var h=Read(f,pos,(int)Math.Min(16,f.Length-pos));long size=U(h,0);int head=8;if(size==1){size=Q(h,8);head=16;}string kind=Text(h,4);if(size<head||size>f.Length-pos)throw new InvalidDataException("视频声明长度越界");
    if(kind=="mdat"){if(mdatEnd!=0||size==head)throw new InvalidDataException("仅支持一个非空视频数据区");mdatStart=pos+head;mdatEnd=pos+size;}
    else if(kind=="moov"){if(moov!=null||size>4*1048576)throw new InvalidDataException("视频元数据重复或超过4MiB预算");moov=Read(f,pos+head,(int)size-head);}
    else if(kind!="ftyp"&&kind!="free")throw new InvalidDataException("尚未支持的视频顶层结构："+kind);pos+=size;
   }
   if(moov==null||mdatEnd==0)throw new InvalidDataException("缺少完整视频数据或元数据");var parser=new Parser(moov,check);var tracks=new List<Track>();var spans=new List<long[]>();
   foreach(var box in parser.Boxes(0,moov.Length)){if(box.Type!="trak")continue;if(tracks.Count>=4)throw new InvalidDataException("视频轨道超过4条预算");var t=new Track();parser.Walk(box.Body,box.End,t,0);tracks.Add(t);
    if(!t.LocalData||t.Sizes==null||t.Offsets==null||t.Map==null||t.TimedSamples!=t.Samples||t.Map[t.Map.GetLength(0)-1,0]>t.Offsets.Length||!((t.Handler=="vide"&&t.Codec=="avc1"&&t.AvcConfig)||(t.Handler=="soun"&&t.Codec=="mp4a")))throw new InvalidDataException("视频轨道、引用或样本表不符合已支持范围");
    long sample=0;int map=0;for(int c=0;c<t.Offsets.Length;c++){check();while(map+1<t.Map.GetLength(0)&&t.Map[map+1,0]<=c+1)map++;long count=t.Map[map,1],bytes=0;if(sample+count>t.Samples)throw new InvalidDataException("视频样本索引越界");for(long s=0;s<count;s++){check();bytes+=t.DefaultSize==0?t.Sizes[sample+s]:t.DefaultSize;}sample+=count;long offset=t.Offsets[c];if(bytes<=0||offset<mdatStart||offset>mdatEnd-bytes)throw new InvalidDataException("视频媒体引用越界");spans.Add(new[]{offset,offset+bytes});}if(sample!=t.Samples)throw new InvalidDataException("视频样本未完整消费");
   }
   if(tracks.Count(t=>t.Handler=="vide")!=1)throw new InvalidDataException("需要一个已支持的AVC视频轨道");long end=mdatStart;foreach(var span in spans.OrderBy(s=>s[0])){if(span[0]<end)throw new InvalidDataException("视频媒体引用重叠");end=span[1];}
   return Format+"|avc1|tracks="+tracks.Count+"|structure-only;rendering-unconfirmed";
  }
 }
}
