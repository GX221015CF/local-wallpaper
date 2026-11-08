#if TEST_BUILD
using System;using System.IO;using System.Linq;using System.Text;using System.Threading.Tasks;using System.Windows.Forms;using System.Collections.Generic;
internal sealed partial class ControlWindow {
 static string TestMd5(string path){using(var stream=File.OpenRead(path))using(var h=System.Security.Cryptography.MD5.Create())return BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
 string QueueFixture(string id,string input,string fixture,bool damaged=false){string files=Path.Combine(fixture,"wallpaper","wpcache","dynamic"),records=Path.Combine(fixture,"cache_info");Directory.CreateDirectory(files);Directory.CreateDirectory(records);string temp=Path.Combine(fixture,id+".tmp");File.Copy(input,temp);
  if(damaged)using(var f=new FileStream(temp,FileMode.Open,FileAccess.Write))f.SetLength(32);
  string md5=TestMd5(temp),target=Path.Combine(files,md5+".cwp");if(!File.Exists(target))File.Move(temp,target);else File.Delete(temp);
  string record=Path.Combine(records,id+"_fixture.dat");byte[] bytes=Encoding.UTF8.GetBytes("0000"+Common.Json.Serialize(new{wpId=id,wpName=md5+".cwp",wpMd5=md5,wpType=2}));for(int i=0;i<bytes.Length;i++)bytes[i]^=0xe7;File.WriteAllBytes(record,bytes);return record;
 }
 internal async Task RunRealCandidateQueue(){try{var catalog=Common.ReadShared(library.FilePath);var sound=Common.Json.Serialize(settings);using(var d=new ImportDialog(this)){d.TestSkipInitialScan=true;d.Show();Application.DoEvents();var check=d.Scan();await Task.Delay(60);ClientShot(d,"real-candidates-queue-progress");await check;var rows=d.list.Items.Cast<ListViewItem>().Select(x=>(ImportCandidate)x.Tag).ToArray();var target=d.list.Items.Cast<ListViewItem>().FirstOrDefault(x=>((ImportCandidate)x.Tag).Id=="400243");Require(target!=null,"Known400243 is absent at现场");target.Selected=true;target.EnsureVisible();d.UpdateSelection();var c=(ImportCandidate)target.Tag;Require(CandidateChecks.Passed(c)&&c.Cover!=null&&c.Check.Sha256=="2e89b04908e04f2e79e09771c62c9904009493b26b08413fe553744a23223b2e","Actual400243 candidate check/cover");Require(rows.All(x=>x.Check!=null&&x.Check.State!="checking"),"Real queue did not finish every candidate");ClientShot(d,"real-candidates-queue-ready");Common.SaveNew(Path.Combine(Common.Root,"evidence","real-candidates.json"),rows.Select(x=>new{id=x.Id,name=x.Name,format=x.Format,resourceState=d.ResourceState(x),check=x.Check,cover=x.Cover,playback=d.PlaybackState(x)}).ToArray());Require(Common.ReadShared(library.FilePath)==catalog&&Common.Json.Serialize(settings)==sound&&CurrentRun==null,"Read-only real queue changed library/sound/playback");d.Close();Common.SaveNew(Path.Combine(Common.Root,"evidence","real-queue-result.json"),new{passed=true,candidates=rows.Length,automaticChecks=true,cover400243=true,catalogUnchanged=true,soundUnchanged=true,formalImports=0,playerStarted=false,userClicked=false,method="Current official local records, TEST_BUILD automatic dialog handlers, isolated copy of current catalog"});}Close();}catch(Exception ex){Common.SaveNew(Path.Combine(Common.Root,"evidence","real-queue-failed.json"),new{error=ex.ToString()});Close();}}
 internal async Task RunWorkflowUi(){if(File.Exists(Path.Combine(Common.Root,"tests","real-candidates"))){await RunRealCandidateQueue();return;}try{
  var baseline=Common.Json.Deserialize<LibraryDocument>(Common.ReadShared(Path.Combine(Common.AppRoot,"data","catalog.json")));
  var movie=baseline.Items.First(i=>i.RegisteredSha256==PackageAnalysis.ConfirmedMovieHash);
  string v2=Path.Combine(Directory.GetParent(Common.AppRoot).FullName,"local_import_guidance_20261101","library","400243","2e89b04908e04f2e79e09771c62c9904009493b26b08413fe553744a23223b2e","400243.cwp");
  Require(Common.Hash(v2)=="2e89b04908e04f2e79e09771c62c9904009493b26b08413fe553744a23223b2e"&&new FileInfo(v2).Length==943952,"400243 identity");
  string fixture=Path.Combine(Common.Root,"tests","fixture");Directory.CreateDirectory(Path.Combine(Common.Root,"tests"));File.WriteAllText(Path.Combine(Common.Root,"tests","import-root.txt"),fixture);
  string r2=QueueFixture("400243",v2,fixture),r3=QueueFixture("395683",movie.SourcePath,fixture),bad=QueueFixture("900001",v2,fixture,true);
  // Unknown directory, constructed from a copy only; never change an original header or magic.
  string unknownTemp=Path.Combine(Common.Root,"tests","unknown.cwp");File.Copy(movie.SourcePath,unknownTemp);using(var stream=new FileStream(unknownTemp,FileMode.Open,FileAccess.Write))using(var b=new BinaryWriter(stream)){stream.Position=8;b.Write(UInt32.MaxValue);}
  QueueFixture("900002",unknownTemp,fixture);
  using(var d=new ImportDialog(this)){d.TestSkipInitialScan=true;d.Show();Application.DoEvents();
   var job=d.Scan();await Task.Delay(20);Require(!d.import.Enabled,"Unchecked candidate import enabled");await job;
   Require(d.list.Items.Count==4,"Expected fixture candidate count");
   var row2=d.list.Items.Cast<ListViewItem>().Single(x=>((ImportCandidate)x.Tag).Id=="400243");var row3=d.list.Items.Cast<ListViewItem>().Single(x=>((ImportCandidate)x.Tag).Id=="395683");
   var c2=(ImportCandidate)row2.Tag;var c3=(ImportCandidate)row3.Tag;
   Require(CandidateChecks.Passed(c2)&&CandidateChecks.Passed(c3)&&c2.Cover!=null&&c3.Cover!=null,"Actual v2/v3 queue/cover failed");
   Require(row2.ImageKey!=""&&row3.ImageKey!=""&&d.list.Items.Cast<ListViewItem>().Any(x=>((ImportCandidate)x.Tag).Check.State=="failed"),"Thumbnails or failure isolation");
   Require(d.list.Items.Cast<ListViewItem>().Any(x=>((ImportCandidate)x.Tag).Check.State=="unknown"),"Unknown structure not isolated");
   Require(c2.Check.CoverMember=="preview.jpg"&&c2.Check.Sha256==Common.Hash(v2),"400243 cover identity");
   row2.Selected=true;row2.EnsureVisible();d.UpdateSelection();Require(d.import.Enabled&&d.analyze.Enabled&&d.CoverState(c2)=="真实图片","v2 actions differ from actual check result");
   ClientShot(d,"queue-v2-real-ready");row3.Selected=true;row3.EnsureVisible();d.UpdateSelection();ClientShot(d,"queue-v3-real-ready");
   var prior=c2.Check;await d.Scan();c2=(ImportCandidate)d.list.Items.Cast<ListViewItem>().Single(x=>((ImportCandidate)x.Tag).Id=="400243").Tag;
   Require(c2.Check.CacheHit&&c2.Check.HashedBytes==0,"Refresh rehashed a passed stable v2");
   Require(d.list.Items.Cast<ListViewItem>().Single(x=>((ImportCandidate)x.Tag).Id=="395683").Tag is ImportCandidate&&((ImportCandidate)d.list.Items.Cast<ListViewItem>().Single(x=>((ImportCandidate)x.Tag).Id=="395683").Tag).Check.HashedBytes==0,"Refresh rehashed stable v3");
   // Same source newly discovered under a second record: sorting uses discovery history.
   QueueFixture("900003",v2,fixture);await d.Scan();
   Require(((ImportCandidate)d.list.Items[0].Tag).Id=="900003"&&((ImportCandidate)d.list.Items[0].Tag).NewlyDiscovered,"New resource not sorted before historical baseline");
   row2=d.list.Items.Cast<ListViewItem>().Single(x=>((ImportCandidate)x.Tag).Id=="400243");row2.Selected=true;d.UpdateSelection();
   await d.ImportSelected();Require(library.Doc.Items.Count==1&&CurrentRun==null,"Isolated import duplicated/autoplayed");
   var saved=library.Doc.Items.Single();Require(File.Exists(saved.CoverPath)&&LocalImport.Under(saved.CoverPath,Path.Combine(Common.Root,"library"))&&WallpaperLibrary.LoadCover(saved)!=null,"Managed homepage cover association");
   Require(saved.RegisteredSha256==prior.Sha256&&saved.PreviewConfirmedSha256==null&&saved.DesktopConfirmedSha256==null,"Import inherited playback confirmation");
   Require(d.ResourceState((ImportCandidate)row2.Tag)=="已入库"&&d.PlaybackState((ImportCandidate)row2.Tag)=="尚未验证","Resource state merged with playback state");
   await d.ImportSelected();Require(library.Doc.Items.Count==1&&d.status.Text.Contains("未重复建卡"),"Real duplicate handler result lost");
   var reloaded=new WallpaperLibrary();Require(reloaded.Doc.Items.Count==1&&File.Exists(reloaded.Doc.Items[0].CoverPath),"Restart lost imported metadata/cover");
   ClientShot(this,"queue-isolated-home");ClientShot(d,"queue-duplicate-result");
   // Corrupt only a generated thumbnail, then recheck the same real source through the existing decoder.
   c2=(ImportCandidate)row2.Tag;File.WriteAllBytes(c2.Cover,new byte[]{1,2,3});Require(!CandidateChecks.Load(c2,()=>{}),"Corrupt thumbnail cache accepted");
   await d.AnalyzeSelected();c2=(ImportCandidate)row2.Tag;Require(CandidateChecks.Passed(c2)&&c2.Cover!=null,"Corrupt thumbnail did not regenerate");
   // Recheck is only the selected target; cancel the actual own process and retry.
   var pending=d.AnalyzeSelected();importOperation.Cancel();await pending;Require(((ImportCandidate)row2.Tag).Check.State=="cancelled"&&!d.import.Enabled&&d.analyze.Enabled&&!importOperation.Busy,"Actual cancellation actions");
   await d.AnalyzeSelected();Require(CandidateChecks.Passed((ImportCandidate)row2.Tag)&&d.import.Enabled,"Cancel then retry did not recover");
   var closing=d.AnalyzeSelected();d.Close();await closing;for(int i=0;i<50&&!d.IsDisposed;i++)await Task.Delay(50);Require(d.IsDisposed&&!importOperation.Busy,"Close did not stop actual candidate worker");
  }
  // Relative cache covers survive moving the private data root.
  string savedRoot=Common.Root,migrated=Path.Combine(Common.Root,"tests","migrated-cache");Directory.CreateDirectory(Path.Combine(migrated,"data","candidate-checks"));
  foreach(string file in Directory.GetFiles(Path.Combine(savedRoot,"data","candidate-checks")))File.Copy(file,Path.Combine(migrated,"data","candidate-checks",Path.GetFileName(file)));
  var stable=LocalImport.ReadRecord(r3);Require(CandidateChecks.Load(stable,()=>{}),"Stable v3 cache unavailable");string relative=stable.Check.CoverRelative;Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(migrated,relative)));File.Copy(stable.Cover,Path.Combine(migrated,relative));
  var moved=LocalImport.ReadRecord(r3);try{Common.Root=migrated;Require(CandidateChecks.Load(moved,()=>{})&&LocalImport.Under(moved.Cover,migrated),"Migrated cache used an old absolute cover path");}finally{Common.Root=savedRoot;}
  string originalRules=PackageAnalysis.RulesPath,changedRules=Path.Combine(Common.Root,"tests","rules-changed.json");File.WriteAllText(changedRules,Common.ReadShared(originalRules)+" ");try{PackageAnalysis.RulesPath=changedRules;Require(!CandidateChecks.Load(stable,()=>{}),"Rule change did not invalidate candidate cache");}finally{PackageAnalysis.RulesPath=originalRules;}
  // Observed file changes are rejected; malformed records do not grant admission.
  var changing=LocalImport.ReadRecord(r2);bool mutated=false;CandidateChecks.Run(changing,()=>{},p=>{if(p.Contains("完整SHA")&&!mutated){mutated=true;using(var f=new FileStream(changing.Source,FileMode.Open,FileAccess.ReadWrite,FileShare.ReadWrite)){f.Position=f.Length-1;int value=f.ReadByte();f.Position--;f.WriteByte((byte)(value^1));}}},true);
  Require(changing.Check.State=="failed"&&!CandidateChecks.Passed(changing),"Changed content was allowed to import");
  var writing=LocalImport.ReadRecord(r3);string observedPhase=null;int stabilityChecks=0;CandidateChecks.Run(writing,()=>{if(observedPhase=="检查文件稳定性"&&++stabilityChecks==2)File.SetLastWriteTimeUtc(writing.Source,DateTime.UtcNow.AddSeconds(-30));},p=>observedPhase=p,true);Require(writing.Check.State=="waiting"&&writing.Check.Reason.Contains("观察到"),"Changing file was mislabeled as an ordinary unchecked candidate");
  string missing=Path.Combine(fixture,"cache_info","900004_fixture.dat");File.WriteAllText(missing,"broken");bool rejected=false;try{LocalImport.ReadRecord(missing);}catch{rejected=true;}Require(rejected,"Damaged record not isolated");
  Require(RuntimeDelivery.Verify(Common.AppRoot)==24,"Complete runtime validation failed");
  string negative=Path.Combine(Common.Root,"tests","runtime-negative");Directory.CreateDirectory(Path.Combine(negative,"rules"));Directory.CreateDirectory(Path.Combine(negative,"build"));
  File.Copy(Path.Combine(Common.AppRoot,"rules","runtime-components.json"),Path.Combine(negative,"rules","runtime-components.json"));
  File.Copy(Path.Combine(Common.AppRoot,"rules","v3-structure-families.json"),Path.Combine(negative,"rules","v3-structure-families.json"));
  bool missingManifest=false;try{RuntimeDelivery.Verify(negative);}catch{missingManifest=true;}Require(missingManifest,"Missing runtime manifest accepted");
  string manifest=Common.ReadShared(Path.Combine(Common.AppRoot,"build","runtime-manifest.json"));File.WriteAllText(Path.Combine(negative,"build","runtime-manifest.json"),manifest.Replace("avcodec-58.dll","../avcodec-58.dll"));bool unsafeManifest=false;try{RuntimeDelivery.Verify(negative);}catch{unsafeManifest=true;}Require(unsafeManifest,"Unsafe runtime path accepted");
  File.WriteAllText(Path.Combine(negative,"build","runtime-manifest.json"),manifest);Directory.CreateDirectory(Path.Combine(negative,"build","runtime"));File.WriteAllBytes(Path.Combine(negative,"build","runtime","avcodec-58.dll"),new byte[]{0});bool corruptRuntime=false;try{RuntimeDelivery.Verify(negative);}catch{corruptRuntime=true;}Require(corruptRuntime,"Corrupt runtime accepted");
  Common.SaveNew(Path.Combine(Common.Root,"evidence","workflow-ui-result.json"),new{passed=true,v2="400243 actual file/preview/isolated import",v3="395683 actual known structure",automaticallyQueued=true,refreshUsesCache=true,migratedCacheUsesRelativeCover=true,ruleChangeInvalidatesCache=true,observedChangingFileWaits=true,unknownIsolated=true,damagedIsolated=true,changedSourceRejected=true,corruptCoverRegenerated=true,cancelRetry=true,boundedClose=true,duplicatePromptPreserved=true,restartCoverPreserved=true,runtimeChecks=new{complete=true,missingManifestRejected=missingManifest,pathTraversalRejected=unsafeManifest,componentCorruptionRejected=corruptRuntime},importedOnlyInIsolatedLibrary=true,formalImports=0,playerStarted=false,newPlaybackConfirmation=false,method="Actual WinForms handlers and import subprocesses with explicit isolated fixture root; not user clicks"});
  Close();
 }catch(Exception ex){Common.ReplaceJson(Path.Combine(Common.Root,"evidence","workflow-ui-failed.json"),new{error=ex.ToString()});Write("candidate-tests-failed",new{error=ex.ToString()});Close();}}
}
#endif
