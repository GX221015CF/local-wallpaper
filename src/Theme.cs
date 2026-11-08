using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Windows.Forms;
internal static class Theme {
 internal static readonly Color Ink=Color.FromArgb(32,34,39),Muted=Color.FromArgb(116,119,128),Blue=Color.FromArgb(0,103,235),Line=Color.FromArgb(226,228,233),Canvas=Color.FromArgb(246,247,249);
 internal static GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
}
internal sealed class SoftButton:Button {
 bool hover,pressed;internal bool Primary;
 internal SoftButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;BackColor=Color.White;ForeColor=Theme.Ink;}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}protected override void OnMouseDown(MouseEventArgs e){pressed=true;Invalidate();base.OnMouseDown(e);}protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Parent==null?Theme.Canvas:Parent.BackColor);bool main=Primary;Color fill=!Enabled?Color.FromArgb(239,240,243):main?(pressed?Color.FromArgb(0,75,185):hover?Color.FromArgb(0,91,216):Theme.Blue):(pressed?Color.FromArgb(227,231,238):hover?Color.FromArgb(244,246,249):Color.White);var r=new RectangleF(1,1,Width-3,Height-3);using(var path=Theme.Round(r,9)){using(var b=new SolidBrush(fill))g.FillPath(b,path);using(var pen=new Pen(main&&Enabled?fill:Theme.Line))g.DrawPath(pen,path);}TextRenderer.DrawText(g,Text,Font,new Rectangle(8,2,Width-16,Height-4),!Enabled?Color.FromArgb(160,163,171):main?Color.White:ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(6,5,Width-13,Height-11));}
}
internal sealed class StatusLinkLabel:LinkLabel {
 internal StatusLinkLabel(){LinkBehavior=LinkBehavior.NeverUnderline;AutoEllipsis=true;TabStop=true;UseMnemonic=false;}
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Links.Clear();if(!String.IsNullOrEmpty(Text))Links.Add(0,Text.Length);}
 protected override void OnForeColorChanged(EventArgs e){base.OnForeColorChanged(e);LinkColor=ForeColor;ActiveLinkColor=ForeColor;VisitedLinkColor=ForeColor;}
}
internal sealed class RoundedPanel:Panel {
 internal RoundedPanel(){DoubleBuffered=true;BackColor=Color.White;}
 protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?Theme.Canvas:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Theme.Round(new RectangleF(1,1,Width-3,Height-3),12)){using(var b=new SolidBrush(BackColor))e.Graphics.FillPath(b,path);using(var p=new Pen(Theme.Line))e.Graphics.DrawPath(p,path);}}
}
