using System;using System.IO;using System.Drawing;using System.Windows.Forms;using System.Threading.Tasks;using System.Diagnostics;using System.Collections.Generic;
internal sealed class FaultHandoffForm:Form {
 readonly FaultRecord fault;readonly TextBox symptom=new TextBox();readonly CheckBox addNote=new CheckBox();internal readonly Button copy=new SoftButton(),open=new SoftButton(),retry=new SoftButton(),demo=new SoftButton();readonly Label result=new Label();readonly Action<string,object> log;readonly Action retryAction;ImportOperation operation;Task copying;bool busy,allowClose,closeRequested;DiagnosticResult package;bool copiedOnce;
#if TEST_BUILD
 internal Func<string,bool> TestClipboard;internal bool TestSkipClipboard;internal Task TestCopy(){return CopyForAgent();}internal bool TestBusy{get{return busy;}}internal DiagnosticResult TestPackage{get{return package;}}
 internal Action<string> TestFolderLauncher;internal Task TestFolder(){return OpenFolder();}
 internal void TestNote(string value){addNote.Checked=!String.IsNullOrEmpty(value);symptom.Text=value;}
 internal string TestSummary;internal bool TestSendRequired{get{return false;}}
#endif
 internal FaultHandoffForm(FaultRecord record,Action<string,object> logger,Action retryCurrent=null){fault=FaultSafety.Sanitize(Common.Json.Deserialize<FaultRecord>(Common.Json.Serialize(record)));if(String.IsNullOrEmpty(fault.IncidentId))fault.IncidentId=fault.Id;log=logger??((p,d)=>{});retryAction=retryCurrent;Text=fault.Demo?"提交演示（不会发送）":"提交给Agent";Font=new Font("Microsoft YaHei UI",10);BackColor=Theme.Canvas;ForeColor=Theme.Ink;ClientSize=new Size(650,365);MinimumSize=new Size(610,365);StartPosition=FormStartPosition.CenterParent;
  var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(26),ColumnCount=1,RowCount=7};foreach(int h in new[]{38,65,35,0,60,48})panel.RowStyles.Add(new RowStyle(SizeType.Absolute,h));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));Controls.Add(panel);
  panel.Controls.Add(new Label{Text=fault.Demo?"试一下怎样交给Agent":"把问题交给Agent",Dock=DockStyle.Fill,Font=new Font(Font.FontFamily,17,FontStyle.Bold)},0,0);
  string summary=fault.Demo?"这是演示，不代表软件出了故障。\n你只需复制信息，粘贴给Agent；不用看懂技术内容。":fault.StatusOnly?"软件会把检查信息一起整理好。\n你可以补充现象，也可以直接复制后粘贴给Agent。":FaultSafety.Text(fault.Operation,50)+"没有完成。\n你可以补充现象，或直接把检查信息交给Agent。";
#if TEST_BUILD
  TestSummary=summary;
#endif
  panel.Controls.Add(new Label{Text=summary,Dock=DockStyle.Fill,ForeColor=Theme.Muted},0,1);
  addNote.Text="我要补充一句（可选）";addNote.Dock=DockStyle.Fill;panel.Controls.Add(addNote,0,2);symptom.Multiline=true;symptom.MaxLength=400;symptom.Dock=DockStyle.Fill;symptom.ScrollBars=ScrollBars.Vertical;symptom.Visible=false;panel.Controls.Add(symptom,0,3);
  addNote.CheckedChanged+=(s,e)=>{symptom.Visible=addNote.Checked;panel.RowStyles[3].Height=addNote.Checked?76:0;ClientSize=new Size(ClientSize.Width,addNote.Checked?441:365);if(addNote.Checked)symptom.Focus();InvalidatePackage();};symptom.TextChanged+=(s,e)=>InvalidatePackage();
  var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,10,0,0)};copy.Text="复制交接信息";copy.Name="CopyFaultToAgent";copy.Width=155;copy.Height=38;((SoftButton)copy).Primary=true;open.Text="打开证据包文件夹";open.Width=190;open.Height=38;retry.Text="重试原操作";retry.Width=120;retry.Height=38;retry.Visible=retryAction!=null&&fault.RetryAllowed;buttons.Controls.Add(copy);buttons.Controls.Add(open);buttons.Controls.Add(retry);panel.Controls.Add(buttons,0,4);
  result.Text="复制后，到Agent聊天里粘贴即可。需要附件时，再附上证据包。";result.Dock=DockStyle.Fill;panel.Controls.Add(result,0,5);
  var foot=new FlowLayoutPanel{Dock=DockStyle.Fill};foot.Controls.Add(new Label{Text="仅在本机准备，不会自动发送或执行修复。",AutoSize=true,ForeColor=Theme.Muted});demo.Text="试用示例";demo.FlatStyle=FlatStyle.Flat;demo.FlatAppearance.BorderSize=0;demo.Width=85;demo.Height=26;demo.Visible=!fault.Demo;foot.Controls.Add(demo);panel.Controls.Add(foot,0,6);
  copy.Click+=async(s,e)=>await CopyForAgent();open.Click+=async(s,e)=>await OpenFolder();retry.Click+=(s,e)=>{if(!busy&&retryAction!=null){Close();retryAction();}};demo.Click+=(s,e)=>{using(var f=new FaultHandoffForm(Demo(),log))f.ShowDialog(this);};
  FormClosing+=async(s,e)=>{if(allowClose)return;if(busy){e.Cancel=true;if(closeRequested)return;closeRequested=true;result.Text="正在取消，请稍候…";if(operation!=null)operation.Cancel();if(copying!=null)await copying;allowClose=true;Close();}};
 }
 void InvalidatePackage(){if(busy)return;package=null;copiedOnce=false;copy.Text="复制交接信息";result.Text="补充内容会一起交给Agent；也可以不填写。";}
 internal static FaultRecord Demo(){return new FaultRecord{SchemaVersion=1,Id="diag-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,12),EventTime=DateTimeOffset.Now.ToString("o"),Category=FaultSafety.Category("check"),Operation="示例资源检查",Stage="演示阶段（未实际读取资源）",Reason="这是安全的交接演示，没有制造真实故障",UserSymptom="用户尚未补充",Demo=true,DataState="演示未修改壁纸列表或用户数据。",CleanupState="演示没有启动播放器；实际日常播放状态尚未核查。",DesktopState="演示不请求播放或桌面切换；现有桌面状态需现场核对。",WorkerState="unknown",PlayerState="unknown",Events=new List<Dictionary<string,object>>()};}
 void ControlsBusy(bool value){busy=value;copy.Enabled=!value;open.Enabled=!value;retry.Enabled=!value;demo.Enabled=!value;addNote.Enabled=!value;symptom.Enabled=!value;}
 async Task EnsurePackage(){fault.UserSymptom=!addNote.Checked||String.IsNullOrWhiteSpace(symptom.Text)?"用户尚未补充":FaultSafety.Text(symptom.Text,400);if(fault.StartupFailure||!DataLocation.Ready){string folder=Path.Combine(Common.WorkRoot,"handoff-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);try{package=await Task.Run(()=>StartupHandoff.BuildEvidence(folder,fault,()=>{if(closeRequested)throw new OperationCanceledException();}));fault.Id=package.Id;return;}finally{DiagnosticBundle.CleanOperation(folder);}}operation=new ImportOperation(log);var answer=await operation.Run("diagnostic",null,null,0,null,null,null,fault);if(closeRequested)throw new OperationCanceledException();var incoming=Common.Json.Deserialize<DiagnosticResult>(Common.Json.Serialize(answer["diagnostic"]));string full=Path.GetFullPath(Path.Combine(Common.Root,incoming.PackageRelative));if(!LocalImport.Under(full,DiagnosticBundle.Root)||Common.Hash(full)!=incoming.PackageSha256||incoming.IncidentId!=fault.IncidentId)throw new InvalidDataException("证据包身份不匹配");package=incoming;fault.Id=incoming.Id;}
 async Task CopyForAgent(){if(busy||closeRequested)return;ControlsBusy(true);result.Text="正在准备交接信息…";copying=BuildAndCopy(false);await copying;}
 internal Task CopyAutomatically(){return Task.FromResult(false);} // Opening the form never skips optional user input.
 async Task OpenFolder(){if(busy||closeRequested)return;ControlsBusy(true);result.Text="正在准备证据包…";copying=BuildAndCopy(true);await copying;}
 async Task BuildAndCopy(bool folderOnly){try{await EnsurePackage();if(closeRequested)throw new OperationCanceledException();if(folderOnly){string full=Path.GetFullPath(Path.Combine(Common.Root,package.PackageRelative));LocalImport.NoLinks(full);
#if TEST_BUILD
   if(TestFolderLauncher!=null)TestFolderLauncher(full);else
#endif
   Process.Start(new ProcessStartInfo("explorer.exe","/select,"+Common.Quote(full)){UseShellExecute=true});result.Text="已打开文件夹。需要附件时，把选中的ZIP发给Agent。";log("diagnostic-folder-opened",new{id=package.Id,package=package.PackageRelative});return;}
   bool copied=false;
#if TEST_BUILD
   if(TestClipboard!=null)copied=TestClipboard(package.ClipboardText);else if(TestSkipClipboard)copied=true;else
#endif
   {Clipboard.SetText(package.ClipboardText);copied=true;}if(!copied)throw new System.Runtime.InteropServices.ExternalException("clipboard-busy");copiedOnce=true;result.Text="已复制。现在到Agent聊天里粘贴即可。";log("diagnostic-copied",new{id=package.Id,package=package.PackageRelative,sha256=package.PackageSha256,bytes=package.Bytes,demo=fault.Demo,reused=package.Reused,attachmentsSent=false});
  }catch(OperationCanceledException){result.Text="已取消，没有改动壁纸或数据。";log("diagnostic-cancelled",new{id=fault.Id});}
  catch(Exception ex){bool basic=false;if(fault.StartupFailure&&package==null&&String.IsNullOrEmpty(fault.DiagnosticFailurePath))fault.DiagnosticFailurePath=StartupHandoff.RecordDiagnosticFailure(fault,ex);if(package==null&&!folderOnly&&!closeRequested)try{string text=DiagnosticBundle.Handoff(fault,"尚未生成："+FaultSafety.Text(ex.Message,140),"尚未核验","证据包准备失败");
#if TEST_BUILD
   if(TestClipboard!=null)basic=TestClipboard(text);else if(TestSkipClipboard)basic=true;else
#endif
   {Clipboard.SetText(text);basic=true;}}catch{}if(fault.StartupFailure&&package==null){string saved=String.IsNullOrEmpty(fault.DiagnosticFailurePath)?"备用诊断记录也未能保存。":"原始启动错误已另存到："+fault.DiagnosticFailurePath;result.Text="启动阶段："+fault.Stage+"。原始错误："+FaultSafety.Text(fault.Reason,220)+"。诊断包准备失败（"+FaultSafety.Text(ex.GetType().Name+"："+ex.Message,220)+"）；"+saved;}else result.Text=basic?"已复制基础说明，但证据包没准备好。可先粘贴给Agent，或稍后重试。":package!=null?"证据包已准备好，复制暂未成功。请重试，或打开文件夹。":"暂时没准备好，请稍后重试。壁纸和原数据没有因此改动。";log("diagnostic-handoff-failed",new{id=fault.Id,errorType=ex.GetType().Name,reason=FaultSafety.Text(ex.Message,240),originalStartupException=fault.StartupFailure?fault.Reason:null,fallbackRecord=fault.DiagnosticFailurePath,packageRetained=package!=null,basicTextCopied=basic});}
  finally{if(operation!=null)try{DiagnosticBundle.CleanOperation(operation.Folder);}catch{}ControlsBusy(false);copy.Text=copiedOnce?"再次复制":"复制交接信息";}}
 internal async Task StopForOwner(){closeRequested=true;if(operation!=null&&operation.Busy)operation.Cancel();if(copying!=null)await copying;allowClose=true;Close();}
}
