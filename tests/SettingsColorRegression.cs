#if TEST_BUILD
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;

internal static class SettingsColorRegression
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static readonly List<object> checks = new List<object>();
    static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Check(string name, Action action)
    {
        try { action(); checks.Add(new { name, passed = true }); }
        catch (Exception error) { checks.Add(new { name, passed = false, error = error.Message }); }
    }
    static async Task Exercise(string report)
    {
        using (var tabs = new MaterialTabs { Size = new Size(720, 420), TestAnimateHidden = true })
        {
            foreach (string name in new[] { "来源与存储", "播放组件", "声音与登录", "详情与诊断" }) tabs.TabPages.Add(name);
            var handle = tabs.Handle;
            var bar = (Control)typeof(MaterialTabs).GetField("bar", Flags).GetValue(tabs);
            var initial = bar.Controls.Cast<Control>().Select(button => button.Bounds).ToArray();
            Check("selected-background", () => Need(bar.Controls[0].BackColor != Theme.Canvas, "selected tab has a distinct background"));
            Color selected = bar.Controls[0].BackColor;
            tabs.SelectedIndex = 3;
            Check("immediate-page", () => Need(tabs.SelectedTab == tabs.TabPages[3] && tabs.SelectedTab.Parent.Controls.GetChildIndex(tabs.SelectedTab) == 0, "new page is brought forward immediately"));
            await Task.Delay(65);
            Color middle = bar.Controls[3].BackColor;
            Check("intermediate-color", () => Need(middle != Theme.Canvas && middle != selected, "background has a measured intermediate color"));
            tabs.SelectedIndex = 1;
            Check("retarget-continuity", () => Need(bar.Controls[3].BackColor != Theme.Canvas, "previous partial color is retained at retarget"));
            foreach (int index in new[] { 2, 0, 3, 1 }) { tabs.SelectedIndex = index; await Task.Delay(18); }
            await Task.Delay(290);
            Check("final-colors", () => Need(!tabs.AnimationRunning && bar.Controls[1].BackColor == selected && bar.Controls.Cast<Control>().Where((button, index) => index != 1).All(button => button.BackColor == Theme.Canvas), "only final target retains selected fill"));
            Check("stable-label-bounds", () => Need(initial.SequenceEqual(bar.Controls.Cast<Control>().Select(button => button.Bounds)), "color transitions do not move labels"));
            tabs.SelectKey(Keys.Home); tabs.SelectKey(Keys.Right); tabs.SelectKey(Keys.End);
            Check("keyboard", () => Need(tabs.SelectedIndex == 3, "Home arrows End switch pages"));
            object[] keyArgs = { Message.Create(tabs.Handle, 0, IntPtr.Zero, IntPtr.Zero), Keys.Control | Keys.Tab };
            Check("ctrl-tab", () => Need((bool)typeof(MaterialTabs).GetMethod("ProcessCmdKey", Flags).Invoke(tabs, keyArgs) && tabs.SelectedIndex == 0, "CtrlTab switches from content"));
            Check("accessibility", () => Need(bar.Controls[0].AccessibleName == tabs.SelectedTab.Text && (bar.Controls[0].AccessibilityObject.State & AccessibleStates.Selected) != 0 && bar.Controls[0].TabStop, "accessible state and focus match page"));
            tabs.TestReduceMotion = true; tabs.SelectedIndex = 0;
            Check("same-page-reduced-motion", () => Need(!tabs.AnimationRunning && bar.Controls[0].BackColor == selected, "changing motion policy settles same selection"));
            tabs.SelectedIndex = 2;
            Check("reduced-motion", () => Need(!tabs.AnimationRunning && bar.Controls[2].BackColor == selected && bar.Controls[0].BackColor == Theme.Canvas, "reduced motion settles all colors immediately"));
            tabs.TestReduceMotion = false; tabs.SelectedIndex = 1; tabs.Visible = false; tabs.Visible = true;
            Check("hide-reopen", () => Need(!tabs.AnimationRunning && bar.Controls[1].BackColor == selected, "reopen settles current page"));
            foreach (float scale in new[] { 1f, 1.25f, 1.5f })
            {
                tabs.SelectedIndex = 3;
                tabs.Font = new Font("Microsoft YaHei UI", 9.5f * scale);
                tabs.Size = new Size((int)(640 * scale), (int)(420 * scale));
                Check("layout-" + scale, () =>
                {
                    Need(!tabs.AnimationRunning && bar.Controls[3].BackColor == selected, "resize settles selected state");
                    foreach (Control button in bar.Controls) Need(button.Width >= TextRenderer.MeasureText(button.Text, button.Font).Width + 12 && button.Height >= button.Font.Height + 10, "text and focus space fit");
                });
                using (var bitmap = new Bitmap(bar.Width, bar.Height))
                {
                    bar.DrawToBitmap(bitmap, bar.ClientRectangle);
                    bitmap.Save(Path.Combine(Path.GetDirectoryName(report), "color-tabs-" + scale + ".png"));
                    Check("paint-" + scale, () => Need(bitmap.GetPixel(bar.Controls[3].Left + bar.Controls[3].Width / 2, 8).ToArgb() == selected.ToArgb(), "selected fill is rendered before input"));
                }
            }
            tabs.Dispose(); Check("dispose", () => Need(!tabs.AnimationRunning, "animation timer is released"));
        }
    }
    [STAThread] static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Control.CheckForIllegalCrossThreadCalls = true;
        using (var timeout = new System.Threading.Timer(_ => Environment.Exit(124), null, 12000, Timeout.Infinite))
        using (var dispatcher = new Control()) using (var context = new ApplicationContext())
        {
            var handle = dispatcher.Handle; int exit = 1;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await Exercise(args[0]); bool passed = checks.All(row => (bool)row.GetType().GetProperty("passed").GetValue(row, null)); Common.SaveNew(args[0], new { passed, checks, scope = "own unshown tabs, real handlers and message loop, offscreen rendering" }); exit = passed ? 0 : 1; }
                catch (Exception error) { Common.SaveNew(args[0], new { passed = false, error = error.ToString(), checks }); }
                finally { context.ExitThread(); }
            }));
            Application.Run(context); return exit;
        }
    }
}
#endif
