using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

// Owned by the UI thread. The token is also observed by the background verifier.
internal sealed class PlaybackPreparation : IDisposable {
 CancellationTokenSource cancellation;
 internal bool Busy { get { return cancellation != null; } }
 internal bool UserCancelled { get; private set; }
 internal string Stage = "idle";
 internal CancellationToken Token { get { return cancellation.Token; } }
 internal bool TryBegin(int budgetMilliseconds) {
  if (Busy) return false;
  cancellation = new CancellationTokenSource();
  cancellation.CancelAfter(budgetMilliseconds);
  UserCancelled = false;
  Stage = "resource-hash";
  return true;
 }
 internal void Cancel() {
  if (!Busy) return;
  UserCancelled = true;
  cancellation.Cancel();
 }
 internal void Complete() {
  if (cancellation == null) return;
  cancellation.Dispose();
  cancellation = null;
 }
 internal Task<string[]> VerifyAsync(string source, string mode, string confirmation, bool builtin) {
  if (!Busy) throw new InvalidOperationException("启动准备尚未取得操作权");
  var token = Token;
  return Task.Run(() => {
   RuntimeInputs.Maintain();token.ThrowIfCancellationRequested();
   string hash = PlaybackResourceCheck.Verify(source, mode, confirmation, builtin, token, value => Stage = value);
   Stage = "component-identity";
   string runtime = RuntimeProvisioning.RequireForStart(token);
   return new[] { hash, runtime };
  }, token);
 }
 public void Dispose() { Cancel(); Complete(); }
}

internal static class PlaybackResourceCheck {
#if TEST_BUILD
 internal static Action<CancellationToken> TestAfterHashChunk;
 internal static Action<CancellationToken> TestAfterCopyChunk;
#endif
 internal static string Hash(string path, CancellationToken token) {
  token.ThrowIfCancellationRequested();
  using (var digest = SHA256.Create())
  using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan)) {
   byte[] buffer = new byte[1048576];
   int count;
   while ((count = input.Read(buffer, 0, buffer.Length)) > 0) {
    token.ThrowIfCancellationRequested();
    digest.TransformBlock(buffer, 0, count, buffer, 0);
    Interlocked.Add(ref Common.HashBytes, count);
#if TEST_BUILD
    if (TestAfterHashChunk != null) TestAfterHashChunk(token);
#endif
   }
   token.ThrowIfCancellationRequested();
   digest.TransformFinalBlock(new byte[0], 0, 0);
   return BitConverter.ToString(digest.Hash).Replace("-", "").ToLowerInvariant();
  }
 }
 internal static string Verify(string path, string mode, string confirmation, bool builtin, CancellationToken token, Action<string> stage) {
  if (mode != "preview" && mode != "desktop") throw new ArgumentException("未知播放操作");
  if (!String.Equals(Path.GetExtension(path), ".cwp", StringComparison.OrdinalIgnoreCase)&&!VideoPackage.Extension(path)) throw new InvalidDataException("请选择已登记的CWP壁纸");
  stage("resource-hash");
  string hash = Hash(path, token);
  stage("resource-policy");
  CwpPolicy.Validate(path, hash, () => token.ThrowIfCancellationRequested());
  token.ThrowIfCancellationRequested();
  if (mode == "desktop" && !(builtin && hash == Common.CwpHash) && confirmation != hash)
   throw new InvalidDataException("请先预览这张壁纸，并点击“画面正确，完成确认”，再设为桌面。");
  return hash;
 }
 internal static void Copy(string source, string destination, CancellationToken token) { Copy(source,destination,token,Int64.MaxValue); }
 internal static void Copy(string source, string destination, CancellationToken token,long maxBytes) {
  token.ThrowIfCancellationRequested();
  bool owned = false;
  try {
   using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1048576, FileOptions.SequentialScan))
   using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
    owned = true;
    if(input.Length>maxBytes)throw new InvalidDataException("源文件在容量预约后变大，未继续复制。");long written=0;
    byte[] buffer = new byte[1048576];
    int count;
    while ((count = input.Read(buffer, 0, buffer.Length)) > 0) {
     token.ThrowIfCancellationRequested();
     if(written>maxBytes-count)throw new InvalidDataException("运行副本超过容量预约，未继续复制。");output.Write(buffer, 0, count);written+=count;
#if TEST_BUILD
     if (TestAfterCopyChunk != null) TestAfterCopyChunk(token);
#endif
    }
    token.ThrowIfCancellationRequested();
    output.Flush(true);
   }
  } catch (Exception original) {
   try { if (owned && File.Exists(destination)) File.Delete(destination); }
   catch (Exception cleanup) { throw new IOException("运行副本写入失败，清理仍未完成。", new AggregateException(original, cleanup)); }
   throw;
  }
 }
}

internal sealed class PlaybackActions {
 internal bool Preview, Desktop, Stop;
 internal string Reason;
 internal static PlaybackActions For(bool runtime, bool checkingRuntime, bool selected, bool supported, bool confirmed,
  bool preparing, bool cancelling, bool transition, bool closing, bool active, bool recoveryRequired, string fileReason) {
  var result = new PlaybackActions();
  result.Stop = !closing && !cancelling && (preparing || active);
  if (closing) result.Reason = "正在退出并清理本次播放任务…";
  else if (preparing) result.Reason = cancelling ? "正在取消启动准备…" : "正在核对所选壁纸和播放组件，可点击“取消启动”。";
  else if (transition) result.Reason = "正在停止或恢复原桌面，请稍候…";
  else if (recoveryRequired) result.Reason = "上次播放清理尚未确认，请先点击“停止并恢复”。";
  else if (checkingRuntime) result.Reason = "正在核对播放组件，完成后即可继续。";
  else if (!selected) result.Reason = "请先选中一张壁纸卡片。";
  else if (!supported) result.Reason = fileReason;
  else if (!runtime) result.Reason = "播放组件尚未准备好，请点击上方组件状态选择已有安装目录。";
  else {
   result.Preview = true;
   result.Desktop = confirmed;
   result.Reason = confirmed ? "可预览或设为桌面。停止后恢复原桌面，再次点击可重新开始。"
    : "先点击“预览”，看见正确画面后完成确认，即可设为桌面。";
  }
  return result;
 }
}
