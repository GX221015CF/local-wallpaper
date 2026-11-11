using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

internal sealed class SettingsPathRow : UserControl
{
    internal readonly TextBox PathBox = new TextBox { ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    internal readonly Label Description = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, UseMnemonic = false };
    internal readonly SoftButton OpenFolder = new SoftButton { Text = "打开文件夹", Dock = DockStyle.Fill };
    internal readonly SoftButton Change;
    string location;
    readonly string explanation;
    Font titleFont;
    Label titleLabel;
    readonly TableLayoutPanel layout;
    bool arranging;

    internal SettingsPathRow(string title, string explanation, Action change)
    {
        this.explanation = explanation;
        BackColor = Theme.Canvas;
        ForeColor = Theme.Ink;
        PathBox.BackColor = Color.White;
        Name = "PathRow-" + title;
        PathBox.AccessibleName = title + "路径";
        OpenFolder.AccessibleName = "打开" + title + "文件夹";
        Height = 122;
        Margin = new Padding(0, 0, 0, 16);
        layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        titleFont = new Font(Font, FontStyle.Bold);
        titleLabel = new Label { Text = title, Dock = DockStyle.Fill, Font = titleFont, UseMnemonic = false };
        layout.Controls.Add(titleLabel, 0, 0);
        layout.Controls.Add(Description, 0, 1);
        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = change == null ? 2 : 3, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        if (change != null) row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        PathBox.Margin = new Padding(0, 5, 8, 0);
        row.Controls.Add(PathBox, 0, 0);
        row.Controls.Add(OpenFolder, 1, 0);
        if (change != null) { Change = new SoftButton { Text = "更改", Dock = DockStyle.Fill }; row.Controls.Add(Change, 2, 0); Change.Click += (s, e) => change(); }
        layout.Controls.Add(row, 0, 2);
        Controls.Add(layout);
        OpenFolder.Click += (s, e) =>
        {
            if (!FolderAvailable(location)) { SetPath(location); return; }
            try { Process.Start(new ProcessStartInfo("explorer.exe", Common.Quote(location)) { UseShellExecute = true }); }
            catch (Exception error) { Description.Text = "文件夹未打开 · " + error.Message; }
        };
    }
    internal static bool FolderAvailable(string path) { return !String.IsNullOrWhiteSpace(path) && Directory.Exists(path); }
    protected override void OnFontChanged(EventArgs e)
    {
        if (titleLabel != null)
        {
            var old = titleFont;
            titleFont = new Font(Font, FontStyle.Bold);
            titleLabel.Font = titleFont;
            old.Dispose();
        }
        base.OnFontChanged(e);
        PerformLayout();
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        if (!arranging && layout != null)
        {
            arranging = true;
            try
            {
                int titleHeight = Font.Height + 8;
                int pathHeight = Math.Max(38, Font.Height + 18);
                int descriptionHeight = TextRenderer.MeasureText(explanation + "  当前位置不存在或尚未确定。", Font,
                    new Size(Math.Max(100, Width), Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + 8;
                layout.RowStyles[0].Height = titleHeight;
                layout.RowStyles[2].Height = pathHeight;
                int needed = titleHeight + pathHeight + descriptionHeight;
                if (Height != needed) Height = needed;
            }
            finally { arranging = false; }
        }
        base.OnLayout(e);
    }
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) titleFont.Dispose(); }
    internal void SetPath(string path)
    {
        location = path;
        if (PathBox.Text != (path ?? "")) PathBox.Text = path ?? "";
        OpenFolder.Enabled = FolderAvailable(path);
        string text = explanation + (OpenFolder.Enabled ? "" : "  当前位置不存在或尚未确定。");
        if (Description.Text != text) Description.Text = text;
    }
}
