using System;using System.IO;using System.Drawing;using System.Windows.Forms;
internal static class HomeLayout {
 internal static Size Client(Size available,float scale){return new Size(Math.Max(640,Math.Min((int)(1180*scale),available.Width-32)),Math.Max(480,Math.Min((int)(850*scale),available.Height-40)));}
 internal static int CardColumns(int width,float scale){return Math.Max(1,(width+(int)(16*scale))/(int)(230*scale));}
}
internal sealed partial class ControlWindow {
 readonly AppSettings settings=AppSettings.Load();float uiScale=1;Panel side;TableLayoutPanel sideRows;readonly Label nowPlaying=new Label();
 readonly LinkLabel selectedSummaryToggle=new LinkLabel();readonly Label selectedSummary=new Label();
 bool summaryExpanded;
 readonly Timer summaryAnimation=new Timer{Interval=16};readonly System.Diagnostics.Stopwatch summaryClock=new System.Diagnostics.Stopwatch();float summaryFrom,summaryTo;
 void ToggleResourceSummary(){
  summaryExpanded=!summaryExpanded;summaryFrom=sideRows.RowStyles[4].Height;summaryTo=summaryExpanded?U(40):0;selectedSummary.Visible=true;
  selectedSummaryToggle.Text=summaryExpanded?"▾ 收起摘要":"▸ 资源摘要";summaryClock.Restart();
  if(SoftButton.MotionEnabled)summaryAnimation.Start();else{sideRows.RowStyles[4].Height=summaryTo;selectedSummary.Visible=summaryExpanded;}
 }

 int U(float x){return (int)Math.Round(x*uiScale);}
 void BuildPolishedUi(){
  using(var g=CreateGraphics())uiScale=g.DpiX/96f;
  AutoScaleMode=AutoScaleMode.None;Font=new Font("Microsoft YaHei UI",9.5f);ForeColor=Theme.Ink;Ink=Theme.Ink;Muted=Theme.Muted;Blue=Theme.Blue;
  Text="本地壁纸 · R33";Name="LocalWallpaperGallery";var area=Screen.PrimaryScreen.WorkingArea;ClientSize=HomeLayout.Client(area.Size,uiScale);
  MinimumSize=new Size(Math.Min(U(900),area.Width-24),Math.Min(U(620),area.Height-32));StartPosition=FormStartPosition.CenterScreen;BackColor=Theme.Canvas;
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(U(24),U(18),U(24),U(16)),ColumnCount=1,RowCount=3};
  root.RowStyles.Add(new RowStyle(SizeType.Absolute,U(70)));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,U(72)));Controls.Add(root);
  var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,Margin=Padding.Empty};
  header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int width in new[]{188,118,92,88})header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,U(width)));
  var title=new Label{Text="本地壁纸",Font=new Font(Font.FontFamily,23,FontStyle.Bold),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};header.Controls.Add(title,0,0);
  var import=new SoftButton{Text="导入已下载壁纸",Name="ImportLenovoLocal"};var add=new SoftButton{Text="添加文件",Name="AddWallpaper"};
  openSettings.Text="设置";openSettings.Name="OpenSettings";openSettings.AccessibleName="设置";openSettings.Click+=(s,e)=>ShowSettings();
  import.Click+=(s,e)=>OpenLocalImport();add.Click+=(s,e)=>AddLocal();Configure(quit,"彻底退出",88);
  var topButtons=new Button[]{import,add,openSettings,quit};for(int i=0;i<topButtons.Length;i++){var button=topButtons[i];button.Dock=DockStyle.Fill;button.Margin=new Padding(U(5),U(14),U(3),U(14));header.Controls.Add(button,i+1,0);}
  root.Controls.Add(header,0,0);
  var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,U(20)));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,U(292)));root.Controls.Add(body,0,1);
  var collection=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=Padding.Empty};
  collection.RowStyles.Add(new RowStyle(SizeType.Absolute,U(36)));collection.RowStyles.Add(new RowStyle(SizeType.Absolute,U(30)));collection.RowStyles.Add(new RowStyle(SizeType.Percent,100));
  count.Font=new Font(Font.FontFamily,13,FontStyle.Bold);count.Dock=DockStyle.Fill;collection.Controls.Add(count,0,0);
  homePreparation.Dock=DockStyle.Fill;homePreparation.ForeColor=Muted;homePreparation.LinkColor=Theme.Blue;homePreparation.LinkBehavior=LinkBehavior.NeverUnderline;
  homePreparation.LinkClicked+=(s,e)=>ShowSettings(!RuntimeProvisioning.Available||runtimeCheckRunning?1:sourceState!=null&&!sourceState.Found?0:3);collection.Controls.Add(homePreparation,0,1);
  cards.Dock=DockStyle.Fill;cards.AutoScroll=true;cards.WrapContents=true;cards.Margin=Padding.Empty;cards.Resize+=(s,e)=>LayoutCards();
  var gallery=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};gallery.Controls.Add(cards);
  emptyState.Dock=DockStyle.Fill;emptyState.BackColor=Theme.Canvas;var emptyLayout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(U(24))};
  emptyTitle.Text="把喜欢的壁纸放在这里";emptyTitle.Font=new Font(Font.FontFamily,18,FontStyle.Bold);emptyTitle.Size=new Size(U(400),U(54));
  emptyLead.Text="查看本机已经下载的资源，检查后选择导入。";emptyLead.Size=new Size(U(400),U(64));emptyLayout.Controls.Add(emptyTitle);emptyLayout.Controls.Add(emptyLead);
  Configure(emptyImport,"查看并导入本机候选",210);emptyImport.Click+=(s,e)=>OpenLocalImport();Configure(emptyAdd,"添加已有 CWP 文件",210);emptyAdd.Click+=(s,e)=>AddLocal();
  Configure(emptyChooseSource,"打开来源设置",210);emptyChooseSource.Click+=(s,e)=>ShowSettings(0);emptyLayout.Controls.Add(emptyImport);emptyLayout.Controls.Add(emptyAdd);emptyLayout.Controls.Add(emptyChooseSource);
  emptyState.Controls.Add(emptyLayout);gallery.Controls.Add(emptyState);collection.Controls.Add(gallery,0,2);body.Controls.Add(collection,0,0);
  side=new RoundedPanel{Dock=DockStyle.Fill,Padding=new Padding(U(16)),AutoScroll=true,Margin=Padding.Empty};body.Controls.Add(side,2,0);
  sideRows=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Margin=Padding.Empty};side.Controls.Add(sideRows);
  Action<Control,int> row=(control,height)=>{int index=sideRows.RowCount++;sideRows.RowStyles.Add(new RowStyle(SizeType.Absolute,U(height)));control.Dock=DockStyle.Fill;control.Margin=new Padding(0,U(3),0,U(3));sideRows.Controls.Add(control,0,index);};
  sideRows.RowCount=0;
  selectedImage.SizeMode=PictureBoxSizeMode.Zoom;selectedImage.BackColor=Color.FromArgb(234,240,250);row(selectedImage,152);
  selectedName.Font=new Font(Font.FontFamily,13,FontStyle.Bold);row(selectedName,45);selectedNote.ForeColor=Muted;row(selectedNote,68);
  selectedSummaryToggle.Text="▸ 资源摘要";selectedSummaryToggle.LinkBehavior=LinkBehavior.NeverUnderline;selectedSummaryToggle.LinkClicked+=(s,e)=>ToggleResourceSummary();
  summaryAnimation.Tick+=(s,e)=>{float t=Math.Min(1,summaryClock.ElapsedMilliseconds/240f);sideRows.RowStyles[4].Height=summaryFrom+(summaryTo-summaryFrom)*(1-(float)Math.Pow(1-t,3));if(t>=1){summaryAnimation.Stop();selectedSummary.Visible=summaryExpanded;}};Disposed+=(s,e)=>summaryAnimation.Dispose();row(selectedSummaryToggle,28);
  selectedSummary.ForeColor=Muted;selectedSummary.AutoEllipsis=true;row(selectedSummary,0);selectedSummary.Visible=false;
  renameWallpaper.Text="重命名";renameWallpaper.Name="RenameWallpaper";renameWallpaper.Click+=(s,e)=>RenameWallpaper(selected);row(renameWallpaper,39);
  Configure(preview,"预览",240);preview.Name="PreviewWallpaper";row(preview,43);
  Configure(confirmPreview,"画面正确，完成确认",240);confirmPreview.Click+=(s,e)=>ConfirmCurrentPreview("human-button");row(confirmPreview,39);
  Configure(desktop,"设为桌面壁纸",240);desktop.Name="ApplyWallpaperToDesktop";((SoftButton)desktop).Primary=true;row(desktop,45);
  Configure(stop,"停止播放",240);stop.Name="StopAndRestoreWallpaper";row(stop,41);
  keepPlaying.Text="关闭界面，继续播放";keepPlaying.Name="HideAndKeepPlaying";keepPlaying.Click+=async(s,e)=>await HideAndKeepPlaying();row(keepPlaying,41);
  Configure(usePreviewCover,"用当前画面作封面",240);usePreviewCover.Click+=async(s,e)=>await SavePreviewCover();row(usePreviewCover,39);
  previewCoverWhy.Font=new Font(Font.FontFamily,8);previewCoverWhy.ForeColor=Muted;row(previewCoverWhy,38);
  removeWallpaper.Text="移除壁纸";removeWallpaper.Click+=async(s,e)=>await RemoveChosenWallpaper();row(removeWallpaper,39);
  nowPlaying.Font=new Font(Font.FontFamily,8.5f);nowPlaying.ForeColor=Muted;row(nowPlaying,34);
  banner.Dock=DockStyle.Fill;banner.Margin=new Padding(0,U(14),0,0);var summary=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=new Padding(U(14),U(6),U(14),U(6))};
  summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));summary.RowStyles.Add(new RowStyle(SizeType.Percent,50));summary.RowStyles.Add(new RowStyle(SizeType.Percent,50));banner.Controls.Add(summary);
  status.Dock=DockStyle.Fill;status.Font=new Font(Font.FontFamily,11,FontStyle.Bold);detail.Dock=DockStyle.Fill;detail.AutoEllipsis=true;activeName.Dock=DockStyle.Fill;activeName.TextAlign=ContentAlignment.MiddleRight;activeName.AutoEllipsis=true;
  homeNotice.Dock=DockStyle.Fill;summary.Controls.Add(status,0,0);summary.Controls.Add(activeName,1,0);summary.Controls.Add(detail,0,1);summary.Controls.Add(homeNotice,1,1);root.Controls.Add(banner,0,2);
  // Technical status controls are retained for the diagnostics surface only.
  lease.Visible=false;countdown.Visible=false;
 }
 void LayoutCards(){if(cards.ClientSize.Width<=0)return;int gap=U(16),available=cards.ClientSize.Width-U(20);int columns=HomeLayout.CardColumns(available,uiScale);int width=Math.Min(U(320),Math.Max(U(180),(available-(columns-1)*gap)/columns));foreach(WallpaperCard card in cards.Controls){card.Size=new Size(width,(int)(width*.61)+U(77));card.Margin=new Padding(0,0,gap,U(18));}}
 bool NeedsPreviewConfirmation(){if(selected==null||library.IsBuiltin(selected))return false;return !library.IsPreviewConfirmedHint(selected);}
 void UpdateFriendlyPlayback(){((SoftButton)preview).Busy=playbackPreparation.Busy&&!transition;((SoftButton)desktop).Busy=State=="starting"||State=="engine-started";((SoftButton)stop).Busy=transition;RefreshRuntimeNotice(true);if(activeRunRequest!=null)activeRunName=WallpaperNames.ActiveName(library,Common.S(activeRunRequest,"selectedCatalogId"),activeRunName);bool playing=PlayerRole=="alive";bool isDesktop=activeRunRequest!=null&&Common.S(activeRunRequest,"mode")=="desktop";stop.Text=playbackPreparation.Busy?(playbackPreparation.UserCancelled?"正在取消启动…":"取消启动"):transition?"正在停止…":(CurrentRun!=null&&!Clean&&!playing)?"停止并恢复":playing?(isDesktop?"恢复原桌面":"关闭预览"):"停止播放";activeName.Text=playing?activeRunName:"";nowPlaying.Text=playing?"正在使用："+activeRunName+" · "+(isDesktop?"桌面":"预览"):"尚未播放";if(State=="mounted")status.Text="桌面壁纸运行中";else if(State=="preview")status.Text="预览中";else if(State=="exited")status.Text="已停止";foreach(WallpaperCard c in cards.Controls){bool active=playing&&activeRunRequest!=null&&Common.S(activeRunRequest,"selectedCatalogId")==c.Item.Id;if(c.Active!=active){c.Active=active;c.Invalidate();}}}
}
