using System;using System.IO;using System.Drawing;using System.Linq;using System.Threading.Tasks;using System.Windows.Forms;using System.Collections.Generic;
internal sealed partial class ControlWindow {
 ImportOperation importOperation;ImportDialog importDialog;
#if TEST_BUILD
 internal bool TestSuppressImportShow;internal Task TestOpenImport(){return OpenLocalImportCore();}
#endif
 async void OpenLocalImport(){await OpenLocalImportCore();}
 async Task OpenLocalImportCore(){if(closing)return;if(sourceState==null||!sourceState.Found){MessageBox.Show(this,sourceState!=null&&sourceState.SavedCustom?"已保存的壁纸来源不可用。请先点击首页的来源状态重新选择符合已知布局的目录。":"尚未找到本机壁纸来源。请先点击首页的来源状态浏览已有缓存位置。","壁纸来源尚未准备好",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}await PauseDiscovery();if(closing)return;if(importDialog!=null&&!importDialog.IsDisposed){importDialog.Activate();return;}importDialog=new ImportDialog(this);importDialog.FormClosed+=(s,e)=>{importDialog=null;discoveryNext=DateTimeOffset.MinValue;};
#if TEST_BUILD
 if(TestSuppressImportShow){var nativeList=importDialog.list.Handle;await importDialog.Scan();return;}
#endif
 importDialog.Show(this);}
 async Task<Dictionary<string,object>> ImportJob(string mode,ImportCandidate c,Action<string,long,long> progress,int testDelay=0){if(importOperation!=null&&importOperation.Busy)throw new InvalidOperationException("已有本地任务，请等待或取消");importOperation=new ImportOperation(Write);var op=importOperation;try{return await op.Run(mode,c,progress,testDelay);}catch{LocalImport.CleanPayload(op.Folder);throw;}}
 async Task StopImport(){var d=importDialog;if(d!=null&&!d.IsDisposed){await d.StopQueue();await d.StopImageLoading();}if(importOperation!=null){await importOperation.Stop();Write("local-import-close-clean",new{operation=importOperation.Folder,busy=importOperation.Busy});}}
 sealed partial class ImportDialog:MaterialDialog {
  readonly ImportPerformance performance=new ImportPerformance();
  readonly ControlWindow owner;readonly ImageList thumbs=new ImageList{ImageSize=new Size(64,40),ColorDepth=ColorDepth.Depth32Bit};
  internal readonly ListView list=new ListView();readonly PictureBox cover=new PictureBox();internal readonly TextBox note=new TextBox();internal readonly Label status=new Label();internal readonly MaterialProgress progress=new MaterialProgress();
  readonly ImportProgressState progressState=new ImportProgressState();
  TableLayoutPanel importLayout;bool sizingProgress;string renderedProgress;
  internal readonly Button import=new SoftButton(),analyze=new SoftButton(),cancel=new SoftButton(),refresh=new SoftButton();readonly ToolTip guidance=new ToolTip{AutoPopDelay=15000};
  readonly TextBox candidateSearch=new TextBox();
  readonly Label searchCount=new Label();
  readonly List<ListViewItem> candidateRows=new List<ListViewItem>();
  bool applyingFilter,deferFilter,copyCancelRequested;
  string appliedQuery;
  bool scanning,copying,queueRunning,queueStop,allowClose,closeRequested;Task queueTask;string phase;int done,total,scanCachedCount;readonly Dictionary<string,int> waitingRetries=new Dictionary<string,int>();
#if TEST_BUILD
  internal bool TestSkipInitialScan;
  internal Action<ImportCandidate> TestQueueResult;
  internal bool TestQueueRunning{get{return queueRunning;}}
  internal ListViewItem TestAddCandidate(ImportCandidate c){return AddCandidate(c);}
  internal Task TestRunQueue(){queueStop=false;queueTask=RunQueue();return queueTask;}
#endif
  internal ImportDialog(ControlWindow window){owner=window;imageCache=new ImportImageCache(performance);Text="导入联想已下载壁纸";Font=window.Font;BackColor=Theme.Canvas;StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(1140,650);MinimumSize=new Size(1040,600);
   var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),RowCount=5,ColumnCount=1};importLayout=root;root.RowStyles.Add(new RowStyle(SizeType.Absolute,82));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));root.RowStyles.Add(new RowStyle(SizeType.Absolute,0));root.RowStyles.Add(new RowStyle(SizeType.Absolute,56));Controls.Add(root);
   var searchRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
   searchRow.Controls.Add(new Label{Text="检查通过后明确导入。图片用于辨认，播放仍需预览确认。",Dock=DockStyle.Fill,ForeColor=Theme.Muted},0,0);
   var searchPanel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};searchPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,20));searchPanel.RowStyles.Add(new RowStyle(SizeType.Absolute,28));searchPanel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
   searchPanel.Controls.Add(new Label{Text="筛选候选 · 名称或ID",AutoSize=true},0,0);candidateSearch.Dock=DockStyle.Fill;searchPanel.Controls.Add(candidateSearch,0,1);searchCount.Dock=DockStyle.Fill;searchCount.ForeColor=Theme.Muted;searchPanel.Controls.Add(searchCount,0,2);searchRow.Controls.Add(searchPanel,1,0);root.Controls.Add(searchRow,0,0);
   candidateSearch.TextChanged+=(s,e)=>ApplyFilter();

   var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,76));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,24));root.Controls.Add(body,0,1);
   list.Dock=DockStyle.Fill;list.View=View.Details;list.SmallImageList=thumbs;var nativeImages=thumbs.Handle;list.FullRowSelect=true;list.MultiSelect=false;list.HideSelection=false;list.ShowItemToolTips=true;
   list.Columns.Add("名称 / ID",210);list.Columns.Add("大小",86);list.Columns.Add("资源",82);list.Columns.Add("检查",90);list.Columns.Add("封面",110);list.Resize+=(s,e)=>{if(list.IsDisposed||list.Disposing||list.Columns.Count==0)return;list.Columns[0].Width=Math.Max(180,list.ClientSize.Width-86-82-90-110-SystemInformation.VerticalScrollBarWidth-4);};body.Controls.Add(list,0,0);
   var side=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};side.RowStyles.Add(new RowStyle(SizeType.Percent,48));side.RowStyles.Add(new RowStyle(SizeType.Percent,52));cover.Dock=DockStyle.Fill;cover.SizeMode=PictureBoxSizeMode.Zoom;cover.BackColor=Color.FromArgb(237,239,243);note.Dock=DockStyle.Fill;note.ReadOnly=true;note.Multiline=true;note.ScrollBars=ScrollBars.Vertical;note.BorderStyle=BorderStyle.None;note.BackColor=Theme.Canvas;note.ForeColor=Theme.Muted;side.Controls.Add(cover,0,0);side.Controls.Add(note,0,1);body.Controls.Add(side,1,0);
   status.Dock=DockStyle.Fill;status.Padding=new Padding(0,8,0,0);status.Text="读取候选后选择壁纸，可主动导入。";status.AutoEllipsis=true;status.UseMnemonic=false;root.Controls.Add(status,0,2);progress.Dock=DockStyle.Fill;root.Controls.Add(progress,0,3);status.Margin=Padding.Empty;progress.Margin=Padding.Empty;Resize+=(s,e)=>LayoutProgress();status.FontChanged+=(s,e)=>LayoutProgress();
   var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(0,4,0,0)};Configure(import,"导入所选壁纸",150);((SoftButton)import).Primary=true;Configure(cancel,"关闭",100);Configure(refresh,"刷新候选",110);Configure(analyze,"重新检查此文件",150);buttons.Controls.Add(import);buttons.Controls.Add(cancel);buttons.Controls.Add(refresh);buttons.Controls.Add(analyze);var help=new SoftButton{Text="提交给Agent",Width=120,Height=38};help.Click+=(s,e)=>owner.OpenFaultHandoff();buttons.Controls.Add(help);root.Controls.Add(buttons,0,4);
   list.SelectedIndexChanged+=(s,e)=>{if(!applyingFilter)UpdateSelection();};import.Click+=async(s,e)=>await ImportSelected();refresh.Click+=async(s,e)=>await Scan();analyze.Click+=async(s,e)=>await AnalyzeSelected();cancel.Click+=async(s,e)=>{if(scanning||copying){if(copying)copyCancelRequested=true;if(owner.importOperation!=null)owner.importOperation.Cancel();progressState.Cancelling();PresentProgress();}else if(queueRunning){await StopQueue();}else Close();};
   guidance.SetToolTip(analyze,"只重新检查所选文件，其他候选不重做；不会导入或播放。未知结构需补充规则，重复检查不会增加支持。");guidance.SetToolTip(import,"只在本文件入库前检查通过后保存；复制中仍复核源文件、完整哈希及管理副本。不会自动播放。");
   Shown+=async(s,e)=>{
#if TEST_BUILD
    if(TestSkipInitialScan)return;
#endif
    await Scan();};
   FormClosing+=async(s,e)=>{if(allowClose)return;if(scanning||copying||queueRunning||imageTasks.Any(task=>!task.IsCompleted)){e.Cancel=true;if(closeRequested)return;closeRequested=true;progressState.Cancelling();PresentProgress();queueStop=true;if(owner.importOperation!=null)owner.importOperation.Cancel();await StopQueue();if(owner.importOperation!=null)await owner.importOperation.Stop();await StopImageLoading();if(!IsDisposed){allowClose=true;Close();}}};
   Disposed+=(s,e)=>{var imageCleanup=StopImageLoading();if(!owner.closing&&!owner.IsDisposed)owner.Write("import-performance-summary",performance.Snapshot());if(cover.Image!=null)cover.Image.Dispose();thumbs.Dispose();guidance.Dispose();};UpdateSelection();
  }
  void PresentProgress() {using(performance.Measure("ui.progress")){
   if(IsDisposed)return;
   string progressKey=progressState.Phase+"|"+progressState.Text+"|"+progressState.Completed+"|"+progressState.Total;if(progressKey==renderedProgress)return;renderedProgress=progressKey;
   status.Text=progressState.Text;status.AccessibleDescription=progressState.Text;
   guidance.SetToolTip(status,progressState.Text);progress.Present(progressState);LayoutProgress();
   owner.Write("import-progress",new{phase=progressState.Phase.ToString(),completed=progressState.Completed,total=progressState.Total,indeterminate=progressState.Indeterminate,text=progressState.Text});
  }}
  void LayoutProgress() {using(performance.Measure("ui.layout")){
   if(sizingProgress||importLayout==null)return;performance.LayoutPasses++;
   sizingProgress=true;
   try {
    float scale;using(var graphics=CreateGraphics())scale=graphics.DpiX/96f;
    status.Padding=new Padding(0,(int)(8*scale),0,(int)(6*scale));
    int width=Math.Max(1,importLayout.ClientSize.Width-importLayout.Padding.Horizontal);
    float textHeight=Math.Max((int)(42*scale),HomeTypography.WrappedHeight(status.Text,status.Font,width,2)+status.Padding.Vertical);
    float trackHeight=progress.TrackVisible?(int)(22*scale):0;
    if(importLayout.RowStyles[2].Height!=textHeight)importLayout.RowStyles[2].Height=textHeight;
    if(importLayout.RowStyles[3].Height!=trackHeight)importLayout.RowStyles[3].Height=trackHeight;
   }finally{sizingProgress=false;}
  }}
  void ShowQueueProgress(string current) {
   if(progressState.Phase==ImportProgressPhase.Cancelling)return;
   progressState.Checking(done,total,scanCachedCount,current);PresentProgress();
  }
  void ApplyFilter() {
   using(performance.Measure("ui.filter")) {
    if(deferFilter||IsDisposed)return;
    string query=candidateSearch.Text.Trim();
    bool queryChanged=!String.Equals(appliedQuery,query,StringComparison.OrdinalIgnoreCase);
    string selectedKey=Selected==null?null:ImportCandidateSearch.Key(Selected);
    var visible=candidateRows.Where(row=>ImportCandidateSearch.Matches((ImportCandidate)row.Tag,query,owner.library.Doc.Items)).ToArray();
    var current=list.Items.Cast<ListViewItem>().ToArray();
    if(!current.SequenceEqual(visible)) {
     ListViewItem top=list.IsHandleCreated&&list.Items.Count>0?list.TopItem:null;
     applyingFilter=true;
     performance.ListRebuilds++;
     try {
      // A complete membership submission resets the native origin after a shrinking filter.
      // Keep the existing row objects, text and image keys, and restore view state after EndUpdate.
      list.BeginUpdate();
      try {
       list.Items.Clear();
       list.Items.AddRange(visible);
      } finally {
       list.EndUpdate();
      }
      var next=visible.FirstOrDefault(row=>ImportCandidateSearch.Key((ImportCandidate)row.Tag)==selectedKey)??visible.FirstOrDefault();
      if(next!=null&&!next.Selected) {
       next.Selected=true;
       next.Focused=true;
      }
      if(!queryChanged&&top!=null&&top.ListView==list)list.TopItem=top;
      if(queryChanged&&next!=null)next.EnsureVisible();
     } finally {
      applyingFilter=false;
     }
    }
    appliedQuery=query;
    string summary=visible.Length==0?"没有匹配候选":"显示 "+visible.Length+" / "+candidateRows.Count+" 项";
    if(searchCount.Text!=summary)searchCount.Text=summary;
    if(visible.Length>0)performance.Mark("first-rows");
    UpdateSelection();
   }
  }
  ImportCandidate Selected{get{return list.SelectedItems.Count==1?list.SelectedItems[0].Tag as ImportCandidate:null;}}
  string Hash(ImportCandidate c){return c==null||c.Check==null?null:c.Check.Sha256;}
  WallpaperItem Registered(ImportCandidate c){var hash=Hash(c);return !PackageAnalysis.HashText(hash)?null:owner.library.Doc.Items.FirstOrDefault(x=>x.RegisteredSha256==hash);}
  internal string ResourceState(ImportCandidate c){return Registered(c)!=null?"已入库":c.NewlyDiscovered?"新发现":"历史资源";}
  internal string PlaybackState(ImportCandidate c){var saved=Registered(c);string hash=Hash(c);return saved==null?"尚未验证":saved.DesktopConfirmedSha256==hash?"已有桌面记录":saved.PreviewConfirmedSha256==hash||saved.Id=="builtin-317495"&&hash==Common.CwpHash?"已有窗口确认":"尚未验证";}
  internal string CandidateRowState(ImportCandidate c){switch(c.Check==null?"queued":c.Check.State){case "checking":return "检查中";case "passed":return "通过";case "unknown":return "结构未知";case "failed":return "检查失败";case "cancelled":return "已取消";case "waiting":return "等待文件稳定";default:return "排队";}}
  internal string CoverState(ImportCandidate c){if(c.Cover!=null&&c.Check!=null&&c.Check.CoverSha256!=null)return c.Check.CoverKind==PreviewCovers.Kind?"预览截图":c.Check.CoverKind==VideoCovers.Kind?"视频画面":"真实图片";if(c.Check==null||c.Check.State=="queued")return "排队读取";if(c.Check.State=="checking")return c.Check.Phase!=null&&c.Check.Phase.Contains("封面")?"读取中":"等待检查";if(c.Check.State=="cancelled")return "已取消";return c.Check.CoverReason!=null&&c.Check.CoverReason.Contains("失败")?"读取失败":"无可靠图片";}
  internal string CandidateState(ImportCandidate c){if(c==null)return "选择候选即可查看结果，无需逐项开始检查。";string state=CandidateRowState(c);if(c.Check==null||c.Check.State=="queued")return "已自动排队；检查与封面读取会逐项进行。";if(c.Check.State=="checking")return "正在检查："+c.Check.Phase;if(c.Check.State=="unknown")return "结构未知："+c.Check.Reason+"。保留候选与身份记录；当前窗口不能导入，需开发补充解析器或规则，重复检查不会增加支持。";if(c.Check.State=="passed")return "入库前检查通过，可主动导入。"+c.Check.Reason;return state+"："+c.Check.Reason+"\n发生阶段："+c.Check.Phase+"\n可用“重新检查此文件”重试。";}
  void ControlsState(){list.Enabled=candidateSearch.Enabled=!copying&&!closeRequested;((SoftButton)import).Busy=copying;((SoftButton)refresh).Busy=scanning;((SoftButton)analyze).Busy=queueRunning;import.Enabled=!scanning&&!copying&&!closeRequested&&CandidateChecks.Passed(Selected);import.Text=copying?"正在导入…":"导入所选壁纸";analyze.Enabled=!scanning&&!copying&&!queueRunning&&!closeRequested&&Selected!=null;refresh.Enabled=!scanning&&!copying&&!queueRunning&&!closeRequested;cancel.Text=copying?"取消导入":scanning||queueRunning?"取消检查":"关闭";}
  void UpdateRow(ListViewItem row){performance.RowUpdates++;using(performance.Measure("ui.row")){var c=(ImportCandidate)row.Tag;string alias=ImportCandidateSearch.Aliases(c,owner.library.Doc.Items).FirstOrDefault(name=>name!=c.Name);row.Text=c.Name+" · "+ResourcePresentation.Id(c.Id,null)+(alias==null?"":"（库内名："+alias+"）");string[] values={(c.Bytes/1048576.0).ToString("F1")+" MB",ResourceState(c),CandidateRowState(c),CoverState(c)};for(int i=0;i<values.Length;i++)row.SubItems[i+1].Text=values[i];row.ToolTipText=ResourcePresentation.Id(c.Id,null)+"\n"+c.Name+"\n库内名称："+String.Join("、",ImportCandidateSearch.Aliases(c,owner.library.Doc.Items))+"\n"+CandidateState(c);}}
  ListViewItem AddCandidate(ImportCandidate c){var row=new ListViewItem(new[]{"","","","",""}){Tag=c};candidateRows.Add(row);UpdateRow(row);Thumbnail(row);if(!deferFilter)ApplyFilter();return row;}
  internal void UpdateSelection(){using(performance.Measure("ui.selection")){var c=Selected;ControlsState();UpdateDetailImage(c);string text=c==null?CandidateState(null):(ResourcePresentation.Id(c.Id,null)+"\n名称："+(c.NameSource==null?"暂未取得官方名称":"已保存官方本地名称")+"\n资源："+ResourceState(c)+"\n检查："+CandidateRowState(c)+"\n封面："+CoverState(c)+"\n\n"+CandidateState(c)+"\n封面说明："+(c.CoverReason??"后台处理尚未完成")+"\n\nSHA-256："+(Hash(c)??"尚未完成")+"\n首次发现："+c.FirstDiscoveredAt+"\n记录更新时间："+c.RecordUpdatedAt+"\n以上不是官方下载时间。\n视频会自动截取开头可识别的一帧；未取得图片时会说明原因。").Replace("\n","\r\n");if(note.Text!=text)note.Text=text;}}
  internal async Task StopQueue(){queueStop=true;if(queueRunning){progressState.Cancelling();PresentProgress();}if(queueRunning&&owner.importOperation!=null)owner.importOperation.Cancel();foreach(ListViewItem row in candidateRows){var c=(ImportCandidate)row.Tag;if(c.Check==null||c.Check.State=="queued"||c.Check.State=="waiting"){c.Check=new CandidateCheckResult{State="cancelled",Phase="排队",Reason="用户取消本次队列；未导入"};UpdateRow(row);}}var task=queueTask;if(task!=null)await task;}
  async Task RunOne(ListViewItem row,bool force){var c=(ImportCandidate)row.Tag;phase="核对本地记录";c.Check=new CandidateCheckResult{State="checking",Phase=phase};c.Cover=null;c.CoverReason="后台读取中";UpdateRow(row);UpdateSelection();owner.Write("candidate-check-start",new{id=c.Id,name=c.Name,source=c.Source,force=force,autoImport=false});
   try{var data=await owner.ImportJob(force?"recheck":"check",c,(p,b,t)=>{if(IsDisposed)return;phase=p;c.Check.Phase=p;ShowQueueProgress(c.Name+" · "+p);UpdateRow(row);if(Object.ReferenceEquals(c,Selected))UpdateSelection();});var checkedCandidate=await Task.Run(()=>{var json=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=8388608};var value=json.Deserialize<ImportCandidate>(json.Serialize(data["candidate"]));OfficialNameStore.ApplySaved(new List<ImportCandidate>{value});return value;});if(IsDisposed||closeRequested)return;row.Tag=checkedCandidate;
#if TEST_BUILD
   if(TestQueueResult!=null)TestQueueResult((ImportCandidate)row.Tag);
#endif
}
   catch(OperationCanceledException){c.Check.State="cancelled";c.Check.Reason="已取消；未导入";c.Check.Phase=phase;}
   catch(Exception ex){c.Check.State="failed";c.Check.Reason=ex.Message;c.Check.Phase=phase;}
   finally{if(!IsDisposed){UpdateRow(row);Thumbnail(row);ApplyFilter();var result=(ImportCandidate)row.Tag;if(result.Check.State=="failed")owner.RecordFault("check","检查本地候选",result.Check.Phase,result.Check.Reason,result,()=>{if(!IsDisposed&&!closeRequested&&!queueRunning&&!copying){row.Selected=true;var ignored=AnalyzeSelected();}});owner.Write("candidate-check-result",new{id=result.Id,name=result.Name,source=result.Source,state=result.Check.State,reason=result.Check.Reason,stage=result.Check.Phase,sha256=result.Check.Sha256,cacheHit=result.Check.CacheHit,cacheReason=result.Check.CacheReason,cacheMissReason=result.CacheDisposition,hashedBytes=result.Check.HashedBytes,coverReads=result.Check.CoverReads,coverDecodedBytes=result.Check.CoverDecodedBytes,videoFrameAttempts=result.Check.VideoFrameAttempts,videoFrameImageBytes=result.Check.VideoFrameImageBytes,videoTransformHashedBytes=result.Check.VideoTransformHashedBytes,coverValidationBytes=result.Check.CoverValidationBytes,hashMilliseconds=result.Check.HashMilliseconds,structureMilliseconds=result.Check.StructureMilliseconds,coverMilliseconds=result.Check.CoverMilliseconds,cover=result.Check.CoverMember,coverSha256=result.Check.CoverSha256,autoImport=false,autoPlay=false});}}}
  async Task RunQueue() {
   queueRunning=true;done=0;
   var pending=candidateRows.Where(row=>{var c=(ImportCandidate)row.Tag;return c.Check==null||c.Check.State=="queued"||c.Check.State=="waiting"||c.Check.CoverRefreshDue;}).ToList();
   total=pending.Count;ControlsState();ShowQueueProgress(null);
   try {
    for(int index=0;index<pending.Count;index++) {
     if(queueStop||owner.closing||closeRequested)break;
     var row=pending[index];ShowQueueProgress(((ImportCandidate)row.Tag).Name);
     await RunOne(row,false);done++;
     var candidate=(ImportCandidate)row.Tag;int retries;waitingRetries.TryGetValue(candidate.Source,out retries);
     if(candidate.Check.State=="waiting"&&retries<2&&!queueStop) {
      waitingRetries[candidate.Source]=retries+1;pending.Add(row);total=pending.Count;
      ShowQueueProgress("等待文件稳定，已追加一次检查");
      await Task.Delay(2100);
     } else ShowQueueProgress(null);
    }
   } finally {
    queueRunning=false;
    if(!IsDisposed) {
     UpdateSelection();
     int failed=candidateRows.Count(row=>((ImportCandidate)row.Tag).Check!=null&&((ImportCandidate)row.Tag).Check.State=="failed");
     int unknown=candidateRows.Count(row=>((ImportCandidate)row.Tag).Check!=null&&((ImportCandidate)row.Tag).Check.State=="unknown");
     string result=queueStop?"检查已取消 · 已处理 "+done+" / "+total+" 次 · 可重新检查所选目标"
      :"检查结束 · 已复用 "+scanCachedCount+" 项 · 本次处理 "+done+" 次"+(failed>0?" · 失败 "+failed+" 项":"")+(unknown>0?" · 结构未知 "+unknown+" 项":"");
     progressState.Finish(queueStop?ImportProgressPhase.Cancelled:failed>0?ImportProgressPhase.Failed:ImportProgressPhase.Completed,result);PresentProgress();
     owner.Write("candidate-queue-finished",new{completed=done,scheduled=total,cached=scanCachedCount,failed=failed,unknown=unknown,cancelled=queueStop,autoImport=false,autoPlay=false});
    }
   }
  }
  internal void RefreshAliases(){foreach(var row in candidateRows)UpdateRow(row);ApplyFilter();}
  internal void RefreshOfficialNames(Dictionary<string,NameEvidence> names){foreach(ListViewItem row in candidateRows){var c=(ImportCandidate)row.Tag;OfficialNameStore.Apply(new List<ImportCandidate>{c},names);UpdateRow(row);}ApplyFilter();}
  internal void RefreshPreviewCover(string hash){foreach(ListViewItem row in candidateRows){var c=(ImportCandidate)row.Tag;if(c.Check!=null&&c.Check.Sha256==hash){PreviewCovers.Attach(c.Check);c.Cover=c.Check.CoverRelative==null?null:Path.Combine(Common.Root,c.Check.CoverRelative);c.CoverReason=c.Check.CoverReason;UpdateRow(row);Thumbnail(row);}}ApplyFilter();}
  internal async Task Scan(){if(scanning||copying||queueRunning||closeRequested)return;scanning=true;queueStop=false;BeginImageGeneration();waitingRetries.Clear();ControlsState();progressState.Reading();PresentProgress();try{var data=await owner.ImportJob("scan",null,null);performance.Mark("scan-result");
   var found=await Task.Run(()=>{using(performance.Measure("worker.parse")){var json=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=8388608};return json.Deserialize<List<ImportCandidate>>(json.Serialize(data["candidates"]));}});
   if(IsDisposed||closeRequested||queueStop)throw new OperationCanceledException();
   using(performance.Measure("ui.names")){owner.ApplyOfficialNames(found);}
   scanCachedCount=found.Count(candidate=>candidate.Check!=null);list.Items.Clear();candidateRows.Clear();thumbs.Images.Clear();
   for(int start=0;start<found.Count;start+=12){
    if(IsDisposed||closeRequested||queueStop)throw new OperationCanceledException();
    using(performance.Measure("ui.list-submit")){
     list.BeginUpdate();deferFilter=true;
     try{foreach(var candidate in found.Skip(start).Take(12))AddCandidate(candidate);}finally{deferFilter=false;list.EndUpdate();}
     ApplyFilter();
    }
    await Task.Delay(1);
   }
   performance.Mark("scan-presented");owner.Write("candidate-scan-ready",new{count=found.Count,queued=found.Count(c=>c.Check==null),cached=found.Count(c=>c.Check!=null),cacheReasons=found.GroupBy(c=>c.CacheDisposition??"unspecified").ToDictionary(g=>g.Key,g=>g.Count()),cacheItems=found.Select(c=>new{id=c.Id,reason=c.CacheDisposition,cacheHit=c.Check!=null,coverRefreshDue=c.Check!=null&&c.Check.CoverRefreshDue}).ToArray(),discoveryMetrics=data.ContainsKey("coverStats")?data["coverStats"]:null,autoImport=false});}catch(OperationCanceledException){queueStop=true;progressState.Finish(ImportProgressPhase.Cancelled,"候选读取已取消");PresentProgress();}catch(Exception ex){queueStop=true;progressState.Finish(ImportProgressPhase.Failed,"候选读取失败："+ex.Message);PresentProgress();owner.RecordFault("check","读取本地候选","scan",ex.Message,null,()=>{if(!IsDisposed&&!closeRequested){var ignored=Scan();}});}finally{scanning=false;if(!IsDisposed)UpdateSelection();}if(!queueStop&&!owner.closing&&!closeRequested){queueTask=RunQueue();await queueTask;}}
  internal async Task AnalyzeSelected(){if(scanning||copying||queueRunning||Selected==null||closeRequested)return;var row=list.SelectedItems[0];queueStop=false;queueRunning=true;done=0;total=1;ShowQueueProgress(((ImportCandidate)row.Tag).Name);queueTask=RunOne(row,true);try{await queueTask;}finally{queueRunning=false;if(!IsDisposed){UpdateSelection();var result=(ImportCandidate)row.Tag;progressState.Finish(result.Check.State=="cancelled"?ImportProgressPhase.Cancelled:result.Check.State=="failed"?ImportProgressPhase.Failed:ImportProgressPhase.Completed,CandidateState(result));PresentProgress();}}}
  internal async Task ImportSelected() {
   if(!CandidateChecks.Passed(Selected)||scanning||copying||closeRequested)return;
   var candidate=Selected;if(owner.closing||closeRequested)return;
   copying=true;copyCancelRequested=false;ControlsState();
   string outcome="";var finalPhase=ImportProgressPhase.Completed;
   try {
    if(queueRunning)await StopQueue();
    if(copyCancelRequested||owner.closing||closeRequested)throw new OperationCanceledException();
    progressState.Copying("正在准备复制并核验",0,candidate.Bytes);PresentProgress();
    var data=await owner.ImportJob("copy",candidate,(phase,bytes,totalBytes)=>{
     if(progressState.Phase==ImportProgressPhase.Cancelling||IsDisposed)return;
     progressState.Copying(phase,bytes,totalBytes);PresentProgress();
    });
    if(owner.closing||owner.importOperation.Cancelled)throw new OperationCanceledException();
    var receipt=Common.Json.Deserialize<ImportReceipt>(Common.Json.Serialize(data["receipt"]));
    progressState.Copying("复制与完整哈希核验已完成",receipt.Bytes,receipt.Bytes);PresentProgress();
    progressState.Registering();PresentProgress();
    bool duplicate;owner.RegisterImport(owner.importOperation,data,out duplicate);
    outcome=duplicate?"相同内容已在列表中，未重复建卡。":"已入库 · 资源和壁纸登记已保存";
    foreach(ListViewItem row in candidateRows)UpdateRow(row);ApplyFilter();
   }catch(OperationCanceledException){finalPhase=ImportProgressPhase.Cancelled;outcome="导入已取消；临时任务已结束。";}
   catch(Exception error){finalPhase=ImportProgressPhase.Failed;outcome="导入未完成："+error.Message;owner.RecordFault("save","导入所选壁纸","copy-or-register",error.Message,candidate);owner.Write("local-import-failed",new{sourceId=candidate.Id,error=error.Message});}
   finally{copying=false;if(!IsDisposed){UpdateSelection();progressState.Finish(finalPhase,outcome);PresentProgress();}}
  }

 }
}
