using System.Collections.Generic;
using VYaml.Annotations;

namespace Shogun.Avalonia.Models;

[YamlObject(NamingConvention.LowerCamelCase)]
public partial class ProjectsWrapper
{
    [YamlMember("projects")]
    public List<Project> Projects { get; set; } = new();
}

[YamlObject(NamingConvention.LowerCamelCase)]
public partial class ShogunQueueWrapper
{
    [YamlMember("queue")]
    public List<ShogunCommand> Queue { get; set; } = new();
}

[YamlObject(NamingConvention.LowerCamelCase)]
public partial class TaskWrapper
{
    [YamlMember("task")]
    public TaskItemYaml? Task { get; set; }
}

[YamlObject(NamingConvention.LowerCamelCase)]
public partial class TaskItemYaml
{
    [YamlMember("task_id")]
    public string? TaskId { get; set; }
    [YamlMember("parent_cmd")]
    public string? ParentCmd { get; set; }
    [YamlMember("description")]
    public string? Description { get; set; }
    [YamlMember("target_path")]
    public string? TargetPath { get; set; }
    [YamlMember("status")]
    public string? Status { get; set; }
    [YamlMember("timestamp")]
    public string? Timestamp { get; set; }
}

[YamlObject(NamingConvention.LowerCamelCase)]
public partial class ReportYaml
{
    [YamlMember("worker_id")]
    public string? WorkerId { get; set; }
    [YamlMember("task_id")]
    public string? TaskId { get; set; }
    [YamlMember("timestamp")]
    public string? Timestamp { get; set; }
    [YamlMember("status")]
    public string? Status { get; set; }
    [YamlMember("result")]
    public string? Result { get; set; }
    [YamlMember("skill_candidate")]
    public SkillCandidateYaml? SkillCandidate { get; set; }
}

[YamlObject(NamingConvention.LowerCamelCase)]
public partial class SkillCandidateYaml
{
    [YamlMember("found")]
    public bool Found { get; set; }
    [YamlMember("name")]
    public string? Name { get; set; }
    [YamlMember("description")]
    public string? Description { get; set; }
    [YamlMember("reason")]
    public string? Reason { get; set; }
}

[YamlObject(NamingConvention.LowerCamelCase)]
public partial class MasterStatusYaml
{
    [YamlMember("last_updated")]
    public string? LastUpdated { get; set; }
    [YamlMember("current_task")]
    public string? CurrentTask { get; set; }
    [YamlMember("task_status")]
    public string? TaskStatus { get; set; }
    [YamlMember("task_description")]
    public string? TaskDescription { get; set; }
    [YamlMember("agents")]
    public Dictionary<string, AgentStatusYaml> Agents { get; set; } = new();
}

[YamlObject(NamingConvention.LowerCamelCase)]
public partial class AgentStatusYaml
{
    [YamlMember("status")]
    public string? Status { get; set; }
    [YamlMember("last_action")]
    public string? LastAction { get; set; }
    [YamlMember("current_subtasks")]
    public int? CurrentSubtasks { get; set; }
    [YamlMember("current_task")]
    public string? CurrentTask { get; set; }
    [YamlMember("progress")]
    public int? Progress { get; set; }
    [YamlMember("last_completed")]
    public string? LastCompleted { get; set; }
}
