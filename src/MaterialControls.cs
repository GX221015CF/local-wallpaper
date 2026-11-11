using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal class MaterialDialog : Form {
 readonly Icon ownedIcon;
 Form placementOwner;
 internal MaterialDialog(){ownedIcon=AppVisuals.LoadIcon();Icon=ownedIcon;BackColor=Theme.Canvas;Font=new Font("Microsoft YaHei UI",10);ForeColor=Theme.Ink;AutoScaleMode=AutoScaleMode.Dpi;}
 internal static Rectangle OwnerPlacement(Rectangle owner,Size size,Rectangle area){
  int width=Math.Min(size.Width,area.Width),height=Math.Min(size.Height,area.Height);
  int x=Math.Max(area.Left,Math.Min(area.Right-width,owner.Left+(owner.Width-width)/2));
  int y=Math.Max(area.Top,Math.Min(area.Bottom-height,owner.Top+(owner.Height-height)/2));
  return new Rectangle(x,y,width,height);
 }
 internal void PlaceNearOwner(Form owner){
  if(owner==null)return;
  placementOwner=owner;
  Rectangle area=Screen.FromControl(owner).WorkingArea;
  StartPosition=FormStartPosition.Manual;
  MinimumSize=new Size(Math.Min(MinimumSize.Width,area.Width),Math.Min(MinimumSize.Height,area.Height));
  Bounds=OwnerPlacement(owner.Bounds,Size,area);
 }
 protected override void OnLoad(EventArgs e){
  base.OnLoad(e);
  if(placementOwner!=null)PlaceNearOwner(placementOwner);
  else if(StartPosition==FormStartPosition.CenterParent&&Owner!=null)PlaceNearOwner(Owner);
 }
 protected override void Dispose(bool disposing){if(disposing){Icon=null;if(ownedIcon!=null)ownedIcon.Dispose();}base.Dispose(disposing);}
}

internal sealed class BufferedCandidateList : ListView {
 internal BufferedCandidateList(){DoubleBuffered=true;}
}

internal sealed class SpringToggle : CheckBox {
 readonly Timer animation=new Timer{Interval=16};readonly System.Diagnostics.Stopwatch clock=new System.Diagnostics.Stopwatch();
 float position,from,target;
 internal SpringToggle(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);AutoSize=false;Height=34;Width=160;Cursor=Cursors.Hand;animation.Tick+=(s,e)=>{float t=Math.Min(1,clock.ElapsedMilliseconds/380f);float spring=1-(float)(Math.Exp(-7*t)*Math.Cos(t*3*Math.PI));position=from+(target-from)*spring;Invalidate();if(t>=1){position=target;animation.Stop();}};}
 protected override void OnCheckedChanged(EventArgs e){from=position;target=Checked?1:0;clock.Restart();if(IsHandleCreated&&SoftButton.MotionEnabled)animation.Start();else position=target;Invalidate();base.OnCheckedChanged(e);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Canvas:Parent.BackColor);float k=g.DpiX/96;var track=new RectangleF(1,Height/2-10*k,38*k,20*k);using(var p=Theme.Round(track,10*k))using(var b=new SolidBrush(Checked?Theme.Blue:Color.FromArgb(188,197,212)))g.FillPath(b,p);using(var b=new SolidBrush(Color.White))g.FillEllipse(b,4*k+Math.Max(-.08f,Math.Min(1.08f,position))*17*k,Height/2-7*k,14*k,14*k);TextRenderer.DrawText(g,Text,Font,new Rectangle((int)(47*k),0,Math.Max(1,Width-(int)(47*k)),Height),Enabled?Theme.Ink:Theme.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,ClientRectangle);}
 protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
}

internal sealed class WallpaperNameInput : UserControl {
 internal readonly TextBox Input=new TextBox{BorderStyle=BorderStyle.None,AutoSize=false};
 readonly Label caption=new Label{Text="壁纸名称",AutoSize=false,ForeColor=Theme.Muted};
 Rectangle editorBounds;
 int InputLineHeight {get{return Math.Max(Input.PreferredHeight,TextRenderer.MeasureText("中文Ag",Input.Font,Size.Empty,TextFormatFlags.NoPadding).Height+4);}}
 internal int ContentHeight {get{return Font.Height+8+InputLineHeight+24;}}
 internal WallpaperNameInput(){
  BackColor=Theme.Canvas;Input.BackColor=Color.White;Input.AccessibleName="壁纸名称";
  Controls.Add(caption);Controls.Add(Input);Height=ContentHeight;
  Input.GotFocus+=(s,e)=>Invalidate();Input.LostFocus+=(s,e)=>Invalidate();
 }
 protected override void OnLayout(LayoutEventArgs e){
  int captionHeight=Font.Height+8;
  caption.SetBounds(0,0,Width,captionHeight);
  editorBounds=new Rectangle(0,captionHeight,Math.Max(1,Width-1),InputLineHeight+22);
  Input.SetBounds(12,editorBounds.Top+11,Math.Max(1,Width-24),InputLineHeight);
  base.OnLayout(e);
 }
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);PerformLayout();Invalidate();}
 protected override void OnPaint(PaintEventArgs e){
  base.OnPaint(e);using(var fill=new SolidBrush(Color.White))e.Graphics.FillRectangle(fill,editorBounds);
  using(var pen=new Pen(Input.Focused?Theme.Blue:Theme.Line,Input.Focused?2:1))e.Graphics.DrawRectangle(pen,editorBounds);
 }
}

internal sealed class MaterialNotice : SummaryLabel {
 readonly Timer timer=new Timer{Interval=16};readonly System.Diagnostics.Stopwatch clock=new System.Diagnostics.Stopwatch();
 internal MaterialNotice(){AutoEllipsis=true;ForeColor=Theme.Blue;timer.Tick+=(s,e)=>{long ms=clock.ElapsedMilliseconds;Padding=new Padding(SoftButton.MotionEnabled?(int)(8*Math.Max(0,1-ms/240d)):0,0,0,0);if(ms>=3500){timer.Stop();Visible=false;}};}
 internal void Present(string value){Text=value;Visible=true;clock.Restart();timer.Start();}
 protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}

internal sealed class LoadingSkeleton : Control {
 readonly Timer timer=new Timer{Interval=80};int step;
 internal LoadingSkeleton(){Height=48;DoubleBuffered=true;timer.Tick+=(s,e)=>{step=(step+1)%6;Invalidate();};VisibleChanged+=(s,e)=>{if(Visible&&SoftButton.MotionEnabled)timer.Start();else timer.Stop();};}
 protected override void OnPaint(PaintEventArgs e){for(int i=0;i<3;i++)using(var b=new SolidBrush(Color.FromArgb(225+((step+i)%3)*4,232+((step+i)%3)*3,242)))e.Graphics.FillRectangle(b,8+i*(Width/3),8,Math.Max(10,Width/3-18),24);}
 protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
