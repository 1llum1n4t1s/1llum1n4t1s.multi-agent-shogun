using System.Threading;
using System.Threading.Tasks;

namespace Shogun.Avalonia.Services;

/// <summary>
/// 将軍→家老→足軽のフローをアプリ内で完結させるオーケストレーター。
/// </summary>
public interface IAgentOrchestrator
{
    /// <summary>殿の入力を受け、将軍AIで家老への指示文を生成する。</summary>
    /// <param name="userInput">殿（上様）の入力。</param>
    /// <param name="projectId">プロジェクトID（未指定のとき null）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>家老に渡す指示文（queue に載せる1件のテキスト）。</returns>
    Task<string> ResolveShogunCommandAsync(string userInput, string? projectId, CancellationToken cancellationToken = default);

    /// <summary>指定したコマンド ID について、家老によるタスク分配→足軽実行→報告→dashboard 更新を行う。</summary>
    /// <param name="commandId">キューに追加したコマンド ID（例: cmd_001）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>成功時は完了メッセージ、失敗時はエラーメッセージ。</returns>
    Task<string> RunAsync(string commandId, CancellationToken cancellationToken = default);
}
