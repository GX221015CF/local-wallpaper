using System;using System.IO;using System.Linq;using System.Text;using System.Threading;using System.Windows.Forms;using System.Collections.Generic;
internal static class LocalTool {
 static bool cancelled;static Dictionary<string,string> options=new Dictionary<string,string>();static System.Diagnostics.Stopwatch taskClock=new System.Diagnostics.Stopwatch();
 static string Value(string key){string v;return options.TryGetValue(key,out v)?v:null;}
 static void Check(){if(cancelled||Value("--cancel-file")!=null&&File.Exists(Value("--cancel-file")))throw new OperationCanceledException("用户已取消，未登记，临时文件将清理");if(taskClock.Elapsed.TotalSeconds>180)throw new TimeoutException("本地处理超过180秒预算，未继续执行");}
 internal static int Run(string[] args){
  Console.OutputEncoding=new UTF8Encoding(false);Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;cancelled=true;};string reportFolder=null;bool choose=args.Length==1&&args[0]=="--choose";
  try{
   string op,path;
   if(choose){using(var dialog=new OpenFileDialog{Title="选择已合法取得的本地壁纸，只分析不自动导入",Filter="本地CWP|*.cwp|所有文件|*.*"}){if(dialog.ShowDialog()!=DialogResult.OK)return 5;path=dialog.FileName;}op="analyze";}
   else {if(args.Length<2)throw new ArgumentException("用法：WallpaperLocalTool.exe analyze|inspect|classify|cover|validate|import|status <本地文件> [--data-root 隔离目录] [--output 报告目录]。普通用户请运行 一次分析本地壁纸.cmd");op=args[0].ToLowerInvariant();path=Path.GetFullPath(args[1]);
    var flags=new[]{"--force","--allow-unverified","--allow-app-library"};var keys=new[]{"--data-root","--output","--rules","--cancel-file","--id","--name","--cover-output"};
    for(int i=2;i<args.Length;i++){string key=args[i];if(options.ContainsKey(key))throw new ArgumentException("参数重复："+key);if(flags.Contains(key))options.Add(key,"true");else if(keys.Contains(key)&&i+1<args.Length)options.Add(key,args[++i]);else throw new ArgumentException("未知或缺值参数："+key);}
   }
   if(!new[]{"analyze","inspect","classify","cover","validate","import","status"}.Contains(op))throw new ArgumentException("未知命令");
   if(Value("--rules")!=null)PackageAnalysis.RulesPath=Path.GetFullPath(Value("--rules"));
   if(op=="import"&&Value("--data-root")==null)throw new ArgumentException("import 必须明确指定 --data-root；测试请使用隔离目录");
   DataLocation.ForTool(Value("--data-root"));
   if(op=="import"&&Value("--allow-app-library")==null)throw new ArgumentException("写入正式登记需明确 --allow-app-library；先正常退出本地壁纸窗口");
   using(var toolGate=new UpgradeGate()){DataLocation.Initialize();using(var dataLease=new DataLease(Common.Root)){
   reportFolder=Value("--output")??Path.Combine(Common.Root,"reports",DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
   Console.Error.WriteLine("正在离线分析；默认不导入、不播放。Ctrl+C 可取消。");
   taskClock.Restart();var r=PackageAnalysis.Analyze(path,Check,Value("--force")!=null);PackageAnalysis.AttachCatalog(r);PackageAnalysis.Reports(r,reportFolder);
   if(r.CoverRelative!=null){string target=Value("--cover-output")??Path.Combine(reportFolder,"真实封面"+Path.GetExtension(r.CoverRelative));if(!File.Exists(target))File.Copy(PackageAnalysis.CoverPath(r),target,false);}
   if(choose){var answer=MessageBox.Show(PackageAnalysis.Text(r)+"\n报告："+reportFolder+"\n\n是否明确导入到本软件首页？选择“否”只保留分析报告。", "本地分析完成",MessageBoxButtons.YesNo,MessageBoxIcon.Information,MessageBoxDefaultButton.Button2);if(answer==DialogResult.Yes){if(!r.ContainerValidated){MessageBox.Show("结构尚未验证，此简易入口保留报告，不导入未知结构。","未导入");}else{op="import";options["--allow-app-library"]="true";r=PackageAnalysis.Analyze(path,Check);}}}
   if(op=="import"){taskClock.Restart();
     var candidate=FindOfficial(path);string id=Value("--id")??(candidate==null?null:candidate.Id),name=Value("--name")??(candidate==null?null:candidate.Name);
     bool duplicate;var item=ManagedCatalog.ImportLocal(path,r,id,name,Value("--allow-unverified")!=null,Check,Value("--name")==null&&candidate!=null?candidate.NameSource:null,out duplicate);PackageAnalysis.AttachCatalog(r);Console.Error.WriteLine(duplicate?"相同内容已存在，未重复建卡。":"已保存管理副本并登记；没有自动播放。");
     PackageAnalysis.Reports(r,reportFolder);if(choose)MessageBox.Show((duplicate?"相同内容已在列表中，未重复建卡。":"已加入首页，没有自动播放。")+"\n请打开本地壁纸查看。","本地入库完成");

   }
   Console.WriteLine(Common.Json.Serialize(new{ok=r.ContainerValidated&&(op!="cover"||r.CoverRelative!=null),operation=op,reportDirectory=Path.GetFullPath(reportFolder),result=r}));
   return r.RuleVersion==null?4:r.ContainerValidated&&(op!="cover"||r.CoverRelative!=null)?0:3;}}
  }catch(Exception ex){int code=ex is OperationCanceledException?5:ex is ArgumentException?2:ex is IOException?6:1;var error=new{ok=false,exitCode=code,error=ex.Message,reportDirectory=reportFolder};Console.WriteLine(Common.Json.Serialize(error));if(reportFolder!=null)try{Directory.CreateDirectory(reportFolder);File.WriteAllText(Path.Combine(reportFolder,"error.json"),Common.Json.Serialize(error),new UTF8Encoding(false));File.WriteAllText(Path.Combine(reportFolder,"错误说明.txt"),ex.Message,new UTF8Encoding(false));}catch{Console.Error.WriteLine("错误报告无法保存，请检查指定报告目录权限。");}if(choose)MessageBox.Show(ex.Message,"本地处理未完成");return code;}
 }
 static ImportCandidate FindOfficial(string path){
  if(!String.Equals(Path.GetDirectoryName(path),Path.Combine(LocalImport.OfficialRoot,"wallpaper","wpcache","dynamic"),StringComparison.OrdinalIgnoreCase))return null;
  string dir=Path.Combine(LocalImport.OfficialRoot,"cache_info");if(!Directory.Exists(dir))return null;int count=0;
  foreach(string record in Directory.EnumerateFiles(dir,"*.dat")){Check();if(++count>2048)break;try{var c=LocalImport.ReadRecord(record);if(c!=null&&String.Equals(c.Source,path,StringComparison.OrdinalIgnoreCase)){OfficialNames.Apply(new List<ImportCandidate>{c},Check);return c;}}catch(OperationCanceledException){throw;}catch{}}
  return null;
 }
 [STAThread]static int Main(string[] args){return Run(args);}
}
