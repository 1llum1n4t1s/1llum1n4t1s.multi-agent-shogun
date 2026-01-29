using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Shogun.Avalonia.Models;

namespace Shogun.Avalonia.Services;

/// <summary>
/// YAML ファイルでプロジェクト設定を永続化するサービス。
/// config/projects.yaml 等は当アプリで参照・更新してよい。フォーク元のコマンド系スクリプト
/// （shutsujin_departure.sh, first_setup.sh, instructions/* 等）は編集せず参照・起動のみとする（FORK_POLICY.md 参照）。
/// </summary>
public class ProjectService : IProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;

    /// <summary>プロジェクト設定ファイルのパスを指定してインスタンスを生成する。</summary>
    public ProjectService(string? filePath = null)
    {
        _filePath = filePath ?? GetDefaultProjectsPath();
    }

    /// <inheritdoc />
    public List<Project> GetProjects()
    {
        if (!File.Exists(_filePath))
        {
            EnsureProjectsFileExists();
            return new List<Project>();
        }

        try
        {
            var yamlContent = File.ReadAllText(_filePath);
            return ParseYaml(yamlContent);
        }
        catch
        {
            return new List<Project>();
        }
    }

    /// <summary>projects.yaml が無いときにディレクトリとデフォルト内容を新規作成する。</summary>
    private void EnsureProjectsFileExists()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        if (File.Exists(_filePath))
            return;
        const string defaultContent = "# Shogun projects\n# - id: \"project-id\"\n#   name: \"Project Name\"\n#   path: \"C:\\path\"\n";
        File.WriteAllText(_filePath, defaultContent);
    }

    /// <inheritdoc />
    public void SaveProjects(List<Project> projects)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var yamlContent = GenerateYaml(projects);
        File.WriteAllText(_filePath, yamlContent);
    }

    private static string GetDefaultProjectsPath()
    {
        var configDir = SettingsService.GetDefaultConfigDirectory();
        return Path.Combine(configDir, "projects.yaml");
    }

    private static List<Project> ParseYaml(string yamlContent)
    {
        var projects = new List<Project>();
        var lines = yamlContent.Split('\n');
        Project? currentProject = null;
        var inDescription = false;
        var descriptionLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            if (trimmed.StartsWith("- id:"))
            {
                if (currentProject != null)
                {
                    if (inDescription)
                        currentProject.Description = string.Join("\n", descriptionLines).Trim();
                    projects.Add(currentProject);
                }
                currentProject = new Project();
                inDescription = false;
                descriptionLines.Clear();
                var id = trimmed.Substring(5).Trim().Trim('"');
                currentProject.Id = id;
            }
            else if (currentProject != null)
            {
                if (trimmed.StartsWith("name:"))
                {
                    currentProject.Name = ExtractValue(trimmed.Substring(5));
                }
                else if (trimmed.StartsWith("path:"))
                {
                    currentProject.Path = ExtractValue(trimmed.Substring(5));
                }
                else if (trimmed.StartsWith("priority:"))
                {
                    currentProject.Priority = ExtractValue(trimmed.Substring(9));
                }
                else if (trimmed.StartsWith("status:"))
                {
                    currentProject.Status = ExtractValue(trimmed.Substring(7));
                }
                else if (trimmed.StartsWith("notion_url:"))
                {
                    currentProject.NotionUrl = ExtractValue(trimmed.Substring(11));
                }
                else if (trimmed.StartsWith("description:"))
                {
                    inDescription = true;
                    var desc = ExtractValue(trimmed.Substring(12));
                    if (!string.IsNullOrEmpty(desc))
                        descriptionLines.Add(desc);
                }
                else if (inDescription && (trimmed.StartsWith("  ") || trimmed.StartsWith("|")))
                {
                    var descLine = trimmed.TrimStart(' ', '|');
                    descriptionLines.Add(descLine);
                }
                else if (inDescription && !trimmed.StartsWith("-"))
                {
                    inDescription = false;
                }
            }
        }

        if (currentProject != null)
        {
            if (inDescription)
                currentProject.Description = string.Join("\n", descriptionLines).Trim();
            projects.Add(currentProject);
        }

        return projects;
    }

    private static string ExtractValue(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('"') && trimmed.EndsWith('"'))
            return trimmed.Substring(1, trimmed.Length - 2);
        return trimmed;
    }

    private static string GenerateYaml(List<Project> projects)
    {
        var lines = new List<string> { "projects:" };
        foreach (var project in projects)
        {
            lines.Add($"  - id: {project.Id}");
            lines.Add($"    name: \"{project.Name}\"");
            lines.Add($"    path: \"{project.Path}\"");
            lines.Add($"    priority: {project.Priority}");
            lines.Add($"    status: {project.Status}");
            if (!string.IsNullOrEmpty(project.NotionUrl))
                lines.Add($"    notion_url: \"{project.NotionUrl}\"");
            if (!string.IsNullOrEmpty(project.Description))
            {
                lines.Add("    description: |");
                foreach (var descLine in project.Description.Split('\n'))
                    lines.Add($"      {descLine}");
            }
            lines.Add("");
        }
        return string.Join("\n", lines);
    }
}
