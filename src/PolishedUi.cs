using System;using System.IO;using System.Drawing;using System.Windows.Forms;
internal static class HomeLayout {
 internal static Size Client(Size available,float scale){return new Size(Math.Max(640,Math.Min((int)(1180*scale),available.Width-32)),Math.Max(480,Math.Min((int)(850*scale),available.Height-40)));}
 internal static int CardColumns(int width,float scale){return Math.Max(1,(width+(int)(16*scale))/(int)(230*scale));}
}
internal sealed partial class ControlWindow {
 readonly AppSettings settings=AppSettings.Load();float uiScale=1;Panel side;TableLayoutPanel sideRows;readonly Label nowPlaying=new Label();
 readonly SoftButton wallpaperMore=new SoftButton();
 readonly MaterialMenu wallpaperMenu=new MaterialMenu();
 WallpaperItem wallpaperMenuTarget;
 readonly MenuToggleState moreToggle=new MenuToggleState();
 bool arrangingSide;
 void LayoutSide(){LayoutSide(confirmPreview.Visible);}
 void LayoutSide(bool confirmationVisible) {
  if (arrangingSide || sideRows == null || sideRows.RowStyles.Count < 9 || side == null) return;
  arrangingSide = true;
  try {
  int width = Math.Max(1, side.DisplayRectangle.Width);
  int available = Math.Max(0, side.Parent == null ? side.DisplayRectangle.Height : side.Parent.ClientSize.Height - side.Padding.Vertical);
  int buttonHeight = Math.Max(U(40), HomeTypography.LineHeight(preview.Font) + U(12));
  int nameHeight = HomeTypography.WrappedHeight(selectedName.Text, selectedName.Font, width, 2) + U(4);
  int noteHeight = HomeTypography.WrappedHeight(selectedNote.Text, selectedNote.Font, width, 2) + U(4);
  int playingHeight = HomeTypography.WrappedHeight(nowPlaying.Text, nowPlaying.Font, width, 2) + U(4);
  int[] heights = { 0, nameHeight, noteHeight, buttonHeight, confirmationVisible ? buttonHeight : 0,
   buttonHeight, buttonHeight, playingHeight };
  int required = 0, visibleRows = 0;
  foreach (int height in heights) { required += height; if (height > 0) visibleRows++; }
  // At short heights use a one-line note before sacrificing the primary controls.
  if (required + visibleRows * U(4) > available) {
   required -= heights[2] - HomeTypography.LineHeight(selectedNote.Font) - U(4);
   heights[2] = HomeTypography.LineHeight(selectedNote.Font) + U(4);
   required -= heights[7] - HomeTypography.LineHeight(nowPlaying.Font) - U(4);
   heights[7] = HomeTypography.LineHeight(nowPlaying.Font) + U(4);
  }
  double ratio = selectedImage.Image == null ? 16d / 9 : (double)selectedImage.Image.Width / selectedImage.Image.Height;
  int desiredImage = Math.Min(U(260), (int)Math.Round(width / ratio));
  // Cap the surface at its comfortable content height instead of stretching an empty white tail.
  available = Math.Min(available, required + desiredImage + (visibleRows + 1) * U(20));
  side.Height = available + side.Padding.Vertical;
  side.Margin = new Padding(0, Math.Max(0, (side.Parent.ClientSize.Height - side.Height) / 2), 0, 0);
  int imageHeight = Math.Max(0, Math.Min(desiredImage, available - required - (visibleRows + 1) * U(4)));
  if (imageHeight < U(48)) imageHeight = 0;
  heights[0] = imageHeight;
  if (imageHeight > 0) visibleRows++;
  int gap = Math.Max(0, Math.Min(U(20), (available - required - imageHeight) / Math.Max(1, visibleRows)));
  sideRows.SuspendLayout();
  for (int i = 0; i < heights.Length; i++) {
   Control control = sideRows.GetControlFromPosition(0, i);
   int margin = heights[i] > 0 ? gap : 0;
   sideRows.RowStyles[i].Height = heights[i] + margin;
   control.Margin = new Padding(0, margin / 2, 0, margin - margin / 2);
  }
  selectedImage.Visible = imageHeight > 0;
  sideRows.ResumeLayout();
  } finally { arrangingSide = false; }
 }
 int U(float x){return (int)Math.Round(x*uiScale);}
 void BuildPolishedUi(){
  using(var g=CreateGraphics())uiScale=g.DpiX/96f;
  AutoScaleMode=AutoScaleMode.None;Font=new Font("Microsoft YaHei UI",9.5f);ForeColor=Theme.Ink;Ink=Theme.Ink;Muted=Theme.Muted;Blue=Theme.Blue;
  Text="本地壁纸 · R40";Name="LocalWallpaperGallery";var area=Screen.PrimaryScreen.WorkingArea;ClientSize=HomeLayout.Client(area.Size,uiScale);
  MinimumSize=new Size(Math.Min(U(900),area.Width-24),Math.Min(U(620),area.Height-32));StartPosition=FormStartPosition.CenterScreen;BackColor=Theme.Canvas;
  var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(U(24),U(12),U(24),U(12)),ColumnCount=1,RowCount=3};
  homeRows=root;
  root.RowStyles.Add(new RowStyle(SizeType.Absolute,U(56)));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,U(56)));Controls.Add(root);
  var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=5,Margin=Padding.Empty};
  header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));foreach(int width in new[]{188,118,92,88})header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,U(width)));
  var title=new Label{Text="本地壁纸",Font=new Font(Font.FontFamily,23,FontStyle.Bold),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft};header.Controls.Add(title,0,0);
  var import=new SoftButton{Text="导入已下载壁纸",Name="ImportLenovoLocal"};var add=new SoftButton{Text="添加文件",Name="AddWallpaper"};
  openSettings.Text="设置";openSettings.Name="OpenSettings";openSettings.AccessibleName="设置";openSettings.Click+=(s,e)=>ShowSettings();
  import.Click+=(s,e)=>OpenLocalImport();add.Click+=(s,e)=>AddLocal();Configure(quit,"彻底退出",88);
  var topButtons=new Button[]{import,add,openSettings,quit};for(int i=0;i<topButtons.Length;i++){var button=topButtons[i];button.Dock=DockStyle.Fill;button.Margin=new Padding(U(5),U(8),U(3),U(8));header.Controls.Add(button,i+1,0);}
  root.Controls.Add(header,0,0);
  var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=Padding.Empty};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,U(20)));body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,U(320)));root.Controls.Add(body,0,1);
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
  side=new RoundedPanel{Dock=DockStyle.Top,Padding=new Padding(U(12)),AutoScroll=false,Margin=Padding.Empty};body.Controls.Add(side,2,0);
  sideRows=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=false,ColumnCount=1,Margin=Padding.Empty};side.Controls.Add(sideRows);
  Action<Control,int> row=(control,height)=>{int index=sideRows.RowCount++;sideRows.RowStyles.Add(new RowStyle(SizeType.Absolute,U(height)));control.Dock=DockStyle.Fill;control.Margin=new Padding(0,U(3),0,U(3));sideRows.Controls.Add(control,0,index);};
  Func<Control,Control,TableLayoutPanel> pair=(first,second)=>{
   var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
   first.Dock=second.Dock=DockStyle.Fill;first.Margin=new Padding(0,0,U(4),0);second.Margin=new Padding(U(4),0,0,0);panel.Controls.Add(first,0,0);panel.Controls.Add(second,1,0);return panel;
  };
  sideRows.RowCount=0;
  selectedImage.SizeMode=PictureBoxSizeMode.Zoom;selectedImage.BackColor=Color.White;row(selectedImage,106);
  selectedName.Font=new Font(Font.FontFamily,12,FontStyle.Bold);row(selectedName,40);selectedNote.ForeColor=Muted;selectedNote.AutoEllipsis=true;selectedNote.UseMnemonic=false;row(selectedNote,38);
  Configure(preview,"预览",120);preview.Name="PreviewWallpaper";Configure(desktop,"设为桌面",120);desktop.Name="ApplyWallpaperToDesktop";((SoftButton)desktop).Primary=true;row(pair(preview,desktop),44);
  Configure(confirmPreview,"画面正确，完成确认",240);confirmPreview.Click+=(s,e)=>ConfirmCurrentPreview("human-button");row(confirmPreview,36);
  Configure(stop,"停止播放",120);stop.Name="StopAndRestoreWallpaper";
  keepPlaying.Text="隐藏界面";keepPlaying.AccessibleName="关闭界面，继续播放";keepPlaying.Name="HideAndKeepPlaying";keepPlaying.Click+=async(s,e)=>await HideAndKeepPlaying();row(pair(stop,keepPlaying),44);
  renameWallpaper.Text="重命名";renameWallpaper.Name="RenameWallpaper";renameWallpaper.Click+=(s,e)=>RenameWallpaper(selected);
  wallpaperMore.Text="更多";wallpaperMore.AccessibleName="更多壁纸操作";
  usePreviewCover.Text="用当前画面作封面";usePreviewCover.Click+=async(s,e)=>await SavePreviewCover();
  removeWallpaper.Text="移除壁纸";removeWallpaper.Click+=async(s,e)=>await RemoveChosenWallpaper();
  var coverAction=wallpaperMenu.Items.Add("用当前预览画面作封面");coverAction.Name="CapturePreviewCover";coverAction.Click+=async(s,e)=>await SavePreviewCover();
  var chooseCover=wallpaperMenu.Items.Add("选择封面图片");chooseCover.Name="ChooseWallpaperCover";chooseCover.Click+=(s,e)=>ChooseCover(wallpaperMenuTarget);
  wallpaperMenu.Items.Add(new ToolStripSeparator());
  var detailsAction=wallpaperMenu.Items.Add("查看所选详情");detailsAction.Name="InspectWallpaper";detailsAction.Click+=(s,e)=>{if(wallpaperMenuTarget==selected)ShowDetails();};
  wallpaperMenu.Items.Add(new ToolStripSeparator());
  var removeAction=wallpaperMenu.Items.Add("移除壁纸（保留文件）");removeAction.Name="RemoveWallpaper";removeAction.Tag="danger";
  removeAction.Click+=async(s,e)=>{if(wallpaperMenuTarget==selected)await RemoveChosenWallpaper();};
  wallpaperMenu.Opening+=(s,e)=>{
   wallpaperMenuTarget=selected;UpdatePreviewCoverButton();UpdateRemoveButton();
   coverAction.Enabled=usePreviewCover.Enabled;coverAction.ToolTipText=previewCoverWhy.Text;
   coverAction.Text=coverAction.Enabled?"用当前预览画面作封面":"预览截图暂不可用";
   removeAction.Enabled=removeWallpaper.Enabled;removeAction.ToolTipText=removeAction.Enabled?"仅移除登记，保留原文件和副本":"当前任务未结束或尚未选择壁纸";
   chooseCover.Enabled=detailsAction.Enabled=selected!=null;
   chooseCover.ToolTipText=selected==null?"请先选择壁纸":"为打开菜单时所选壁纸设置图片";
   wallpaperMenu.Prepare(uiScale);
  };
  wallpaperMenu.Closed+=(s,e)=>{if(!closing&&!IsDisposed&&ContainsFocus)wallpaperMore.Focus();};
  wallpaperMenu.Closing+=(s,e)=>moreToggle.Closing(e.CloseReason,wallpaperMore.RectangleToScreen(wallpaperMore.ClientRectangle).Contains(Cursor.Position));
  wallpaperMore.MouseLeave+=(s,e)=>moreToggle.AbandonPointerClick();
  wallpaperMore.MouseUp+=(s,e)=>{if(!wallpaperMore.ClientRectangle.Contains(e.Location))moreToggle.AbandonPointerClick();};
  wallpaperMore.Click+=(s,e)=>{
   var intent=moreToggle.Click(wallpaperMenu.Visible);
   if(intent==MenuToggleIntent.Close)wallpaperMenu.Close();
   else if(intent==MenuToggleIntent.Open)wallpaperMenu.Show(wallpaperMore,new Point(0,wallpaperMore.Height));
  };row(pair(renameWallpaper,wallpaperMore),38);
  nowPlaying.Font=new Font(Font.FontFamily,9);nowPlaying.ForeColor=Muted;nowPlaying.AutoEllipsis=true;row(nowPlaying,26);
  sideRows.RowStyles.Add(new RowStyle(SizeType.Percent,100));sideRows.RowCount++;
  side.Resize+=(s,e)=>LayoutSide();body.Resize+=(s,e)=>LayoutSide();sideRows.Resize+=(s,e)=>LayoutSide();Disposed+=(s,e)=>wallpaperMenu.Dispose();LayoutSide();
  banner.Dock=DockStyle.Fill;banner.Margin=new Padding(0,U(10),0,0);var summary=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Padding=new Padding(U(14),U(6),U(14),U(6))};
  summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));summary.RowStyles.Add(new RowStyle(SizeType.Absolute,1));summary.RowStyles.Add(new RowStyle(SizeType.Absolute,1));banner.Controls.Add(summary);footerRows=summary;
  status.Dock=DockStyle.Fill;status.Font=new Font(Font.FontFamily,11,FontStyle.Bold);detail.Dock=DockStyle.Fill;detail.AutoEllipsis=true;activeName.Dock=DockStyle.Fill;activeName.TextAlign=ContentAlignment.MiddleRight;activeName.AutoEllipsis=true;
  homeNotice.Dock=DockStyle.Fill;summary.Controls.Add(status,0,0);summary.Controls.Add(activeName,1,0);summary.Controls.Add(detail,0,1);summary.Controls.Add(homeNotice,1,1);root.Controls.Add(banner,0,2);
  foreach(Control label in new Control[]{status,detail,activeName,homeNotice}){label.Margin=Padding.Empty;label.Padding=Padding.Empty;label.FontChanged+=(s,e)=>LayoutFooter();}
  status.AutoEllipsis=true;status.TextAlign=detail.TextAlign=ContentAlignment.MiddleLeft;
  root.Resize+=(s,e)=>LayoutFooter();LayoutFooter();
  // Technical status controls are retained for the diagnostics surface only.
  lease.Visible=false;countdown.Visible=false;
 }
 void LayoutCards(){if(cards.ClientSize.Width<=0)return;int gap=U(16),available=cards.ClientSize.Width-U(20);int columns=HomeLayout.CardColumns(available,uiScale);int width=Math.Min(U(320),Math.Max(U(180),(available-(columns-1)*gap)/columns));foreach(WallpaperCard card in cards.Controls){card.Size=new Size(width,(int)(width*.61)+U(77));card.Margin=new Padding(0,0,gap,U(18));}}
 bool NeedsPreviewConfirmation(){if(selected==null||library.IsBuiltin(selected))return false;return !library.IsPreviewConfirmedHint(selected);}
 void UpdateFriendlyPlayback(){((SoftButton)preview).Busy=playbackPreparation.Busy&&!transition;((SoftButton)desktop).Busy=State=="starting"||State=="engine-started";((SoftButton)stop).Busy=transition;LayoutSide();RefreshRuntimeNotice(true);if(activeRunRequest!=null)activeRunName=WallpaperNames.ActiveName(library,Common.S(activeRunRequest,"selectedCatalogId"),activeRunName);bool playing=PlayerRole=="alive";bool isDesktop=activeRunRequest!=null&&Common.S(activeRunRequest,"mode")=="desktop";stop.Text=playbackPreparation.Busy?(playbackPreparation.UserCancelled?"正在取消启动…":"取消启动"):transition?"正在停止…":(CurrentRun!=null&&!Clean&&!playing)?"停止并恢复":playing?(isDesktop?"恢复原桌面":"关闭预览"):"停止播放";activeName.Text=playing?activeRunName:"";nowPlaying.Text=playing?"正在使用："+activeRunName+" · "+(isDesktop?"桌面":"预览"):"尚未播放";if(State=="mounted")status.Text="桌面壁纸运行中";else if(State=="preview")status.Text="预览中";else if(State=="exited")status.Text="已停止";foreach(WallpaperCard c in cards.Controls){bool active=playing&&activeRunRequest!=null&&Common.S(activeRunRequest,"selectedCatalogId")==c.Item.Id;if(c.Active!=active){c.Active=active;c.UpdateAccessibleState();c.Invalidate();}}}
}
