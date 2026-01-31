using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shogun.Avalonia.Services;

/// <summary>
/// 将軍・家老・足軽の Claude Code CLI を常駐プロセスで起動し、ジョブごとに終了させない。
/// プロセスの終了はアプリ本体の終了時のみ。
/// </summary>
public interface IClaudeCodeProcessHost
{
    /// <summary>全ロールの常駐プロセスを起動する。アプリ起動時に1回呼ぶ。起動完了時に各ロール名を報告する。</summary>
    /// <param name="onProcessReady">プロセスが起動完了したときのコールバック（roleLabel, "起動完了"）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    Task StartAllAsync(Func<string, string, Task>? onProcessReady = null, CancellationToken cancellationToken = default);

    /// <summary>指定ロールの常駐プロセスにジョブを送り、結果を返す。</summary>
    /// <param name="roleLabel">将軍 / 家老 / 足軽N 等。</param>
    /// <param name="userPrompt">ユーザープロンプト。</param>
    /// <param name="systemPromptPath">システムプロンプトファイルのパス。</param>
    /// <param name="modelId">使用するモデルID（オプション）。</param>
    /// <param name="thinking">Thinking モードを使うか（オプション）。</param>
    /// <param name="progress">進捗（任意）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>成功と出力文字列。</returns>
    Task<(bool Success, string Output)> RunJobAsync(
        string roleLabel,
        string userPrompt,
        string systemPromptPath,
        string? modelId = null,
        bool thinking = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>全常駐プロセスを終了する。アプリ終了時に呼ぶ。</summary>
    void StopAll();
}
