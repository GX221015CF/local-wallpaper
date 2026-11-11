#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class InteractionRegression
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    static string report;
    static readonly List<object> Checks = new List<object>();
    static readonly List<object> Metrics = new List<object>();
    static object Field(object owner, string name) { return owner.GetType().GetField(name, Flags).GetValue(owner); }
    static object Call(object owner, string name, params object[] values)
    {
        return owner.GetType().GetMethods(Flags).Single(method => method.Name == name && method.GetParameters().Length == values.Length).Invoke(owner, values);
    }
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Check(string name, Action action)
    {
        try { action(); Checks.Add(new { name = name, passed = true }); }
        catch (Exception error) { Checks.Add(new { name = name, passed = false, error = error.Message }); }
    }
    static void Prepare()
    {
        if (File.Exists(Path.Combine(Common.Root, "interaction-fixture.json"))) return;
        string folder = Path.Combine(Common.Root, "tests", "home-resources");
        Directory.CreateDirectory(folder);
        var library = new WallpaperLibrary();
        library.Doc.Items.Clear();
        for (int index = 0; index < 8; index++)
        {
            string source = (string)typeof(MultiWallpaperRegression).GetMethod("Fixture", Flags).Invoke(null, new object[] { folder, "home-" + index, (byte)(40 + index) });
            bool duplicate;
            var item = library.Add(source, out duplicate);
            string picture = Path.Combine(folder, "cover-" + index + ".jpg");
            using (var bitmap = new Bitmap(1920, 1080))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(80 + index, 120, 200));
                bitmap.Save(picture, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            item.CoverPath = picture;
            item.Name = "自有壁纸 " + index;
        }
        library.Save();
        Common.SaveNew(Path.Combine(Common.Root, "interaction-fixture.json"), new { count = 8, image = "1920x1080", owned = true });
    }
    static double Time(Action action)
    {
        var clock = Stopwatch.StartNew();
        action();
        return clock.Elapsed.TotalMilliseconds;
    }
    static async Task WaitHome(ControlWindow owner)
    {
        var method = typeof(ControlWindow).GetMethod("WaitForHomeImages", Flags);
        if (method != null) await (Task)method.Invoke(owner, null);
    }
    static async Task Exercise()
    {
        using (var owner = new ControlWindow(null, 0, null))
        {
            ((System.Windows.Forms.Timer)Field(owner, "presentation")).Stop();
            ((System.Windows.Forms.Timer)Field(owner, "poll")).Stop();
            owner.Bounds = new Rectangle(240, 160, 1000, 720);
            var library = (WallpaperLibrary)Field(owner, "library");
            await WaitHome(owner);
            var selectionTimes = new List<double>();
            var saves = new List<double>();
            var images = new List<double>();
            var identity = new List<double>();
            var layouts = new List<double>();
            for (int index = 0; index < 20; index++)
            {
                var item = library.Doc.Items[index % library.Doc.Items.Count];
                saves.Add(Time(() => library.Select(item)));
                images.Add(Time(() => { using (var image = WallpaperLibrary.LoadCover(item)) Need(image != null, "owned large cover readable"); }));
                identity.Add(Time(() => library.Status(item)));
                layouts.Add(Time(() => Call(owner, "LayoutSide")));
                selectionTimes.Add(Time(() => Call(owner, "SelectWallpaper", item)));
                await Task.Delay(5);
            }
            await WaitHome(owner);
            var cards = (FlowLayoutPanel)Field(owner, "cards");
            var beforeCards = cards.Controls.Cast<Control>().ToArray();
            double firstTick = Time(() => Call(owner, "PresentationTick"));
            int firstReplaced = beforeCards.Count(control => control.IsDisposed);
            beforeCards = cards.Controls.Cast<Control>().ToArray();
            double nextTick = Time(() => Call(owner, "PresentationTick"));
            int nextReplaced = beforeCards.Count(control => control.IsDisposed);
            Metrics.Add(new { stage = "home", selectionMaximumMs = selectionTimes.Max(), selectionTotalMs = selectionTimes.Sum(), saveMaximumMs = saves.Max(), saveTotalMs = saves.Sum(), directCoverMaximumMs = images.Max(), directCoverTotalMs = images.Sum(), identityMaximumMs = identity.Max(), layoutMaximumMs = layouts.Max(), firstPeriodicMs = firstTick, firstPeriodicCardsReplaced = firstReplaced, nextPeriodicMs = nextTick, nextPeriodicCardsReplaced = nextReplaced });

            var dialog = (Form)Activator.CreateInstance(typeof(ControlWindow).GetNestedType("ImportDialog", Flags), Flags, null, new object[] { owner }, null);
            dialog.GetType().GetField("TestSkipInitialScan", Flags).SetValue(dialog, true);
            var handle = dialog.Handle;
            int moves = 0, layoutsCount = 0;
            dialog.LocationChanged += (s, e) => moves++;
            dialog.SizeChanged += (s, e) => moves++;
            dialog.Layout += (s, e) => layoutsCount++;
            typeof(MaterialDialog).GetMethod("OnShown", Flags).Invoke(dialog, new object[] { EventArgs.Empty });
            await Task.Delay(320);
            Metrics.Add(new { stage = "dialog-shown", geometryChanges = moves, layoutEvents = layoutsCount, finalBounds = dialog.Bounds });
            Check("stable-dialog-geometry", () => Need(moves == 0, "shown import must keep its initial Bounds stable"));
            int progressLayouts = 0;
            var layout = (TableLayoutPanel)Field(dialog, "importLayout");
            layout.Layout += (s, e) => progressLayouts++;
            var state = (ImportProgressState)Field(dialog, "progressState");
            for (int index = 0; index < 20; index++)
            {
                state.Checking(index, 20, 61, index % 2 == 0 ? "短名称" : new string('长', 100));
                Call(dialog, "PresentProgress");
            }
            Metrics.Add(new { stage = "progress", layoutEvents = progressLayouts, statusRow = layout.RowStyles[2].Height, trackRow = layout.RowStyles[3].Height });
            dialog.Dispose();

            owner.TestSuppressRenameShow = true;
            Call(owner, "RenameWallpaper", library.Selected);
            var rename = (Form)Field(owner, "renameWindow");
            Metrics.Add(new { stage = "rename", bounds = rename.Bounds, ownerBounds = owner.Bounds });
            Check("rename-near-owner", () => Need(owner.Bounds.Contains(new Point(rename.Left + rename.Width / 2, rename.Top + rename.Height / 2)), "nonmodal rename center must be near owner"));
            Call(rename, "CancelEdit");
            await WaitHome(owner);
            if (argsMode == "functional") await Functional(owner);
        }
    }
    static string argsMode;
    static void Layout(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls) Layout(child);
    }
    static void RenderTree(Control control, Graphics graphics, Point offset)
    {
        var handle = control.Handle;
        using (var part = new Bitmap(Math.Max(1, control.Width), Math.Max(1, control.Height)))
        {
            control.DrawToBitmap(part, new Rectangle(Point.Empty, part.Size));
            graphics.DrawImageUnscaled(part, offset);
        }
        foreach (Control child in control.Controls)
            RenderTree(child, graphics, new Point(offset.X + child.Left, offset.Y + child.Top));
    }
    static async Task Functional(ControlWindow owner)
    {
        var library = (WallpaperLibrary)Field(owner, "library");
        Check("more-toggle-close-order-and-command-target", () =>
        {
            var toggle = new MenuToggleState();
            Need(toggle.Click(false) == MenuToggleIntent.Open, "first click opens");
            Need(toggle.Click(true) == MenuToggleIntent.Close, "visible second click closes");
            toggle.Closing(ToolStripDropDownCloseReason.AppClicked, true);
            Need(toggle.Click(false) == MenuToggleIntent.None, "auto close before button Click cannot reopen");
            Need(toggle.Click(false) == MenuToggleIntent.Open, "subsequent click opens again");
            foreach (var reason in new[] { ToolStripDropDownCloseReason.Keyboard, ToolStripDropDownCloseReason.ItemClicked, ToolStripDropDownCloseReason.AppClicked })
            {
                toggle.Closing(reason, false);
                Need(toggle.Click(false) == MenuToggleIntent.Open, "outside, Esc or command allows normal reopen");
            }
            toggle.Closing(ToolStripDropDownCloseReason.AppClicked, true);
            toggle.AbandonPointerClick();
            Need(toggle.Click(false) == MenuToggleIntent.Open, "aborted pointer click leaves no suppression");
            var menu = (MaterialMenu)Field(owner, "wallpaperMenu");
            Call(menu, "OnOpening", new System.ComponentModel.CancelEventArgs());
            var target = (WallpaperItem)Field(owner, "wallpaperMenuTarget");
            Call(owner, "SelectWallpaper", library.Doc.Items.First(item => item != target));
            Need(Object.ReferenceEquals(Field(owner, "wallpaperMenuTarget"), target), "opened menu retains original resource");
        });
        await WaitHome(owner);
        Check("rename-repeat-placement-cancel-save-esc", () =>
        {
            var item = library.Selected;
            string original = item.Name;
            foreach (Point point in new[] { new Point(60, 70), new Point(680, 210) })
            {
                owner.Location = point;
                Call(owner, "RenameWallpaper", item);
                var dialog = (RenameWallpaperDialog)Field(owner, "renameWindow");
                Rectangle area = Screen.FromControl(owner).WorkingArea;
                Need(area.Contains(dialog.Bounds), "rename remains in owner work area");
                Need(dialog.Bounds == MaterialDialog.OwnerPlacement(owner.Bounds, dialog.Size, area), "actual nonmodal entry explicitly centers");
                dialog.Size = new Size((int)(dialog.Width * 1.25f), (int)(dialog.Height * 1.25f));
                typeof(MaterialDialog).GetMethod("OnLoad", Flags).Invoke(dialog, new object[] { EventArgs.Empty });
                Need(dialog.Bounds == MaterialDialog.OwnerPlacement(owner.Bounds, dialog.Size, area), "final load-time size is centered again before display");
                dialog.Editor.Input.Text = "";
                dialog.CancelEdit();
                Need(item.Name == original, "cancel after invalid input preserves name");
            }
            Call(owner, "RenameWallpaper", item);
            var saved = (RenameWallpaperDialog)Field(owner, "renameWindow");
            saved.Editor.Input.Text = "自有重命名成功";
            Need(saved.TrySave() && item.Name == "自有重命名成功", "actual rename saves");
            Call(owner, "RenameWallpaper", item);
            var reopened = (RenameWallpaperDialog)Field(owner, "renameWindow");
            Need(reopened.Editor.Input.Text == item.Name, "reopen reads last saved name");
            Call(reopened, "ProcessDialogKey", Keys.Escape);
            Need(reopened.IsDisposed, "Esc closes nonmodal editor");
            foreach (float scale in new[] { 1f, 1.25f, 1.5f })
            {
                Rectangle area = new Rectangle(-1200, 0, 1000, 700);
                Rectangle placed = MaterialDialog.OwnerPlacement(new Rectangle(-1500, 500, 900, 600), new Size((int)(480 * scale), (int)(280 * scale)), area);
                Need(area.Contains(placed), "negative monitor coordinates and scaled sizes clamp");
            }
        });
        Check("settings-path-rows-font-layout-copy-and-missing", () =>
        {
            var window = (Form)Field(owner, "settingsWindow");
            foreach (int group in new[] { 0, 1, 0, 2, 3, 0 }) ((MaterialTabs)Field(owner, "settingsTabs")).SelectedIndex = group;
            foreach (string field in new[] { "sourcePathRow", "libraryPathRow", "namesPathRow", "componentPathRow" })
            {
                var row = (SettingsPathRow)Field(owner, field);
                string path = Path.Combine(Common.Root, new string('长', 160));
                row.SetPath(path);
                Need(row.PathBox.ReadOnly && row.PathBox.Text == path && !row.OpenFolder.Enabled, "full missing path remains readable and disabled");
                row.PathBox.SelectAll();
                Need(row.PathBox.SelectedText == path, "full path can be selected for copying");
                foreach (float scale in new[] { 1f, 1.25f, 1.5f })
                {
                    row.Width = (int)(500 * scale);
                    row.Font = new Font(owner.Font.FontFamily, 9.5f * scale);
                    Layout(row);
                    Need(row.PathBox.Width >= 100, "path field remains usable at narrow scaled width");
                    Need(row.PathBox.Right <= row.PathBox.Parent.ClientSize.Width, "path stays inside row");
                    Need(row.OpenFolder.Right <= row.OpenFolder.Parent.ClientSize.Width, "folder button remains inside row");
                    Need(row.Description.Bottom <= row.PathBox.Parent.Top, "description and path never overlap");
                    using (var bitmap = new Bitmap(row.Width, row.Height))
                    {
                        using (var graphics = Graphics.FromImage(bitmap)) RenderTree(row, graphics, Point.Empty);
                        int ink = 0;
                        for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                        {
                            Color pixel = bitmap.GetPixel(x, y);
                            if (pixel.R < 100 && pixel.G < 100 && pixel.B < 100) ink++;
                        }
                        Need(ink > 50, "separate own child rendering includes actual text");
                        bitmap.Save(Path.Combine(Path.GetDirectoryName(report), field + "-" + scale + ".png"));
                    }
                }
                row.SetPath(Common.Root);
                Need(row.OpenFolder.Enabled, "existing location enables folder operation");
                Need(field == "sourcePathRow" ? row.Change != null : row.Change == null, "only formally changeable source offers change");
            }
        });
        await WaitHome(owner);
        var a = library.Doc.Items[0];
        var b = library.Doc.Items[1];
        string red = Path.Combine(Common.Root, "tests", "home-red.png"), blue = Path.Combine(Common.Root, "tests", "home-blue.bmp");
        Action<string, Color> picture = (path, color) =>
        {
            using (var bitmap = new Bitmap(80, 50)) using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(color);
                bitmap.Save(path, path.EndsWith(".bmp") ? System.Drawing.Imaging.ImageFormat.Bmp : System.Drawing.Imaging.ImageFormat.Png);
            }
        };
        picture(red, Color.Red); picture(blue, Color.Blue); a.CoverPath = red; b.CoverPath = blue;
        ImportImageCache.TestBeforeDecode = (path, token) => { if (path == red) { token.WaitHandle.WaitOne(100); token.ThrowIfCancellationRequested(); } };
        try { Call(owner, "SelectWallpaper", a); Call(owner, "SelectWallpaper", b); await WaitHome(owner); }
        finally { ImportImageCache.TestBeforeDecode = null; }
        var view = (PictureBox)Field(owner, "selectedImage");
        Check("home-late-image-bmp-and-same-selection", () =>
        {
            Need(view.Image != null && ((Bitmap)view.Image).GetPixel(1, 1).B == 255, "late red cannot replace selected blue BMP");
            Need(((Label)Field(owner, "selectedName")).Text.Contains(b.Name) && library.Doc.SelectedId == b.Id, "name, bitmap and saved identity agree");
            string hash = Common.Hash(library.FilePath);
            long reads = ((ImportPerformance)Field(owner, "homePerformance")).ImageReads;
            Call(owner, "SelectWallpaper", b);
            Need(Common.Hash(library.FilePath) == hash, "same selection leaves persistent catalog unchanged");
            Need(((ImportPerformance)Field(owner, "homePerformance")).ImageReads == reads, "same selection reuses displayed bitmap without reading again");
        });
        picture(blue, Color.Green);
        Call(owner, "RefreshSelection"); await WaitHome(owner);
        Check("home-cover-change-and-save-failure", () =>
        {
            Need(((Bitmap)view.Image).GetPixel(1, 1).G == Color.Green.G, "changed cover receives a new identity");
            string failure = Path.Combine(Common.Root, "tests", "fail-catalog-save");
            File.WriteAllText(failure, "owned TEST_BUILD failure");
            try { Call(owner, "SelectWallpaper", a); Need(library.Doc.SelectedId == b.Id && Object.ReferenceEquals(Field(owner, "selected"), b), "failed save retains selected B"); }
            finally { File.Delete(failure); }
        });
        Call(owner, "SelectWallpaper", a);
        await owner.StopHomeImages();
        var cache = (ImportImageCache)Field(owner, "homeImages");
        Check("home-close-cancels-and-releases", () => Need(cache.PendingCount == 0 && cache.EntryCount == 0 && view.Image == null, "pending images and cache released"));
    }
    [STAThread] static int Main(string[] args)
    {
        if (args[0] == "--local-import") return LocalImport.Worker(args[1]);
        report = args[0];
        argsMode = args[1];
        Application.EnableVisualStyles();
        Control.CheckForIllegalCrossThreadCalls = true;
        using (var deadline = new System.Threading.Timer(state => Environment.Exit(124), null, 45000, Timeout.Infinite))
        using (var host = new Control())
        using (var context = new ApplicationContext())
        {
            Prepare();
            var handle = host.Handle;
            int exit = 1;
            host.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await Exercise();
                    bool passed = Checks.All(value => (bool)value.GetType().GetProperty("passed").GetValue(value, null));
                    Common.SaveNew(report, new { passed = passed, checks = Checks, metrics = Metrics, scope = "normal own handlers, suppressed Show, timer message loop and owned large images; no visible window or input automation" });
                    exit = passed ? 0 : 1;
                }
                catch (Exception error) { Common.SaveNew(report, new { passed = false, error = error.ToString(), checks = Checks, metrics = Metrics }); }
                finally { context.ExitThread(); }
            }));
            Application.Run(context);
            return exit;
        }
    }
}
#endif
