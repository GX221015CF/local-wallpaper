using System;
using System.IO;

internal sealed class PlaybackFailure {
 internal string Stage, Error;
 internal static bool Failed(int exitCode,PlaybackFailure failure){return exitCode!=0||failure!=null;}
 internal string Message {
  get {
   string text = Error ?? "播放器失败";
   int end = text.IndexOfAny(new[] { '\r', '\n' });
   if (end >= 0) text = text.Substring(0, end);
   return FaultSafety.Text(text, 220);
  }
 }
 internal static bool KeepPrimary(FaultRecord primary, bool supplement, string state) {
  return supplement && primary != null &&
   (primary.Category == FaultSafety.Category("start") || primary.Category == FaultSafety.Category("stop")) &&
   (state.Contains("failed") || state.Contains("unconfirmed") || state == "stop-timeout" || state == "worker-lost" || state == "worker-stalled");
 }
 internal static PlaybackFailure ReadHost(string run) {
  PlaybackFailure failure = null;
  using (var lines = new IncrementalLines(Path.Combine(run, "host.jsonl"))) {
   foreach (var row in lines.Pull()) {
    string phase = Common.S(row, "phase");
    if (phase != "engine-failed" && phase != "attach-failed" && phase != "stop-error" && phase != "detach-failed") continue;
    var data = Common.Obj(row["data"]);
    string error = Common.S(data, "error");
    if(error.Length>0){if(failure==null)failure=new PlaybackFailure{Stage=phase,Error=error};else failure.Error+="\n["+phase+"] "+error;}
   }
  }
  return failure;
 }
}
