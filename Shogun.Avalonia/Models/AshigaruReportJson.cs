using VYaml.Annotations;

namespace Shogun.Avalonia.Models;

/// <summary>
/// 足軽が返す報告（YAML パース用）。フォーク元の queue/reports/ashigaru{N}_report.yaml 形式に合わせる。
/// </summary>
[YamlObject]
public partial class AshigaruReportJson
{
    /// <summary>タスク ID。</summary>
    [YamlMember("task_id")]
    public string? TaskId { get; set; }

    /// <summary>発令日時（ISO8601）。</summary>
    [YamlMember("timestamp")]
    public string? Timestamp { get; set; }

    /// <summary>状態（done 等）。</summary>
    [YamlMember("status")]
    public string? Status { get; set; }

    /// <summary>結果要約。</summary>
    [YamlMember("result")]
    public string? Result { get; set; }

    /// <summary>スキル化候補を発見したか（必須）。</summary>
    [YamlMember("skill_candidate_found")]
    public bool SkillCandidateFound { get; set; }

    /// <summary>スキル名（found が true の場合）。</summary>
    [YamlMember("skill_candidate_name")]
    public string? SkillCandidateName { get; set; }

    /// <summary>スキル説明（found が true の場合）。</summary>
    [YamlMember("skill_candidate_description")]
    public string? SkillCandidateDescription { get; set; }

    /// <summary>スキル化理由（found が true の場合）。</summary>
    [YamlMember("skill_candidate_reason")]
    public string? SkillCandidateReason { get; set; }
}
