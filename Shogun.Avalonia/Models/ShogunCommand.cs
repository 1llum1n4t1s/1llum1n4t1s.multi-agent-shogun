using VYaml.Annotations;

namespace Shogun.Avalonia.Models;

/// <summary>
/// 将軍→家老の指示キュー 1 件（queue/shogun_to_karo.yaml の queue 要素）。
/// </summary>
[YamlObject]
public partial class ShogunCommand
{
    /// <summary>コマンド ID（例: cmd_001）。</summary>
    [YamlMember("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>発令日時（ISO8601）。</summary>
    [YamlMember("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>指示内容。</summary>
    [YamlMember("command")]
    public string Command { get; set; } = string.Empty;

    /// <summary>対象プロジェクト ID（任意）。</summary>
    [YamlMember("project")]
    public string? Project { get; set; }

    /// <summary>優先度（high / medium / low 等、任意）。</summary>
    [YamlMember("priority")]
    public string? Priority { get; set; }

    /// <summary>状態（pending / in_progress / done 等）。</summary>
    [YamlMember("status")]
    public string Status { get; set; } = "pending";
}
