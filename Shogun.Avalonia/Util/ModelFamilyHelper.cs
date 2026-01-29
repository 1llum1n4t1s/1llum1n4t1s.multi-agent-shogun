using System;
using System.Collections.Generic;
using System.Linq;

namespace Shogun.Avalonia.Util;

/// <summary>
/// モデル ID のファミリ（haiku / sonnet / opus）判定と、同一ファミリ内の最新モデル取得を行うヘルパー。
/// 公式で Haiku 4.5 等に更新された場合、保存済みの Haiku 3.5 等を同一ファミリの最新に自動更新するために使用する。
/// </summary>
public static class ModelFamilyHelper
{
    /// <summary>モデル ID からファミリ（haiku / sonnet / opus）を取得する。判定できない場合は null。</summary>
    /// <param name="modelId">モデル ID（例: claude-3-5-haiku-20241022）。</param>
    /// <returns>haiku, sonnet, opus のいずれか。判定できない場合は null。</returns>
    public static string? GetModelFamily(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return null;
        var id = modelId.Trim();
        if (id.Contains("haiku", StringComparison.OrdinalIgnoreCase))
            return "haiku";
        if (id.Contains("sonnet", StringComparison.OrdinalIgnoreCase))
            return "sonnet";
        if (id.Contains("opus", StringComparison.OrdinalIgnoreCase))
            return "opus";
        return null;
    }

    /// <summary>一覧の先頭（API は新しい順のことが多い）から、指定ファミリに属する最初のモデル ID を返す。同一ファミリの「最新」として扱う。</summary>
    /// <param name="modelIds">Claude Code から取得したモデル ID 一覧。</param>
    /// <param name="family">haiku, sonnet, opus のいずれか。</param>
    /// <returns>該当するモデル ID。なければ null。</returns>
    public static string? GetLatestInFamily(IReadOnlyList<string> modelIds, string family)
    {
        if (modelIds == null || modelIds.Count == 0 || string.IsNullOrWhiteSpace(family))
            return null;
        return modelIds.FirstOrDefault(id => string.Equals(GetModelFamily(id), family, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>指定ファミリに属するモデル ID のうち、ID 文字列で昇順ソートしたとき最後（数字が一番大きい想定）のものを返す。未選択時の初期値に使用。</summary>
    /// <param name="modelIds">取得済みモデル ID 一覧。</param>
    /// <param name="family">haiku, sonnet, opus のいずれか。</param>
    /// <returns>該当するモデル ID。なければ null。</returns>
    public static string? GetLatestIdInFamilyBySort(IReadOnlyList<string> modelIds, string family)
    {
        if (modelIds == null || modelIds.Count == 0 || string.IsNullOrWhiteSpace(family))
            return null;
        return modelIds
            .Where(id => string.Equals(GetModelFamily(id), family, StringComparison.OrdinalIgnoreCase))
            .OrderBy(id => id, StringComparer.Ordinal)
            .LastOrDefault();
    }

    /// <summary>保存済みモデルを、取得済み一覧内の同一ファミリの最新に更新する。一覧に該当がなければ現状のまま返す。</summary>
    /// <param name="currentModelId">現在保存されているモデル ID。</param>
    /// <param name="availableModelIds">Claude Code から取得したモデル ID 一覧。</param>
    /// <returns>同一ファミリの最新モデル ID。更新できなければ currentModelId をそのまま返す。</returns>
    public static string UpgradeToLatestInFamily(string? currentModelId, IReadOnlyList<string> availableModelIds)
    {
        if (string.IsNullOrWhiteSpace(currentModelId))
            return currentModelId ?? string.Empty;
        if (availableModelIds == null || availableModelIds.Count == 0)
            return currentModelId;
        var family = GetModelFamily(currentModelId);
        if (family == null)
            return currentModelId;
        var latest = GetLatestInFamily(availableModelIds, family);
        return !string.IsNullOrWhiteSpace(latest) ? latest : currentModelId;
    }
}
