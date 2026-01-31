using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Shogun.Avalonia.Models;
using VYaml.Serialization;

namespace Shogun.Avalonia.Services;

/// <summary>
/// フォーク元の queue / dashboard を読み書きするサービス。
/// </summary>
public class ShogunQueueService : IShogunQueueService
{
    private readonly ISettingsService _settingsService;
    private readonly IProjectService _projectService;

    /// <summary>サービスを生成する。</summary>
    public ShogunQueueService(ISettingsService settingsService, IProjectService? projectService = null)
    {
        _settingsService = settingsService;
        _projectService = projectService ?? new ProjectService();
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
        if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
            return parent.TrimEnd(Path.DirectorySeparatorChar, '/');
        
        return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, '/');
    }

    /// <inheritdoc />
    public IReadOnlyList<ShogunCommand> ReadShogunToKaro()
    {
        var path = Path.Combine(GetRepoRoot(), "queue", "shogun_to_karo.yaml");
        if (!File.Exists(path))
            return Array.Empty<ShogunCommand>();
        try
        {
            var bytes = File.ReadAllBytes(path);
            var wrapper = YamlSerializer.Deserialize<ShogunQueueWrapper>(bytes);
            return wrapper?.Queue ?? new List<ShogunCommand>();
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
        
        var task = new TaskWrapper
        {
            Task = new TaskItemYaml
            {
                TaskId = taskId,
                ParentCmd = parentCmd,
                Description = description,
                TargetPath = targetPath,
                Status = status,
                Timestamp = timestamp
            }
        };
        var bytes = YamlSerializer.Serialize(task);
        File.WriteAllBytes(path, bytes.ToArray());
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
        
        var report = new ReportYaml
        {
            WorkerId = $"ashigaru{ashigaruIndex}",
            TaskId = taskId,
            Timestamp = timestamp,
            Status = status,
            Result = result,
            SkillCandidate = new SkillCandidateYaml
            {
                Found = skillCandidateFound,
                Name = skillCandidateName,
                Description = skillCandidateDescription,
                Reason = skillCandidateReason
            }
        };
        var bytes = YamlSerializer.Serialize(report);
        File.WriteAllBytes(path, bytes.ToArray());
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

    private void WriteShogunToKaro(IReadOnlyList<ShogunCommand> list)
    {
        var wrapper = new ShogunQueueWrapper { Queue = list.ToList() };
        var bytes = YamlSerializer.Serialize(wrapper);
        var path = Path.Combine(GetRepoRoot(), "queue", "shogun_to_karo.yaml");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, bytes.ToArray());
    }

    /// <inheritdoc />
    public void WriteMasterStatus(DateTime updated, string? currentTask, string taskStatus, string? taskDescription, IReadOnlyList<TaskAssignmentItem>? assignments)
    {
        // master_status.yaml は構造が複雑なため、一旦現状維持か、必要なら VYaml 用のモデルを作成する。
        // ここでは VYaml を使用するようにモデルを定義して移行する。
        var repo = GetRepoRoot();
        if (string.IsNullOrEmpty(repo))
            return;
        var dir = Path.Combine(repo, "status");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "master_status.yaml");

        var status = new MasterStatusYaml
        {
            LastUpdated = updated.ToString("yyyy-MM-dd HH:mm"),
            CurrentTask = currentTask,
            TaskStatus = taskStatus,
            TaskDescription = taskDescription,
            Agents = new Dictionary<string, AgentStatusYaml>()
        };

        status.Agents["shogun"] = new AgentStatusYaml { Status = "idle", LastAction = null };
        status.Agents["karo"] = new AgentStatusYaml { Status = taskStatus == "in_progress" ? "busy" : "idle", CurrentSubtasks = assignments?.Count ?? 0, LastAction = null };

        var maxAshigaru = GetAshigaruCount();
        for (var i = 1; i <= maxAshigaru; i++)
        {
            var a = assignments?.FirstOrDefault(x => x.Ashigaru == i);
            var ashigaruStatus = a == null ? "idle" : (taskStatus == "done" || taskStatus == "failed" ? "done" : "in_progress");
            var currentTaskId = a != null && taskStatus == "in_progress" ? (a.TaskId ?? "null") : "null";
            var lastCompleted = a != null && taskStatus == "done" ? (a.TaskId ?? "null") : "null";
            var progress = a != null && taskStatus == "done" ? 100 : (a != null ? 0 : 0);

            status.Agents[$"ashigaru{i}"] = new AgentStatusYaml
            {
                Status = ashigaruStatus,
                CurrentTask = currentTaskId == "null" ? null : currentTaskId,
                Progress = progress,
                LastCompleted = lastCompleted == "null" ? null : lastCompleted
            };
        }

        var bytes = YamlSerializer.Serialize(status);
        File.WriteAllBytes(path, bytes.ToArray());
    }

    /// <inheritdoc />
    public AppSettings GetSettings()
    {
        return _settingsService.Get();
    }

    /// <inheritdoc />
    public string GetDocumentOutputPath(string? projectId = null)
    {
        var settings = _settingsService.Get();
        var root = settings.DocumentRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Shogun")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Shogun");
        }
        else
        {
            root = Environment.ExpandEnvironmentVariables(root);
        }

        if (string.IsNullOrEmpty(projectId))
            return root;

        var project = _projectService.GetProjects().FirstOrDefault(p => p.Id == projectId);
        if (project == null || string.IsNullOrEmpty(project.Name))
            return root;

        return Path.Combine(root, project.Name);
    }
}
