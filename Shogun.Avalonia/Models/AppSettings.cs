using VYaml.Annotations;

namespace Shogun.Avalonia.Models;

/// <summary>
/// アプリケーション設定モデル。
/// skill.*, screenshot.*, logging.* は config/settings.yaml。足軽人数・役割別モデルはアプリで編集可能。
/// </summary>
[YamlObject]
public partial class AppSettings
{
    /// <summary>足軽の人数（1～20）。将軍・家老以外の実働エージェント数。</summary>
    public int AshigaruCount { get; set; } = 8;

    /// <summary>スキル保存先（config: skill.save_path）。生成スキルの保存先。</summary>
    public string SkillSavePath { get; set; } = string.Empty;

    /// <summary>ローカルスキル保存先（config: skill.local_path）。プロジェクト専用スキル。</summary>
    public string SkillLocalPath { get; set; } = string.Empty;

    /// <summary>スクリーンショット保存先（config: screenshot.path）。</summary>
    public string ScreenshotPath { get; set; } = string.Empty;

    /// <summary>ログレベル（config: logging.level）。debug, info, warn, error。</summary>
    public string LogLevel { get; set; } = "info";

    /// <summary>ログ保存先（config: logging.path）。</summary>
    public string LogPath { get; set; } = string.Empty;

    /// <summary>AI モデル名（共通・未指定時）。Claude Code から取得した値のみ使用。</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>将軍用モデル（空のときは ModelName を使用。殿の入力を受け家老への指示文を生成する）。</summary>
    public string ModelShogun { get; set; } = string.Empty;

    /// <summary>家老用モデル（空のときは ModelName を使用）。</summary>
    public string ModelKaro { get; set; } = string.Empty;

    /// <summary>足軽用モデル（空のときは ModelName を使用）。</summary>
    public string ModelAshigaru { get; set; } = string.Empty;

    /// <summary>将軍用モデルで Thinking を使うか。</summary>
    public bool ThinkingShogun { get; set; }

    /// <summary>家老用モデルで Thinking を使うか。</summary>
    public bool ThinkingKaro { get; set; }

    /// <summary>足軽用モデルで Thinking を使うか。</summary>
    public bool ThinkingAshigaru { get; set; }

    /// <summary>家老のコード改修権限モード。AlwaysAllow / AlwaysReject / PromptUser。</summary>
    public string KaroExecutionPermissionMode { get; set; } = "PromptUser";

    /// <summary>API エンドポイント（Claude では未使用。将来用に保持）。config にはなし。</summary>
    public string ApiEndpoint { get; set; } = string.Empty;

    /// <summary>ワークスペースルート（queue/dashboard/instructions の親フォルダ）。空のときは config の親を使用。</summary>
    public string RepoRoot { get; set; } = string.Empty;

    /// <summary>ドキュメント出力先ルート（報告書等の作成先）。</summary>
    public string DocumentRoot { get; set; } = string.Empty;
}
