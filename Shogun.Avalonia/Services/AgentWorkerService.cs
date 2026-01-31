using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Shogun.Avalonia.Models;
using Shogun.Avalonia.Util;

namespace Shogun.Avalonia.Services;

/// <summary>
/// 将軍・家老・足軽のワーカーをアプリ起動時に起動し、殿のメッセージをジョブとして投入するサービス。
/// 常駐プロセスはアプリ終了時まで終了しない。
/// </summary>
public class AgentWorkerService : IAgentWorkerService
{
    private readonly IClaudeCodeRunService _runService;
    private readonly IShogunQueueService _queueService;
    private readonly IClaudeCodeProcessHost _processHost;
    private Channel<AgentJob>? _shogunChannel;
    private Channel<KaroJob>? _karoChannel;
    private List<Channel<AshigaruJob>>? _ashigaruChannels;
    private CancellationTokenSource? _workerCts;
    private Task? _shogunLoop;
    private Task? _karoLoop;
    private List<Task>? _ashigaruLoops;
    private bool _started;
    private ISettingsService? _settingsService;

    /// <summary>サービスを生成する。</summary>
    public AgentWorkerService(IClaudeCodeRunService runService, IShogunQueueService queueService, IClaudeCodeProcessHost processHost)
    {
        _runService = runService;
        _queueService = queueService;
        _processHost = processHost;
    }

    /// <summary>設定サービスを設定する（確認モード参照用）。</summary>
    public void SetSettingsService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public async Task StartAllAsync(Func<string, string, Task>? onProcessReady = null, CancellationToken cancellationToken = default)
    {
        if (_started)
            return;
        _started = true;
        await _processHost.StartAllAsync(onProcessReady, cancellationToken).ConfigureAwait(false);
        _workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _workerCts.Token;
        var ashigaruCount = _queueService.GetAshigaruCount();
        _shogunChannel = Channel.CreateUnbounded<AgentJob>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _karoChannel = Channel.CreateUnbounded<KaroJob>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _ashigaruChannels = new List<Channel<AshigaruJob>>(ashigaruCount);
        for (var i = 0; i < ashigaruCount; i++)
            _ashigaruChannels.Add(Channel.CreateUnbounded<AshigaruJob>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }));
        _ashigaruLoops = new List<Task>(ashigaruCount);
        _shogunLoop = RunShogunLoopAsync(ct);
        _karoLoop = RunKaroLoopAsync(ct);
        for (var n = 0; n < ashigaruCount; n++)
        {
            var index = n;
            _ashigaruLoops.Add(RunAshigaruLoopAsync(index + 1, ct));
        }
        Logger.Log($"エージェントワーカーを起動しました（将軍・家老・足軽{ashigaruCount}）。", LogLevel.Info);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> SubmitMessageAsync(
        string userInput,
        string? projectId,
        IProgress<string> shogunProgress,
        IProgress<string> karoProgress,
        IProgress<string> reportProgress,
        Func<int, IProgress<string>> ashigaruProgressFor,
        CancellationToken cancellationToken = default)
    {
        if (_shogunChannel == null)
        {
            Logger.Log("エージェントワーカーが未起動です。StartAllAsync を先に呼んでください。", LogLevel.Error);
            return "エージェントワーカーが未起動です。アプリを再起動してください。";
        }
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new AgentJob
        {
            UserInput = userInput,
            ProjectId = projectId,
            ShogunProgress = shogunProgress,
            KaroProgress = karoProgress,
            ReportProgress = reportProgress,
            AshigaruProgressFor = ashigaruProgressFor,
            ResultTcs = tcs
        };
        await _shogunChannel.Writer.WriteAsync(job, cancellationToken).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task RunShogunLoopAsync(CancellationToken ct)
    {
        if (_shogunChannel == null || _karoChannel == null)
            return;
        await foreach (var job in _shogunChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var resolved = await _runService.ResolveShogunCommandAsync(job.UserInput, job.ProjectId, job.ShogunProgress, ct).ConfigureAwait(false);
                var id = _queueService.AppendCommand(resolved, job.ProjectId, "medium");
                Logger.Log($"将軍が指示を解決しました。コマンドをキューに追加: {id}", LogLevel.Debug);
                await _karoChannel.Writer.WriteAsync(new KaroJob { Job = job, CommandId = id }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogException("将軍ワーカーで例外", ex);
                job.ResultTcs.TrySetResult($"エラー: {ex.Message}");
            }
        }
    }

    private async Task RunKaroLoopAsync(CancellationToken ct)
    {
        if (_karoChannel == null || _ashigaruChannels == null)
            return;
        await foreach (var karoJob in _karoChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var job = karoJob.Job;
            try
            {
                var karoOk = await _runService.RunKaroAsync(job.KaroProgress, ct).ConfigureAwait(false);
                if (!karoOk)
                {
                    job.ResultTcs.TrySetResult($"指示をキューに追加しました（{karoJob.CommandId}）。家老の実行に失敗しました。ダッシュボードで確認してください。");
                    continue;
                }
                var ashigaruCount = _queueService.GetAshigaruCount();
                var assigned = new List<int>();
                for (var i = 1; i <= ashigaruCount; i++)
                {
                    var taskContent = _queueService.ReadTaskYaml(i);
                    if (!string.IsNullOrWhiteSpace(taskContent) && (taskContent.Contains("task:", StringComparison.Ordinal) || taskContent.Contains("status:", StringComparison.Ordinal)))
                        assigned.Add(i);
                }
                if (assigned.Count == 0)
                {
                    job.ResultTcs.TrySetResult($"指示をキューに追加しました（{karoJob.CommandId}）。家老（Claude Code）の実行が完了しました。（割り当てられた足軽なし）");
                    continue;
                }
                var doneTcsList = new List<TaskCompletionSource<bool>>(assigned.Count);
                foreach (var n in assigned)
                {
                    if (n < 1 || n > _ashigaruChannels.Count)
                        continue;
                    var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    doneTcsList.Add(doneTcs);
                    await _ashigaruChannels[n - 1].Writer.WriteAsync(new AshigaruJob { AshigaruIndex = n, Progress = job.AshigaruProgressFor(n), DoneTcs = doneTcs }, ct).ConfigureAwait(false);
                }
                await Task.WhenAll(doneTcsList.Select(t => t.Task)).ConfigureAwait(false);

                var settings = _settingsService?.Get();
                var permissionMode = settings?.KaroExecutionPermissionMode ?? "PromptUser";
                var shouldExecuteKaroPhase3 = false;

                if (permissionMode == "AlwaysAllow")
                {
                    shouldExecuteKaroPhase3 = true;
                }
                else if (permissionMode == "AlwaysReject")
                {
                    shouldExecuteKaroPhase3 = false;
                }
                else if (permissionMode == "PromptUser")
                {
                    var approvalBlock = new PaneBlock
                    {
                        Content = "足軽の報告書をもとに、コード改修を実行しますか？",
                        Status = "* ユーザー確認待ち",
                        Timestamp = DateTime.Now
                    };
                    karoJob.ApprovalBlock = approvalBlock;
                    job.KaroProgress.Report($"[確認待機]");

                    var approved = await approvalBlock.RequestApprovalAsync().ConfigureAwait(false);
                    shouldExecuteKaroPhase3 = approved;
                }

                if (shouldExecuteKaroPhase3)
                {
                    var karoExecOk = await _runService.RunKaroExecutionAsync(job.KaroProgress, ct).ConfigureAwait(false);
                    var reportOk = await _runService.RunKaroReportAggregationAsync(job.ReportProgress, ct).ConfigureAwait(false);
                    var resultMessage = (karoExecOk && reportOk)
                        ? $"指示をキューに追加しました（{karoJob.CommandId}）。家老・足軽{assigned.Count}名・家老（改修）・家老（報告集約）の実行が完了しました。"
                        : $"指示をキューに追加しました（{karoJob.CommandId}）。家老・足軽の実行は完了しましたが、家老（改修・報告集約）に失敗しました。ダッシュボードで確認してください。";
                    job.ResultTcs.TrySetResult(resultMessage);
                }
                else
                {
                    var reportOk = await _runService.RunKaroReportAggregationAsync(job.ReportProgress, ct).ConfigureAwait(false);
                    var resultMessage = reportOk
                        ? $"指示をキューに追加しました（{karoJob.CommandId}）。家老・足軽{assigned.Count}名・家老（報告集約）の実行が完了しました。（コード改修は却下されました）"
                        : $"指示をキューに追加しました（{karoJob.CommandId}）。家老・足軽の実行は完了しましたが、家老（報告集約）に失敗しました。ダッシュボードで確認してください。";
                    job.ResultTcs.TrySetResult(resultMessage);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("家老ワーカーで例外", ex);
                job.ResultTcs.TrySetResult($"エラー: {ex.Message}");
            }
        }
    }

    private async Task RunAshigaruLoopAsync(int ashigaruIndex, CancellationToken ct)
    {
        var ch = _ashigaruChannels?[ashigaruIndex - 1];
        if (ch == null)
            return;
        await foreach (var aj in ch.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                var ok = await _runService.RunAshigaruAsync(aj.AshigaruIndex, aj.Progress, ct).ConfigureAwait(false);
                aj.DoneTcs.TrySetResult(ok);
            }
            catch (Exception ex)
            {
                Logger.LogException($"足軽{aj.AshigaruIndex}ワーカーで例外", ex);
                aj.DoneTcs.TrySetResult(false);
            }
        }
    }

    private sealed class AgentJob
    {
        internal string UserInput { get; set; } = "";
        internal string? ProjectId { get; set; }
        internal IProgress<string> ShogunProgress { get; set; } = null!;
        internal IProgress<string> KaroProgress { get; set; } = null!;
        internal IProgress<string> ReportProgress { get; set; } = null!;
        internal Func<int, IProgress<string>> AshigaruProgressFor { get; set; } = null!;
        internal TaskCompletionSource<string> ResultTcs { get; set; } = null!;
    }

    private sealed class KaroJob
    {
        internal AgentJob Job { get; set; } = null!;
        internal string CommandId { get; set; } = "";
        internal PaneBlock? ApprovalBlock { get; set; }
    }

    private sealed class AshigaruJob
    {
        internal int AshigaruIndex { get; set; }
        internal IProgress<string> Progress { get; set; } = null!;
        internal TaskCompletionSource<bool> DoneTcs { get; set; } = null!;
    }

    /// <inheritdoc />
    public void StopAll()
    {
        try
        {
            _workerCts?.Cancel();
        }
        catch { /* キャンセルは失敗しても続行 */ }
        _processHost.StopAll();
    }
}
