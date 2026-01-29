using System.Threading;
using System.Threading.Tasks;

namespace Shogun.Avalonia.Services;

/// <summary>
/// AI チャットサービス。
/// </summary>
public interface IAiService
{
    /// <summary>チャットメッセージを送信してAI応答を取得する。</summary>
    /// <param name="message">ユーザーメッセージ。</param>
    /// <param name="projectContext">プロジェクトのコンテキスト情報。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>AI応答。</returns>
    Task<string> SendChatMessageAsync(string message, string? projectContext = null, CancellationToken cancellationToken = default);

    /// <summary>システムプロンプトとユーザーメッセージでAI応答を取得する（家老・足軽用）。</summary>
    /// <param name="systemPrompt">システムプロンプト（役割・指示書）。</param>
    /// <param name="userMessage">ユーザーメッセージ。</param>
    /// <param name="modelOverride">使用するモデル名（空のときは設定の共通モデルを使用）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>AI応答。</returns>
    Task<string> SendWithSystemAsync(string systemPrompt, string userMessage, string? modelOverride = null, CancellationToken cancellationToken = default);

    /// <summary>サービスが利用可能か（APIキーが設定されているか）。</summary>
    bool IsAvailable { get; }
}
