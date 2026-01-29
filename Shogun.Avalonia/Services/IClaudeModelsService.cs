using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Shogun.Avalonia.Services;

/// <summary>
/// models.dev API から利用可能な Claude モデル一覧を取得するサービス。
/// API キーは扱わない。
/// </summary>
public interface IClaudeModelsService
{
    /// <summary>models.dev API で利用可能な Claude モデル一覧（ID と表示名）を取得する。</summary>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>モデル ID と表示名のリスト。取得失敗時は空リスト。</returns>
    Task<IReadOnlyList<(string Id, string Name)>> GetModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>models.dev API で利用可能な Claude モデル ID 一覧を取得する。</summary>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>モデル ID のリスト。取得失敗時は空リスト。</returns>
    Task<IReadOnlyList<string>> GetModelIdsAsync(CancellationToken cancellationToken = default);
}
