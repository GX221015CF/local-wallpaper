using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal enum ImportProgressPhase {
 Idle, Reading, Checking, Copying, Registering, Cancelling, Completed, Cancelled, Failed
}

internal sealed class ImportProgressState {
 internal ImportProgressPhase Phase { get; private set; }
 internal string Text { get; private set; }
 internal long Completed { get; private set; }
 internal long Total { get; private set; }
 internal bool Active {
  get { return Phase == ImportProgressPhase.Reading || Phase == ImportProgressPhase.Checking
   || Phase == ImportProgressPhase.Copying || Phase == ImportProgressPhase.Registering
   || Phase == ImportProgressPhase.Cancelling; }
 }
 internal bool Indeterminate { get { return Active && Total <= 0; } }
 internal double Fraction { get { return Total <= 0 ? 0 : Math.Min(1, Math.Max(0, (double)Completed / Total)); } }

 internal void Reading() {
  Set(ImportProgressPhase.Reading, "正在读取本地候选，核对已有检查缓存…", 0, 0);
 }
 internal void Checking(int completed, int total, int reused, string current) {
  Set(ImportProgressPhase.Checking,
   "已复用 " + reused + " 项 · 本次检查 " + completed + " / " + total + " 次"
   + (String.IsNullOrEmpty(current) ? "" : " · " + current), completed, total);
 }
 internal void Copying(string stage, long bytes, long total) {
  string count = total > 0 ? " · " + (Math.Max(0, bytes) / 1048576d).ToString("F1")
   + " / " + (total / 1048576d).ToString("F1") + " MB" : "";
  Set(ImportProgressPhase.Copying, stage + count, bytes, total);
 }
 internal void Registering() {
  Set(ImportProgressPhase.Registering, "复制核验已完成，正在保存壁纸登记…", 0, 0);
 }
 internal void Cancelling() {
  Set(ImportProgressPhase.Cancelling, "正在取消并等待本次任务结束…", 0, 0);
 }
 internal void Finish(ImportProgressPhase phase, string text) {
  if (phase != ImportProgressPhase.Completed && phase != ImportProgressPhase.Cancelled && phase != ImportProgressPhase.Failed)
   throw new ArgumentException("结束状态无效", "phase");
  Set(phase, text, 0, 0);
 }
 void Set(ImportProgressPhase phase, string text, long completed, long total) {
  Phase = phase;
  Text = text;
  Total = Math.Max(0, total);
  Completed = Math.Max(0, Math.Min(completed, Total));
 }
}

internal sealed class MaterialProgress : Control {
 readonly Timer animation = new Timer { Interval = 32 };
 readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
 ImportProgressState state;
 internal bool TrackVisible { get { return state != null && state.Active; } }
 internal bool AnimationRunning { get { return animation.Enabled; } }
#if TEST_BUILD
 internal bool TestReduceMotion;
#endif
 bool MotionAllowed {
  get {
#if TEST_BUILD
   if (TestReduceMotion) return false;
#endif
   return SoftButton.MotionEnabled;
  }
 }
 internal MaterialProgress() {
  SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
  TabStop = false;
  AccessibleRole = AccessibleRole.ProgressBar;
  animation.Tick += (sender, args) => {
   if (!TrackVisible || !MotionAllowed) animation.Stop();
   Invalidate();
  };
 }
 internal void Present(ImportProgressState value) {
  state = value;
  AccessibleName = value.Text;
  AccessibleDescription = value.Indeterminate ? "总量尚未确定" : value.Active
   ? value.Completed + " / " + value.Total : "任务已结束";
  if (Visible && value.Active && value.Indeterminate && MotionAllowed) animation.Start();
  else animation.Stop();
  Invalidate();
 }
 protected override void OnVisibleChanged(EventArgs e) {
  base.OnVisibleChanged(e);
  if (!Visible) animation.Stop();
  else if (state != null) Present(state);
 }
 protected override void OnPaint(PaintEventArgs e) {
  e.Graphics.Clear(Parent == null ? Theme.Canvas : Parent.BackColor);
  if (!TrackVisible || Width < 8 || Height < 4) return;
  var graphics = e.Graphics;
  graphics.SmoothingMode = SmoothingMode.AntiAlias;
  float thickness = Math.Min(Height - 2, 6 * graphics.DpiX / 96f);
  var bounds = new RectangleF(1, (Height - thickness) / 2, Width - 2, thickness);
  using (var track = Theme.Round(bounds, thickness / 2)) {
   using (var brush = new SolidBrush(Color.FromArgb(223, 232, 249))) graphics.FillPath(brush, track);
   RectangleF fill = bounds;
   if (state.Indeterminate) {
    fill.Width *= .28f;
    float position = MotionAllowed ? (clock.ElapsedMilliseconds % 1600) / 1600f : .5f;
    fill.X += (bounds.Width - fill.Width) * position;
   } else fill.Width *= (float)state.Fraction;
   if (fill.Width < 1) return;
   using (var segment = Theme.Round(fill, thickness / 2))
   using (var brush = new SolidBrush(Theme.Blue)) graphics.FillPath(brush, segment);
  }
 }
 protected override void Dispose(bool disposing) {
  if (disposing) animation.Dispose();
  base.Dispose(disposing);
 }
}
