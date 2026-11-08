using System;using System.IO;using System.Reflection;using System.Reflection.Emit;using System.Collections.Generic;using System.Web.Script.Serialization;
// Metadata only; never constructs a Form or invokes a target method.
internal static class MultiWiring {
 static Dictionary<short,OpCode> opcodes=new Dictionary<short,OpCode>();
 static List<string> Calls(MethodInfo method){
  var calls=new List<string>();byte[] body=method.GetMethodBody().GetILAsByteArray();
  for(int i=0;i<body.Length;){short value=body[i++];if(value==0xfe)value=unchecked((short)(0xfe00|body[i++]));OpCode op=opcodes[value];int size;
   switch(op.OperandType){
    case OperandType.InlineNone:size=0;break;
    case OperandType.ShortInlineBrTarget:case OperandType.ShortInlineI:case OperandType.ShortInlineVar:size=1;break;
    case OperandType.InlineVar:size=2;break;
    case OperandType.InlineI8:case OperandType.InlineR:size=8;break;
    case OperandType.InlineSwitch:size=4+4*BitConverter.ToInt32(body,i);break;
    default:size=4;break;
   }
   if(op.OperandType==OperandType.InlineMethod){MethodBase target=method.Module.ResolveMethod(BitConverter.ToInt32(body,i));calls.Add(target.DeclaringType.Name+"."+target.Name);}
   i+=size;
  }return calls;
 }
 static int Main(string[] args){
  if(args.Length!=2)return 2;var cases=new List<object>();bool passed=true;
  try{
   AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve+=(s,e)=>Assembly.ReflectionOnlyLoad(e.Name);
   foreach(var field in typeof(OpCodes).GetFields(BindingFlags.Static|BindingFlags.Public)){if(field.FieldType==typeof(OpCode)){var op=(OpCode)field.GetValue(null);opcodes[op.Value]=op;}}
   var assembly=Assembly.ReflectionOnlyLoadFrom(args[0]);var ui=assembly.GetType("ControlWindow",true);
   string[,] required={{"RefreshRuntimeNotice","RuntimePresentation.Runtime"},{"SelectWallpaper","WallpaperLibrary.Select"},{"RegisterImport","ControlWindow.SelectWallpaper"},{"RefreshPlaybackActions","MultiWallpaperPresentation.Selection"},{"PresentationTick","ControlWindow.UpdateFriendlyPlayback"},{"UpdatePreviewCoverButton","RuntimePresentation.CoverUnavailable"}};
   for(int i=0;i<required.GetLength(0);i++){var method=ui.GetMethod(required[i,0],BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);var calls=Calls(method);bool ok=calls.Contains(required[i,1]);cases.Add(new{caller=required[i,0],target=required[i,1],passed=ok});passed&=ok;}
   var nested=ui.GetNestedTypes(BindingFlags.NonPublic);foreach(string methodName in new[]{"StartMode","StopManaged"}){Type machine=Array.Find(nested,t=>t.Name.Contains("<"+methodName+">"));bool wired=machine!=null&&Calls(machine.GetMethod("MoveNext",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)).Contains(methodName=="StartMode"?"PlaybackSwitch.Run":"PlaybackSwitch.CancelKeepsOld");cases.Add(new{name=methodName+"-actual-async-wiring",passed=wired});passed&=wired;}
   bool testType=assembly.GetType("MultiWallpaperRegression",false)!=null||assembly.GetType("MultiWiring",false)!=null;
   cases.Add(new{name="test-harness-not-in-release",passed=!testType});passed&=!testType;
   File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{passed=passed,cases=cases,method="ReflectionOnlyLoadFrom and decoded IL call targets from actual release app",targetMethodsInvoked=false,guiCreated=false,engineLoaded=false}));
  }catch(Exception ex){File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(new{passed=false,error=ex.ToString(),cases=cases}));return 2;}
  return passed?0:1;
 }
}
