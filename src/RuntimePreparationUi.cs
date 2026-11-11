using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class ControlWindow {
    RuntimeDiscoveryResult runtimeDiscovery;
    readonly SoftButton checkComponents = new SoftButton();
    readonly SoftButton adoptComponents = new SoftButton();
    readonly SoftButton chooseComponents = new SoftButton();
    readonly LinkLabel personalLocationsToggle = new LinkLabel();
    readonly System.Windows.Forms.Timer preparationFold = new System.Windows.Forms.Timer { Interval = 16 };
    readonly System.Diagnostics.Stopwatch preparationFoldClock = new System.Diagnostics.Stopwatch();
    TableLayoutPanel preparationPanel;
    bool locationsExpanded;
    float foldStart, foldTarget;

    Control BuildPreparationArea() {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        preparationPanel = panel;
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, U(47)));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, U(33)));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        libraryHint.Dock = DockStyle.Fill;
        libraryHint.AutoEllipsis = true;
        libraryHint.AccessibleName = "播放组件准备状态";
        panel.Controls.Add(libraryHint, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
        checkComponents.Text = "重新检查组件";
        adoptComponents.Text = "采用已核验组件";
        chooseComponents.Text = "选择组件目录";
        foreach (var button in new[] { checkComponents, adoptComponents, chooseComponents }) {
            button.Size = new Size(U(130), U(29));
            button.Margin = new Padding(0, 0, U(6), 0);
            actions.Controls.Add(button);
        }
        checkComponents.Click += (s, e) => { if (runtimeCheckRunning) CancelRuntimeCheck(); else StartRuntimeCheckOnStartup(); };
        adoptComponents.Click += (s, e) => AdoptDiscoveredComponents();
        chooseComponents.Click += (s, e) => ChooseRuntimeLocation();
        panel.Controls.Add(actions, 0, 1);
        personalLocationsToggle.Dock = DockStyle.Fill;
        personalLocationsToggle.LinkBehavior = LinkBehavior.NeverUnderline;
        personalLocationsToggle.LinkClicked += (s, e) => TogglePersonalLocations();
        panel.Controls.Add(personalLocationsToggle, 0, 2);
        panel.Controls.Add(new Label{Text="完整身份核验不代表已加载或正在播放。取消保留原配置，播放开始前仍核验。",Dock=DockStyle.Fill,ForeColor=Muted}, 0, 3);
        preparationFold.Tick += (s, e) => {
            float progress = Math.Min(1, preparationFoldClock.ElapsedMilliseconds / 340f);
            ResizePreparationFold(foldStart + (foldTarget - foldStart) * (1 - (float)Math.Pow(1 - progress, 3)));
            if (progress >= 1) preparationFold.Stop();
        };
        FormClosed += (s, e) => preparationFold.Dispose();
        return panel;
    }

    void TogglePersonalLocations() {
        locationsExpanded = !locationsExpanded;
        foldStart = preparationPanel.RowStyles[3].Height;
        foldTarget = locationsExpanded ? U(48) : 0;
        if (SoftButton.MotionEnabled) { preparationFoldClock.Restart(); preparationFold.Start(); }
        else ResizePreparationFold(foldTarget);
        UpdatePreparationActions();
    }

    void ResizePreparationFold(float height) {
        preparationPanel.RowStyles[3].Height = height;
        var parent = preparationPanel.Parent as TableLayoutPanel;
        if (parent != null && parent.RowStyles.Count>1) parent.RowStyles[1].Height = U(112) + height;
    }

    void UpdatePreparationActions() {
        checkComponents.Text = runtimeCheckRunning ? (runtimeCancelRequested ? "正在取消…" : "取消组件检查") : "重新检查组件";
        checkComponents.Enabled = !closing && !runtimeCancelRequested;
        checkComponents.Busy = runtimeCheckRunning && !runtimeCancelRequested;
        chooseComponents.Enabled = !closing && !runtimeCheckRunning && !transition;
        adoptComponents.Enabled = !closing && !runtimeCheckRunning && !transition && !RuntimeProvisioning.Available
            && runtimeDiscovery != null && runtimeDiscovery.CandidateRoot != null;
        personalLocationsToggle.Text = (locationsExpanded ? "▾ 收起核验说明" : "▸ 展开核验说明");
    }

    async void StartRuntimeCheckOnStartup() {
        if (closing || runtimeCheckRunning) return;
        await RunComponentCheck(null);
    }

    async void ChooseRuntimeLocation() {
        if (closing || transition) return;
        if (runtimeCheckRunning) { CancelRuntimeCheck(); return; }
        using (var dialog = new FolderBrowserDialog { Description = "选择已合法安装的联想壁纸目录。仅核验组件，不加载或播放。", ShowNewFolderButton = false }) {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!ConfirmComponentAdoption(dialog.SelectedPath)) return;
            await RunComponentCheck(dialog.SelectedPath);
        }
    }

    bool ConfirmComponentAdoption(string root) {
        return MessageBox.Show(this, "采用这个本地播放组件位置？\n\n" + root + "\n\n程序将核验23项文件身份并保存位置。组件需由你合法准备。采用后仍由你点击预览或桌面播放。", "确认采用播放组件", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    async void AdoptDiscoveredComponents() {
        if (closing || transition || runtimeCheckRunning || runtimeDiscovery == null || runtimeDiscovery.CandidateRoot == null) return;
        string root = runtimeDiscovery.CandidateRoot;
        if (ConfirmComponentAdoption(root)) await RunComponentCheck(root);
    }

    async Task RunComponentCheck(string adoptRoot) {
        if (runtimeCheckRunning || closing) return;
        int generation = ++runtimeCheckGeneration;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(RuntimeDiscovery.BudgetSeconds));
        runtimeCheck = cts;
        runtimeCheckRunning = true;
        runtimeCancelRequested = false;
        runtimeNotice = "正在检查组件，最多30秒。";
        RefreshSelection();
        RefreshRuntimeNotice();
        UpdatePreparationActions();
        Write("runtime-location-check-started", new { stage = adoptRoot == null ? "bounded-discovery" : "explicit-adoption", playerStarted = false });
        try {
            if (adoptRoot == null) {
                var result = await Task.Run(() => RuntimeDiscovery.Check(cts.Token), cts.Token);
                if (closing || IsDisposed) return;
                if (generation != runtimeCheckGeneration) {
                    runtimeNotice = "本次组件检查已取消并结束。未采用发现结果，可重新检查。";
                    return;
                }
                runtimeDiscovery = result;
                runtimeNotice = result.Message + (result.CandidateRoot == null ? "" : "\n位置：" + result.CandidateRoot);
            } else {
                string message = await Task.Run(() => RuntimeProvisioning.SelectAndSave(adoptRoot, cts.Token), cts.Token);
                if (closing || IsDisposed) return;
                if (generation != runtimeCheckGeneration) {
                    runtimeNotice = "取消请求到达时组件核验和保存已完成。当前位置：" + RuntimeProvisioning.Root + "。当前未播放。";
                    return;
                }
                runtimeNotice = message + "\n位置：" + RuntimeProvisioning.Root;
            }
            Write("runtime-preparation-result", new { adopted = adoptRoot != null, available = RuntimeProvisioning.Available, message = runtimeNotice, playerStarted = false });
        } catch (OperationCanceledException) {
            if (!closing && !IsDisposed) runtimeNotice = runtimeCancelRequested ? "组件检查已取消。可重新检查；原保存位置保留。" : "组件检查超过30秒，已停止。可重新检查或选择目录。";
        } catch (Exception error) {
            if (!closing && !IsDisposed) {
                runtimeNotice = "组件检查未通过：" + error.Message + " 可重新检查或选择组件目录。";
                Write("runtime-preparation-failed", new { error = error.ToString(), savedConfigurationPreserved = true });
            }
        } finally {
            if (runtimeCheck == cts) {
                runtimeCheck = null;
                runtimeCheckRunning = false;
                runtimeCancelRequested = false;
                if (!closing && !IsDisposed) { RefreshSelection(); RefreshRuntimeNotice(); UpdatePreparationActions(); }
            }
            cts.Dispose();
        }
    }

    void RefreshRuntimeNotice(bool onlyIfOwned = false) {
        if (!RuntimePresentation.MayRefresh(libraryHint.IsDisposed, library.Error != null, onlyIfOwned, libraryHint.Text, runtimeRenderedNotice)) return;
        string mode = activeRunRequest == null ? null : Common.S(activeRunRequest, "mode");
        string message = RuntimePresentation.Runtime(RuntimeProvisioning.Available, runtimeCheckRunning, runtimeCancelRequested,
            RuntimeProvisioning.CheckedAt, runtimeNotice, PlayerRole == "alive", State, mode);
        // Keep the actual discovery result and recovery action visible while idle.
        if (!runtimeCheckRunning && PlayerRole != "alive" && !String.IsNullOrEmpty(runtimeNotice)) message = runtimeNotice;
        runtimeRenderedNotice = message;
        libraryHint.Text = message;
        libraryHint.ForeColor = RuntimeProvisioning.Available || runtimeCheckRunning ? Muted : Color.FromArgb(157, 82, 28);
        UpdatePreparationActions();
    }

    void CancelRuntimeCheck() {
        if (runtimeCheck == null || runtimeCancelRequested) return;
        runtimeCancelRequested = true;
        runtimeCheckGeneration++;
        runtimeCheck.Cancel();
        RefreshRuntimeNotice();
        UpdatePreparationActions();
    }
}
