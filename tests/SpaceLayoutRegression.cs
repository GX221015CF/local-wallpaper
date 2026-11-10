#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class SpaceLayoutRegression {
 static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static readonly List<object> Results=new List<object>();
 static object Field(object owner,string name){return owner.GetType().GetField(name,Flags).GetValue(owner);}
 static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 static void Check(string name,Action run){try{run();Results.Add(new{name=name,passed=true});}catch(Exception error){Results.Add(new{name=name,passed=false,error=error.ToString()});}}
 static void Layout(Control control){control.PerformLayout();foreach(Control child in control.Controls)Layout(child);}
 static int Line(Font font){return TextRenderer.MeasureText("国Ag",font,Size.Empty,TextFormatFlags.SingleLine|TextFormatFlags.NoPadding).Height;}
 static void Call(object target,string method,params object[] args){target.GetType().GetMethod(method,Flags,null,args.Select(value=>value.GetType()).ToArray(),null).Invoke(target,args);}
 static IEnumerable<Control> Descendants(Control root){yield return root;foreach(Control child in root.Controls)foreach(Control nested in Descendants(child))yield return nested;}
 static WallpaperItem Fixture(WallpaperLibrary library,string name,byte seed){
  string folder=Path.Combine(Common.Root,"fixture");Directory.CreateDirectory(folder);
  string path=(string)typeof(MultiWallpaperRegression).GetMethod("Fixture",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{folder,name,seed});
  bool duplicate;return library.Add(path,out duplicate);
 }
 static WallpaperCard Card(ControlWindow window,string id){return ((FlowLayoutPanel)Field(window,"cards")).Controls.Cast<WallpaperCard>().Single(card=>card.Item.Id==id);}
 static Bitmap Paint(Control control,int width,int height){
  control.Size=new Size(width,height);var bitmap=new Bitmap(width,height);
  using(var graphics=Graphics.FromImage(bitmap)){
   graphics.Clear(Color.White);
   control.GetType().GetMethod("OnPaint",Flags).Invoke(control,new object[]{new PaintEventArgs(graphics,new Rectangle(0,0,width,height))});
  }
  return bitmap;
 }
 static void Composite(Control control,Graphics canvas,Point origin,Control skip){
  if(control==skip||control.Width<1||control.Height<1)return;
  using(var layer=new Bitmap(control.Width,control.Height))using(var graphics=Graphics.FromImage(layer)){
   graphics.Clear(Color.White);
   var paint=new PaintEventArgs(graphics,control.ClientRectangle);
   if(!(control is Label))control.GetType().GetMethod("OnPaintBackground",Flags).Invoke(control,new object[]{paint});
   control.GetType().GetMethod("OnPaint",Flags).Invoke(control,new object[]{paint});
   canvas.DrawImageUnscaled(layer,origin);
  }
  foreach(Control child in control.Controls)Composite(child,canvas,new Point(origin.X+child.Left,origin.Y+child.Top),skip);
 }
 [STAThread] static int Main(string[] args){
  Application.EnableVisualStyles();
  Check("actual-footer-two-font-lines-fit-without-overlap",()=>{
   using(var window=new ControlWindow(null,0,null)){
    window.ClientSize=new Size(1180,850);Layout(window);
    foreach(string name in new[]{"status","detail","activeName","homeNotice"}){
     var label=(Label)Field(window,name);
     Need(label.ClientSize.Height-label.Padding.Vertical>=Line(label.Font),name+" actual text requires "+Line(label.Font)+"px but has "+label.ClientSize.Height+"px");
    }
    var title=(Label)Field(window,"status");var detail=(Label)Field(window,"detail");
    Need(title.Bottom+title.Margin.Bottom<=detail.Top-detail.Margin.Top,"title glyph line must finish before description");
   }
  });
  Check("actual-side-cover-grows-with-available-space",()=>{
   using(var window=new ControlWindow(null,0,null)){
    var side=(Panel)Field(window,"side");var rows=(TableLayoutPanel)Field(window,"sideRows");
    typeof(ControlWindow).GetField("uiScale",Flags).SetValue(window,1f);
    window.MinimumSize=Size.Empty;window.ClientSize=new Size(1180,540);Layout(window);
    typeof(ControlWindow).GetMethod("LayoutSide",Flags,null,Type.EmptyTypes,null).Invoke(window,null);float compact=rows.RowStyles[0].Height;
    window.ClientSize=new Size(1180,850);Layout(window);
    typeof(ControlWindow).GetMethod("LayoutSide",Flags,null,Type.EmptyTypes,null).Invoke(window,null);
    Need(rows.RowStyles[0].Height>=150&&rows.RowStyles[0].Height>compact,"ample height must increase cover beyond old 106px cap: compact="+compact+", ample="+rows.RowStyles[0].Height);
   }
  });
  Check("font-measured-layout-at-three-scales-and-common-work-areas",()=>{
   foreach(float scale in new[]{1f,1.25f,1.5f})foreach(Size work in new[]{new Size(1280,720),new Size(1366,768),new Size(1920,1040)}){
    using(var window=new ControlWindow(null,0,null)){
     float actual=(float)Field(window,"uiScale");
     var fonts=Descendants(window).Select(control=>new{control=control,font=new Font(control.Font.FontFamily,control.Font.Size*scale/actual,control.Font.Style)}).ToArray();
     foreach(var entry in fonts)entry.control.Font=entry.font;
     typeof(ControlWindow).GetField("uiScale",Flags).SetValue(window,scale);
     var root=(TableLayoutPanel)window.Controls[0];root.Padding=new Padding((int)(24*scale),(int)(12*scale),(int)(24*scale),(int)(12*scale));root.RowStyles[0].Height=56*scale;
     var body=(TableLayoutPanel)root.GetControlFromPosition(0,1);body.ColumnStyles[1].Width=20*scale;body.ColumnStyles[2].Width=320*scale;
     var side=(Panel)Field(window,"side");side.Padding=new Padding((int)(12*scale));
     var footer=(TableLayoutPanel)((Control)Field(window,"banner")).Controls[0];footer.Padding=new Padding((int)(14*scale),(int)(6*scale),(int)(14*scale),(int)(6*scale));
     ((Label)Field(window,"selectedName")).Text="已选中："+new string('长',80);
     ((Label)Field(window,"selectedNote")).Text=new string('说',100);((Label)Field(window,"nowPlaying")).Text="正在使用："+new string('长',80);
     window.MinimumSize=Size.Empty;window.ClientSize=HomeLayout.Client(work,scale);Layout(window);Call(window,"LayoutFooter");Layout(window);
     foreach(bool confirmation in new[]{false,true}){
      typeof(ControlWindow).GetMethod("LayoutSide",Flags,null,new[]{typeof(bool)},null).Invoke(window,new object[]{confirmation});Layout(window);
      var rows=(TableLayoutPanel)Field(window,"sideRows");
      Need(confirmation?rows.RowStyles[4].Height>0:rows.RowStyles[4].Height==0,"confirmation row budget tracks the tested state");
      Need(!side.AutoScroll&&rows.RowStyles.Cast<RowStyle>().Where(row=>row.SizeType==SizeType.Absolute).Sum(row=>row.Height)<=side.DisplayRectangle.Height+1,"all measured right rows fit "+scale+" "+work+" confirmation="+confirmation);
      foreach(string field in new[]{"preview","desktop","stop","keepPlaying","renameWallpaper","wallpaperMore"}){
       var button=(Control)Field(window,field);Need(button.Height>=Line(button.Font)+4&&button.TabStop,"normal font/click area for "+field);
      }
     }
     foreach(string field in new[]{"status","detail","activeName","homeNotice"}){
      var label=(Label)Field(window,field);Need(label.Height>=Line(label.Font),"footer line fits "+scale+" "+field);
     }
     foreach(var entry in fonts)entry.font.Dispose();
    }
   }
  });
  Check("status-transitions-long-names-errors-notices-stay-in-their-rows",()=>{
   using(var window=new ControlWindow(null,0,null)){
    window.ClientSize=new Size(1180,750);Layout(window);
    var title=(Label)Field(window,"status");var detail=(Label)Field(window,"detail");var active=(Label)Field(window,"activeName");var notice=(MaterialNotice)Field(window,"homeNotice");
    active.Text=new string('长',80);notice.Present("名称已保存");
    foreach(string state in new[]{"idle","starting","engine-started","preview","mounted","exited","cancelled","start-failed"}){
     Call(window,"SetState",state,"状态 "+state,new string('错',200)+"\n第二行完整诊断");Layout(window);
     Need(title.Bottom<=detail.Top&&title.Height>=Line(title.Font)&&detail.Height>=Line(detail.Font),"nonoverlapping actual label allocation for "+state);
     Need(detail.AccessibleDescription.Contains("第二行完整诊断"),"unabridged error remains accessible");
     foreach(Control label in new Control[]{title,detail,active,notice})using(var bitmap=Paint(label,label.Width,label.Height)){
      Need(bitmap.Width>0,"bounded offscreen text render completes");
      if(state=="exited")bitmap.Save(Path.Combine(Path.GetDirectoryName(args[0]),"footer-"+label.GetType().Name+"-"+label.Name+label.GetHashCode()+".png"));
     }
    }
   }
  });
  Check("more-menu-native-lifecycle-styling-grouping-and-bound-target",()=>{
   using(var window=new ControlWindow(null,0,null)){
    var menu=(ContextMenuStrip)Field(window,"wallpaperMenu");
    Need(menu is MaterialMenu&&menu.Renderer is MaterialMenuRenderer&&menu.AutoClose,"mature native dropdown lifecycle kept with owned renderer");
    for(int repeat=0;repeat<3;repeat++){
     typeof(ToolStripDropDown).GetMethod("OnOpening",Flags).Invoke(menu,new object[]{new System.ComponentModel.CancelEventArgs()});
     Need(menu.Items.OfType<ToolStripSeparator>().Count()==2,"cover/details/removal grouped");
     Need(!menu.Items[0].Enabled&&menu.Items[0].ToolTipText.Length>0,"no own preview means capture disabled with reason");
     Need(menu.Items[menu.Items.Count-1].Tag.Equals("danger")&&menu.Items[menu.Items.Count-1].Text.Contains("保留文件"),"danger operation expresses existing file retention");
     Need(Object.ReferenceEquals(Field(window,"wallpaperMenuTarget"),Field(window,"selected")),"menu captures correct selected item");
     menu.PerformLayout();foreach(ToolStripItem item in menu.Items)if(!(item is ToolStripSeparator))Need(item.Height>=Line(menu.Font)+12,"menu text and padding readable");
     menu.Close(ToolStripDropDownCloseReason.Keyboard);
    }
    var itemToDraw=menu.Items[1];itemToDraw.Select();
    using(var bitmap=new Bitmap(300,60))using(var graphics=Graphics.FromImage(bitmap)){
     graphics.Clear(Color.White);typeof(MaterialMenuRenderer).GetMethod("OnRenderMenuItemBackground",Flags).Invoke(menu.Renderer,new object[]{new ToolStripItemRenderEventArgs(graphics,itemToDraw)});
     Need(bitmap.GetPixel(20,10).ToArgb()==MaterialMenuRenderer.Hover.ToArgb(),"native system-blue hover replaced by pale Material hover");bitmap.Save(Path.Combine(Path.GetDirectoryName(args[0]),"menu-hover-owned-offscreen.png"));
    }
   }
  });
  Check("confirmation-colors-use-current-formal-identity-and-never-hash-in-paint",()=>{
   using(var window=new ControlWindow(null,0,null)){
    var library=(WallpaperLibrary)Field(window,"library");var first=Fixture(library,"确认样本",4);var second=Fixture(library,"未确认样本",8);
    Call(window,"RefreshCards");Need(!Card(window,first.Id).PreviewConfirmed&&!Card(window,second.Id).PreviewConfirmed,"registration and structure checks alone are neutral");
    library.ConfirmPreview(first,first.RegisteredSha256,"owned-test-run","isolated-handler-test");Call(window,"RefreshCards");
    var card=Card(window,first.Id);Need(card.PreviewConfirmed&&card.Compatibility.StartsWith("窗口已确认"),"same-resource formal confirmation drives color and text");
    long hits=library.HintHits;
    foreach(bool selected in new[]{false,true})foreach(bool active in new[]{false,true}){
     card.Selected=selected;card.Active=active;card.UpdateAccessibleState();
     using(var bitmap=Paint(card,300,260)){
      Need(bitmap.GetPixel(20,245).ToArgb()==Color.FromArgb(237,247,240).ToArgb(),"confirmation tint remains distinct from selection/running state");
      bitmap.Save(Path.Combine(Path.GetDirectoryName(args[0]),"owned-card-confirmed-"+selected+"-"+active+".png"));
     }
     Need(card.AccessibleDescription.Contains("有效预览确认")&&card.AccessibleDescription.Contains(selected?"当前选中":"未选中"),"color has independent text and selection state");
    }
    Need(library.HintHits==hits,"paint never calls hash or display-identity checker");
    WallpaperNames.Rename(library,first,"持续名称");Call(window,"RefreshCards");Need(Card(window,first.Id).PreviewConfirmed,"rename preserves identity-backed tint");
    first.PreviewConfirmedSha256=new string('f',64);Call(window,"RefreshCards");Need(!Card(window,first.Id).PreviewConfirmed,"other resource confirmation never tints card");
    first.PreviewConfirmedSha256=first.RegisteredSha256;
    string moved=first.SourcePath+".missing";File.Move(first.SourcePath,moved);Call(window,"PresentationTick");Need(!Card(window,first.Id).PreviewConfirmed&&Card(window,first.Id).Compatibility=="文件不存在","missing resource overrides old confirmation");File.Move(moved,first.SourcePath);
    File.WriteAllText(first.SourcePath,"invalid owned replacement");Call(window,"PresentationTick");Need(!Card(window,first.Id).PreviewConfirmed,"changed invalid file returns to neutral");
   }
  });
  Check("owned-offscreen-side-and-footer-composites",()=>{
   using(var window=new ControlWindow(null,0,null))using(var cover=new Bitmap(640,360)){
    using(var graphics=Graphics.FromImage(cover)){
     graphics.Clear(Color.FromArgb(226,239,254));
     using(var brush=new SolidBrush(Color.FromArgb(190,222,207)))graphics.FillEllipse(brush,60,60,270,270);
     TextRenderer.DrawText(graphics,"自有布局示意",window.Font,new Point(350,140),Theme.Ink);
    }
    window.ClientSize=new Size(1180,850);var picture=(PictureBox)Field(window,"selectedImage");picture.Image=cover;
    ((Label)Field(window,"selectedName")).Text="已选中：林间光影 · 自有布局样本";
    ((Label)Field(window,"selectedNote")).Text="可预览或设为桌面";
    Call(window,"SetState","exited","已停止","播放器与worker分别确认已退出，或明确未创建播放器");
    Layout(window);typeof(ControlWindow).GetMethod("LayoutSide",Flags,null,Type.EmptyTypes,null).Invoke(window,null);Layout(window);
    foreach(string field in new[]{"side","banner"}){
     var panel=(Control)Field(window,field);using(var bitmap=new Bitmap(panel.Width,panel.Height)){
      using(var graphics=Graphics.FromImage(bitmap))Composite(panel,graphics,Point.Empty,(Control)Field(window,"confirmPreview"));
      int inkPixels=0;for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){var pixel=bitmap.GetPixel(x,y);if(pixel.R<100&&pixel.G<100&&pixel.B<100)inkPixels++;}
      Need(inkPixels>80,"offscreen composite must include actual content, not only invisible-parent background");
      bitmap.Save(Path.Combine(Path.GetDirectoryName(args[0]),"owned-"+field+"-offscreen.png"));
     }
    }
    picture.Image=null;
   }
  });
  bool passed=Results.All(row=>(bool)row.GetType().GetProperty("passed").GetValue(row,null));
  Common.SaveNew(args[0],new{passed=passed,results=Results,scope="actual unshown ControlWindow layout and GDI font measurement, no UI automation, engine or playback"});
  return passed?0:1;
 }
}
#endif
