#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class NativeInputRegression
{
    const BindingFlags Flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void WindowInput(IntPtr self, IntPtr window, uint message, IntPtr buttons, IntPtr coordinates);
    sealed class Fake : IDisposable
    {
        internal readonly IntPtr Table = Marshal.AllocHGlobal(0x6c), Self = Marshal.AllocHGlobal(IntPtr.Size);
        internal readonly List<object> Inputs = new List<object>();
        internal readonly List<string> Calls = new List<string>();
        internal IntPtr Window; internal bool FailStart, ThrowInput;
        readonly List<Delegate> delegates = new List<Delegate>();
        internal Fake()
        {
            for (int index = 0; index < 0x6c; index += 4) Marshal.WriteIntPtr(Table, index, IntPtr.Zero);
            Marshal.WriteIntPtr(Self, Table);
            Put(0, new EngineCore.GetWindow(delegate(IntPtr self, out IntPtr window) { window = Window; return 0; }));
            Put(4, new EngineCore.SetPath((self, path) => 0)); Put(12, new EngineCore.SetPath((self, path) => 0));
            Put(0x18, new EngineCore.SetNumber((self, value) => 0)); Put(0x20, new EngineCore.SetNumber((self, value) => 0));
            Put(0x48, new EngineCore.SetNumber((self, value) => 0)); Put(0x4c, new EngineCore.SetNumber((self, value) => 0));
            Put(0x2c, new EngineCore.Start((self, callback, reserve) => { Calls.Add("start"); return FailStart ? -1 : 0; }));
            Put(0x30, new EngineCore.Stop(self => { Calls.Add("stop"); return 0; }));
            WindowInput input = (self, window, message, buttons, coordinates) =>
            {
                if (self != Self || window != Window || message != 0x200 || buttons != IntPtr.Zero) throw new InvalidOperationException("input ABI identity mismatch");
                if (ThrowInput) throw new IOException("TEST_BUILD input failure");
                int packed = coordinates.ToInt32();
                Inputs.Add(new { x = (short)(packed & 65535), y = (short)((uint)packed >> 16), message }); Calls.Add("move");
            };
            var inputType = typeof(EngineCore).GetNestedType("WindowEvent", Flags);
            Put(0x3c, inputType == null ? input : Delegate.CreateDelegate(inputType, input.Target, input.Method));
        }
        void Put(int slot, Delegate function) { delegates.Add(function); Marshal.WriteIntPtr(Table, slot, Marshal.GetFunctionPointerForDelegate(function)); }
        internal IntPtr Create(IntPtr window, IntPtr options) { Window = window; Calls.Add("create"); return Self; }
        internal void Release(IntPtr self) { Calls.Add("release"); }
        public void Dispose() { Marshal.FreeHGlobal(Self); Marshal.FreeHGlobal(Table); GC.KeepAlive(delegates); }
    }
    static readonly List<object> checks = new List<object>();
    static void Need(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
    static object Field(object value, string name) { return value.GetType().GetField(name, Flags).GetValue(value); }
    static object Call(object value, string name, params object[] arguments) { return value.GetType().GetMethod(name, Flags).Invoke(value, arguments); }
    static void Check(string name, Action action) { try { action(); checks.Add(new { name, passed = true }); } catch (Exception error) { checks.Add(new { name, passed = false, error = error.ToString() }); } }
    static Point InputPoint(object value) { return new Point((short)value.GetType().GetProperty("x").GetValue(value, null), (short)value.GetType().GetProperty("y").GetValue(value, null)); }
    static void Fixture(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "input.cwp"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(folder, "request.json"), "{\"runToken\":\"owned-fixture\",\"audioMuted\":true,\"selectedName\":\"隔离输入夹具\"}");
    }
    static async Task DesktopPolling(string parent)
    {
        foreach (float scale in new[] { 1f, 1.25f, 1.5f })
        {
            string folder = Path.Combine(parent, "desktop-" + scale); Fixture(folder);
            Point origin = new Point(-1920, -120), client = new Point((int)(120 * scale), (int)(80 * scale));
            Point screen = new Point(origin.X + client.X, origin.Y + client.Y);
            DesktopHost.TestPointerPosition = () => screen;
            DesktopHost.TestPointerToClient = point => new Point(point.X - origin.X, point.Y - origin.Y);
            using (var fake = new Fake())
            {
                EngineCore.TestOpenFactory = fake.Create; EngineCore.TestOpenRelease = fake.Release;
                using (var host = new DesktopHost(folder, "desktop", folder, "input.cwp", folder, "fixture", "fixture"))
                {
                    host.ClientSize = new Size((int)(640 * scale), (int)(360 * scale)); host.PerformLayout();
                    var handle = host.Handle; Call(host, "Start", null, EventArgs.Empty);
                    ((System.Windows.Forms.Timer)Field(host, "tick")).Stop();
                    await Task.Delay(80);
                    Check("desktop-screen-to-render-pixels-" + scale, () => Need(fake.Inputs.Count == 1 && InputPoint(fake.Inputs[0]) == client, "actual pointer timer maps negative screen origin into render pixels"));
                    int stationary = fake.Inputs.Count; await Task.Delay(70);
                    Check("desktop-stationary-no-repeat-" + scale, () => Need(fake.Inputs.Count == stationary, "unchanged cursor does not enqueue repeated messages"));
                    int before = fake.Inputs.Count;
                    for (int index = 1; index <= 20; index++) Call(host, "RelayPointer", new Point(client.X + index, client.Y + index));
                    client.Offset(20, 20); screen = new Point(origin.X + client.X, origin.Y + client.Y);
                    await Task.Delay(80);
                    Check("coalesced-latest-point-" + scale, () => Need(fake.Inputs.Count <= before + 2 && InputPoint(fake.Inputs.Last()) == client, "burst is bounded and final physical point arrives"));
                    before = fake.Inputs.Count; screen = new Point(origin.X - 1, origin.Y - 1); await Task.Delay(70);
                    Check("desktop-outside-viewport-" + scale, () => Need(fake.Inputs.Count == before, "other work area is ignored"));
                    screen = new Point(origin.X + client.X, origin.Y + client.Y); await Task.Delay(70);
                    Check("desktop-reenter-" + scale, () => Need(fake.Inputs.Count == before + 1, "reentry refreshes the valid target"));
                    if (scale == 1.5f)
                    {
                        fake.ThrowInput = true; screen.Offset(1, 1); await Task.Delay(70);
                        Check("input-failure-disables-only-relay", () => Need((bool)Field(host, "pointerFailed") && !((System.Windows.Forms.Timer)Field(host, "pointerTick")).Enabled && !(bool)Field(host, "cleanupAttempted"), "first input failure stops its timer and preserves normal playback cleanup"));
                    }
                    var core = (EngineCore)Field(host, "engine"); Call(host, "CleanupPlayback");
                    int ended = fake.Inputs.Count; screen.Offset(1, 1); await Task.Delay(60);
                    Check("desktop-stop-no-late-input-" + scale, () => Need(!core.CanForwardPointer && !core.ForwardMouseMove(2, 3) && fake.Inputs.Count == ended && !((System.Windows.Forms.Timer)Field(host, "pointerTick")).Enabled && fake.Calls.Count(x => x == "release") == 1, "stop cancels input before release"));
                }
                using (File.Open(Path.Combine(folder, "host.jsonl"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
        }
        using (var fake = new Fake()) using (var core = new EngineCore((phase, value) => { }))
        {
            fake.FailStart = true;
            try { core.TestInitialize(new IntPtr(123), parent, "input.cwp", 0, fake.Create, fake.Release); } catch (Exception) { }
            Check("failed-start-no-input", () => Need(!core.CanForwardPointer && !core.ForwardMouseMove(1, 2) && fake.Inputs.Count == 0, "failed initialization never enables input"));
        }
        using (var fake = new Fake()) using (var core = new EngineCore((phase, value) => { }))
        {
            core.TestInitialize(new IntPtr(123), parent, "input.mp4", 0, fake.Create, fake.Release);
            Check("video-input-remains-inactive", () => Need(!core.CanForwardPointer && !core.ForwardMouseMove(1, 2), "movie-only input is limited to the CWP path"));
        }
    }
    static async Task Exercise(string folder, bool baseline)
    {
        Fixture(folder);
        using (var fake = new Fake())
        {
            Point fixturePoint = new Point(120, 80);
            var positionField = typeof(DesktopHost).GetField("TestPointerPosition", Flags);
            var mappingField = typeof(DesktopHost).GetField("TestPointerToClient", Flags);
            if (positionField != null) positionField.SetValue(null, new Func<Point>(() => fixturePoint));
            if (mappingField != null) mappingField.SetValue(null, new Func<Point,Point>(point => point));
            EngineCore.TestOpenFactory = fake.Create; EngineCore.TestOpenRelease = fake.Release;
            using (var host = new DesktopHost(folder, "preview", folder, "input.cwp", folder, "fixture", "fixture"))
            {
                host.PerformLayout(); var handle = host.Handle;
                var surface = (Control)Field(host, "surface"); surface.PerformLayout();
                Call(host, "Start", null, EventArgs.Empty);
                ((System.Windows.Forms.Timer)Field(host, "tick")).Stop();
                typeof(Control).GetMethod("OnMouseMove", Flags).Invoke(surface, new object[] { new MouseEventArgs(MouseButtons.None, 0, 120, 80, 0) });
                Check("actual-preview-surface-to-native-queue", () => Need(fake.Inputs.Count == 1, "normal Surface.MouseMove must reach pinned slot0x3c once"));
                if (!baseline)
                {
                    var core = (EngineCore)Field(host, "engine");
                    Check("invalid-coordinates-rejected", () => { int before = fake.Inputs.Count; Call(core, "ForwardMouseMove", -1, 2); Call(core, "ForwardMouseMove", 32768, 3); Need(fake.Inputs.Count == before, "out-of-range input is not packed"); });
                    typeof(Control).GetMethod("OnMouseMove", Flags).Invoke(surface, new object[] { new MouseEventArgs(MouseButtons.None, 0, 120, 80, 0) });
                    Check("stationary-point-deduplicated", () => Need(fake.Inputs.Count == 1, "duplicate point does not fill the native queue"));
                    Call(host, "RelayPointer", new Point(surface.Width + 4, 0));
                    Check("outside-viewport-rejected", () => Need(fake.Inputs.Count == 1, "outside viewport does not become a valid point"));
                    fixturePoint = new Point(121, 81); await Task.Delay(45);
                    Call(host, "RelayPointer", fixturePoint);
                    Check("latest-target-and-abi", () => Need(fake.Inputs.Count == 2 && fake.Calls[0] == "create" && fake.Calls[1] == "start", "input follows successful native start"));
                    await Task.Delay(75);
                }
                Call(host, "CleanupPlayback");
                int ended = fake.Inputs.Count;
                if (!baseline) { Call(host, "RelayPointer", new Point(122, 82)); await Task.Delay(55); }
                Check("stop-and-release", () => Need(fake.Inputs.Count == ended && fake.Calls.Count(x => x == "stop") == 1 && fake.Calls.Count(x => x == "release") == 1, "cleanup ends input before native stop and release"));
            }
        }
        if (!baseline) await DesktopPolling(folder);
    }
    [STAThread] static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        using (var timeout = new System.Threading.Timer(_ => Environment.Exit(124), null, 15000, Timeout.Infinite))
        using (var dispatcher = new Control()) using (var context = new ApplicationContext())
        {
            int exit = 1; var handle = dispatcher.Handle;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                object result;
                try { await Exercise(args[1], args[2] == "baseline"); bool passed = checks.All(x => (bool)x.GetType().GetProperty("passed").GetValue(x, null)); result = new { passed, checks, scope = "actual unshown DesktopHost.Start and Surface.MouseMove, native ABI substitute, no third-party engine", renderingProven = false }; exit = passed ? 0 : 1; }
                catch (Exception error) { result = new { passed = false, error = error.ToString(), checks }; }
                finally { EngineCore.TestOpenFactory = null; EngineCore.TestOpenRelease = null; var position = typeof(DesktopHost).GetField("TestPointerPosition", Flags); if (position != null) position.SetValue(null, null); var mapping = typeof(DesktopHost).GetField("TestPointerToClient", Flags); if (mapping != null) mapping.SetValue(null, null); }
                File.WriteAllText(args[0], new JavaScriptSerializer().Serialize(result)); context.ExitThread();
            }));
            Application.Run(context); return exit;
        }
    }
}
#endif
