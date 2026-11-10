using System;using System.Drawing;using System.Drawing.Drawing2D;using System.Windows.Forms;
internal static class Theme {
 internal static readonly Color Ink=Color.FromArgb(31,31,31),Muted=Color.FromArgb(86,94,110),Blue=Color.FromArgb(11,87,208),Line=Color.FromArgb(218,225,238),Canvas=Color.FromArgb(247,249,252);
 internal static GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
}
internal sealed class SoftButton : Button {
    bool hover, pressed, busy;
    internal bool Primary;
    readonly Timer animation = new Timer { Interval = 16 };
    readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    long pressAt, rippleAt = -1000;
    Point rippleOrigin;
    float pressFrom, pressTo;
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint parameter, out bool value, uint flags);
    internal static bool MotionEnabled { get { bool enabled; return SystemParametersInfo(0x1042, 0, out enabled, 0) && enabled && !SystemInformation.HighContrast; } }
    internal bool Busy {
        get { return busy; }
        set { if (busy == value) return; busy = value; Animate(); Invalidate(); }
    }
    internal SoftButton() {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
        BackColor = Color.White; ForeColor = Theme.Ink;
        animation.Tick += (s, e) => {
            Invalidate();
            if ((!busy || !MotionEnabled) && clock.ElapsedMilliseconds - rippleAt >= 480 && clock.ElapsedMilliseconds - pressAt >= 150) animation.Stop();
        };
    }
    float PressAmount() {
        double t = MotionEnabled ? Math.Min(1, (clock.ElapsedMilliseconds - pressAt) / 150d) : 1;
        return pressFrom + (pressTo - pressFrom) * (float)(1 - Math.Pow(1 - t, 3));
    }
    void SetPressed(bool value) {
        pressed = value; pressFrom = PressAmount(); pressTo = value ? 1 : 0; pressAt = clock.ElapsedMilliseconds;
        Animate(); Invalidate();
    }
    void Animate() { if (MotionEnabled) animation.Start(); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; SetPressed(false); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { rippleOrigin = e.Location; SetPressed(true); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { SetPressed(false); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { rippleOrigin = new Point(Width / 2, Height / 2); SetPressed(true); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) SetPressed(false); base.OnKeyUp(e); }
    protected override void OnClick(EventArgs e) {
        if (rippleOrigin == Point.Empty) rippleOrigin = new Point(Width / 2, Height / 2);
        rippleAt = clock.ElapsedMilliseconds; Animate(); Invalidate(); base.OnClick(e);
    }
    protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) SetPressed(false); base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { SetPressed(false); base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e) {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent == null ? Theme.Canvas : Parent.BackColor);
        Color fill = !Enabled ? Color.FromArgb(239,240,243) : Primary ? (pressed ? Color.FromArgb(0,75,185) : hover ? Color.FromArgb(0,91,216) : Theme.Blue)
            : pressed ? Color.FromArgb(227,231,238) : hover ? Color.FromArgb(232,240,254) : Color.White;
        float insetX = (Width - 3) * .02f * PressAmount(), insetY = (Height - 3) * .02f * PressAmount();
        var bounds = new RectangleF(1 + insetX, 1 + insetY, Width - 3 - insetX * 2, Height - 3 - insetY * 2);
        using (var path = Theme.Round(bounds, 19)) {
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            using (var pen = new Pen(Primary && Enabled ? fill : Theme.Line)) g.DrawPath(pen, path);
            double age = (clock.ElapsedMilliseconds - rippleAt) / 480d;
            if (Enabled && MotionEnabled && age >= 0 && age < 1) {
                var saved = g.Save(); g.SetClip(path);
                float radius = (float)(Math.Max(Width, Height) * 1.2 * age);
                using (var brush = new SolidBrush(Color.FromArgb((int)(36 * (1 - age)), Primary ? Color.White : Theme.Blue)))
                    g.FillEllipse(brush, rippleOrigin.X - radius, rippleOrigin.Y - radius, radius * 2, radius * 2);
                g.Restore(saved);
            }
        }
        Color ink = !Enabled ? Color.FromArgb(160,163,171) : Primary ? Color.White : ForeColor;
        var textBounds = new Rectangle(8 + (busy ? 17 : 0), 2, Width - 16 - (busy ? 17 : 0), Height - 4);
        TextRenderer.DrawText(g, Text, Font, textBounds, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        if (busy) using (var pen = new Pen(ink, 2)) {
            float angle = MotionEnabled ? (clock.ElapsedMilliseconds % 800) * 360f / 800 : -90;
            g.DrawArc(pen, 9, Height / 2 - 5, 10, 10, angle, 270);
        }
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(6,5,Width-13,Height-11));
    }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
internal sealed class StatusLinkLabel:LinkLabel {
 internal StatusLinkLabel(){LinkBehavior=LinkBehavior.NeverUnderline;AutoEllipsis=true;TabStop=true;UseMnemonic=false;}
 protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Links.Clear();if(!String.IsNullOrEmpty(Text))Links.Add(0,Text.Length);}
 protected override void OnForeColorChanged(EventArgs e){base.OnForeColorChanged(e);LinkColor=ForeColor;ActiveLinkColor=ForeColor;VisitedLinkColor=ForeColor;}
}
internal sealed class RoundedPanel:Panel {
 internal RoundedPanel(){DoubleBuffered=true;BackColor=Color.White;}
 protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?Theme.Canvas:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=Theme.Round(new RectangleF(1,1,Width-3,Height-3),20)){using(var b=new SolidBrush(BackColor))e.Graphics.FillPath(b,path);using(var p=new Pen(Theme.Line))e.Graphics.DrawPath(p,path);}}
}
