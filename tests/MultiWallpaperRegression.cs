#if TEST_BUILD
using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.Security.Cryptography;using System.IO.Compression;using System.Text;
internal static class MultiWallpaperRegression {
 static List<string> checks=new List<string>();
 static void Need(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
 static byte[] Pack(byte[] data,bool compress){
  if(compress)using(var mem=new MemoryStream()){using(var d=new DeflateStream(mem,CompressionMode.Compress,true))d.Write(data,0,data.Length);data=mem.ToArray();}
  byte[] password=Encoding.ASCII.GetBytes("123"),salt,key,iv;
  using(var h=SHA256.Create())salt=h.ComputeHash(password);using(var h=MD5.Create()){key=h.ComputeHash(salt.Concat(h.ComputeHash(password)).ToArray());iv=h.ComputeHash(salt);}
  using(var aes=Aes.Create()){aes.Key=key;aes.IV=iv;using(var e=aes.CreateEncryptor())return e.TransformFinalBlock(data,0,data.Length);}
 }
 static byte[] Table(uint a,uint al,uint b,uint bl){using(var mem=new MemoryStream())using(var w=new BinaryWriter(mem)){w.Write(2u);foreach(var entry in new[]{Tuple.Create(a,al,"preview.png"),Tuple.Create(b,bl,"sample.bin")}){var n=Encoding.UTF8.GetBytes(entry.Item3);w.Write(entry.Item1);w.Write(entry.Item2);w.Write(0u);w.Write((uint)n.Length);w.Write(n);}return mem.ToArray();}}
 // Same known CWP-v2 container layout as prior import fixture. No renderable scene.
 static string Fixture(string folder,string name,byte value){
  byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/lN8AAAAASUVORK5CYII=");
  byte[] preview=Pack(png,true),payload=Pack(Enumerable.Repeat(value,512).ToArray(),true),table=new byte[0];uint size=0;
  for(int i=0;i<8;i++){table=Pack(Table(16+size,(uint)preview.Length,16+size+(uint)preview.Length,(uint)payload.Length),false);if(table.Length==size)break;size=(uint)table.Length;}
  string path=Path.Combine(folder,name+".cwp");using(var w=new BinaryWriter(File.Create(path))){w.Write(0x02505743u);w.Write((uint)(16+table.Length+preview.Length+payload.Length));w.Write((uint)table.Length);w.Write(0u);w.Write(table);w.Write(preview);w.Write(payload);}return path;
 }
 static WallpaperItem Import(string path,string label){
  string id=label=="A"?"730023":"730024",sourceRoot=Path.Combine(Common.Root,"fixtures","source");
  Directory.CreateDirectory(Path.Combine(sourceRoot,"cache_info"));Directory.CreateDirectory(Path.Combine(sourceRoot,"wallpaper","wpcache","dynamic"));
  File.WriteAllText(Path.Combine(Common.Root,"tests","import-root.txt"),sourceRoot);
  string md5;using(var h=MD5.Create())md5=BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
  string source=Path.Combine(sourceRoot,"wallpaper","wpcache","dynamic",md5+".cwp");File.Copy(path,source,false);File.SetLastWriteTimeUtc(source,DateTime.UtcNow.AddMinutes(-1));
  string record=Path.Combine(sourceRoot,"cache_info",id+"_fixture.dat");byte[] encoded=Encoding.UTF8.GetBytes("0000"+Common.Json.Serialize(new{wpId=id,wpName=md5+".cwp",wpMd5=md5,wpType=2}));for(int i=0;i<encoded.Length;i++)encoded[i]^=0xe7;File.WriteAllBytes(record,encoded);
  var candidate=LocalImport.ReadRecord(record);string op=Path.Combine(Common.WorkRoot,"multi-"+label);Directory.CreateDirectory(op);CoverCache.WorkFolder=Path.Combine(op,"cover-temp");
  CandidateChecks.Run(candidate,()=>{},null,true);Need(CandidateChecks.Passed(candidate),"valid-controlled-v2-candidate-"+label);
  using(var parent=System.Diagnostics.Process.GetCurrentProcess()){Common.SaveNew(Path.Combine(op,"request.json"),new{mode="copy",candidate=candidate,parentPid=parent.Id,parentCreation=ProcessProof.Creation(parent.Handle).ToString()});}
  File.WriteAllText(Path.Combine(op,"go"),"");Need(LocalImport.Worker(op)==0,"actual-copy-handler-"+label);
  var result=Common.Read(Path.Combine(op,"result.json"));var receipt=Common.Json.Deserialize<ImportReceipt>(Common.Json.Serialize(result["receipt"]));bool duplicate;
  var library=new WallpaperLibrary();var item=ManagedCatalog.Commit(library,receipt,op,Convert.ToBoolean(result["existingTargetVerified"]),()=>{},out duplicate);LocalImport.CleanPayload(op);return item;
 }
 static void Fails(Action action){try{action();throw new Exception("expected failure absent");}catch(IOException){}}
 static void Cancelled(Task action){try{action.GetAwaiter().GetResult();throw new Exception("expected cancellation absent");}catch(OperationCanceledException){}}
 static async Task Switches(string source,string invalid){
  int active=1,stops=0,starts=0;string selected="B";var sw=new PlaybackSwitch();
  Func<Task<bool>> stop=()=>{Need(active==1,"stop-sees-one-owned-run");stops++;active=0;return Task.FromResult(true);};
  Action<string[]> launch=v=>{Need(active==0,"launch-only-after-old-stopped");active=1;starts++;};
  Func<Task<string[]>> verify=()=>Task.FromResult(new[]{PlaybackResourceCheck.Verify(source,"preview",null,false,CancellationToken.None,_=>{}),"inert-no-engine"});
  string result=await sw.Run(verify,()=>selected=="B",v=>false,()=>active==1,stop,launch,CancellationToken.None);
  Need(result=="launch-requested"&&starts==1&&stops==1&&active==1&&!sw.Busy,"actual-coordinator-stop-before-one-new-launch");
  int before=stops;result=await sw.Run(verify,()=>true,v=>true,()=>true,stop,launch,CancellationToken.None);Need(result=="same-active"&&stops==before&&starts==1,"same-resource-mode-does-not-duplicate");
  try{await sw.Run(()=>Task.FromResult(new[]{PlaybackResourceCheck.Verify(invalid,"preview",null,false,CancellationToken.None,_=>{}),"none"}),()=>true,v=>false,()=>true,stop,launch,CancellationToken.None);throw new Exception("invalid preparation passed");}catch(InvalidDataException){}
  Need(stops==before&&active==1&&starts==1&&!sw.LaunchAttempted,"failed-actual-preparation-keeps-old-run");
  using(var cancel=new CancellationTokenSource()){cancel.Cancel();Cancelled(sw.Run(verify,()=>true,v=>false,()=>true,stop,launch,cancel.Token));}
  Need(active==1&&stops==before,"cancel-before-replacement-keeps-old-run");
  var pending=new TaskCompletionSource<string[]>();var first=sw.Run(()=>pending.Task,()=>true,v=>false,()=>true,stop,launch,CancellationToken.None);
  Need(sw.CancelKeepsOld("user-stop")&&!sw.CancelKeepsOld("controller-close"),"cancel-new-preparation-keeps-old-but-close-does-not");
  result=await sw.Run(verify,()=>true,v=>false,()=>true,stop,launch,CancellationToken.None);Need(result=="busy"&&starts==1,"concurrent-repeat-click-rejected");pending.SetResult(new[]{"fixture","none"});await first;
  result=await sw.Run(verify,()=>true,v=>false,()=>true,()=>Task.FromResult(false),launch,CancellationToken.None);Need(result=="cleanup-unconfirmed"&&active==1&&starts==2,"uncertain-old-cleanup-blocks-new-run");
  selected="B";using(var cancel=new CancellationTokenSource()){Cancelled(sw.Run(verify,()=>selected=="B",v=>false,()=>true,()=>{active=0;cancel.Cancel();return Task.FromResult(true);},launch,cancel.Token));}
  Need(active==0&&starts==2&&!sw.LaunchAttempted,"cancel-after-stop-never-restarts-or-launches");
  active=1;selected="B";Cancelled(sw.Run(verify,()=>selected=="B",v=>false,()=>true,()=>{active=0;selected="C";return Task.FromResult(true);},launch,CancellationToken.None));Need(active==0&&starts==2,"selection-change-during-stop-blocks-stale-B");
  selected="B";try{await sw.Run(verify,()=>true,v=>false,()=>false,stop,v=>{throw new IOException("fixture-new-launch-failure");},CancellationToken.None);throw new Exception("launch should fail");}catch(IOException){}
  Need(active==0&&!sw.Busy&&sw.LaunchAttempted,"failed-new-launch-no-false-success-and-gate-released");
  result=await sw.Run(verify,()=>true,v=>false,()=>false,stop,launch,CancellationToken.None);Need(active==1&&result=="launch-requested","retry-after-failed-switch");
  using(var preparation=new PlaybackPreparation()){
   Need(preparation.TryBegin(1000),"actual-preparation-starts-for-cancel-path");
   PlaybackResourceCheck.TestAfterHashChunk=t=>preparation.Cancel();
   try{Cancelled(sw.Run(()=>preparation.VerifyAsync(source,"preview",null,false),()=>true,v=>false,()=>true,stop,launch,preparation.Token));Need(active==1&&!sw.Busy,"cancel-during-actual-async-resource-prepare-keeps-old");}
   finally{PlaybackResourceCheck.TestAfterHashChunk=null;preparation.Complete();}
  }
  var actions=PlaybackActions.For(true,false,true,true,false,false,false,false,false,true,false,"");Need(actions.Preview&&!actions.Desktop&&actions.Stop,"unconfirmed-B-can-preview-but-not-desktop-while-A-active");
  actions=PlaybackActions.For(true,false,true,true,true,false,false,false,true,true,false,"");Need(!actions.Preview&&!actions.Desktop&&!actions.Stop,"closing-blocks-all-new-actions");
  string note=MultiWallpaperPresentation.Selection("B","A","隔离A",true,"可以预览");Need(note.Contains("隔离A")&&note.Contains("不会切换"),"selected-B-and-running-A-explicit");
  Need(MultiWallpaperPresentation.AttemptFailure("B","source changed","A",true).Contains("原任务“A”仍在运行"),"attempt-failure-names-kept-old-run");
 }
 static int Main(string[] args){
  if(args.Length!=2)return 2;string report=Path.GetFullPath(args[1]);
  try{
   Common.Root=Path.Combine(Common.AppRoot,"tests");Directory.CreateDirectory(Path.Combine(Common.Root,"data"));Directory.CreateDirectory(Path.Combine(Common.Root,"tests"));
   if(args[0]=="reopen"){
    var lib=new WallpaperLibrary();Need(lib.Doc.Items.Count==2&&lib.Selected.ResourceId=="730024","fresh-process-two-items-and-selected-B");
    var a=lib.Doc.Items.Single(x=>x.ResourceId=="730023");var b=lib.Doc.Items.Single(x=>x.ResourceId=="730024");Need(a.PreviewConfirmedSha256==a.RegisteredSha256&&b.PreviewConfirmedSha256==null,"fresh-process-independent-confirmations");
    Need(lib.Doc.Items.All(x=>File.Exists(x.SourcePath)&&Common.Hash(x.SourcePath)==x.RegisteredSha256&&x.SourcePath.StartsWith(Path.Combine(Common.Root,"library"))),"fresh-process-resource-relations");
    Need(!AppSettings.Load().Muted&&AppSettings.Load().Volume==35,"fresh-process-settings-readback");
    Need(!Directory.Exists(Path.Combine(Common.Root,"logs"))&&!File.Exists(LoginRestore.TargetPath),"reopen-reading-selection-does-not-launch-or-save-restore-target");
   }else{
    string fixtures=Path.Combine(Common.Root,"fixtures");Directory.CreateDirectory(fixtures);string pa=Fixture(fixtures,"A",11),pb=Fixture(fixtures,"B",29),bad=Path.Combine(fixtures,"invalid.cwp");File.WriteAllBytes(bad,new byte[64]);
    Common.SaveNew(Path.Combine(Common.Root,"data","catalog.json"),new LibraryDocument{Version=1,Items=new List<WallpaperItem>()});
    var a=Import(pa,"A");var b=Import(pb,"B");var lib=new WallpaperLibrary();a=lib.Doc.Items.Single(x=>x.ResourceId=="730023");b=lib.Doc.Items.Single(x=>x.ResourceId=="730024");
    Need(a.RegisteredSha256!=b.RegisteredSha256&&a.CoverPath!=b.CoverPath&&a.SourceRecordPath!=b.SourceRecordPath,"two-real-import-handlers-distinct-registration-resource-cover");
    lib.Select(a);string disk=Common.Hash(lib.FilePath),fail=Path.Combine(Common.Root,"tests","fail-catalog-save");File.WriteAllText(fail,"controlled failure");
    Fails(()=>lib.Select(b));Need(lib.Selected==a&&Common.Hash(lib.FilePath)==disk,"selection-save-failure-rolls-back-memory-and-file");
    Fails(()=>lib.ConfirmPreview(a,a.RegisteredSha256,"fixture-run","fixture-no-user-acceptance"));Need(a.PreviewConfirmedSha256==null&&Common.Hash(lib.FilePath)==disk,"confirmation-save-failure-rolls-back-memory-and-file");File.Delete(fail);
    File.WriteAllText(fail,"controlled failure");Fails(()=>{bool dup;lib.Add(pb,out dup);});Need(lib.Doc.Items.Count==2&&lib.Selected==a&&Common.Hash(lib.FilePath)==disk,"add-save-failure-no-phantom-card");File.Delete(fail);
#if !LEGACY_R22
    bool foreign=false;try{lib.ConfirmPreview(new WallpaperItem{Id=a.Id,SourcePath=a.SourcePath,RegisteredSha256=a.RegisteredSha256},a.RegisteredSha256,"bad","fixture");}catch(InvalidDataException){foreign=true;}Need(foreign,"foreign-item-confirmation-rejected");
#endif
    lib.ConfirmPreview(a,a.RegisteredSha256,"fixture-run","TEST_BUILD-fixture-only");Need(lib.CanMountExact(a,a.RegisteredSha256)&&!lib.CanMountExact(b,b.RegisteredSha256),"A-confirmation-not-applied-to-B");
    bool gated=false;try{PlaybackResourceCheck.Verify(b.SourcePath,"desktop",a.PreviewConfirmedSha256,false,CancellationToken.None,_=>{});}catch(InvalidDataException){gated=true;}Need(gated,"actual-B-desktop-resource-gate-rejects-A-hash");
    byte[] original=File.ReadAllBytes(a.SourcePath);using(var f=new FileStream(a.SourcePath,FileMode.Open,FileAccess.Write)){f.Position=f.Length-1;f.WriteByte((byte)(original[original.Length-1]^1));}
    bool stale=false;try{PlaybackResourceCheck.Verify(a.SourcePath,"desktop",a.PreviewConfirmedSha256,false,CancellationToken.None,_=>{});}catch(InvalidDataException){stale=true;}Need(stale,"resource-change-rejects-old-confirmation");File.WriteAllBytes(a.SourcePath,original);
    Switches(b.SourcePath,bad).GetAwaiter().GetResult();lib.Select(b);new AppSettings{Version=1,Muted=false,Volume=35}.Save();
    Need(lib.Doc.Items.Count==2&&b.PreviewConfirmedSha256==null&&lib.Doc.Items.All(x=>x.DesktopConfirmedSha256==null),"switch-tests-create-no-new-confirmations");
   }
   Common.SaveNew(report,new{passed=true,mode=args[0],checks=checks,guiConstructed=false,thirdPartyEngineLoaded=false,nativePlaybackExecuted=false,switchEvidence="actual production coordinator with controlled stop/launch delegates; not real rendered switching",containerFixture="valid known v2 directory/cover and inert sample member, no renderable scene, not a playback-success fixture",persistentSettingsScope="isolated app only; no WindowsStartupStore accessed"});return 0;
  }catch(Exception ex){Common.SaveNew(report,new{passed=false,checks=checks,error=ex.ToString(),guiConstructed=false,engineLoaded=false});return 1;}
 }
}
#endif
