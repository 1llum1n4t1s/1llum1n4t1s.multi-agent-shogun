using System.Collections.Generic;
using VYaml.Annotations;

namespace Shogun.Avalonia.Models;

/// <summary>
/// 家老が返すタスク割り当て（YAML パース用）。
/// </summary>
[YamlObject]
public partial class TaskAssignmentYaml
{
    /// <summary>割り当て一覧。</summary>
    [YamlMember("tasks")]
    public List<TaskAssignmentItem>? Assignments { get; set; }
}

/// <summary>
/// 1 件の足軽へのタスク割り当て。
/// </summary>
[YamlObject]
public partial class TaskAssignmentItem
{
    /// <summary>足軽番号（1～8）。</summary>
    [YamlMember("ashigaru_id")]
    public int Ashigaru { get; set; }

    /// <summary>タスク ID（例: cmd_001_1）。</summary>
    [YamlMember("task_id")]
    public string? TaskId { get; set; }

    /// <summary>親コマンド ID（例: cmd_001）。</summary>
    [YamlMember("parent_cmd")]
    public string? ParentCmd { get; set; }

    /// <summary>タスク説明。</summary>
    [YamlMember("description")]
    public string? Description { get; set; }

    /// <summary>対象パス（任意）。</summary>
    [YamlMember("target_path")]
    public string? TargetPath { get; set; }
}
