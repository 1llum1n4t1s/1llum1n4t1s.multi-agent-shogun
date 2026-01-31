using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Shogun.Avalonia.Util;

namespace Shogun.Avalonia.Services;

/// <summary>
/// models.dev API から利用可能な Claude モデル ID 一覧を取得するサービス。
/// API キーは扱わない。
/// </summary>
public class ClaudeCodeModelsService : IClaudeModelsService
{
    /// <summary>models.dev API の URL。</summary>
    private const string ModelsDevApiUrl = "https://models.dev/api.json";

    private static readonly HttpClient SharedHttpClient = new();
    private static readonly Regex ModelIdRegex = new(@"claude[-0-9a-z\-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>「claude-code」は CLI パッケージ名でありモデル ID ではない。フォールバックに使わずエラー扱いする。</summary>
    public static bool IsInvalidModelId(string? id) =>
        string.Equals(id?.Trim(), "claude-code", StringComparison.OrdinalIgnoreCase);

    /// <summary>パッケージ名「claude-code」等を除外する。公式モデル ID はバージョン・日付で数字を含む。</summary>
    private static bool IsValidModelId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && !IsInvalidModelId(id) && ModelIdRegex.IsMatch(id);

    /// <summary>サービスを生成する。</summary>
    public ClaudeCodeModelsService()
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Id, string Name)>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var dev = await GetModelsFromModelsDevAsync(cancellationToken).ConfigureAwait(false);
        if (dev.Count > 0)
            Logger.Log($"取得したモデル一覧（models.dev: {dev.Count}件）: {string.Join(", ", dev.Select(x => x.Id))}", LogLevel.Info);
        else
            Logger.Log("models.dev からモデル一覧を取得できませんでした（0件）。", LogLevel.Warning);
        return dev;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetModelIdsAsync(CancellationToken cancellationToken = default)
    {
        var models = await GetModelsAsync(cancellationToken).ConfigureAwait(false);
        return models.Select(m => m.Id).ToList();
    }

    /// <summary>models.dev API から Claude モデル一覧（ID と表示名）を取得する。</summary>
    private async Task<IReadOnlyList<(string Id, string Name)>> GetModelsFromModelsDevAsync(CancellationToken cancellationToken)
    {
        var list = new List<(string Id, string Name)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var response = await SharedHttpClient.GetAsync(ModelsDevApiUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var provider in root.EnumerateObject())
            {
                if (!provider.Value.TryGetProperty("name", out var providerName) || providerName.GetString() != "Anthropic")
                    continue;
                if (!provider.Value.TryGetProperty("models", out var models))
                    continue;
                foreach (var model in models.EnumerateObject())
                {
                    var modelName = model.Value.TryGetProperty("name", out var nameEl) ? nameEl.GetString()?.Trim() : null;
                    if (string.IsNullOrEmpty(modelName) || !modelName.Contains("(latest)", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var id = model.Value.TryGetProperty("id", out var idEl) ? idEl.GetString()?.Trim() : model.Name;
                    if (string.IsNullOrEmpty(id)) continue;
                    if (id.IndexOf('/') is int i and >= 0)
                        id = id[(i + 1)..];
                    if (IsValidModelId(id) && seen.Add(id))
                        list.Add((id, modelName));
                }
            }
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (IsInvalidModelId(list[i].Id))
                    list.RemoveAt(i);
            }
        }
        catch (HttpRequestException ex)
        {
            Logger.Log($"models.dev 取得で HTTP 例外: {ex.Message}", LogLevel.Warning);
        }
        catch (JsonException ex)
        {
            Logger.Log($"models.dev JSON 解析で例外: {ex.Message}", LogLevel.Warning);
        }
        catch (OperationCanceledException)
        {
            Logger.Log("models.dev 取得がタイムアウトしました。", LogLevel.Warning);
        }
        catch (Exception ex)
        {
            Logger.Log($"models.dev 取得で例外: {ex.Message}", LogLevel.Warning);
        }
        return list;
    }
}
