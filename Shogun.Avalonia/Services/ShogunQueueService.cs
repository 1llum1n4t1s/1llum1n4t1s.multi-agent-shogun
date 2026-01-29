using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Shogun.Avalonia.Models;

namespace Shogun.Avalonia.Services;

/// <summary>
/// フォーク元の queue / dashboard を読み書きするサービス。
/// </summary>
public class ShogunQueueService : IShogunQueueService
{
    private readonly ISettingsService _settingsService;

    /// <summary>サービスを生成する。</summary>
    public ShogunQueueService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <inheritdoc />
    public int GetAshigaruCount()
    {
        var n = _settingsService.Get().AshigaruCount;
        if (n < 1) return 8;
        if (n > 20) return 20;
        return n;
    }

    /// <inheritdoc />
    public string GetRepoRoot()
    {
        var s = _settingsService.Get();
        if (!string.IsNullOrWhiteSpace(s.RepoRoot))
            return s.RepoRoot.TrimEnd(Path.DirectorySeparatorChar, '/');
        var configDir = SettingsService.GetDefaultConfigDirectory();
        var parent = Path.GetDirectoryName(configDir);
        return string.IsNullOrEmpty(parent) ? string.Empty : parent;
    }

    /// <inheritdoc />
    public IReadOnlyList<ShogunCommand> ReadShogunToKaro()
    {
        var path = Path.Combine(GetRepoRoot(), "queue", "shogun_to_karo.yaml");
        if (!File.Exists(path))
            return Array.Empty<ShogunCommand>();
        try
        {
            var content = File.ReadAllText(path);
            return ParseShogunToKaroYaml(content);
        }
        catch
        {
            return Array.Empty<ShogunCommand>();
        }
    }

    /// <inheritdoc />
    public string AppendCommand(string command, string? projectId = null, string? priority = null)
    {
        var repo = GetRepoRoot();
        var queueDir = Path.Combine(repo, "queue");
        var path = Path.Combine(queueDir, "shogun_to_karo.yaml");
        Directory.CreateDirectory(queueDir);

        var list = ReadShogunToKaro().ToList();
        var maxNum = 0;
        foreach (var item in list)
        {
            if (item.Id.StartsWith("cmd_", StringComparison.Ordinal) &&
                int.TryParse(item.Id.AsSpan(4), out var n) && n > maxNum)
                maxNum = n;
        }
        var id = $"cmd_{maxNum + 1:D3}";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        list.Add(new ShogunCommand
        {
            Id = id,
            Timestamp = timestamp,
            Command = command,
            Project = projectId,
            Priority = priority ?? "medium",
            Status = "pending"
        });
        WriteShogunToKaro(list);
        return id;
    }

    /// <inheritdoc />
    public string ReadDashboardMd()
    {
        var path = Path.Combine(GetRepoRoot(), "dashboard.md");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <inheritdoc />
    public string ReadTaskYaml(int ashigaruIndex)
    {
        var max = GetAshigaruCount();
        if (ashigaruIndex < 1 || ashigaruIndex > max)
            return string.Empty;
        var path = Path.Combine(GetRepoRoot(), "queue", "tasks", $"ashigaru{ashigaruIndex}.yaml");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <inheritdoc />
    public string ReadReportYaml(int ashigaruIndex)
    {
        var max = GetAshigaruCount();
        if (ashigaruIndex < 1 || ashigaruIndex > max)
            return string.Empty;
        var path = Path.Combine(GetRepoRoot(), "queue", "reports", $"ashigaru{ashigaruIndex}_report.yaml");
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <inheritdoc />
    public void WriteTaskYaml(int ashigaruIndex, string taskId, string parentCmd, string description, string? targetPath, string status, string timestamp)
    {
        var max = GetAshigaruCount();
        if (ashigaruIndex < 1 || ashigaruIndex > max)
            return;
        var dir = Path.Combine(GetRepoRoot(), "queue", "tasks");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"ashigaru{ashigaruIndex}.yaml");
        var targetPathStr = targetPath ?? "null";
        var sb = new StringBuilder();
        sb.AppendLine($"# 足軽{ashigaruIndex}専用タスクファイル");
        sb.AppendLine("task:");
        sb.AppendLine($"  task_id: {EscapeYaml(taskId)}");
        sb.AppendLine($"  parent_cmd: {EscapeYaml(parentCmd)}");
        sb.AppendLine($"  description: {EscapeYaml(description)}");
        sb.AppendLine($"  target_path: {EscapeYaml(targetPathStr)}");
        sb.AppendLine($"  status: {EscapeYaml(status)}");
        sb.AppendLine($"  timestamp: {EscapeYaml(timestamp)}");
        File.WriteAllText(path, sb.ToString());
    }

    /// <inheritdoc />
    public void WriteReportYaml(int ashigaruIndex, string taskId, string timestamp, string status, string result, bool skillCandidateFound = false, string? skillCandidateName = null, string? skillCandidateDescription = null, string? skillCandidateReason = null)
    {
        var max = GetAshigaruCount();
        if (ashigaruIndex < 1 || ashigaruIndex > max)
            return;
        var dir = Path.Combine(GetRepoRoot(), "queue", "reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"ashigaru{ashigaruIndex}_report.yaml");
        var sb = new StringBuilder();
        sb.AppendLine($"worker_id: ashigaru{ashigaruIndex}");
        sb.AppendLine($"task_id: {EscapeYaml(taskId)}");
        sb.AppendLine($"timestamp: {EscapeYaml(timestamp)}");
        sb.AppendLine($"status: {EscapeYaml(status)}");
        sb.AppendLine($"result: {EscapeYaml(result)}");
        sb.AppendLine("skill_candidate:");
        sb.AppendLine($"  found: {skillCandidateFound.ToString().ToLowerInvariant()}");
        sb.AppendLine($"  name: {(string.IsNullOrEmpty(skillCandidateName) ? "null" : EscapeYaml(skillCandidateName))}");
        sb.AppendLine($"  description: {(string.IsNullOrEmpty(skillCandidateDescription) ? "null" : EscapeYaml(skillCandidateDescription))}");
        sb.AppendLine($"  reason: {(string.IsNullOrEmpty(skillCandidateReason) ? "null" : EscapeYaml(skillCandidateReason))}");
        File.WriteAllText(path, sb.ToString());
    }

    /// <inheritdoc />
    public void WriteDashboardMd(string content)
    {
        var path = Path.Combine(GetRepoRoot(), "dashboard.md");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, content);
    }

    /// <inheritdoc />
    public void UpdateCommandStatus(string commandId, string status)
    {
        var list = ReadShogunToKaro().ToList();
        var cmd = list.FirstOrDefault(c => string.Equals(c.Id, commandId, StringComparison.Ordinal));
        if (cmd != null)
        {
            cmd.Status = status;
            WriteShogunToKaro(list);
        }
    }

    private static List<ShogunCommand> ParseShogunToKaroYaml(string content)
    {
        var list = new List<ShogunCommand>();
        var lines = content.Split('\n');
        var inQueue = false;
        ShogunCommand? current = null;
        var indent = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimEnd();
            if (trimmed.StartsWith("queue:", StringComparison.Ordinal))
            {
                inQueue = true;
                continue;
            }
            if (!inQueue)
                continue;
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;
            var itemIndent = line.Length - line.TrimStart().Length;
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                if (current != null)
                    list.Add(current);
                current = new ShogunCommand();
                var rest = trimmed.Substring(2).Trim();
                if (rest.StartsWith("id:", StringComparison.Ordinal))
                {
                    current.Id = ExtractYamlValue(rest.Substring(3).Trim());
                }
                indent = itemIndent;
                continue;
            }
            if (current != null && itemIndent > indent)
            {
                if (trimmed.StartsWith("id:", StringComparison.Ordinal))
                    current.Id = ExtractYamlValue(trimmed.Substring(3).Trim());
                else if (trimmed.StartsWith("timestamp:", StringComparison.Ordinal))
                    current.Timestamp = ExtractYamlValue(trimmed.Substring(10).Trim());
                else if (trimmed.StartsWith("command:", StringComparison.Ordinal))
                    current.Command = ExtractYamlValue(trimmed.Substring(8).Trim());
                else if (trimmed.StartsWith("project:", StringComparison.Ordinal))
                    current.Project = ExtractYamlValue(trimmed.Substring(8).Trim());
                else if (trimmed.StartsWith("priority:", StringComparison.Ordinal))
                    current.Priority = ExtractYamlValue(trimmed.Substring(9).Trim());
                else if (trimmed.StartsWith("status:", StringComparison.Ordinal))
                    current.Status = ExtractYamlValue(trimmed.Substring(7).Trim());
            }
        }
        if (current != null)
            list.Add(current);
        return list;
    }

    private static string ExtractYamlValue(string v)
    {
        var t = v.Trim();
        if (t.StartsWith('"') && t.EndsWith('"'))
            return t.Substring(1, t.Length - 2).Replace("\\\"", "\"");
        if (t.StartsWith('\'') && t.EndsWith('\''))
            return t.Substring(1, t.Length - 2);
        return t;
    }

    private void WriteShogunToKaro(IReadOnlyList<ShogunCommand> list)
    {
        var sb = new StringBuilder();
        sb.AppendLine("queue:");
        foreach (var c in list)
        {
            sb.AppendLine($"  - id: {EscapeYaml(c.Id)}");
            sb.AppendLine($"    timestamp: {EscapeYaml(c.Timestamp)}");
            sb.AppendLine($"    command: {EscapeYaml(c.Command)}");
            if (!string.IsNullOrEmpty(c.Project))
                sb.AppendLine($"    project: {EscapeYaml(c.Project)}");
            if (!string.IsNullOrEmpty(c.Priority))
                sb.AppendLine($"    priority: {EscapeYaml(c.Priority)}");
            sb.AppendLine($"    status: {EscapeYaml(c.Status)}");
        }
        var path = Path.Combine(GetRepoRoot(), "queue", "shogun_to_karo.yaml");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, sb.ToString());
    }

    /// <inheritdoc />
    public void WriteMasterStatus(DateTime updated, string? currentTask, string taskStatus, string? taskDescription, IReadOnlyList<TaskAssignmentItem>? assignments)
    {
        var repo = GetRepoRoot();
        if (string.IsNullOrEmpty(repo))
            return;
        var dir = Path.Combine(repo, "status");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "master_status.yaml");
        var karoStatus = taskStatus == "in_progress" ? "busy" : "idle";
        var subtaskCount = assignments?.Count ?? 0;
        var sb = new StringBuilder();
        sb.AppendLine("last_updated: " + EscapeYaml(updated.ToString("yyyy-MM-dd HH:mm")));
        sb.AppendLine("current_task: " + (string.IsNullOrEmpty(currentTask) ? "null" : EscapeYaml(currentTask)));
        sb.AppendLine("task_status: " + (string.IsNullOrEmpty(taskStatus) ? "idle" : taskStatus));
        sb.AppendLine("task_description: " + (string.IsNullOrEmpty(taskDescription) ? "null" : EscapeYaml(taskDescription)));
        sb.AppendLine("agents:");
        sb.AppendLine("  shogun:");
        sb.AppendLine("    status: idle");
        sb.AppendLine("    last_action: null");
        sb.AppendLine("  karo:");
        sb.AppendLine("    status: " + karoStatus);
        sb.AppendLine("    current_subtasks: " + subtaskCount);
        sb.AppendLine("    last_action: null");
        var maxAshigaru = GetAshigaruCount();
        for (var i = 1; i <= maxAshigaru; i++)
        {
            var a = assignments?.FirstOrDefault(x => x.Ashigaru == i);
            var ashigaruStatus = a == null ? "idle" : (taskStatus == "done" || taskStatus == "failed" ? "done" : "in_progress");
            var currentTaskId = a != null && taskStatus == "in_progress" ? (a.TaskId ?? "null") : "null";
            var lastCompleted = a != null && taskStatus == "done" ? (a.TaskId ?? "null") : "null";
            var progress = a != null && taskStatus == "done" ? 100 : (a != null ? 0 : 0);
            sb.AppendLine("  ashigaru" + i + ":");
            sb.AppendLine("    status: " + ashigaruStatus);
            sb.AppendLine("    current_task: " + (string.IsNullOrEmpty(currentTaskId) || currentTaskId == "null" ? "null" : EscapeYaml(currentTaskId)));
            sb.AppendLine("    progress: " + progress);
            sb.AppendLine("    last_completed: " + (string.IsNullOrEmpty(lastCompleted) || lastCompleted == "null" ? "null" : EscapeYaml(lastCompleted)));
        }
        File.WriteAllText(path, sb.ToString());
    }

    private static string EscapeYaml(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "\"\"";
        if (s.Contains('\n') || s.Contains('"') || s.Contains('#') || s.IndexOfAny(new[] { ':', '[', ']', '{', '}', ',' }) >= 0)
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return "\"" + s + "\"";
    }
}
