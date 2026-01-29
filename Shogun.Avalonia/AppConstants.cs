using System;

namespace Shogun.Avalonia;

/// <summary>
/// アプリ内で参照する定数。足軽人数は設定（1～20）で変更可能。既定値は AppSettings.AshigaruCount = 8。
/// モデル名は Claude Code（ClaudeModelsService 等）から取得した値のみ使用し、定数・フォールバックは使用しない。
/// </summary>
public static class AppConstants
{
    /// <summary>足軽人数の既定値（設定未読時などのフォールバック）。</summary>
    public const int DefaultAshigaruCount = 8;

    /// <summary>Velopack 更新元の GitHub リポジトリ URL。環境変数 VELOPACK_GITHUB_REPO で上書き可能。</summary>
    public static readonly string VelopackGitHubRepoUrl =
        Environment.GetEnvironmentVariable("VELOPACK_GITHUB_REPO")?.Trim()
        ?? "https://github.com/1llum1n4t1s/multi-agent-shogun";
}
