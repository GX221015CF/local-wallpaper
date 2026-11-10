using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;
using System.Web.Script.Serialization;

// Separate local verification harness. Not part of the shipping application.
public sealed class StabilityBindingDomain : MarshalByRefObject {
 public int Execute(string[] args){return PublishedStabilityCheck.Execute(args);}
}
internal static class PublishedStabilityCheck {
 const BindingFlags Flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
 static Assembly app;
 static object Call(string type,string name,params object[] values){
  try{return app.GetType(type,true).GetMethods(Flags).Single(method=>method.IsStatic&&method.Name==name&&method.GetParameters().Length==values.Length&&method.GetParameters().Select((parameter,index)=>values[index]==null||parameter.ParameterType.IsInstanceOfType(values[index])).All(match=>match)).Invoke(null,values);}
  catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
 }
 static void Set(string type,string name,object value){app.GetType(type,true).GetField(name,Flags).SetValue(null,value);}
 static object Get(object value,string name){return value.GetType().GetProperty(name,Flags).GetValue(value,null);}
 static void Need(bool value,string description){if(!value)throw new InvalidDataException(description);}
 static int Main(string[] args){
  if(args[0]=="bind"){
   var domain=AppDomain.CreateDomain("verified-r32-binding",null,new AppDomainSetup{ApplicationBase=Path.Combine(Path.GetFullPath(args[2]),"build")});
   try{return ((StabilityBindingDomain)domain.CreateInstanceFromAndUnwrap(Assembly.GetExecutingAssembly().Location,typeof(StabilityBindingDomain).FullName)).Execute(args);}
   finally{AppDomain.Unload(domain);}
  }
  return Execute(args);
 }
 internal static int Execute(string[] args){
  var json=new JavaScriptSerializer{MaxJsonLength=16777216};
  try{
   string mode=args[0],root=Path.GetFullPath(args[1]);
   app=Assembly.LoadFrom(Path.Combine(root,"build","LocalWallpaper.exe"));
   Set("Common","AppRoot",root);Set("Common","Root",root);
   Call("VersionStore","VerifyRelease",root,Call("Common","Hash",Path.Combine(root,"build","release.json")),1);
   Need(app.GetType("Common").GetField("TestBeforeAtomicCommit",Flags)==null,"shipping assembly has no atomic injection hook");
   if(mode=="bind"){
    string previous=Path.GetFullPath(args[2]);object prior=Call("DataLocation","LoadBound",Path.Combine(previous,"build","data-location.json"));
    Set("Common","AppRoot",previous);Call("FirstUseSetup","EnsureVersionBinding",root,prior);
    File.WriteAllText(args[3],json.Serialize(new{passed=true,mode=mode,storeId=Get(prior,"StoreId"),dataRoot=Get(prior,"DataRoot"),guiStarted=false}));return 0;
   }
   object location=Call("DataLocation","LoadBound",Path.Combine(root,"build","data-location.json"));Call("DataLocation","ValidateStore",location,false);
   Set("DataLocation","Current",location);Set("DataLocation","Ready",true);string data=(string)Get(location,"DataRoot");Set("Common","Root",data);
   var library=Activator.CreateInstance(app.GetType("WallpaperLibrary",true),true);var document=library.GetType().GetField("Doc",Flags).GetValue(library);
   var items=(System.Collections.IList)Get(document,"Items");var snapshots=new List<object>();
   using(var budget=new CancellationTokenSource(30000)){
    Call("RuntimeProvisioning","Initialize",budget.Token);
    foreach(object item in items){
     string source=(string)Get(item,"SourcePath"),hash=(string)Get(item,"RegisteredSha256"),id=(string)Get(item,"ResourceId");
     Need(File.Exists(source),"existing personal resource remains present");
     if(id=="28987"||id=="398863"||id=="402242")Need((string)Call("Common","Hash",source)==hash,"affected resource identity stays exact");
     snapshots.Add(new{id=id,source=source,hash=hash,cover=Get(item,"CoverPath"),previewConfirmation=Get(item,"PreviewConfirmedSha256")});
    }
   }
   Need((bool)app.GetType("RuntimeProvisioning").GetProperty("Available",Flags).GetValue(null,null),"saved components pass unchanged production identity contract");
   File.WriteAllText(args[3],json.Serialize(new{passed=true,mode=mode,storeId=Get(location,"StoreId"),dataRoot=data,count=items.Count,selectedId=Get(document,"SelectedId"),items=snapshots,guiStarted=false,engineLoaded=false,evidenceKind="Published assembly handlers, persisted personal data, no main GUI Entry"}));return 0;
  }catch(Exception error){File.WriteAllText(args[3],json.Serialize(new{passed=false,error=error.ToString()}));return 1;}
 }
}
