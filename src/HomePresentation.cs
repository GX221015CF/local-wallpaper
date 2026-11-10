using System;
using System.Drawing;
using System.Windows.Forms;

internal static class HomeTypography {
 internal static int LineHeight(Font font) {
  return TextRenderer.MeasureText("国Ag", font, Size.Empty,
   TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height;
 }
 internal static int WrappedHeight(string text, Font font, int width, int maxLines) {
  int line = LineHeight(font);
  int measured = TextRenderer.MeasureText(String.IsNullOrEmpty(text) ? "国Ag" : text,
   font, new Size(Math.Max(1, width), Int32.MaxValue),
   TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Height;
  return Math.Max(line, Math.Min(line * maxLines, measured));
 }
}

// A bounded single-line surface keeps long status/error text out of adjacent rows.
internal class SummaryLabel : Label {
 protected override void OnPaint(PaintEventArgs e) {
  Rectangle bounds = new Rectangle(Padding.Left, Padding.Top,
   Math.Max(0, ClientSize.Width - Padding.Horizontal),
   Math.Max(0, ClientSize.Height - Padding.Vertical));
  TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
   | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
  if (TextAlign == ContentAlignment.MiddleRight) flags |= TextFormatFlags.Right;
  TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor, flags);
 }
}

internal sealed partial class ControlWindow {
 TableLayoutPanel homeRows, footerRows;
 bool arrangingFooter;
 void LayoutFooter() {
  if (arrangingFooter || homeRows == null || footerRows == null) return;
  arrangingFooter = true;
  try {
   int titleHeight = Math.Max(HomeTypography.LineHeight(status.Font), HomeTypography.LineHeight(activeName.Font)) + U(4);
   int noteHeight = Math.Max(HomeTypography.LineHeight(detail.Font), HomeTypography.LineHeight(homeNotice.Font)) + U(4);
   footerRows.RowStyles[0].Height = titleHeight;
   footerRows.RowStyles[1].Height = noteHeight;
   homeRows.RowStyles[2].Height = banner.Margin.Vertical + footerRows.Padding.Vertical + titleHeight + noteHeight;
  } finally { arrangingFooter = false; }
 }
}
