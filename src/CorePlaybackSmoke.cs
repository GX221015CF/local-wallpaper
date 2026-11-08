#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Forms;

// A bounded headless entry using the production preparation, resource, component,
// worker rejection and persisted-library code. It never creates a ControlWindow.
internal static class CorePlaybackSmoke {
 static readonly List<string> checks = new List<string>();
 static void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks.Add(message); }
 static void Setup(string suite) {
  if (!LocalImport.Under(suite, Path.Combine(Common.AppRoot, "tests"))) throw new InvalidDataException("测试报告必须位于显式隔离根");
  Common.Root = Path.Combine(Common.AppRoot, "tests", "data");
  Directory.CreateDirectory(Path.Combine(Common.Root, "data"));
 }
 static void TestContract(string suite) {
  RuntimeProvisioning.TestContractRows = Common.Json.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(suite, "fixture-contract.json")));
 }
 static string Verify(string source, string mode, string confirmation, CancellationToken token) {
  return PlaybackResourceCheck.Verify(source, mode, confirmation, false, token, _ => {});
 }
 static void Cancelled(Task task) {
  try { task.GetAwaiter().GetResult(); throw new InvalidOperationException("取消没有生效"); }
  catch (OperationCanceledException) { }
 }
 internal static int Run(string suite) {
  suite=Path.GetFullPath(suite);
  try {
   Setup(suite);Directory.CreateDirectory(suite);
   string source=Path.Combine(Common.Root,"fixture.cwp");
   byte[] payload=new byte[2097152];Array.Copy(BitConverter.GetBytes(0x02505743u),payload,4);Array.Copy(BitConverter.GetBytes((uint)payload.Length),0,payload,4,4);
   File.WriteAllBytes(source,payload);string hash=Common.Hash(source);
   var item=new WallpaperItem{Id="core-fixture",Name="隔离核心路径夹具",SourcePath=source,RegisteredSha256=hash,OriginKind="official-local-import"};
   Common.SaveNew(Path.Combine(Common.Root,"data","catalog.json"),new LibraryDocument{Version=1,SelectedId=item.Id,Items=new List<WallpaperItem>{item}});
   Common.SaveNew(Path.Combine(Common.Root,"data","settings.json"),new AppSettings{Version=1,Muted=true,Volume=30});
   string catalogBefore=Common.Hash(Path.Combine(Common.Root,"data","catalog.json"));
   string install=Path.GetFullPath(Path.Combine(Common.AppRoot,"..","component-fixtures"));Directory.CreateDirectory(install);
   var rows=new Dictionary<string,string>();
   var contract=Common.Read(Path.Combine(Common.AppRoot,"rules","runtime-components.json"));
   foreach(object obj in (System.Collections.IEnumerable)contract["files"]){var row=Common.Obj(obj);string name=Common.S(row,"name");if(name=="Desktop317495Host-refined.exe")continue;string file=Path.Combine(install,name);File.WriteAllText(file,"Self-owned inert fixture for "+name);rows.Add(name,Common.Hash(file));}
   Need(rows.Count==23,"隔离组件夹具恰好23项，均为自有惰性文本，未加载");
   Common.SaveNew(Path.Combine(suite,"fixture-contract.json"),rows);TestContract(suite);
   RuntimeProvisioning.SelectAndSave(install);string configBefore=Common.Hash(Path.Combine(Common.Root,"data","runtime-location.json"));
   using(var preparation=new PlaybackPreparation()) {
    Need(preparation.TryBegin(2000),"启动准备取得唯一操作权");
    Need(!preparation.TryBegin(2000),"重复提交被操作门拒绝");
    var verified=preparation.VerifyAsync(source,"preview",null,false).GetAwaiter().GetResult();
    Need(verified[0]==hash&&DataLocation.Same(verified[1],install),"实际异步准备处理器返回资源完整哈希及核验组件根");preparation.Complete();
    Need(preparation.TryBegin(2000),"完成后允许重新开始");
    using(var entered=new ManualResetEventSlim(false)) {
     PlaybackResourceCheck.TestAfterHashChunk=token=>{entered.Set();token.WaitHandle.WaitOne(1000);token.ThrowIfCancellationRequested();};
     var pending=preparation.VerifyAsync(source,"preview",null,false);
     Need(entered.Wait(1000),"后台资源哈希已开始，测试无需窗口");preparation.Cancel();Cancelled(pending);
     Need(preparation.UserCancelled&&preparation.Stage=="resource-hash","主动取消保留准确阶段");
     PlaybackResourceCheck.TestAfterHashChunk=null;preparation.Complete();
    }
    Need(preparation.TryBegin(40),"重试准备允许设置明确测试预算");
    PlaybackResourceCheck.TestAfterHashChunk=token=>{token.WaitHandle.WaitOne(1000);token.ThrowIfCancellationRequested();};
    Cancelled(preparation.VerifyAsync(source,"preview",null,false));Need(!preparation.UserCancelled&&preparation.Token.IsCancellationRequested,"预算超时与主动取消可以区分");
    PlaybackResourceCheck.TestAfterHashChunk=null;preparation.Complete();
    Need(preparation.TryBegin(2000),"超时后允许重试");Need(preparation.VerifyAsync(source,"preview",null,false).GetAwaiter().GetResult()[0]==hash,"取消及超时后实际处理器重试成功");preparation.Complete();
   }
   bool desktopRejected=false;try{Verify(source,"desktop",null,CancellationToken.None);}catch(InvalidDataException){desktopRejected=true;}
   Need(desktopRejected,"未确认资源拒绝桌面操作");Need(Verify(source,"desktop",hash,CancellationToken.None)==hash,"明确同哈希确认可通过桌面资源门，未创建播放器");
   string changed=Path.Combine(Common.Root,"changed.cwp");File.WriteAllBytes(changed,payload);using(var file=File.OpenWrite(changed)){file.Position=20;file.WriteByte(15);}
   bool changedRejected=false;try{Verify(changed,"desktop",hash,CancellationToken.None);}catch(InvalidDataException){changedRejected=true;}Need(changedRejected,"文件内容变化使旧画面确认失效");
   string bad=Path.Combine(Common.Root,"unsupported.cwp");File.WriteAllBytes(bad,new byte[32]);string phase=null;bool invalid=false;
   try{PlaybackResourceCheck.Verify(bad,"preview",null,false,CancellationToken.None,value=>phase=value);}catch(Exception ex){invalid=true;Common.SaveNew(Path.Combine(suite,"rejected-diagnostic.json"),FaultSafety.Sanitize(new FaultRecord{SchemaVersion=1,Id="diag-20261106-000000-000000000018",Category=FaultSafety.Category("start"),Stage=phase,Reason=ex.ToString()}));}
   Need(invalid&&phase=="resource-policy","格式拒绝保留实际resource-policy阶段和异常正文");
   bool readFailed=false;using(var locked=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.None)){try{Verify(source,"preview",null,CancellationToken.None);}catch(IOException){readFailed=true;}}
   Need(readFailed,"读取失败有异常结果，未降为成功");
   string copy=Path.Combine(Common.Root,"copy.cwp");
   using(var cancel=new CancellationTokenSource()){PlaybackResourceCheck.TestAfterCopyChunk=token=>cancel.Cancel();bool observed=false;try{PlaybackResourceCheck.Copy(source,copy,cancel.Token);}catch(OperationCanceledException){observed=true;}finally{PlaybackResourceCheck.TestAfterCopyChunk=null;}Need(observed&&!File.Exists(copy),"运行副本复制取消后删除本次半成品");}
   File.WriteAllText(copy,"existing-protected-file");bool existingRejected=false;try{PlaybackResourceCheck.Copy(source,copy,CancellationToken.None);}catch(IOException){existingRejected=true;}Need(existingRejected&&File.ReadAllText(copy)=="existing-protected-file","复制失败保护已存在目标");File.Delete(copy);
   PlaybackResourceCheck.Copy(source,copy,CancellationToken.None);Need(Common.Hash(copy)==hash,"运行副本重试成功且完整哈希一致");File.Delete(copy);
   using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();bool stopped=false;try{RuntimeProvisioning.RequireForStart(cancelled.Token);}catch(OperationCanceledException){stopped=true;}Need(stopped&&RuntimeProvisioning.Available&&Common.Hash(Path.Combine(Common.Root,"data","runtime-location.json"))==configBefore,"启动前组件复核取消保护已核验状态和配置");}
   string changedComponent=Path.Combine(install,rows.Keys.First());string original=File.ReadAllText(changedComponent);File.WriteAllText(changedComponent,"changed");bool componentRejected=false;try{RuntimeProvisioning.RequireForStart();}catch(InvalidOperationException){componentRejected=true;}Need(componentRejected&&!RuntimeProvisioning.Available,"启动前组件变化被实际身份核验拒绝");File.WriteAllText(changedComponent,original);RuntimeProvisioning.SelectAndSave(install);
   var ready=PlaybackActions.For(true,false,true,true,false,false,false,false,false,false,false,"");Need(ready.Preview&&!ready.Desktop&&ready.Reason.Contains("完成确认"),"动作模型明确先预览确认再应用");
   var preparing=PlaybackActions.For(true,false,true,true,true,true,false,true,false,false,false,"");Need(!preparing.Preview&&!preparing.Desktop&&preparing.Stop,"准备时仅可取消，不能重复启动");
   var missing=PlaybackActions.For(false,false,true,true,true,false,false,false,false,false,false,"");Need(!missing.Preview&&!missing.Desktop&&missing.Reason.Contains("播放组件"),"缺组件时两项播放动作禁用且原因明确");
   var recovering=PlaybackActions.For(true,false,true,true,true,false,false,false,false,true,true,"");Need(!recovering.Preview&&!recovering.Desktop&&recovering.Stop,"清理未确认时只允许停止恢复");
   var closing=PlaybackActions.For(true,false,true,true,true,false,false,false,true,true,false,"");Need(!closing.Preview&&!closing.Desktop&&!closing.Stop,"关闭状态阻止新操作");
   // Exercise the actual worker handler only on paths that finish before native launch.
   foreach(bool cancel in new[]{true,false}) {
    string run=Path.Combine(Common.Root,"logs",cancel?"worker-cancel":"worker-reject");Directory.CreateDirectory(run);
    string input=cancel?source:bad;Common.SaveNew(Path.Combine(run,"request.json"),new{mode="preview",cwp=input,seconds=0,playbackPolicy="continuous",expectedSha256=Common.Hash(input)});
    if(cancel)Common.SaveNew(Path.Combine(run,"stop.request.json"),new{reason="isolated-before-player"});
    int code=LeaseWorker.Run(run);var result=Common.Read(Path.Combine(run,"result.json"));
    Need(code==(cancel?0:1)&&!Convert.ToBoolean(result["playerCreated"])&&!Convert.ToBoolean(result["forcedOwnProcess"]),cancel?"实际worker在准备前取消并正常返回，无播放器":"实际worker拒绝无效资源并记录失败，无播放器");
   }
   Need(Common.Hash(Path.Combine(Common.Root,"data","catalog.json"))==catalogBefore,"所有准备成功或失败都保持原登记及空播放确认");
   Common.SaveNew(Path.Combine(suite,"parent.json"),new{source=source,hash=hash,install=install,catalogSha256=catalogBefore});
   Common.SaveNew(Path.Combine(suite,"result.json"),new{passed=true,checks=checks,classification="实际共享处理器、动作模型及worker拒绝路径，TEST_BUILD；未创建GUI或播放器",playerStarted=false,desktopChanged=false,releaseGuiTested=false});
   return 0;
  }catch(Exception ex){Directory.CreateDirectory(suite);Common.SaveNew(Path.Combine(suite,"failure.json"),new{passed=false,checks=checks,error=ex.ToString()});return 1;}
  finally{PlaybackResourceCheck.TestAfterHashChunk=null;PlaybackResourceCheck.TestAfterCopyChunk=null;}
 }
 internal static int Reopen(string suite) {
  try {
   suite=Path.GetFullPath(suite);Setup(suite);TestContract(suite);
   var parent=Common.Read(Path.Combine(suite,"parent.json"));
   bool initiallyAvailable=RuntimeProvisioning.Available;
   string install=RuntimeProvisioning.RequireForStart(); // Deliberately no Initialize call.
   Need(!initiallyAvailable&&DataLocation.Same(install,Common.S(parent,"install")),"全新进程无需继承主窗口内存状态即可核验保存组件配置");
   var library=new WallpaperLibrary();var item=library.Selected;
   Need(library.Doc.Items.Count==1&&item.RegisteredSha256==Common.S(parent,"hash")&&item.PreviewConfirmedSha256==null&&item.DesktopConfirmedSha256==null,"全新进程读取相同登记和资源，播放确认仍为空");
   Need(Common.Hash(library.FilePath)==Common.S(parent,"catalogSha256"),"全新进程未改变登记字节");
   Common.SaveNew(Path.Combine(suite,"reopen.json"),new{passed=true,pid=Process.GetCurrentProcess().Id,exe=Application.ExecutablePath,initiallyAvailable=initiallyAvailable,root=Common.Root,install=install,checks=checks,method="fresh TEST_BUILD process, actual saved config verification and WallpaperLibrary",playerStarted=false,desktopChanged=false});return 0;
  }catch(Exception ex){Common.SaveNew(Path.Combine(suite,"reopen-failure.json"),new{passed=false,error=ex.ToString()});return 1;}
 }
}
#endif
