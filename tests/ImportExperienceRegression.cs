#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class ImportExperienceRegression {
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public;
 static readonly List<object> Results=new List<object>();
 static void Need(bool value,string text){if(!value)throw new InvalidOperationException(text);}
 static object Field(object value,string name){return value.GetType().GetField(name,Flags).GetValue(value);}
 static object Call(object value,string name,params object[] args){return value.GetType().GetMethods(Flags).Single(m=>m.Name==name&&m.GetParameters().Length==args.Length).Invoke(value,args);}
 static void Check(string name,Action test){try{test();Results.Add(new{name=name,passed=true});}catch(Exception error){Results.Add(new{name=name,passed=false,error=error.ToString()});}}
 static Form Dialog(ControlWindow owner){return (Form)Activator.CreateInstance(typeof(ControlWindow).GetNestedType("ImportDialog",Flags),Flags,null,new object[]{owner},null);}
 static string NameFixture(string label,int count,string[] ids,string[] names){
  string folder=Path.Combine(Common.Root,"tests","name-fixture-"+label);Directory.CreateDirectory(folder);
  byte[] index=new byte[368+131072*4],data=new byte[8192+count*256];
  Action<byte[],int,uint> put=(bytes,offset,value)=>Array.Copy(BitConverter.GetBytes(value),0,bytes,offset,4);
  put(index,0,0xc103cac3);put(index,4,0x30000);put(index,8,(uint)count);put(index,28,131072);
  for(int i=0;i<count;i++){
   int bucket=i+1,offset=8192+i*256;put(index,368+bucket*4,0xa0010000u+(uint)i);put(data,offset,(uint)bucket);
   int target=i-(count-ids.Length);string url="https://unrelated.invalid/owned-fixture";
   if(target>=0){
    url="https://pc-store.lenovomm.cn/pcwallpapercontent/wallpaper/jingling/detailDynamic?wallpaperId="+ids[target];
    byte[] body=System.Text.Encoding.UTF8.GetBytes(Common.Json.Serialize(new{status=0,data=new{wallpaperId=ids[target],wallpaperName=names[target]}}));
    File.WriteAllBytes(Path.Combine(folder,"f_"+(target+1).ToString("x6")),body);put(data,offset+44,(uint)body.Length);put(data,offset+60,0x80000000u+(uint)target+1);
   }
   byte[] key=System.Text.Encoding.UTF8.GetBytes(url);put(data,offset+32,(uint)key.Length);Array.Copy(key,0,data,offset+96,key.Length);
  }
  File.WriteAllBytes(Path.Combine(folder,"index"),index);File.WriteAllBytes(Path.Combine(folder,"data_1"),data);
  File.WriteAllText(Path.Combine(Common.Root,"tests","name-cache-root.txt"),folder);return folder;
 }
 static ImportCandidate Resource(string label,string id,byte seed,bool noCover=false){
  string folder=Path.Combine(Common.Root,"tests","source");Directory.CreateDirectory(Path.Combine(folder,"cache_info"));Directory.CreateDirectory(Path.Combine(folder,"wallpaper","wpcache","dynamic"));
  File.WriteAllText(Path.Combine(Common.Root,"tests","import-root.txt"),folder);
  string file=(string)typeof(MultiWallpaperRegression).GetMethod("Fixture",Flags).Invoke(null,new object[]{folder,label,seed});string md5;
  if(noCover){
   byte[] resource=File.ReadAllBytes(file);int count=BitConverter.ToInt32(resource,8);
   byte[] decoded=(byte[])typeof(CoverCache).GetMethod("Decrypt",Flags).Invoke(null,new object[]{resource.Skip(16).Take(count).ToArray()});
   byte[] before=System.Text.Encoding.UTF8.GetBytes("preview.png"),after=System.Text.Encoding.UTF8.GetBytes("payload.png");
   for(int index=0;index<=decoded.Length-before.Length;index++)if(decoded.Skip(index).Take(before.Length).SequenceEqual(before)){Array.Copy(after,0,decoded,index,after.Length);break;}
   byte[] packed=(byte[])typeof(MultiWallpaperRegression).GetMethod("Pack",Flags).Invoke(null,new object[]{decoded,false});Need(packed.Length==count,"owned table replacement keeps source layout");Array.Copy(packed,0,resource,16,count);File.WriteAllBytes(file,resource);
  }
  using(var hash=System.Security.Cryptography.MD5.Create())md5=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(file))).Replace("-","").ToLowerInvariant();
  string source=Path.Combine(folder,"wallpaper","wpcache","dynamic",md5+".cwp");File.Copy(file,source);File.SetLastWriteTimeUtc(source,DateTime.UtcNow.AddMinutes(-2));
  byte[] record=System.Text.Encoding.UTF8.GetBytes("0000"+Common.Json.Serialize(new{wpId=id,wpName=md5+".cwp",wpMd5=md5,wpType=2}));for(int i=0;i<record.Length;i++)record[i]^=0xe7;
  string path=Path.Combine(folder,"cache_info",id+"_fixture.dat");File.WriteAllBytes(path,record);return LocalImport.ReadRecord(path);
 }
 [STAThread]static int Main(string[] args){
  if(args[0]=="--local-import")return LocalImport.Worker(args[1]);
  if(args.Length>1&&args[1]=="cache-probe"){
   var rows=new List<object>();
   for(int pass=0;pass<2;pass++){
    var clock=System.Diagnostics.Stopwatch.StartNew();int hits=0,total=0;long validation=CandidateChecks.ImageValidationBytes;
    var reasons=new Dictionary<string,int>();
    foreach(string record in Directory.GetFiles(Path.Combine(LocalImport.DataRoot,"cache_info"),"*_*.dat")){
     var candidate=LocalImport.ReadRecord(record);if(candidate==null)continue;total++;if(CandidateChecks.Load(candidate,()=>{}))hits++;
     string reason=candidate.CacheDisposition;reasons[reason]=reasons.ContainsKey(reason)?reasons[reason]+1:1;
    }
    rows.Add(new{pass=pass,total=total,hits=hits,elapsedMilliseconds=clock.Elapsed.TotalMilliseconds,imageValidationBytes=CandidateChecks.ImageValidationBytes-validation,reasons=reasons});
   }
   Common.SaveNew(args[0],new{passes=rows,sourceHashedBytes=CandidateChecks.SourceHashedBytes,previewMembersRead=CandidateChecks.PreviewMembersRead,scope="read-only known official metadata/resources; copied small check metadata/thumbnails in isolated own root"});return 0;
  }
  if(args.Length>1&&args[1]=="reopen"){
   string folder=Path.Combine(Common.Root,"tests","name-fixture-large");File.WriteAllText(Path.Combine(Common.Root,"tests","name-cache-root.txt"),folder);
   var names=OfficialNames.ReadNames(new[]{"999991","999992"},()=>{});
   Need(names.Count==2&&OfficialNames.LastDiagnostics.ScanCacheHit&&OfficialNames.BodiesRead==0,"new process reuses completed bounded name scan");
   Common.SaveNew(args[0],new{passed=true,checks=new[]{"new-process-name-scan-readback-zero-body-reads"}});return 0;
  }
  if(args.Length>1&&args[1]=="real-names"){
   string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Lenovo/LeAppStore/storecache/Cache/Cache_Data");File.WriteAllText(Path.Combine(Common.Root,"tests","name-cache-root.txt"),root);
   var ids=Common.Json.Deserialize<string[]>(File.ReadAllText(args[2]));var reports=new List<NameReadDiagnostics>();Dictionary<string,NameEvidence> found=new Dictionary<string,NameEvidence>();
   for(int batch=0;batch<10;batch++){found=OfficialNames.ReadNames(ids,()=>{});reports.Add(Common.Json.Deserialize<NameReadDiagnostics>(Common.Json.Serialize(OfficialNames.LastDiagnostics)));if(!OfficialNames.LastDiagnostics.MoreWork)break;}
   Common.SaveNew(args[0],new{names=found,requested=ids,missing=ids.Where(id=>!found.ContainsKey(id)).ToArray(),diagnostics=reports,reason=OfficialNames.LastReason,scope="bounded readonly local allowlisted official response cache; own isolated cursor only",engineLoaded=false});return 0;
  }
  Application.EnableVisualStyles();
  Check("formal-search-textchanged-filters-and-clears",()=>{
   using(var owner=new ControlWindow(null,0,null))using(var dialog=Dialog(owner)){
    var list=(ListView)Field(dialog,"list");var search=(TextBox)Field(dialog,"candidateSearch");
    Call(dialog,"TestAddCandidate",new ImportCandidate{Id="395417",Name="林间的风",Source="owned-a"});
    Call(dialog,"TestAddCandidate",new ImportCandidate{Id="395741",Name="雨中少女",Source="owned-b"});
    search.Text="雨中";Need(list.Items.Count==1&&((ImportCandidate)list.Items[0].Tag).Id=="395741","typing name must filter visible candidates at actual TextChanged entry");
    search.Text="不存在";Need(list.Items.Count==0,"zero matches must clear visible list");search.Text="";Need(list.Items.Count==2,"clear restores all candidates");
   }
  });
  Check("stable-passed-no-cover-reuses-identity-and-structure",()=>{
   string folder=Path.Combine(Common.Root,"fixture");Directory.CreateDirectory(folder);
   string path=(string)typeof(MultiWallpaperRegression).GetMethod("Fixture",Flags).Invoke(null,new object[]{folder,"no-cover",(byte)7});
   string md5;using(var hash=System.Security.Cryptography.MD5.Create())md5=BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();
   var c=new ImportCandidate{Id="395417",Source=path,Record=Path.Combine(folder,"owned.dat"),RecordHash=new string('a',64),Md5=md5};
   string cache=(string)typeof(CandidateChecks).GetMethod("Cache",Flags).Invoke(null,new object[]{c});Directory.CreateDirectory(Path.GetDirectoryName(cache));
   Common.SaveNew(cache,new CandidateCheckResult{Version=1,State="passed",Signature=CoverCache.Signature(path),RecordHash=c.RecordHash,Rules=CandidateChecks.Rules(),Sha256=Common.Hash(path),Md5=md5,CheckedAt=DateTimeOffset.UtcNow.AddMinutes(-10).ToString("o"),CoverReason="没有静态封面"});
   Need(CandidateChecks.Load(c,()=>{})&&c.Check.HashedBytes==0,"stable negative cover must not expire structural proof every five seconds");
  });
  Check("query-survives-late-candidates-official-names-and-library-aliases",()=>{
   using(var owner=new ControlWindow(null,0,null))using(var dialog=Dialog(owner)){
    var list=(ListView)Field(dialog,"list");IntPtr handle=list.Handle;var search=(TextBox)Field(dialog,"candidateSearch");
    search.Text="夏日";Call(dialog,"TestAddCandidate",new ImportCandidate{Id="111111",Name="夏日的风",Source="late-a"});
    Call(dialog,"TestAddCandidate",new ImportCandidate{Id="222222",Name="本地壁纸 222222",Source="late-b"});
    Need(list.Items.Count==1,"query applies to late additions");
    var library=(WallpaperLibrary)Field(owner,"library");library.Doc.Items.Add(new WallpaperItem{Id="alias",ResourceId="222222",Name="我的夏日",CustomName="我的夏日",SourcePath=Path.Combine(Common.Root,"owned-alias.cwp")});
    search.Text="我的夏日";Need(list.Items.Count==1&&((ImportCandidate)list.Items[0].Tag).Id=="222222"&&list.Items[0].Text.Contains("库内名"),"same-ID custom alias matches and explains display");
    search.Text="官方晴空";Need(list.Items.Count==0,"no match clears selection");
    Call(dialog,"RefreshOfficialNames",new Dictionary<string,NameEvidence>{{"222222",new NameEvidence{Id="222222",Name="官方晴空",Kind="official-local-wallpaper-response",SourceSha256=new string('a',64),IndexSha256=new string('b',64),SourceBytes=100,ResponsePath="/pcwallpapercontent/wallpaper/jingling/detailDynamic"}}});
    Need(list.Items.Count==1&&((ImportCandidate)Call(dialog,"get_Selected")).Id=="222222","async name refresh reapplies query and selected identity");
    Need(((TextBox)Field(dialog,"note")).Text.Contains("ID 222222"),"right details follow visible selected identity");
    search.Text="111";Need(list.Items.Count==1,"partial ID query");search.Text="";Need(list.Items.Count==2,"clear preserves all queued models");
    Need(list.Columns.Count==5&&!list.Columns.Cast<ColumnHeader>().Any(column=>column.Text=="播放记录"),"removed column has no width reservation");
   }
  });
  Check("centered-home-group-and-short-card-labels",()=>{
   using(var owner=new ControlWindow(null,0,null)){
    owner.MinimumSize=Size.Empty;
    foreach(Size size in new[]{new Size(1180,850),new Size(1180,680),new Size(1040,620)}){
     owner.ClientSize=size;
     foreach(Control control in owner.Controls)control.PerformLayout();Call(owner,"LayoutSide");owner.PerformLayout();
     var side=(Panel)Field(owner,"side");
     Need(Math.Abs(side.Top-(side.Parent.ClientSize.Height-side.Height)/2)<=2,"whole measured surface centered in body at "+size);
     Need(side.Top>=0&&side.Bottom<=side.Parent.ClientSize.Height&&!side.AutoScroll,"centered group fits body without scrolling");
    }
   }
   var item=new WallpaperItem{Id="owned",ResourceId="395417",Name="本地壁纸 395417"};
   foreach(string state in new[]{"已导入 · 播放未验证","播放待确认","播放未验证"})Need(ResourcePresentation.Card(item,state)=="未验证 · ID 395417","equivalent unconfirmed texts compact");
   Need(ResourcePresentation.Card(item,"窗口已确认 · 桌面已验证")=="已确认 · ID 395417","valid confirmation display shortened");
   Need(ResourcePresentation.Card(item,"文件不存在").Contains("文件不存在")&&item.Name=="本地壁纸 395417","error and user title preserved");
  });
  Check("bounded-large-name-index-progress-completion-and-negative-cache",()=>{
   NameFixture("large",4327,new[]{"999991","999992"},new[]{"晴空花园","林间光影"});int calls=0,totalEntries=0;Dictionary<string,NameEvidence> names;
   do{names=OfficialNames.ReadNames(new[]{"999991","999992"},()=>{});calls++;totalEntries+=OfficialNames.LastDiagnostics.EntriesRead;Need(OfficialNames.LastDiagnostics.EntriesRead<=1024,"per-call entry budget");Need(calls<=6,"bounded full traversal progresses");}while(OfficialNames.LastDiagnostics.MoreWork);
   Need(calls==5&&totalEntries==4327&&names.Count==2&&names["999991"].Name=="晴空花园","4327 records complete over five actual batches");
   var again=OfficialNames.ReadNames(new[]{"999991","999992"},()=>{});Need(again.Count==2&&OfficialNames.LastDiagnostics.ScanCacheHit&&OfficialNames.BodiesRead==0,"repeat name lookup avoids entry/body rereads");
   int missingCalls=0;do{OfficialNames.ReadNames(new[]{"888888"},()=>{});missingCalls++;}while(OfficialNames.LastDiagnostics.MoreWork&&missingCalls<7);
   Need(OfficialNames.LastDiagnostics.Outcome=="checked-no-match","absent target distinguished from incomplete budget");
  });
  Check("name-cancel-conflict-corrupt-chain-and-changing-cache",()=>{
   bool cancelled=false;try{OfficialNames.ReadNames(new[]{"999991"},()=>{throw new OperationCanceledException();});}catch(OperationCanceledException){cancelled=true;}Need(cancelled&&OfficialNames.LastDiagnostics.Outcome=="cancelled","active cancellation propagates");
   NameFixture("conflict",2,new[]{"777777","777777"},new[]{"名称一","名称二"});Need(OfficialNames.ReadNames(new[]{"777777"},()=>{}).Count==0&&OfficialNames.LastReason.Contains("冲突"),"conflicting same-ID names rejected");
   string bad=NameFixture("cycle",1,new string[0],new string[0]);byte[] bytes=File.ReadAllBytes(Path.Combine(bad,"data_1"));Array.Copy(BitConverter.GetBytes(0xa0010000u),0,bytes,8196,4);File.WriteAllBytes(Path.Combine(bad,"data_1"),bytes);
   Need(OfficialNames.ReadNames(new[]{"777777"},()=>{}).Count==0&&OfficialNames.LastDiagnostics.FailureKind=="corrupt-cache","chain loop rejected");
   string changed=NameFixture("changed",1,new[]{"777778"},new[]{"变化样本"});bool injected=false;
   var result=OfficialNames.ReadNames(new[]{"777778"},()=>{if(!injected&&OfficialNames.LastDiagnostics.Stage=="response-consistency-read"){injected=true;File.AppendAllText(Path.Combine(changed,"f_000001")," ");}});
   Need(injected&&result.Count==0&&OfficialNames.LastDiagnostics.ChangedDuringRead,"response change prevents mixed proof");
   string late=NameFixture("late-body",1,new[]{"777779"},new[]{"迟到名称"});string body=Path.Combine(late,"f_000001");File.Move(body,body+".held");
   Need(OfficialNames.ReadNames(new[]{"777779"},()=>{}).Count==0,"missing response preserves names");File.Move(body+".held",body);
   Need(OfficialNames.ReadNames(new[]{"777779"},()=>{}).ContainsKey("777779"),"newly available response invalidates completed negative cursor");
  });
  Check("real-check-reopen-rule-source-cover-refresh-and-force-metrics",()=>{
   var candidate=Resource("cache-metrics","733331",14);CoverCache.WorkFolder=Path.Combine(Common.WorkRoot,"owned-cover-metrics");Directory.CreateDirectory(CoverCache.WorkFolder);
   CandidateChecks.Run(candidate,()=>{},null,true);Need(CandidateChecks.Passed(candidate)&&candidate.Check.HashedBytes==candidate.Bytes,"first actual processor hashes exact resource bytes");
   long firstBytes=candidate.Check.HashedBytes;Need(CandidateChecks.Load(candidate,()=>{})&&candidate.Check.HashedBytes==0,"reopen uses identity/structure result without resource hash");
   string rules=PackageAnalysis.RulesPath;byte[] original=File.ReadAllBytes(rules);File.AppendAllText(rules," ");Need(!CandidateChecks.Load(candidate,()=>{})&&candidate.CacheDisposition=="rules-changed","rule fingerprint invalidates cache");File.WriteAllBytes(rules,original);
   CandidateChecks.Run(candidate,()=>{},null,true);Need(candidate.Check.HashedBytes==firstBytes,"explicit recheck performs exact target hash");
   string cache=(string)typeof(CandidateChecks).GetMethod("Cache",Flags).Invoke(null,new object[]{candidate});var proof=Common.Json.Deserialize<CandidateCheckResult>(Common.ReadShared(cache));proof.CoverRelative=null;proof.CoverSha256=null;proof.CoverReason="封面临时读取失败";proof.CoverAttempts=0;proof.CoverCheckedAt=DateTimeOffset.UtcNow.AddMinutes(-2).ToString("o");Common.ReplaceJson(cache,proof);
   Need(CandidateChecks.Load(candidate,()=>{})&&candidate.Check.CoverRefreshDue,"cover retry retains structural proof");CandidateChecks.Run(candidate,()=>{},null);Need(CandidateChecks.Passed(candidate)&&candidate.Check.HashedBytes==0&&candidate.Check.CacheReason=="identity-structure-hit-cover-refreshed","cover-only retry never rehashes source");
   File.SetLastWriteTimeUtc(candidate.Source,DateTime.UtcNow);Need(!CandidateChecks.Load(candidate,()=>{})&&candidate.CacheDisposition=="source-identity-changed","source metadata change invalidates resource proof");
   File.WriteAllText(cache,"invalid cached JSON");Need(!CandidateChecks.Load(candidate,()=>{})&&candidate.CacheDisposition=="cache-corrupt","damaged proof safely falls back");
  });
  Check("filtered-choice-copy-register-and-hidden-queue-cancel",()=>{
   var candidate=Resource("filtered-import","733332",17);CoverCache.WorkFolder=Path.Combine(Common.WorkRoot,"owned-filter-cover");Directory.CreateDirectory(CoverCache.WorkFolder);CandidateChecks.Run(candidate,()=>{},null,true);
   using(var owner=new ControlWindow(null,0,null))using(var dialog=Dialog(owner)){
    var list=(ListView)Field(dialog,"list");IntPtr handle=list.Handle;
    Call(dialog,"TestAddCandidate",candidate);Call(dialog,"TestAddCandidate",new ImportCandidate{Id="733333",Name="隐藏的排队项",Source="hidden-waiting"});
    ((TextBox)Field(dialog,"candidateSearch")).Text="733332";Need(((ImportCandidate)Call(dialog,"get_Selected")).Id==candidate.Id,"filtered selection points to intended model");
    dialog.GetType().GetField("queueRunning",Flags).SetValue(dialog,true);dialog.GetType().GetField("queueTask",Flags).SetValue(dialog,System.Threading.Tasks.Task.FromResult(0));
    Call(dialog,"ControlsState");Need(((Button)Field(dialog,"import")).Enabled,"passed target remains actionable while other queue entries exist");
    ((System.Threading.Tasks.Task)Call(dialog,"StopQueue")).GetAwaiter().GetResult();Need(CandidateChecks.Passed(candidate),"queue stop preserves passed target");
    var all=(List<ListViewItem>)Field(dialog,"candidateRows");Need(((ImportCandidate)all[1].Tag).Check.State=="cancelled","hidden pending model cancelled safely");
    var operation=new ImportOperation((phase,data)=>{});using(var parent=System.Diagnostics.Process.GetCurrentProcess())Common.SaveNew(Path.Combine(operation.Folder,"request.json"),new{mode="copy",candidate=candidate,parentPid=parent.Id,parentCreation=ProcessProof.Creation(parent.Handle).ToString()});File.WriteAllText(Path.Combine(operation.Folder,"go"),"");
    Need(LocalImport.Worker(operation.Folder)==0,"actual filtered candidate copy handler exits normally");
    var output=Common.Read(Path.Combine(operation.Folder,"result.json"));Call(owner,"RegisterImport",operation,output,false);
    var library=(WallpaperLibrary)Field(owner,"library");Need(library.Doc.Items.Any(item=>item.ResourceId==candidate.Id&&item.RegisteredSha256==candidate.Check.Sha256),"formal registration corresponds to filtered stable identity");
   }
  });
  Check("stable-no-preview-actual-check-repeat-hashes-zero-bytes",()=>{
   var candidate=Resource("no-preview-real","733334",23,true);CoverCache.WorkFolder=Path.Combine(Common.WorkRoot,"owned-no-preview");Directory.CreateDirectory(CoverCache.WorkFolder);
   CandidateChecks.Run(candidate,()=>{},null,true);Need(CandidateChecks.Passed(candidate)&&candidate.Cover==null,"actual valid container has no static preview member");
   string cache=(string)typeof(CandidateChecks).GetMethod("Cache",Flags).Invoke(null,new object[]{candidate});var proof=Common.Json.Deserialize<CandidateCheckResult>(Common.ReadShared(cache));proof.CheckedAt=DateTimeOffset.UtcNow.AddMinutes(-10).ToString("o");Common.ReplaceJson(cache,proof);
   var repeat=LocalImport.ReadRecord(candidate.Record);CandidateChecks.Run(repeat,()=>{},null);Need(CandidateChecks.Passed(repeat)&&repeat.Check.HashedBytes==0&&repeat.Check.CoverReads==0,"actual no-preview repeat uses stable negative cover and structural proof");
  });
  bool passed=Results.All(r=>(bool)r.GetType().GetProperty("passed").GetValue(r,null));Common.SaveNew(args[0],new{passed=passed,results=Results,sourceHashedBytes=CandidateChecks.SourceHashedBytes,coverMembersRead=CandidateChecks.PreviewMembersRead,scope="formal unshown filter events, actual cache/name/copy/registration handlers with owned small fixtures; no engine or input automation"});return passed?0:1;
 }
}
#endif
