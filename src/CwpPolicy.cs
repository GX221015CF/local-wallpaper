// Playback acceptance is independent from the file's path and from human compatibility confirmation.
// A structurally verified v3 package may be opened in a window for per-resource observation;
// desktop mode still requires a matching PreviewConfirmedSha256 in WallpaperLibrary.
using System;using System.IO;
internal static class CwpPolicy {
 internal const string Movie395647Hash="bc91c36fa341f8327b1301e9b063a53a0774eab5d9659fe9dca9f4a778e761c1";
 internal static string Validate(string path,string exactHash){return Validate(path,exactHash,()=>{});}
 internal static string Validate(string path,string exactHash,Action check){
  check();if(VideoPackage.Extension(path)){return VideoPackage.Validate(path,check);}
  using(var r=new BinaryReader(File.OpenRead(path))){
   if(r.BaseStream.Length<32)throw new InvalidDataException("CWP文件过短；未启动播放器。");
   uint magic=r.ReadUInt32(),declared=r.ReadUInt32();
   if(declared!=r.BaseStream.Length)throw new InvalidDataException("CWP声明长度与实际长度不符；未启动播放器。");
   if(magic==0x02505743)return "CWP-v2-existing-scope";
   if(magic!=0x03505743)throw new InvalidDataException("尚不支持此CWP格式；未启动播放器，不回退其他壁纸。");
   if(exactHash==Movie395647Hash){
    // The complete SHA-256 binds all reviewed metadata and payload bytes, not only a version marker.
    if(r.BaseStream.Length!=57985664||r.ReadUInt32()!=272||r.ReadInt32()!=-44)throw new InvalidDataException("已审查v3的头部结构不符，拒绝启动。");
    return "CWP-v3-reviewed-395647-movie-hash";
   }
   // This is a bounded structural gate, not a playback claim. V3Package performs the
   // directory, path, project metadata and preview-member checks for the known family.
   // Preserve the historical, exact-hash acceptance already confirmed by the user.
   if(exactHash==PackageAnalysis.ConfirmedMovieHash&&r.BaseStream.Length==64208480)return "historical-exact-hash-window-acceptance";
   var analysis=PackageAnalysis.Analyze(path,check,false,exactHash);
   if(analysis.WindowAttemptAllowed)return "CWP-v3-rule-gated-window-attempt:"+analysis.Family;
   throw new InvalidDataException("此v3文件未通过结构规则："+analysis.Reason+"；仅可登记，不能预览或播放。");
  }
 }
}
