using System;
using System.IO;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class FirstUseSetup {
#if TEST_BUILD
 internal static Action<string> TestFailurePoint;
 static void Point(string name){if(TestFailurePoint!=null)TestFailurePoint(name);}
#else
 static void Point(string name){}
#endif
 internal static int RunInteractive(){
  if(File.Exists(DataLocation.BindingPath))return 1;
  StartupHandoff.SetStage("显示首次使用位置");
  using(var form=new FirstUseLocationForm()){
   if(form.ShowDialog()!=DialogResult.OK)return 0;
   if(form.PreviouslyUsed){StartupHandoff.SetStage("等待Agent核对已有数据位置");StartupHandoff.Show(new FileNotFoundException("数据位置配置缺失；若以前使用过，请由Agent核对原有数据位置。未创建空库。",DataLocation.BindingPath),StartupHandoff.CurrentStage);return -1;}
   StartupHandoff.SetStage("提交首次使用数据初始化");InitializeNew(form.SelectedParent,Common.AppRoot,DataLocation.BindingPath);
  }
  return 1;
 }

 internal static bool InitializeNew(string parent){return InitializeNew(parent,Common.AppRoot,DataLocation.BindingPath);}
 internal static bool InitializeNew(string selectedParent,string programRoot,string bindingPath){
  if(String.IsNullOrWhiteSpace(selectedParent))return false;
  if(File.Exists(bindingPath))throw new InvalidOperationException("已有数据位置配置；首次初始化已拒绝，不会覆盖。");
  string parent=DataLocation.SafePath(selectedParent),program=DataLocation.SafePath(programRoot),binding=Path.GetFullPath(bindingPath);
  LocalImport.NoLinks(parent);LocalImport.NoLinks(Path.GetDirectoryName(binding));
  if(!Directory.Exists(parent))throw new DirectoryNotFoundException("所选文件夹不存在。");
  if(!LocalImport.Under(binding,program)||Path.GetFileName(binding)!="data-location.json")throw new InvalidDataException("数据位置绑定必须保存在当前程序版本目录中。");
  string final=Path.Combine(parent,"LocalWallpaper"),stage=Path.Combine(parent,".LocalWallpaper-setup-"+Guid.NewGuid().ToString("N"));
  string control=Path.Combine(stage,"Control"),data=Path.Combine(stage,"Data"),finalControl=Path.Combine(final,"Control"),finalData=Path.Combine(final,"Data");
  DataLocation.DataPath(finalData,program);
  if(Directory.Exists(final)||File.Exists(final))throw new IOException("目标 LocalWallpaper 文件夹已存在；为保护已有数据，未覆盖。请选择其他位置或提交给Agent核对。");
  string releasePath=VersionStore.ReleasePath(program);LocalImport.NoLinks(releasePath);string releaseHash=Common.Hash(releasePath);VersionStore.VerifyRelease(program,releaseHash,1);
  bool moved=false;
  try{
   Directory.CreateDirectory(control);Directory.CreateDirectory(Path.Combine(data,"data"));
   string storeId=Guid.NewGuid().ToString("N");
   WriteNew(Path.Combine(data,"store.json"),new StoreMarker{SchemaVersion=1,DataFormat=1,StoreId=storeId});
   WriteNew(Path.Combine(data,"data","catalog.json"),new LibraryDocument{Version=1,SelectedId=null,Items=new List<WallpaperItem>()});
   WriteNew(Path.Combine(data,"data","settings.json"),new AppSettings{Version=1,Muted=true,Volume=30});
   // Publish the data tree atomically, then write its control record and binding.
   Directory.Move(stage,final);moved=true;
   var deployment=new Deployment{SchemaVersion=1,DataFormat=1,StoreId=storeId,DataRoot=finalData,ActiveRoot=program,ReleaseSha256=releaseHash,CommittedAt=DateTimeOffset.Now.ToString("o")};
   WriteNew(Path.Combine(finalControl,"deployment.json"),deployment);
   DataLocation.ValidateStore(deployment,false);
   Point("before-binding-publish");
   WriteAtomic(binding,new LocationBinding{SchemaVersion=1,ControlRoot=finalControl,DataRoot=finalData,StoreId=storeId});
   var loaded=DataLocation.LoadBound(binding);DataLocation.ValidateStore(loaded,false);VersionStore.VerifyRelease(program,loaded.ReleaseSha256,loaded.DataFormat);
   DataLocation.Current=loaded;DataLocation.Ready=true;DataLocation.Failure=null;DataLocation.Phase="首次初始化完成";Common.Root=loaded.DataRoot;
   return true;
  }catch{
   if(Directory.Exists(stage)){LocalImport.NoLinks(stage);Directory.Delete(stage,true);}
   // If no binding was published, the newly created tree is still owned by this failed operation.
   if(moved&&!File.Exists(binding)&&Directory.Exists(final)){LocalImport.NoLinks(final);Directory.Delete(final,true);}
   throw;
  }
 }

 internal static void EnsureVersionBinding(string versionRoot,Deployment current){
  versionRoot=DataLocation.SafePath(versionRoot);DataLocation.DataPath(current.DataRoot,versionRoot);string path=Path.Combine(versionRoot,"build","data-location.json");
  LocalImport.NoLinks(Path.GetDirectoryName(path));
  if(File.Exists(path)){var found=DataLocation.ReadBinding(path);if(!DataLocation.Same(found.ControlRoot,DataLocation.ControlRoot)||!DataLocation.Same(found.DataRoot,current.DataRoot)||found.StoreId!=current.StoreId)throw new InvalidDataException("目标程序已有不同的数据绑定，拒绝覆盖或切换。");return;}
  WriteAtomic(path,new LocationBinding{SchemaVersion=1,ControlRoot=DataLocation.ControlRoot,DataRoot=current.DataRoot,StoreId=current.StoreId});
  var verified=DataLocation.ReadBinding(path);if(verified.StoreId!=current.StoreId||!DataLocation.Same(verified.DataRoot,current.DataRoot))throw new InvalidDataException("目标程序绑定写后校验失败；版本关联未提交。");
 }

 static void WriteNew(string path,object value){using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var w=new StreamWriter(f,new System.Text.UTF8Encoding(false))){w.Write(Common.Json.Serialize(value));w.Flush();f.Flush(true);}}
 static void WriteAtomic(string path,object value){string folder=Path.GetDirectoryName(path);Directory.CreateDirectory(folder);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{WriteNew(temp,value);if(File.Exists(path))throw new IOException("已有配置，拒绝覆盖："+Path.GetFileName(path));File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
}

internal sealed class FirstUseLocationForm:Form {
 readonly TextBox parentPath=new TextBox();readonly Label actualPath=new Label(),problem=new Label();internal string SelectedParent;internal bool PreviouslyUsed;
 internal FirstUseLocationForm(){
  Text="首次使用 · 选择数据位置";Name="FirstUseLocation";StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=true;ClientSize=new System.Drawing.Size(660,330);Font=new System.Drawing.Font("Microsoft YaHei UI",9.5f);BackColor=System.Drawing.Color.White;
  var title=new Label{Text="欢迎使用本地壁纸",Font=new System.Drawing.Font(Font.FontFamily,17,System.Drawing.FontStyle.Bold),AutoSize=true,Location=new System.Drawing.Point(24,20)};Controls.Add(title);
  Controls.Add(new Label{Text="推荐位置来自当前 Windows 用户的本地应用数据文件夹。可以直接开始，也可以浏览修改。",AutoSize=true,Location=new System.Drawing.Point(26,64)});
  parentPath.Location=new System.Drawing.Point(28,101);parentPath.Size=new System.Drawing.Size(500,28);parentPath.Text=Recommended();parentPath.TextChanged+=(s,e)=>UpdateActual();Controls.Add(parentPath);
  var browse=new Button{Text="浏览…",Location=new System.Drawing.Point(540,99),Size=new System.Drawing.Size(92,30)};browse.Click+=(s,e)=>Browse();Controls.Add(browse);
  Controls.Add(new Label{Text="实际数据位置",Font=new System.Drawing.Font(Font.FontFamily,9.5f,System.Drawing.FontStyle.Bold),AutoSize=true,Location=new System.Drawing.Point(28,143)});
  actualPath.Location=new System.Drawing.Point(28,168);actualPath.Size=new System.Drawing.Size(604,38);actualPath.AutoEllipsis=true;Controls.Add(actualPath);
  Controls.Add(new Label{Text="这里保存壁纸列表、设置和封面。以后导入的壁纸文件副本也会放在此位置的 library 中，可能占用较多空间。联想缓存不会搬动。",Location=new System.Drawing.Point(28,211),Size=new System.Drawing.Size(604,40)});
  problem.Location=new System.Drawing.Point(28,254);problem.Size=new System.Drawing.Size(400,22);problem.ForeColor=System.Drawing.Color.Firebrick;Controls.Add(problem);
  var start=new Button{Text="开始使用",Name="StartFirstUse",Location=new System.Drawing.Point(357,286),Size=new System.Drawing.Size(112,32),DialogResult=DialogResult.None};start.Click+=(s,e)=>Start();Controls.Add(start);
  var old=new Button{Text="以前使用过…",Name="PreviouslyUsed",Location=new System.Drawing.Point(475,286),Size=new System.Drawing.Size(113,32)};old.Click+=(s,e)=>{PreviouslyUsed=true;DialogResult=DialogResult.OK;Close();};Controls.Add(old);
  var cancel=new Button{Text="取消",Name="CancelFirstUse",Location=new System.Drawing.Point(596,286),Size=new System.Drawing.Size(48,32)};cancel.Click+=(s,e)=>{DialogResult=DialogResult.Cancel;Close();};Controls.Add(cancel);AcceptButton=start;CancelButton=cancel;UpdateActual();
 }
 static string Recommended(){try{return SourceLocation.RecommendedDataParent;}catch{return "";}}
 string DataPath(){if(String.IsNullOrWhiteSpace(parentPath.Text)||!Path.IsPathRooted(parentPath.Text))return "请选择本机文件夹";try{return Path.Combine(Path.GetFullPath(parentPath.Text),"LocalWallpaper","Data");}catch{return "请选择有效的本机文件夹";}}
 void UpdateActual(){actualPath.Text=DataPath();}
 void Browse(){using(var d=new FolderBrowserDialog{Description="选择存放本软件数据的父文件夹；程序将在其中新建 LocalWallpaper\\Control 和 LocalWallpaper\\Data。已有其他文件不受影响。",ShowNewFolderButton=true,SelectedPath=Directory.Exists(parentPath.Text)?parentPath.Text:""})if(d.ShowDialog(this)==DialogResult.OK)parentPath.Text=d.SelectedPath;}
 void Start(){
  problem.Text="";
  try{string parent=DataLocation.SafePath(parentPath.Text);LocalImport.NoLinks(parent);if(!Directory.Exists(parent))throw new DirectoryNotFoundException("所选文件夹不存在。请先在“浏览”中选择已有位置。");string target=Path.Combine(parent,"LocalWallpaper");if(Directory.Exists(target)||File.Exists(target))throw new IOException("此位置已存在 LocalWallpaper 数据。为保护旧数据，本次不会新建空库；请选择“以前使用过…”交给Agent核对，或浏览其他位置。");SelectedParent=parent;DialogResult=DialogResult.OK;Close();}
  catch(Exception ex){problem.Text=ex.Message;}
 }
}
