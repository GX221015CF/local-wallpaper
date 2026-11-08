using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Web.Script.Serialization;

internal sealed class V3Entry {
 public string Name { get; set; }
 public long Offset { get; set; }
 public long PhysicalOffset { get; set; }
 public long DecodedBytes { get; set; }
 public long StoredBytes { get; set; }
 public int Blocks { get; set; }
 public List<uint> Boundaries { get; set; }
}
internal sealed class V3Inspection {
 public string Path { get; set; }
 public long Bytes { get; set; }
 public string Sha256 { get; set; }
 public string Magic { get; set; }
 public uint DeclaredBytes { get; set; }
 public uint TableStoredBytes { get; set; }
 public int Bias { get; set; }
 public int TableDecodedBytes { get; set; }
 public string TableSha256 { get; set; }
 public int MemberCount { get; set; }
 public bool TableFullyConsumed { get; set; }
 public bool SpansThroughEof { get; set; }
 public string ProjectType { get; set; }
 public int ProjectVersion { get; set; }
 public string ProjectTitle { get; set; }
 public string PreviewMember { get; set; }
 public string PreviewSha256 { get; set; }
 public int PreviewBytes { get; set; }
 public List<V3Entry> Entries { get; set; }
 public string Classification { get; set; }
 public string Reason { get; set; }
}

internal static class V3Package {
 internal const uint Magic = 0x03505743;
 internal const int MaxTable = 1024 * 1024;
 internal const int MaxImage = 8 * 1024 * 1024;
 internal const int MaxMembers = 20480;
 internal const int MaxName = 1024;
 internal const int MaxBlocks = 4096;
 static readonly byte[] Password = Encoding.ASCII.GetBytes("519000");
 static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
 static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex(h.ComputeHash(b)); }
 internal static string HashFile(string path, Action check = null) {
  using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
  using (var h = SHA256.Create()) {
   byte[] b = new byte[262144]; int n;
   while ((n = f.Read(b, 0, b.Length)) > 0) { if (check != null) check(); h.TransformBlock(b, 0, n, null, 0); }
   h.TransformFinalBlock(new byte[0], 0, 0); return Hex(h.Hash);
  }
 }
 static byte[] Exact(BinaryReader r, int count) { byte[] b = r.ReadBytes(count); if (b.Length != count) throw new InvalidDataException("v3目录或成员被截断"); return b; }
 static byte[] Decrypt(byte[] encrypted) {
  if (encrypted.Length == 0 || encrypted.Length % 16 != 0) throw new InvalidDataException("v3加密块长度不符合AES块结构");
  byte[] s, key, iv, m;
  using (var h = SHA256.Create()) s = h.ComputeHash(Password);
  using (var h = MD5.Create()) { m = h.ComputeHash(Password); key = h.ComputeHash(s.Concat(m).ToArray()); iv = h.ComputeHash(s); }
  using (var aes = Aes.Create()) { aes.Key = key; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; using (var d = aes.CreateDecryptor()) return d.TransformFinalBlock(encrypted, 0, encrypted.Length); }
 }
 static byte[] DecodeBlock(byte[] encrypted, long expected, int budget, Action check) {
  byte[] compressed = Decrypt(encrypted);
  using (var input = new MemoryStream(compressed, false))
  using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
  using (var output = new MemoryStream()) {
   byte[] buf = new byte[32768]; int n;
   while ((n = deflate.Read(buf, 0, buf.Length)) > 0) { if (check != null) check(); if (output.Length + n > budget) throw new InvalidDataException("v3解压超过当前成员预算"); output.Write(buf, 0, n); }
   if (expected >= 0 && output.Length != expected) throw new InvalidDataException("v3成员声明长度与解码长度不符");
   return output.ToArray();
  }
 }
 static string Normalize(string name) {
  string n = name.Replace('\\', '/');
  if (n.Length == 0 || n.StartsWith("/") || n.Any(c => c < 32 || c == ':')) throw new InvalidDataException("v3成员路径含绝对路径、空段或非法字符");
  if (n.Split('/').Any(x => x.Length == 0 || x == "." || x == "..")) throw new InvalidDataException("v3成员路径含回退段");
  return n;
 }
 static Dictionary<string, object> JsonObject(byte[] bytes) {
  try { return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes)); }
  catch { return null; }
 }
 static V3Inspection ReadCore(string path, bool hash, Action check) {
  if (check == null) check = delegate { };
  FileInfo before = new FileInfo(path); if (!before.Exists) throw new FileNotFoundException("v3文件不存在", path);
  if (before.Length < 32 || before.Length > UInt32.MaxValue) throw new InvalidDataException("v3文件长度异常");
  var result = new V3Inspection { Path = Path.GetFullPath(path), Bytes = before.Length, Magic = null, Entries = new List<V3Entry>() };
  using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
  using (var r = new BinaryReader(stream)) {
   uint magic = r.ReadUInt32(); result.Magic = "0x" + magic.ToString("x8"); if (magic != Magic) throw new NotSupportedException("不是已证实的v3容器魔数");
   result.DeclaredBytes = r.ReadUInt32(); result.TableStoredBytes = r.ReadUInt32(); result.Bias = r.ReadInt32();
   if (result.DeclaredBytes != stream.Length) throw new InvalidDataException("v3声明长度与实际长度不符");
   if (result.TableStoredBytes == 0 || result.TableStoredBytes > MaxTable || 16L + result.TableStoredBytes > stream.Length) throw new InvalidDataException("v3目录长度超过预算");
   check(); byte[] table = DecodeBlock(Exact(r, checked((int)result.TableStoredBytes)), -1, MaxTable, check);
   result.TableDecodedBytes = table.Length; result.TableSha256 = Sha(table);
   using (var memory = new MemoryStream(table, false)) using (var t = new BinaryReader(memory, new UTF8Encoding(false, true))) {
    if (memory.Length < 4) throw new InvalidDataException("v3目录缺少成员数");
    uint count = t.ReadUInt32(); if (count == 0 || count > MaxMembers) throw new InvalidDataException("v3成员数量超过预算");
    long previous = 16L + result.TableStoredBytes; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < count; i++) {
     check(); if (memory.Length - memory.Position < 16) throw new InvalidDataException("v3目录项不完整");
     uint offset = t.ReadUInt32(), decoded = t.ReadUInt32(), stored = t.ReadUInt32(), blocks = t.ReadUInt32();
     if (blocks == 0 || blocks > MaxBlocks) throw new InvalidDataException("v3成员块数超过预算");
     var boundaries = new List<uint>(); for (uint b = 1; b < blocks; b++) boundaries.Add(t.ReadUInt32());
     if (memory.Length - memory.Position < 4) throw new InvalidDataException("v3成员名长度缺失"); uint nameLength = t.ReadUInt32();
     if (nameLength == 0 || nameLength > MaxName || nameLength > memory.Length - memory.Position) throw new InvalidDataException("v3成员名长度异常");
     string name = Normalize(new UTF8Encoding(false, true).GetString(Exact(t, checked((int)nameLength))));
     if (!names.Add(name)) throw new InvalidDataException("v3成员名重复");
     long physical = (long)offset + result.Bias;
     if (stored == 0 || physical != previous || physical < 16L + result.TableStoredBytes || physical + stored > stream.Length) throw new InvalidDataException("v3成员范围不连续或越界");
     if (boundaries.Any(x => x == 0 || x >= stored) || boundaries.SequenceEqual(boundaries.OrderBy(x => x)) == false || boundaries.Distinct().Count() != boundaries.Count) throw new InvalidDataException("v3块边界不合法");
     result.Entries.Add(new V3Entry { Name = name, Offset = offset, PhysicalOffset = physical, DecodedBytes = decoded, StoredBytes = stored, Blocks = checked((int)blocks), Boundaries = boundaries });
     previous = physical + stored;
    }
    result.TableFullyConsumed = memory.Position == memory.Length; result.SpansThroughEof = previous == stream.Length;
    if (!result.TableFullyConsumed || !result.SpansThroughEof) throw new InvalidDataException("v3目录未完整覆盖成员数据");
   }
   result.MemberCount = result.Entries.Count;
   var preview = result.Entries.FirstOrDefault(x => x.Name.Equals("preview.jpg", StringComparison.OrdinalIgnoreCase) || x.Name.Equals("preview.png", StringComparison.OrdinalIgnoreCase));
   if (preview != null) { result.PreviewMember = preview.Name; if (preview.Blocks == 1 && preview.StoredBytes <= MaxImage && preview.DecodedBytes <= MaxImage) { stream.Position = preview.PhysicalOffset; byte[] image = DecodeBlock(Exact(r, checked((int)preview.StoredBytes)), preview.DecodedBytes, MaxImage, check); result.PreviewBytes = image.Length; result.PreviewSha256 = Sha(image); } }
   var project = result.Entries.FirstOrDefault(x => x.Name.Equals("project.json", StringComparison.OrdinalIgnoreCase));
   if (project != null && project.Blocks == 1 && project.StoredBytes <= MaxImage && project.DecodedBytes <= MaxImage) {
    stream.Position = project.PhysicalOffset; var obj = JsonObject(DecodeBlock(Exact(r, checked((int)project.StoredBytes)), project.DecodedBytes, MaxImage, check));
    if (obj != null) { result.ProjectType = obj.ContainsKey("type") ? Convert.ToString(obj["type"]) : null; if (obj.ContainsKey("projectVersion")) result.ProjectVersion = Convert.ToInt32(obj["projectVersion"]); result.ProjectTitle = obj.ContainsKey("title") ? Convert.ToString(obj["title"]) : null; }
   }
  }
  FileInfo after = new FileInfo(path); if (before.Length != after.Length || before.LastWriteTimeUtc.Ticks != after.LastWriteTimeUtc.Ticks) throw new InvalidDataException("v3文件读取前后发生变化");
  if (hash) result.Sha256 = HashFile(path, check);
  result.Classification = result.ProjectType == "movie" && result.ProjectVersion == 115 && result.PreviewMember != null ? "known-v3-directory-movie-115" : "unknown-v3-project-structure";
  result.Reason = result.Classification == "known-v3-directory-movie-115" ? "共享已证实目录语法；播放仍需按完整SHA-256单独确认" : "容器目录可读，但项目类型、版本或封面成员不匹配已登记结构族";
  return result;
 }
 internal static V3Inspection Inspect(string path, Action check = null) { return ReadCore(path, true, check); }
 internal static V3Inspection InspectStructure(string path, Action check) { return ReadCore(path, false, check); }
 internal static byte[] ReadSmallMember(string path,V3Inspection info,string name,Action check) {
  var e=info.Entries.SingleOrDefault(v=>v.Name==name);
  if(e==null||e.Blocks!=1||e.StoredBytes>1048576||e.DecodedBytes>1048576)throw new InvalidDataException("必要元数据缺失、非单块或超过1MiB预算："+name);
  check();using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var r=new BinaryReader(stream)){stream.Position=e.PhysicalOffset;return DecodeBlock(Exact(r,(int)e.StoredBytes),e.DecodedBytes,1048576,check);}
 }
 internal static string Classify(string path, out string reason) { var x = ReadCore(path, false, null); reason = x.Reason; return x.Classification; }
 internal static byte[] ReadPreview(string path, Action check, out string member) {
  var x = ReadCore(path, false, check); member = x.PreviewMember; if (member == null) throw new NotSupportedException("v3包没有根目录preview.jpg或preview.png");
  string selected = member; var e = x.Entries.First(v => v.Name.Equals(selected, StringComparison.OrdinalIgnoreCase)); if (e.Blocks != 1 || e.StoredBytes > MaxImage || e.DecodedBytes > MaxImage) throw new InvalidDataException("v3封面不是单块或超过封面预算");
  using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var r = new BinaryReader(stream)) { stream.Position = e.PhysicalOffset; return DecodeBlock(Exact(r, checked((int)e.StoredBytes)), e.DecodedBytes, MaxImage, check); }
 }
}
