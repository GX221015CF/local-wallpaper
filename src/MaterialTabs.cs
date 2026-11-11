using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Forms;

internal sealed class MaterialTabs : UserControl
{
    internal sealed class PageCollection : Collection<Panel>
    {
        readonly MaterialTabs owner;
        internal PageCollection(MaterialTabs owner) { this.owner = owner; }
        internal void Add(string text) { Add(new Panel { Text = text }); }
        protected override void InsertItem(int index, Panel item)
        {
            base.InsertItem(index, item);
            owner.AddPage(item, index);
        }
    }
    sealed class LabelButton : Button
    {
        internal bool Chosen;
        sealed class LabelAccessibility : Control.ControlAccessibleObject
        {
            readonly LabelButton button;
            internal LabelAccessibility(LabelButton button) : base(button) { this.button = button; }
            public override AccessibleStates State { get { return base.State | (button.Chosen ? AccessibleStates.Selected : AccessibleStates.None); } }
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new LabelAccessibility(this); }
        internal LabelButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Color.FromArgb(237, 242, 250);
            FlatAppearance.MouseDownBackColor = Color.FromArgb(225, 235, 250);
            BackColor = Theme.Canvas; UseVisualStyleBackColor = false; UseMnemonic = false;
            AccessibleRole = AccessibleRole.PageTab;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            graphics.Clear(Parent == null ? Theme.Canvas : Parent.BackColor);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Round(new RectangleF(2, 3, Width - 4, Height - 6), 12))
            using (var fill = new SolidBrush(BackColor)) graphics.FillPath(fill, path);
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(8, 4, Width - 16, Height - 8), ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(graphics, new Rectangle(6, 6, Width - 12, Height - 12), ForeColor, BackColor);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    }
    sealed class LabelBar : Panel
    {
        internal LabelBar() { DoubleBuffered = true; }
    }
    readonly LabelBar bar = new LabelBar { Dock = DockStyle.Top };
    readonly Panel pages = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Canvas };
    readonly Timer animation = new Timer { Interval = 16 };
    readonly Stopwatch clock = new Stopwatch();
    Color[] start = new Color[0];
    static readonly Color SelectedFill = Color.FromArgb(221, 234, 253);
    int selected = -1;
    bool arranging;
    internal readonly PageCollection TabPages;
    internal bool AnimationRunning { get { return animation.Enabled; } }
#if TEST_BUILD
    internal bool TestAnimateHidden;
    internal bool TestReduceMotion;
#endif
    bool MotionAllowed
    {
        get
        {
#if TEST_BUILD
            if (TestReduceMotion) return false;
            if (TestAnimateHidden) return true;
#endif
            return Visible && IsHandleCreated && SoftButton.MotionEnabled;
        }
    }
    internal MaterialTabs()
    {
        TabPages = new PageCollection(this); BackColor = Theme.Canvas;
        Controls.Add(pages); Controls.Add(bar);
        animation.Tick += (s, e) => Advance();
        bar.Resize += (s, e) => ArrangeTabs();
        AccessibleName = "设置分组";
    }
    internal Panel SelectedTab { get { return selected < 0 ? null : TabPages[selected]; } }
    internal int SelectedIndex
    {
        get { return selected; }
        set
        {
            if (value < 0 || value >= TabPages.Count) throw new ArgumentOutOfRangeException("value");
            if (selected == value) { if (!MotionAllowed) Snap(); return; }
            Advance(); selected = value;
            for (int index = 0; index < TabPages.Count; index++)
            {
                TabPages[index].Visible = index == value;
                var button = (LabelButton)bar.Controls[index];
                button.Chosen = index == value;
                button.ForeColor = index == value ? Theme.Blue : Theme.Muted;
                button.TabStop = index == value;
                button.AccessibleDescription = index == value ? "当前选中" : "切换设置分组";
            }
            SelectedTab.BringToFront();
            start = new Color[bar.Controls.Count];
            for (int index = 0; index < start.Length; index++) start[index] = bar.Controls[index].BackColor;
            if (!MotionAllowed) Snap();
            else { clock.Restart(); animation.Start(); }
        }
    }
    void AddPage(Panel page, int index)
    {
        page.Dock = DockStyle.Fill; page.Margin = Padding.Empty; page.Padding = Padding.Empty;
        page.Visible = false; pages.Controls.Add(page);
        var button = new LabelButton { Text = page.Text, AccessibleName = page.Text, Margin = Padding.Empty, ForeColor = Theme.Muted, TabStop = false };
        button.Click += (s, e) => { SelectedIndex = TabPages.IndexOf(page); button.Focus(); };
        button.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Home || e.KeyCode == Keys.End)
            {
                SelectKey(e.KeyCode); e.Handled = true; e.SuppressKeyPress = true;
            }
        };
        bar.Controls.Add(button); bar.Controls.SetChildIndex(button, index);
        if (selected < 0) SelectedIndex = 0;
        ArrangeTabs();
    }
    internal void SelectKey(Keys key)
    {
        if (TabPages.Count == 0) return;
        int next = key == Keys.Home ? 0 : key == Keys.End ? TabPages.Count - 1
            : (selected + (key == Keys.Left ? -1 : 1) + TabPages.Count) % TabPages.Count;
        SelectedIndex = next; bar.Controls[next].Focus();
    }
    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
        {
            SelectKey((keyData & Keys.Shift) != 0 ? Keys.Left : Keys.Right); return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }
    void ArrangeTabs()
    {
        if (arranging || bar == null) return;
        arranging = true;
        try
        {
            bar.Height = Math.Max(44, Font.Height + 24);
            for (int index = 0; index < bar.Controls.Count; index++)
            {
                int left = index * bar.Width / bar.Controls.Count;
                int right = (index + 1) * bar.Width / bar.Controls.Count;
                bar.Controls[index].SetBounds(left, 0, right - left, bar.Height - 6);
            }
            Snap();
        }
        finally { arranging = false; }
    }
    void Advance()
    {
        if (!animation.Enabled) return;
        double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / 220);
        float ease = (float)(1 - Math.Pow(1 - t, 3));
        for (int index = 0; index < bar.Controls.Count; index++)
        {
            Color from = start[index], to = index == selected ? SelectedFill : Theme.Canvas;
            bar.Controls[index].BackColor = Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * ease),
                (int)Math.Round(from.G + (to.G - from.G) * ease),
                (int)Math.Round(from.B + (to.B - from.B) * ease));
        }
        if (t >= 1 || !MotionAllowed) Snap();
    }
    void Snap()
    {
        animation.Stop();
        for (int index = 0; index < bar.Controls.Count; index++) bar.Controls[index].BackColor = index == selected ? SelectedFill : Theme.Canvas;
    }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); ArrangeTabs(); }
    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); ArrangeTabs(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); ArrangeTabs(); }
    protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
}
