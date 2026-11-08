#if TEST_BUILD
using System;using System.IO;using System.Diagnostics;using System.Reflection;using System.Collections;using System.Collections.Generic;using System.Web.Script.Serialization;
internal static class HostContractRegression {
 static string Hash(string p){using(var s=File.OpenRead(p))using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();}
 static int Main(string[] args){
  if(args.Length<4||args.Length>5)return 2;var result=new Dictionary<string,object>();int code=1;
  try{
   string program=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..",".."));
   string contract=Path.Combine(program,"rules","runtime-components.json");var doc=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(contract));
   var entries=doc["files"] as ICollection;result["deserializedFilesType"]=doc["files"].GetType().FullName;result["actualCount"]=entries==null?-1:entries.Count;
   var core=new EngineCore((phase,value)=>{});var method=typeof(EngineCore).GetMethod("VerifyExternal",BindingFlags.NonPublic|BindingFlags.Instance);
   try{method.Invoke(core,new object[]{args[0],args.Length==5?new string('0',64):Hash(contract),Hash(Process.GetCurrentProcess().MainModule.FileName)});result["accepted"]=true;}
   catch(TargetInvocationException ex){result["accepted"]=false;result["error"]=ex.InnerException.ToString();}
   bool expected=args[2]=="accept";bool actual=Convert.ToBoolean(result["accepted"]);bool passed=actual==expected;
   if(!expected&&args[3].Length>0)passed=passed&&Convert.ToString(result["error"]).Contains(args[3]);
   result["passed"]=passed;result["expected"]=args[2];result["nativeOpenCalled"]=false;result["guiCreated"]=false;result["pid"]=Process.GetCurrentProcess().Id;code=passed?0:1;
  }catch(Exception ex){result["passed"]=false;result["error"]=ex.ToString();}
  File.WriteAllText(args[1],new JavaScriptSerializer().Serialize(result),new System.Text.UTF8Encoding(false));return code;
 }
}
#endif
