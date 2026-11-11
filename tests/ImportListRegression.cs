#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class ImportListRegression
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr first, IntPtr second);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rectangle rectangle);
    [DllImport("user32.dll")] static extern bool GetUpdateRect(IntPtr window, out Rectangle rectangle, bool erase);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NativeItem
    {
        public uint mask;
        public int item, subItem;
        public uint state, stateMask;
        public IntPtr text;
        public int capacity, image;
        public IntPtr parameter;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr window, int message, IntPtr index, ref NativeItem item);
    static readonly List<object> Snapshots = new List<object>();
    static readonly List<object> Results = new List<object>();
    static string report;

    static object Field(object owner, string name) { return owner.GetType().GetField(name, Flags).GetValue(owner); }
    static object Call(object owner, string name, params object[] values)
    {
        return owner.GetType().GetMethods(Flags).Single(method => method.Name == name && method.GetParameters().Length == values.Length).Invoke(owner, values);
    }
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static string Id(ListViewItem row) { return ((ImportCandidate)row.Tag).Id; }
    static string NativeText(ListView list, int index)
    {
        IntPtr buffer = Marshal.AllocHGlobal(2048);
        try
        {
            var item = new NativeItem { text = buffer, capacity = 1024 };
            SendMessage(list.Handle, 0x1073, new IntPtr(index), ref item);
            return Marshal.PtrToStringUni(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    static void Handles(Control control)
    {
        var handle = control.Handle;
        control.PerformLayout();
        foreach (Control child in control.Controls) Handles(child);
    }
    static async Task Images(Form dialog) { await (Task)Call(dialog, "WaitForImages"); }
    static void Prepare()
    {
        if (File.Exists(Path.Combine(Common.Root, "native-fixture-ready.json"))) return;
        Directory.CreateDirectory(Path.Combine(Common.Root, "tests"));
        string missing = Path.Combine(Common.Root, "tests", "no-official-names");
        Directory.CreateDirectory(missing);
        File.WriteAllText(Path.Combine(Common.Root, "tests", "name-cache-root.txt"), missing);
        for (int index = 0; index < 61; index++)
        {
            string id = index == 0 ? "401627" : index == 17 ? "401610" : index == 39 ? "401999" : index == 60 ? "401001" : (750000 + index).ToString();
            var candidate = (ImportCandidate)typeof(ImportExperienceRegression).GetMethod("Resource", Flags).Invoke(null, new object[] { "native-" + index, id, (byte)(90 + index), false });
            CoverCache.WorkFolder = Path.Combine(Common.WorkRoot, "fixture-" + index);
            Directory.CreateDirectory(CoverCache.WorkFolder);
            CandidateChecks.Run(candidate, () => { }, null, true);
            Need(CandidateChecks.Passed(candidate), "owned fixture passes actual check");
            string picture = Path.Combine(Common.Root, "data", "native-covers", id + ".jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(picture));
            using (var bitmap = new Bitmap(360, 203))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(70 + index, 130, 200));
                bitmap.Save(picture, System.Drawing.Imaging.ImageFormat.Jpeg);
            }
            candidate.Cover = picture;
            typeof(CandidateChecks).GetMethod("FinishCover", Flags).Invoke(null, new object[] { candidate, candidate.Check });
            typeof(CandidateChecks).GetMethod("SaveResult", Flags).Invoke(null, new object[] { candidate, candidate.Check, new Action(() => { }) });
        }
        Common.SaveNew(Path.Combine(Common.Root, "native-fixture-ready.json"), new { count = 61, specialIds = new[] { "401627", "401610", "401999", "401001" } });
    }
    static string[] Expected(Form dialog)
    {
        var rows = (List<ListViewItem>)Field(dialog, "candidateRows");
        var query = (TextBox)Field(dialog, "candidateSearch");
        return rows.Where(row => ImportCandidateSearch.Matches((ImportCandidate)row.Tag, query.Text, new WallpaperItem[0])).Select(Id).ToArray();
    }
    static void Snapshot(Form dialog, string name, bool check)
    {
        var list = (ListView)Field(dialog, "list");
        var header = SendMessage(list.Handle, 0x101f, IntPtr.Zero, IntPtr.Zero);
        Rectangle headerRect;
        GetClientRect(header, out headerRect);
        Rectangle update;
        bool invalidated = GetUpdateRect(list.Handle, out update, false);
        var rows = list.Items.Cast<ListViewItem>().Select(row => new { id = Id(row), index = row.Index, bounds = row.Bounds, selected = row.Selected }).ToArray();
        var nativeTexts = Enumerable.Range(0, rows.Length).Select(index => NativeText(list, index)).ToArray();
        var selected = list.SelectedItems.Count == 1 ? (ImportCandidate)list.SelectedItems[0].Tag : null;
        string[] expected = Expected(dialog);
        int firstRowInk = 0;
        using (var bitmap = new Bitmap(list.Width, list.Height))
        {
            list.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            // Look in the first body text area, excluding the header, picture and selection fill.
            int bottom = Math.Min(bitmap.Height, headerRect.Height + 41);
            for (int y = headerRect.Height + 2; y < bottom; y++)
                for (int x = 74; x < Math.Min(350, bitmap.Width); x++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (pixel.R < 90 && pixel.G < 90 && pixel.B < 90) firstRowInk++;
                }
            bitmap.Save(Path.Combine(Path.GetDirectoryName(report), name + ".png"));
        }
        var images = (ImageList)Field(dialog, "thumbs");
        var updateField = typeof(ListView).GetField("updateCounter", Flags);
        int imagesPending = ((ImportImageCache)Field(dialog, "imageCache")).PendingCount;
        Snapshots.Add(new { name = name, expected = expected, managedIds = rows.Select(row => row.id).ToArray(), nativeTexts = nativeTexts, nativeCount = SendMessage(list.Handle, 0x1004, IntPtr.Zero, IntPtr.Zero).ToInt32(), nativeTopIndex = SendMessage(list.Handle, 0x1027, IntPtr.Zero, IntPtr.Zero).ToInt32(), topItem = list.TopItem == null ? null : Id(list.TopItem), client = list.ClientRectangle, header = headerRect, rows = rows, invalidated = invalidated, update = update, firstBodyTextInk = firstRowInk, imageHandle = images.Handle.ToInt64(), imageSize = images.ImageSize, imagesPending = imagesPending, updateDepth = updateField == null ? null : updateField.GetValue(list), selectedId = selected == null ? null : selected.Id, detail = ((TextBox)Field(dialog, "note")).Text, importEnabled = ((Button)Field(dialog, "import")).Enabled });
        if (!check) return;
        Need(rows.Select(row => row.id).SequenceEqual(expected), name + " native/managed order must match stable model order");
        Need(SendMessage(list.Handle, 0x1004, IntPtr.Zero, IntPtr.Zero).ToInt32() == expected.Length, name + " native count matches model");
        Need(nativeTexts.SequenceEqual(list.Items.Cast<ListViewItem>().Select(row => row.Text)), name + " independent native text order agrees with managed rows");
        if (rows.Length > 0 && rows.Length <= 4)
        {
            Need(rows[0].bounds.Top >= headerRect.Height && rows[0].bounds.Top <= headerRect.Height + 3, name + " first matching row must start below header before any click");
            Need(rows.Last().bounds.Bottom <= list.ClientSize.Height, name + " all four results fit in view");
            Need(firstRowInk > 15, name + " first row text is drawn without a click");
            for (int index = 1; index < rows.Length; index++) Need(rows[index].bounds.Top == rows[index - 1].bounds.Bottom, name + " row rectangles are consecutive");
        }
        if (selected == null) Need(!((Button)Field(dialog, "import")).Enabled, name + " empty selection disables import");
        else Need(expected.Contains(selected.Id) && ((TextBox)Field(dialog, "note")).Text.Contains("ID " + selected.Id), name + " detail and import identity match native selection");
    }
    static void Check(string name, Action test)
    {
        try { test(); Results.Add(new { name = name, passed = true }); }
        catch (Exception error) { Results.Add(new { name = name, passed = false, error = error.Message }); }
    }
    static async Task Exercise()
    {
        using (var owner = new ControlWindow(null, 0, null))
        {
            owner.TestSuppressImportShow = true;
            typeof(ControlWindow).GetField("sourceState", Flags).SetValue(owner, new SourceRootState { Found = true, Root = LocalImport.DataRoot });
            ImportImageCache.TestBeforeDecode = (path, token) =>
            {
                if (path.Contains("native-covers")) { token.WaitHandle.WaitOne(30); token.ThrowIfCancellationRequested(); }
            };
            Task opening = owner.TestOpenImport();
            var dialog = (Form)Field(owner, "importDialog");
            Handles(dialog);
            var query = (TextBox)Field(dialog, "candidateSearch");
            query.Text = "401";
            await opening;
            Check("query-before-images", () => Snapshot(dialog, "query-before-images", true));
            ImportImageCache.TestBeforeDecode = null;
            await Images(dialog);
            Check("query-after-images", () => Snapshot(dialog, "query-after-images", true));
            var list = (ListView)Field(dialog, "list");
            foreach (int position in new[] { 0, 30, 60 })
            {
                query.Text = "";
                Check("clear-before-scroll-" + position, () => Snapshot(dialog, "clear-before-scroll-" + position, true));
                list.TopItem = list.Items[position];
                await Task.Delay(20);
                Snapshot(dialog, "scrolled-" + position, false);
                query.Text = "401";
                Check("filter-from-" + position, () => Snapshot(dialog, "filter-from-" + position, true));
            }
            foreach (string value in new[] { "no-match", "", "75005", "401", "", "401" })
            {
                query.Text = value;
                string name = "quick-" + Results.Count;
                Check(name, () => Snapshot(dialog, name, true));
            }
            dialog.Close();
            await Images(dialog);
            Need(dialog.IsDisposed, "dialog closes normally");
            // Force one owned proof miss, leaving the other 60 reusable.
            var candidate = LocalImport.ReadRecord(Directory.GetFiles(Path.Combine(LocalImport.DataRoot, "cache_info"))[0]);
            string cache = (string)typeof(CandidateChecks).GetMethod("Cache", Flags).Invoke(null, new object[] { candidate });
            File.Delete(cache);
            await owner.TestOpenImport();
            dialog = (Form)Field(owner, "importDialog");
            Handles(dialog);
            query = (TextBox)Field(dialog, "candidateSearch");
            query.Text = "401";
            await Images(dialog);
            Check("partial-recheck", () => Snapshot(dialog, "partial-recheck", true));
            var beforeNames = (List<ListViewItem>)Field(dialog, "candidateRows");
            var named = (ImportCandidate)beforeNames.First(row => Id(row) == "401627").Tag;
            var names = new Dictionary<string, NameEvidence> { { named.Id, new NameEvidence { Id = named.Id, Name = "迟到的名称", Kind = "official-local-wallpaper-response", SourceSha256 = new string('a', 64), IndexSha256 = new string('b', 64), SourceBytes = 64, ResponsePath = "/pcwallpapercontent/wallpaper/jingling/detailDynamic" } } };
            Call(dialog, "RefreshOfficialNames", names);
            query.Text = "迟到";
            Check("late-name", () => Snapshot(dialog, "late-name", true));
            await (Task)Call(dialog, "ImportSelected");
            var library = (WallpaperLibrary)Field(owner, "library");
            Need(library.Doc.Items.Any(item => item.ResourceId == named.Id && item.RegisteredSha256 == named.Check.Sha256), "filtered explicit import commits the selected native resource identity");
            Check("after-filtered-import", () => Snapshot(dialog, "after-filtered-import", true));
            dialog.Close();
            await Images(dialog);
            foreach (float scale in new[] { 1f, 1.25f, 1.5f })
            {
                await owner.TestOpenImport();
                dialog = (Form)Field(owner, "importDialog");
                dialog.Scale(new SizeF(scale, scale));
                Handles(dialog);
                query = (TextBox)Field(dialog, "candidateSearch");
                query.Text = "401";
                await Images(dialog);
                string name = "scale-" + scale;
                Check(name, () => Snapshot(dialog, name, true));
                dialog.Close();
                await Images(dialog);
            }
        }
    }
    static async Task Minimal()
    {
        using (var owner = new ControlWindow(null, 0, null))
        using (var dialog = (Form)Activator.CreateInstance(typeof(ControlWindow).GetNestedType("ImportDialog", Flags), Flags, null, new object[] { owner }, null))
        {
            Handles(dialog);
            var list = (ListView)Field(dialog, "list");
            list.Dock = DockStyle.None;
            list.Size = new Size(600, 100);
            for (int index = 0; index < 3; index++)
                Call(dialog, "TestAddCandidate", new ImportCandidate { Id = index == 0 ? "401627" : "75000" + index, Name = "自有行 " + index, Source = "owned-" + index });
            list.TopItem = list.Items[2];
            Snapshot(dialog, "minimal-before", false);
            ((TextBox)Field(dialog, "candidateSearch")).Text = "401";
            Check("minimal-no-image-no-worker", () => Snapshot(dialog, "minimal-after", true));
            dialog.Close();
            await Images(dialog);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        if (args[0] == "--local-import") return LocalImport.Worker(args[1]);
        report = args[0];
        Application.EnableVisualStyles();
        Control.CheckForIllegalCrossThreadCalls = true;
        using (var deadline = new System.Threading.Timer(state => Environment.Exit(124), null, 45000, Timeout.Infinite))
        using (var host = new Control())
        using (var context = new ApplicationContext())
        {
            if (args[1] != "minimal") Prepare();
            var handle = host.Handle;
            int exit = 1;
            host.BeginInvoke(new Action(async () =>
            {
                try
                {
                    if (args[1] == "minimal") await Minimal();
                    else await Exercise();
                    bool passed = Results.All(value => (bool)value.GetType().GetProperty("passed").GetValue(value, null));
                    Common.SaveNew(report, new { passed = passed, checks = Results, snapshots = Snapshots, scope = "owned native handles, actual async import entry, Application.Run and DrawToBitmap before input clicks; no visible window or window automation" });
                    exit = passed ? 0 : 1;
                }
                catch (Exception error) { Common.SaveNew(report, new { passed = false, error = error.ToString(), checks = Results, snapshots = Snapshots }); }
                finally { ImportImageCache.TestBeforeDecode = null; context.ExitThread(); }
            }));
            Application.Run(context);
            return exit;
        }
    }
}
#endif
