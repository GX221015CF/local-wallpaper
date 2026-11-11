using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal enum MenuToggleIntent { None, Open, Close }
internal sealed class MenuToggleState {
 bool dismissedOnTrigger;
 internal void Closing(ToolStripDropDownCloseReason reason,bool triggerHit){dismissedOnTrigger=reason==ToolStripDropDownCloseReason.AppClicked&&triggerHit;}
 internal void AbandonPointerClick(){dismissedOnTrigger=false;}
 internal MenuToggleIntent Click(bool visible){
  if(dismissedOnTrigger){dismissedOnTrigger=false;return MenuToggleIntent.None;}
  return visible?MenuToggleIntent.Close:MenuToggleIntent.Open;
 }
}

internal sealed class MaterialMenu : ContextMenuStrip {
 readonly Font ownedFont;
 internal MaterialMenu() {
  ownedFont = new Font("Microsoft YaHei UI", 10);
  Font = ownedFont;
  BackColor = Color.White;
  ForeColor = Theme.Ink;
  ShowImageMargin = false;
  ShowCheckMargin = false;
  ShowItemToolTips = true;
  Padding = new Padding(6);
  Renderer = new MaterialMenuRenderer();
 }
 internal void Prepare(float scale) {
  Padding = new Padding((int)Math.Round(6 * scale));
  foreach (ToolStripItem item in Items) {
   if (item is ToolStripSeparator) continue;
   item.Padding = new Padding((int)(12 * scale), (int)(8 * scale), (int)(12 * scale), (int)(8 * scale));
   item.AutoSize = true;
   item.AccessibleDescription = item.ToolTipText;
  }
 }
 protected override void Dispose(bool disposing) {
  base.Dispose(disposing);
  if (disposing) ownedFont.Dispose();
 }
}

internal sealed class MaterialMenuRenderer : ToolStripProfessionalRenderer {
 internal static readonly Color Hover = Color.FromArgb(232, 240, 254);
 internal static readonly Color Danger = Color.FromArgb(179, 38, 30);
 protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
  e.Graphics.Clear(Color.White);
 }
 protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) {
  using (var pen = new Pen(Theme.Line))
   e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
 }
 protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
  if (!e.Item.Selected || !e.Item.Enabled) return;
  bool danger = Object.Equals(e.Item.Tag, "danger");
  var bounds = new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2);
  e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
  using (var path = Theme.Round(bounds, 8))
  using (var brush = new SolidBrush(danger ? Color.FromArgb(252, 235, 233) : Hover))
   e.Graphics.FillPath(brush, path);
 }
 protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
  e.TextColor = !e.Item.Enabled ? Theme.Muted : Object.Equals(e.Item.Tag, "danger") ? Danger : Theme.Ink;
  base.OnRenderItemText(e);
 }
 protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
  using (var pen = new Pen(Theme.Line))
   e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2);
 }
}
