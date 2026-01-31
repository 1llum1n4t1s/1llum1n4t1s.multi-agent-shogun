using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Shogun.Avalonia.Models;
using Shogun.Avalonia.Util;
using VYaml.Serialization;

namespace Shogun.Avalonia.Services;

/// <summary>
/// YAML ファイルでプロジェクト設定を永続化するサービス。
/// </summary>
public class ProjectService : IProjectService
{
    private readonly string _filePath;

    public ProjectService(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultProjectsPath();
    }

    public List<Project> GetProjects()
    {
        if (!File.Exists(_filePath))
        {
            EnsureProjectsFileExists();
            return new List<Project>();
        }

        var text = File.ReadAllText(_filePath);
        var manualProjects = ParseProjectsManually(text);
        if (manualProjects.Count > 0)
            return manualProjects;

        try
        {
            var bytes = File.ReadAllBytes(_filePath);
            var wrapper = YamlSerializer.Deserialize<ProjectsWrapper>(bytes);
            if (wrapper?.Projects != null && wrapper.Projects.Count > 0)
                return wrapper.Projects;
        }
        catch (Exception ex)
        {
            Logger.Log($"projects.yaml の VYaml パースに失敗しました: {ex.Message}", LogLevel.Warning);
        }
        return new List<Project>();
    }

    private List<Project> ParseProjectsManually(string yaml)
    {
        var projects = new List<Project>();
        var lines = yaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        Project? currentProject = null;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

            var dashIdIdx = trimmed.IndexOf("- id:", StringComparison.Ordinal);
            if (dashIdIdx >= 0)
            {
                var valuePart = trimmed.Substring(dashIdIdx + 5).Trim();
                currentProject = new Project { Id = ExtractValue(valuePart) };
                projects.Add(currentProject);
            }
            else if (currentProject != null)
            {
                if (trimmed.StartsWith("name:")) currentProject.Name = ExtractValue(trimmed.Substring(5));
                else if (trimmed.StartsWith("path:")) currentProject.Path = ExtractValue(trimmed.Substring(5));
                else if (trimmed.StartsWith("priority:")) currentProject.Priority = ExtractValue(trimmed.Substring(9));
                else if (trimmed.StartsWith("status:")) currentProject.Status = ExtractValue(trimmed.Substring(7));
                else if (trimmed.StartsWith("notion_url:")) currentProject.NotionUrl = ExtractValue(trimmed.Substring(11));
                else if (trimmed.StartsWith("description:")) currentProject.Description = ExtractValue(trimmed.Substring(12));
            }
        }
        return projects;
    }

    private string ExtractValue(string v)
    {
        v = v.Trim();
        if (v.StartsWith('"') && v.EndsWith('"') && v.Length >= 2) return v.Substring(1, v.Length - 2).Replace("\\\\", "\\");
        if (v.StartsWith('\'') && v.EndsWith('\'') && v.Length >= 2) return v.Substring(1, v.Length - 2);
        return v;
    }

    private void EnsureProjectsFileExists()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        if (File.Exists(_filePath))
            return;
        const string defaultContent = "projects: []\n";
        File.WriteAllText(_filePath, defaultContent);
    }

    public void SaveProjects(List<Project> projects)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var yaml = BuildProjectsYaml(projects);
        File.WriteAllText(_filePath, yaml, new UTF8Encoding(false));
    }

    private static string BuildProjectsYaml(List<Project> projects)
    {
        var sb = new StringBuilder();
        sb.AppendLine("projects:");
        foreach (var p in projects)
        {
            sb.AppendLine("  - id: " + QuoteYamlScalar(p.Id));
            sb.AppendLine("    name: " + QuoteYamlScalar(p.Name));
            sb.AppendLine("    path: " + QuoteYamlScalar(p.Path));
            sb.AppendLine("    priority: " + QuoteYamlScalar(p.Priority));
            sb.AppendLine("    status: " + QuoteYamlScalar(p.Status));
            sb.AppendLine("    notion_url: " + QuoteYamlScalar(p.NotionUrl));
            sb.AppendLine("    description: " + QuoteYamlScalar(p.Description));
        }
        return sb.ToString();
    }

    private static string QuoteYamlScalar(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "''";
        if (value.Contains('\\', StringComparison.Ordinal) || value.Contains('"', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains(':', StringComparison.Ordinal))
            return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        if (value.Contains(' ', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal))
            return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        return value;
    }

    private static string GetDefaultProjectsPath()
    {
        var configDir = SettingsService.GetDefaultConfigDirectory();
        return Path.Combine(configDir, "projects.yaml");
    }
}
