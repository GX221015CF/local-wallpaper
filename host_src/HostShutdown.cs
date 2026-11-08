using System;using System.Collections.Generic;
// Used by the actual FormClosing path; no GUI or native calls in this coordinator.
internal sealed class HostShutdown {
 internal readonly List<Exception> Errors=new List<Exception>();
 internal bool Failed{get{return Errors.Count!=0;}}
 internal static int ExitCode(string reason,bool cleanupFailed){return reason=="engine-failed"?3:cleanupFailed?4:0;}
 internal static HostShutdown Complete(Action stop,Action detach,Action<string,object> log){
  var result=new HostShutdown();
  result.Attempt(stop,"stop-error",log);
  result.Attempt(detach,"detach-failed",log);
  return result;
 }
 void Attempt(Action action,string phase,Action<string,object> log){
  try{action();}catch(Exception original){Errors.Add(original);try{log(phase,new{error=original.ToString(),cleanupSucceeded=false});}catch(Exception diagnostic){Errors.Add(diagnostic);}}
 }
}
