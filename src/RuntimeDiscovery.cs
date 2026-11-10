using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

internal sealed class RuntimeCandidate {
    public string Root { get; set; }
    public string State { get; set; }
    public string Detail { get; set; }
}

internal sealed class RuntimeDiscoveryResult {
    public string State { get; set; }
    public string Message { get; set; }
    public string CandidateRoot { get; set; }
    public List<RuntimeCandidate> Candidates { get; set; }
}

// Discovery is read-only. Adoption remains an explicit UI action using SelectAndSave.
internal static class RuntimeDiscovery {
    internal const int BudgetSeconds = 30;
#if TEST_BUILD
    internal static int TestBudgetMilliseconds = 30000;
    internal static Action<CancellationToken> TestBeforeVerify;
#endif

    internal static IEnumerable<string> KnownRoots() {
        return new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetEnvironmentVariable("ProgramW6432")
        }.Where(folder => !String.IsNullOrWhiteSpace(folder))
         .Select(folder => Path.Combine(folder, "LxWallpaper"))
         .Distinct(StringComparer.OrdinalIgnoreCase).Take(3);
    }

    internal static RuntimeDiscoveryResult Check(CancellationToken cancellation) {
        return Check(KnownRoots(), cancellation);
    }

    internal static RuntimeDiscoveryResult Check(IEnumerable<string> roots, CancellationToken cancellation) {
        using (var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation)) {
#if TEST_BUILD
            budget.CancelAfter(TestBudgetMilliseconds);
#else
            budget.CancelAfter(TimeSpan.FromSeconds(BudgetSeconds));
#endif
            try { return Inspect(roots, budget.Token); }
            catch (OperationCanceledException) {
                cancellation.ThrowIfCancellationRequested();
                throw new TimeoutException("组件检查超过30秒，已停止本次检查。可重试或手动选择安装目录。");
            }
        }
    }

    static RuntimeDiscoveryResult Inspect(IEnumerable<string> roots, CancellationToken token) {
        token.ThrowIfCancellationRequested();
        bool saved = RuntimeProvisioning.HasSavedLocation;
        RuntimeProvisioning.Initialize(token);
        var result = new RuntimeDiscoveryResult { Candidates = new List<RuntimeCandidate>() };
        if (RuntimeProvisioning.Available) {
            result.State = "saved-ready";
            result.CandidateRoot = RuntimeProvisioning.Root;
            result.Message = "已保存的组件位置已通过23项身份核验，当前未播放。";
            result.Candidates.Add(new RuntimeCandidate { Root = RuntimeProvisioning.Root, State = result.State, Detail = result.Message });
            return result;
        }
        string savedFailure = saved ? RuntimeProvisioning.Message : null;
        foreach (string root in roots.Where(p => !String.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(3)) {
            token.ThrowIfCancellationRequested();
            var row = new RuntimeCandidate { Root = root };
            result.Candidates.Add(row);
            if (!Directory.Exists(root)) { row.State = "not-found"; row.Detail = "此标准安装位置不存在。"; continue; }
            try {
#if TEST_BUILD
                if (TestBeforeVerify != null) TestBeforeVerify(token);
#endif
                RuntimeProvisioning.VerifyRoot(root, token);
                row.State = "candidate-ready";
                row.Detail = "23项身份核验通过，等待你明确采用。";
                if (result.CandidateRoot == null) result.CandidateRoot = root;
            } catch (OperationCanceledException) { throw; }
            catch (FileNotFoundException error) { row.State = "missing-files"; row.Detail = error.Message; }
            catch (InvalidDataException error) { row.State = "version-mismatch"; row.Detail = error.Message; }
            catch (Exception error) { row.State = "unreadable"; row.Detail = error.Message; }
        }
        result.State = result.CandidateRoot != null ? "candidate-ready" : saved ? "saved-invalid" : "not-found";
        result.Message = result.CandidateRoot != null
            ? "发现兼容组件，点击“采用已核验组件”确认保存。尚未加载或播放。"
            : saved ? savedFailure : "未找到兼容组件。可能尚未安装、安装在其他位置，或当前版本不匹配。可手动选择目录。";
        if (saved && result.CandidateRoot != null) result.Message = savedFailure + " 另发现兼容位置，需你明确采用。";
        if (result.CandidateRoot == null) {
            var failure = result.Candidates.FirstOrDefault(row => row.State != "not-found");
            if (failure != null) { result.State = failure.State; result.Message += " " + failure.Detail; }
        }
        return result;
    }
}
