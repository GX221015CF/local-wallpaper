using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal class MaterialDialog : Form {
 readonly Icon ownedIcon;
 readonly Timer animation=new Timer{Interval=16};
 readonly System.Diagnostics.Stopwatch clock=new System.Diagnostics.Stopwatch();
 Rectangle finalBounds;
 internal MaterialDialog(){ownedIcon=AppVisuals.LoadIcon();Icon=ownedIcon;BackColor=Theme.Canvas;Font=new Font("Microsoft YaHei UI",10);ForeColor=Theme.Ink;AutoScaleMode=AutoScaleMode.Dpi;}
 protected override void OnShown(EventArgs e){
  base.OnShown(e);if(!SoftButton.MotionEnabled)return;
  finalBounds=Bounds;clock.Restart();
  animation.Tick+=(s,args)=>{double t=Math.Min(1,clock.ElapsedMilliseconds/240d);float scale=(float)(.96+.04*(1-Math.Pow(1-t,3)));Bounds=new Rectangle(finalBounds.X+(int)(finalBounds.Width*(1-scale)/2),finalBounds.Y+(int)(finalBounds.Height*(1-scale)/2),(int)(finalBounds.Width*scale),(int)(finalBounds.Height*scale));if(t>=1){Bounds=finalBounds;animation.Stop();}};
  animation.Start();
 }
 protected override void Dispose(bool disposing){if(disposing){animation.Dispose();Icon=null;if(ownedIcon!=null)ownedIcon.Dispose();}base.Dispose(disposing);}
}

internal sealed class SpringToggle : CheckBox {
 readonly Timer animation=new Timer{Interval=16};readonly System.Diagnostics.Stopwatch clock=new System.Diagnostics.Stopwatch();
 float position,from,target;
 internal SpringToggle(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);AutoSize=false;Height=34;Width=160;Cursor=Cursors.Hand;animation.Tick+=(s,e)=>{float t=Math.Min(1,clock.ElapsedMilliseconds/380f);float spring=1-(float)(Math.Exp(-7*t)*Math.Cos(t*3*Math.PI));position=from+(target-from)*spring;Invalidate();if(t>=1){position=target;animation.Stop();}};}
 protected override void OnCheckedChanged(EventArgs e){from=position;target=Checked?1:0;clock.Restart();if(IsHandleCreated&&SoftButton.MotionEnabled)animation.Start();else position=target;Invalidate();base.OnCheckedChanged(e);}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Canvas:Parent.BackColor);float k=g.DpiX/96;var track=new RectangleF(1,Height/2-10*k,38*k,20*k);using(var p=Theme.Round(track,10*k))using(var b=new SolidBrush(Checked?Theme.Blue:Color.FromArgb(188,197,212)))g.FillPath(b,p);using(var b=new SolidBrush(Color.White))g.FillEllipse(b,4*k+Math.Max(-.08f,Math.Min(1.08f,position))*17*k,Height/2-7*k,14*k,14*k);TextRenderer.DrawText(g,Text,Font,new Rectangle((int)(47*k),0,Math.Max(1,Width-(int)(47*k)),Height),Enabled?Theme.Ink:Theme.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,ClientRectangle);}
 protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
}

internal sealed class FloatingNameInput : UserControl {
 internal readonly TextBox Input=new TextBox{BorderStyle=BorderStyle.None};
 readonly Label caption=new Label();readonly Timer animation=new Timer{Interval=16};readonly System.Diagnostics.Stopwatch clock=new System.Diagnostics.Stopwatch();
 float position=1,start,target=1;
 internal FloatingNameInput(){Height=64;BackColor=Color.White;Padding=new Padding(14);caption.Text="壁纸名称";caption.ForeColor=Theme.Muted;caption.AutoSize=true;Input.Location=new Point(14,32);Input.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top;Input.Width=Width-28;caption.Location=new Point(14,8);Controls.Add(Input);Controls.Add(caption);Input.TextChanged+=(s,e)=>Animate();Input.GotFocus+=(s,e)=>Animate();Input.LostFocus+=(s,e)=>Animate();animation.Tick+=(s,e)=>{float t=Math.Min(1,clock.ElapsedMilliseconds/240f);position=start+(target-start)*t;caption.Top=8+(int)(13*(1-position));Invalidate();if(t>=1)animation.Stop();};Resize+=(s,e)=>Input.Width=Math.Max(20,Width-28);}
 void Animate(){start=position;target=Input.Focused||Input.TextLength>0?1:0;clock.Restart();if(SoftButton.MotionEnabled)animation.Start();else{position=target;caption.Top=8+(int)(13*(1-position));}Invalidate();}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Input.Focused?Theme.Blue:Theme.Line,Input.Focused?2:1))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);}
 protected override void Dispose(bool disposing){if(disposing)animation.Dispose();base.Dispose(disposing);}
}

internal sealed class MaterialNotice : SummaryLabel {
 readonly Timer timer=new Timer{Interval=16};readonly System.Diagnostics.Stopwatch clock=new System.Diagnostics.Stopwatch();
 internal MaterialNotice(){AutoEllipsis=true;ForeColor=Theme.Blue;timer.Tick+=(s,e)=>{long ms=clock.ElapsedMilliseconds;Padding=new Padding(SoftButton.MotionEnabled?(int)(8*Math.Max(0,1-ms/240d)):0,0,0,0);if(ms>=3500){timer.Stop();Visible=false;}};}
 internal void Present(string value){Text=value;Visible=true;clock.Restart();timer.Start();}
 protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}

internal sealed class MaterialTabs : TabControl {
 internal MaterialTabs(){DrawMode=TabDrawMode.OwnerDrawFixed;ItemSize=new Size(130,42);SizeMode=TabSizeMode.Fixed;Padding=new Point(12,8);}
 protected override void OnSelectedIndexChanged(EventArgs e){base.OnSelectedIndexChanged(e);Invalidate();}
 protected override void OnResize(EventArgs e){base.OnResize(e);Invalidate();}
 protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);Invalidate();}
 protected override void OnDrawItem(DrawItemEventArgs e){
  var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;using(var background=new SolidBrush(Theme.Canvas))g.FillRectangle(background,e.Bounds);
  bool chosen=e.Index==SelectedIndex;
  if(chosen){var bounds=new RectangleF(e.Bounds.X+2,e.Bounds.Y+3,e.Bounds.Width-4,e.Bounds.Height-6);
   using(var path=Theme.Round(bounds,17))using(var brush=new SolidBrush(Color.FromArgb(211,227,253)))g.FillPath(brush,path);
   using(var pen=new Pen(Theme.Blue,2))g.DrawLine(pen,e.Bounds.Left+24,e.Bounds.Bottom-3,e.Bounds.Right-24,e.Bounds.Bottom-3);
  }
  TextRenderer.DrawText(g,TabPages[e.Index].Text,Font,e.Bounds,chosen?Theme.Blue:Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
 }
}
internal sealed class LoadingSkeleton : Control {
 readonly Timer timer=new Timer{Interval=80};int step;
 internal LoadingSkeleton(){Height=48;DoubleBuffered=true;timer.Tick+=(s,e)=>{step=(step+1)%6;Invalidate();};VisibleChanged+=(s,e)=>{if(Visible&&SoftButton.MotionEnabled)timer.Start();else timer.Stop();};}
 protected override void OnPaint(PaintEventArgs e){for(int i=0;i<3;i++)using(var b=new SolidBrush(Color.FromArgb(225+((step+i)%3)*4,232+((step+i)%3)*3,242)))e.Graphics.FillRectangle(b,8+i*(Width/3),8,Math.Max(10,Width/3-18),24);}
 protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
