#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class VisualStabilityRegression
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly List<object> Records = new List<object>();
    static readonly List<object> Checks = new List<object>();
    static string report;
    static string mode;
    static object Field(object owner, string name) { return owner.GetType().GetField(name, Flags).GetValue(owner); }
    static object Call(object owner, string name, params object[] args)
    {
        return owner.GetType().GetMethods(Flags).Single(method => method.Name == name && method.GetParameters().Length == args.Length).Invoke(owner, args);
    }
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Check(string name, Action action)
    {
        try { action(); Checks.Add(new { name = name, passed = true }); }
        catch (Exception error) { Checks.Add(new { name = name, passed = false, error = error.Message }); }
    }
    static void Layout(Control root) { root.PerformLayout(); foreach (Control child in root.Controls) Layout(child); }
    static Rectangle Bounds(Control child, Control root)
    {
        var bounds = child.Bounds;
        for (Control parent = child.Parent; parent != null && parent != root; parent = parent.Parent) bounds.Offset(parent.Left, parent.Top);
        return bounds;
    }
    static Rectangle[] Geometry(ControlWindow owner)
    {
        Layout(owner);
        return new[] { "side", "selectedImage", "selectedName", "selectedNote", "preview", "desktop", "stop", "renameWallpaper", "wallpaperMore" }
            .Select(name => Bounds((Control)Field(owner, name), owner)).ToArray();
    }
    static void Record(ControlWindow owner, string name)
    {
        var image = (PictureBox)Field(owner, "selectedImage");
        Records.Add(new { name = name, geometry = Geometry(owner), image = image.Image == null ? Size.Empty : image.Image.Size, description = image.AccessibleDescription });
    }
    static string Picture(string name, Size size)
    {
        string path = Path.Combine(Common.Root, "tests", name + ".png");
        using (var image = new Bitmap(size.Width, size.Height)) using (var g = Graphics.FromImage(image))
        {
            g.Clear(Color.SteelBlue); g.FillRectangle(Brushes.Orange, 0, 0, Math.Max(1, size.Width / 8), size.Height);
            image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        return path;
    }
    static async Task Exercise()
    {
        using (var owner = new ControlWindow(null, 0, null))
        {
            ((System.Windows.Forms.Timer)Field(owner, "presentation")).Stop();
            ((System.Windows.Forms.Timer)Field(owner, "poll")).Stop();
            await owner.WaitForHomeImages();
            var library = (WallpaperLibrary)Field(owner, "library");
            string[] paths = { Picture("wide", new Size(640, 240)), Picture("portrait", new Size(240, 640)), Picture("square", new Size(360, 360)), null };
            for (int index = 0; index < 4; index++) { library.Doc.Items[index].CoverPath = paths[index]; library.Doc.Items[index].Name = "相同短名称"; }
            foreach (Size window in new[] { new Size(1180, 850), new Size(960, 700) })
            {
                owner.ClientSize = window;
                for (int pass = 0; pass < 2; pass++)
                {
                    Rectangle[] baseline = null;
                    for (int index = 0; index < 4; index++)
                    {
                        var item = library.Doc.Items[index];
                        var clock = Stopwatch.StartNew();
                        Call(owner, "SelectWallpaper", item);
                        string name = window.Width + "-pass" + pass + "-shape" + index;
                        var cleared = Geometry(owner); Record(owner, name + "-cleared");
                        await owner.WaitForHomeImages();
                        var arrived = Geometry(owner); Record(owner, name + "-arrived");
                        if (baseline == null) baseline = cleared;
                        var expected = baseline;
                        Check(name, () => Need(expected.SequenceEqual(cleared) && cleared.SequenceEqual(arrived), "selection, clear and arrived phases must have identical right-side Bounds"));
                        Records.Add(new { name = name + "-timing", elapsedMs = clock.Elapsed.TotalMilliseconds });
                    }
                    var selected = library.Doc.Items[3];
                    var before = Geometry(owner);
                    selected.Name = new string('长', 70);
                    Call(owner, "RefreshSelection");
                    Check(window.Width + "-text-pass" + pass, () => Need(before.SequenceEqual(Geometry(owner)), "long name must use reserved rows without moving actions"));
                    selected.Name = "相同短名称";
                }
            }
            if (mode == "functional") await Functional(owner);
            Records.Add(new { name = "home-image-counters", performance = ((ImportPerformance)Field(owner, "homePerformance")).Snapshot() });
            await owner.StopHomeImages();
        }
    }
    static void Render(Control control, Graphics graphics, Point offset)
    {
        var handle = control.Handle;
        using (var image = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height)))
        {
            control.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            graphics.DrawImageUnscaled(image, offset);
        }
        foreach (Control child in control.Controls) Render(child, graphics, new Point(offset.X + child.Left, offset.Y + child.Top));
    }
    static async Task Functional(ControlWindow owner)
    {
        var library = (WallpaperLibrary)Field(owner, "library");
        foreach (float scale in new[] { 1f, 1.25f, 1.5f })
        {
            typeof(ControlWindow).GetField("uiScale", Flags).SetValue(owner, scale);
            owner.Font = new Font(owner.Font.FontFamily, 9.5f * scale);
            owner.ClientSize = new Size((int)(960 * scale), (int)(700 * scale));
            var left = library.Doc.Items[0]; var right = library.Doc.Items[1];
            left.CoverPath = Picture("late-portrait-" + scale, new Size(240, 640));
            right.CoverPath = Picture("late-wide-" + scale, new Size(640, 240));
            ImportImageCache.TestBeforeDecode = (path, token) => { token.WaitHandle.WaitOne(80); token.ThrowIfCancellationRequested(); };
            Call(owner, "SelectWallpaper", left); var pending = Geometry(owner);
            Need(((ImportImageCache)Field(owner, "homeImages")).PendingCount > 0, "cold decode is actually pending");
            Call(owner, "SelectWallpaper", right); var switched = Geometry(owner);
            await owner.WaitForHomeImages();
            ImportImageCache.TestBeforeDecode = null;
            var final = Geometry(owner);
            var picture = (PictureBox)Field(owner, "selectedImage");
            Check("scaled-late-image-" + scale, () =>
            {
                Need(pending.SequenceEqual(switched) && switched.SequenceEqual(final), "late portrait and final wide share stable geometry");
                Need(picture.Image != null && picture.Image.Width > picture.Image.Height && picture.SizeMode == PictureBoxSizeMode.Zoom, "last selected wide image remains fully fitted");
                var side = (Control)Field(owner, "side");
                foreach (string name in new[] { "preview", "desktop", "stop", "renameWallpaper", "wallpaperMore" })
                    Need(Bounds(side, owner).Contains(Bounds((Control)Field(owner, name), owner)), "action remains inside stable surface");
            });
            string state = owner.State;
            owner.State = "starting"; Call(owner, "RefreshPlaybackActions");
            Check("scaled-status-text-" + scale, () => Need(final.SequenceEqual(Geometry(owner)), "task notice length uses reserved lines"));
            owner.State = state; Call(owner, "RefreshPlaybackActions");

            using (var rename = new RenameWallpaperDialog(new string('中', 80), value => { throw new IOException("owned save failure"); }))
            {
                rename.Font = new Font(owner.Font.FontFamily, 10 * scale);
                rename.ClientSize = new Size((int)(520 * scale), (int)(270 * scale));
                Layout(rename);
                Check("rename-layout-" + scale, () =>
                {
                    var input = rename.Editor.Input;
                    Need(input.Height >= TextRenderer.MeasureText("中文Ag", input.Font, Size.Empty, TextFormatFlags.NoPadding).Height, "input font height fits single line");
                    Need(input.Width >= 240 * scale, "editor has independent usable width");
                    input.SelectAll(); Need(input.SelectedText.Length == 80, "long Chinese name supports normal selection");
                    input.SelectionStart = 40; input.SelectionLength = 0; Need(input.SelectionStart == 40, "cursor placement remains normal");
                    Need(!rename.TrySave() && input.Text.Length == 80 && rename.Validation.Text.Contains("未保存"), "failed save keeps input and inline reason");
                    Need(rename.AcceptButton == rename.SaveButton && rename.CancelButton == rename.CancelButtonControl, "Enter and cancel remain wired");
                });
                using (var bitmap = new Bitmap(rename.Width, rename.Height))
                {
                    using (var graphics = Graphics.FromImage(bitmap)) Render(rename, graphics, Point.Empty);
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(report), "rename-" + scale + ".png"));
                }
                rename.CancelEdit(); Need(rename.IsDisposed, "cancel after failed save closes");
            }
        }
        using (var tabs = new MaterialTabs { Size = new Size(720, 420), TestAnimateHidden = true })
        {
            for (int index = 0; index < 4; index++) tabs.TabPages.Add("设置分组" + index);
            var handle = tabs.Handle;
            tabs.SelectedIndex = 3;
            await Task.Delay(70);
            RectangleF current = tabs.Indicator;
            tabs.SelectedIndex = 1;
            Check("tabs-retarget-immediate-content", () =>
            {
                Need(tabs.SelectedTab == tabs.TabPages[1] && tabs.TabPages[1].Parent.Controls.GetChildIndex(tabs.TabPages[1]) == 0, "page changes immediately");
                Need((RectangleF)Field(tabs, "start") == tabs.Indicator && tabs.Indicator.X >= current.X,
                    "retarget starts at the current elapsed interpolation, without a queued old origin");
            });
            foreach (int index in new[] { 0, 2, 3, 1, 3 }) { tabs.SelectedIndex = index; await Task.Delay(15); }
            await Task.Delay(300);
            Check("tabs-final-animation-target", () => Need(tabs.SelectedIndex == 3 && tabs.Indicator == tabs.TargetIndicator && !tabs.AnimationRunning, "one bounded animation finishes at final page"));
            tabs.SelectKey(Keys.Home); tabs.SelectKey(Keys.Right); tabs.SelectKey(Keys.End);
            Check("tabs-keyboard", () => Need(tabs.SelectedIndex == 3, "Home, arrows and End update pages"));
            object[] keyArgs = { Message.Create(tabs.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Tab };
            Need((bool)typeof(MaterialTabs).GetMethod("ProcessCmdKey", Flags).Invoke(tabs, keyArgs) && tabs.SelectedIndex == 0, "CtrlTab advances from page focus");
            tabs.TestReduceMotion = true; tabs.SelectedIndex = 0;
            Check("tabs-reduced-motion", () => Need(tabs.Indicator == tabs.TargetIndicator && !tabs.AnimationRunning, "reduced motion snaps immediately"));
            tabs.TestReduceMotion = false; tabs.SelectedIndex = 2; tabs.Size = new Size(580, 380);
            Check("tabs-resize-cancels", () => Need(tabs.SelectedIndex == 2 && tabs.Indicator == tabs.TargetIndicator && !tabs.AnimationRunning, "resize cancels outdated coordinates"));
            tabs.Visible = false;
            tabs.SelectedIndex = 1;
            tabs.Visible = true;
            Check("tabs-hidden-reopen", () => Need(tabs.SelectedIndex == 1 && tabs.Indicator == tabs.TargetIndicator && !tabs.AnimationRunning, "visibility refresh uses current page"));
            using (var bitmap = new Bitmap(tabs.Width, tabs.Height))
            {
                using (var graphics = Graphics.FromImage(bitmap)) Render(tabs, graphics, Point.Empty);
                bitmap.Save(Path.Combine(Path.GetDirectoryName(report), "tabs.png"));
            }
            tabs.Dispose(); Need(!tabs.AnimationRunning, "tabs timer is disposed");
        }
        var actual = (MaterialTabs)Field(owner, "settingsTabs");
        Check("actual-settings-four-labels-and-pages", () =>
        {
            Need(actual.TabPages.Select(page => page.Text).SequenceEqual(new[] { "来源与存储", "播放组件", "声音与登录", "详情与诊断" }), "all existing setting pages retained");
            var bar = (Control)Field(actual, "bar");
            for (int index = 0; index < 4; index++)
            {
                actual.SelectedIndex = index;
                Need(bar.Controls[index].AccessibleName == actual.SelectedTab.Text
                    && (bar.Controls[index].AccessibilityObject.State & AccessibleStates.Selected) != 0,
                    "accessible selected label matches immediate content");
            }
            foreach (float scale in new[] { 1f, 1.25f, 1.5f })
            {
                actual.Font = new Font(owner.Font.FontFamily, 9.5f * scale);
                actual.Size = new Size((int)(640 * scale), (int)(480 * scale));
                Layout(actual);
                Need(actual.Indicator == actual.TargetIndicator, "real header resize follows current label");
                foreach (Control button in bar.Controls)
                    Need(button.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width, "real label text fits common scaling");
            }
        });
    }
    [STAThread] static int Main(string[] args)
    {
        if (args[0] == "--local-import") return LocalImport.Worker(args[1]);
        report = args[0]; mode = args[1]; Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        using (var deadline = new System.Threading.Timer(state => Environment.Exit(124), null, 45000, Timeout.Infinite))
        using (var host = new Control()) using (var context = new ApplicationContext())
        {
            typeof(InteractionRegression).GetMethod("Prepare", Flags).Invoke(null, null);
            var handle = host.Handle; int exit = 1;
            host.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await Exercise();
                    bool passed = Checks.All(row => (bool)row.GetType().GetProperty("passed").GetValue(row, null));
                    Common.SaveNew(report, new { passed = passed, checks = Checks, records = Records, scope = "actual SelectWallpaper and async home image submit, own unshown controls and Application.Run; no visible window or input automation" });
                    exit = passed ? 0 : 1;
                }
                catch (Exception error) { Common.SaveNew(report, new { passed = false, error = error.ToString(), checks = Checks, records = Records }); }
                finally { ImportImageCache.TestBeforeDecode = null; context.ExitThread(); }
            }));
            Application.Run(context); return exit;
        }
    }
}
#endif
