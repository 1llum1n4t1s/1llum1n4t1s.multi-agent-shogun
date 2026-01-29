using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shogun.Avalonia.Services;

/// <summary>
/// アプリ内の Claude Code CLI を起動し、家老・足軽としてキュー処理を実行するサービス。
/// </summary>
public interface IClaudeCodeRunService
{
    /// <summary>家老として Claude Code CLI を起動する。queue/shogun_to_karo.yaml を読んで足軽へ割り当てる指示を実行する。作業ディレクトリは RepoRoot。</summary>
    /// <param name="progress">進捗メッセージ（任意）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>成功したら true。CLI 未インストール・起動失敗・終了コード非0 のときは false。</returns>
    Task<bool> RunKaroAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>足軽 N として Claude Code CLI を起動する。queue/tasks/ashigaru{N}.yaml の任務を実行し、queue/reports/ashigaru{N}_report.yaml に報告する。</summary>
    /// <param name="ashigaruIndex">足軽番号（1～GetAshigaruCount()）。</param>
    /// <param name="progress">進捗メッセージ（任意）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>成功したら true。</returns>
    Task<bool> RunAshigaruAsync(int ashigaruIndex, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>家老として報告集約を実行する。queue/reports/ をスキャンし、dashboard.md の「戦果」を更新する。</summary>
    /// <param name="progress">進捗メッセージ（任意）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>成功したら true。</returns>
    Task<bool> RunKaroReportAggregationAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
