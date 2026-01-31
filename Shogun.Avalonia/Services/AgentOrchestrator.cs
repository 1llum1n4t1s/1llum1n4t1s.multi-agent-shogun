using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Shogun.Avalonia.Models;
using Shogun.Avalonia.Util;
using VYaml.Serialization;

namespace Shogun.Avalonia.Services;

/// <summary>
/// 将軍→家老→足軽のフローをアプリ内で完結させるオーケストレーター。
/// </summary>
public class AgentOrchestrator : IAgentOrchestrator
{
    private readonly IShogunQueueService _queueService;
    private readonly IAiService _aiService;
    private readonly IInstructionsLoader _instructionsLoader;
    private readonly ISettingsService _settingsService;

    /// <summary>サービスを生成する。</summary>
    public AgentOrchestrator(IShogunQueueService queueService, IAiService aiService, IInstructionsLoader instructionsLoader, ISettingsService? settingsService = null)
    {
        _queueService = queueService;
        _aiService = aiService;
        _instructionsLoader = instructionsLoader;
        _settingsService = settingsService ?? new SettingsService();
    }

    /// <inheritdoc />
    public async Task<string> ResolveShogunCommandAsync(string userInput, string? projectId, CancellationToken cancellationToken = default)
    {
        Logger.Log("ResolveShogunCommandAsync: CLI 連携モードのため入力をそのまま返します。", LogLevel.Debug);
        return userInput;
    }

    /// <inheritdoc />
    public async Task<string> RunAsync(string commandId, CancellationToken cancellationToken = default)
    {
        Logger.Log($"AgentOrchestrator.RunAsync 開始: commandId='{commandId}'", LogLevel.Info);
        return "エラー: 当アプリでは直接のオーケストレーションは行いません。Claude Code CLI を使用してください。";
    }

    private static string ExtractYamlFromResponse(string response)
    {
        var trimmed = response.Trim();
        var match = Regex.Match(trimmed, @"```(?:yaml)?\s*([\s\S]*?)```", RegexOptions.IgnoreCase);
        if (match.Success)
            return match.Groups[1].Value.Trim();

        // コードブロックがない場合、YAML らしい開始部分を探す
        var lines = trimmed.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        var yamlLines = new List<string>();
        bool foundStart = false;
        foreach (var line in lines)
        {
            if (!foundStart && (line.TrimStart().StartsWith("tasks:", StringComparison.OrdinalIgnoreCase) || 
                                line.TrimStart().StartsWith("---") ||
                                line.TrimStart().StartsWith("worker_id:", StringComparison.OrdinalIgnoreCase) ||
                                line.TrimStart().StartsWith("task:", StringComparison.OrdinalIgnoreCase)))
            {
                foundStart = true;
            }
            if (foundStart)
            {
                yamlLines.Add(line);
            }
        }
        if (yamlLines.Count > 0)
            return string.Join("\n", yamlLines);

        return trimmed;
    }

    private static List<TaskAssignmentItem>? ParseTaskAssignments(string response)
    {
        try
        {
            var yaml = ExtractYamlFromResponse(response);
            var bytes = Encoding.UTF8.GetBytes(yaml);
            var wrapper = YamlSerializer.Deserialize<TaskAssignmentYaml>(bytes);
            return wrapper?.Assignments;
        }
        catch
        {
            return null;
        }
    }

    private static AshigaruReportJson? ParseAshigaruReport(string response)
    {
        try
        {
            var yaml = ExtractYamlFromResponse(response);
            var bytes = Encoding.UTF8.GetBytes(yaml);
            return YamlSerializer.Deserialize<AshigaruReportJson>(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static string ExtractReportResult(string reportYaml)
    {
        if (string.IsNullOrWhiteSpace(reportYaml))
            return "-";
        var line = reportYaml.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith("result:", StringComparison.Ordinal));
        if (line == null)
            return "-";
        var value = line.IndexOf(':', StringComparison.Ordinal) >= 0 ? line.Substring(line.IndexOf(':', StringComparison.Ordinal) + 1).Trim() : "";
        if (value.StartsWith('"') && value.EndsWith('"'))
            value = value.Substring(1, value.Length - 2).Replace("\\\"", "\"");
        return string.IsNullOrEmpty(value) ? "-" : value;
    }

    private string ReadProjectsYaml()
    {
        var path = System.IO.Path.Combine(_queueService.GetRepoRoot(), "config", "projects.yaml");
        return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : "";
    }

    private void UpdateDashboardInProgress(string currentDashboard, string commandId, List<TaskAssignmentItem> assignments)
    {
        var inProgressContent = $"- {commandId}: タスク分配済み（" + string.Join(", ", assignments.Select(a => $"足軽{a.Ashigaru}: {(a.Description?.Length > 20 ? a.Description.Substring(0, 20) + "…" : a.Description)}")) + "）";
        var newContent = AppendToDashboardSection(currentDashboard, "進行中", inProgressContent);
        var updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        newContent = Regex.Replace(newContent, @"最終更新[^\n]*", "最終更新: " + updated);
        _queueService.WriteDashboardMd(newContent);
    }

    private void UpdateDashboardResults(string currentDashboard, List<(string time, string battlefield, string mission, string result)> rows)
    {
        if (rows.Count == 0)
            return;
        var lines = currentDashboard.Split('\n').ToList();
        var updated = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        var tableRows = rows.Select(r => $"| {r.time} | {EscapeTable(r.battlefield)} | {EscapeTable(r.mission)} | {EscapeTable(r.result)} |").ToList();
        var newContent = AppendDashboardTableRows(lines, "本日の戦果", tableRows, updated);
        _queueService.WriteDashboardMd(newContent);
    }

    private static string ReplaceDashboardSection(List<string> lines, string sectionKeyword, string newBody, string updatedTimestamp)
    {
        var result = new List<string>();
        var i = 0;
        while (i < lines.Count)
        {
            result.Add(lines[i]);
            if (lines[i].Contains("最終更新", StringComparison.Ordinal))
            {
                i++;
                result[result.Count - 1] = "最終更新: " + updatedTimestamp;
                if (lines[i].Contains("(Last Updated)", StringComparison.Ordinal))
                    result[result.Count - 1] += " (Last Updated)";
                continue;
            }
            if (lines[i].StartsWith("## ", StringComparison.Ordinal) && lines[i].Contains(sectionKeyword, StringComparison.Ordinal))
            {
                i++;
                result.Add(newBody);
                while (i < lines.Count && !lines[i].TrimStart().StartsWith("## ", StringComparison.Ordinal))
                    i++;
                continue;
            }
            i++;
        }
        return string.Join(Environment.NewLine, result);
    }

    private static string AppendDashboardTableRows(List<string> lines, string sectionKeyword, List<string> newRows, string updatedTimestamp)
    {
        var result = new List<string>();
        var i = 0;
        while (i < lines.Count)
        {
            result.Add(lines[i]);
            if (lines[i].Contains("最終更新", StringComparison.Ordinal))
            {
                var idx = result.Count - 1;
                result[idx] = Regex.Replace(result[idx], @"最終更新[^\n]*", "最終更新: " + updatedTimestamp);
                i++;
                continue;
            }
            if (lines[i].StartsWith("## ", StringComparison.Ordinal) && lines[i].Contains(sectionKeyword, StringComparison.Ordinal))
            {
                i++;
                while (i < lines.Count && !lines[i].TrimStart().StartsWith("## ", StringComparison.Ordinal))
                {
                    result.Add(lines[i]);
                    if (lines[i].StartsWith("|", StringComparison.Ordinal) && lines[i].Contains("---", StringComparison.Ordinal))
                    {
                        foreach (var row in newRows)
                            result.Add(row);
                    }
                    i++;
                }
                continue;
            }
            i++;
        }
        return string.Join(Environment.NewLine, result);
    }

    private static string EscapeTable(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "-";
        return s.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", "").Replace("\n", " ");
    }

    /// <summary>指定セクションの末尾に 1 行を追記する（フォーク元: 進行中・スキル化候補・要対応の追記用）。</summary>
    private static string AppendToDashboardSection(string currentDashboard, string sectionKeyword, string newLine)
    {
        var lines = currentDashboard.Split('\n').ToList();
        var result = new List<string>();
        for (var i = 0; i < lines.Count; i++)
        {
            result.Add(lines[i]);
            if (lines[i].StartsWith("## ", StringComparison.Ordinal) && lines[i].Contains(sectionKeyword, StringComparison.Ordinal))
            {
                i++;
                while (i < lines.Count && !lines[i].TrimStart().StartsWith("## ", StringComparison.Ordinal))
                {
                    result.Add(lines[i]);
                    i++;
                }
                result.Add(newLine);
                if (i < lines.Count)
                    result.Add(lines[i]);
            }
        }
        return string.Join(Environment.NewLine, result);
    }
}
