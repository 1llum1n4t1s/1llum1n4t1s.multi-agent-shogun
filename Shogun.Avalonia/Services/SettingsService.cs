using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Shogun.Avalonia.Models;

namespace Shogun.Avalonia.Services;

/// <summary>設定サービス。すべての設定を config/settings.yaml で一元管理する。</summary>
public class SettingsService : ISettingsService
{
    private readonly string _yamlPath;
    private AppSettings? _current;

    /// <summary>設定ファイルのパスを指定してインスタンスを生成する。</summary>
    /// <param name="yamlPath">config/settings.yaml のパス。null のとき既定の config ディレクトリを使用。</param>
    public SettingsService(string? yamlPath = null, string? appDataYamlPath = null)
    {
        _yamlPath = yamlPath ?? GetDefaultSettingsYamlPath();
    }

    /// <inheritdoc />
    public AppSettings Get()
    {
        if (_current != null)
            return _current;

        EnsureYamlDefaults();
        var settings = ReadYamlAll();
        _current = settings;
        return _current;
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        _current = settings;
        WriteYamlAll(settings);
    }

    private AppSettings ReadYamlAll()
    {
        var s = new AppSettings();
        if (!File.Exists(_yamlPath))
            return s;

        var lines = File.ReadAllLines(_yamlPath);
        var inSkill = false;
        var inScreenshot = false;
        var inLogging = false;
        var inAgents = false;
        var inModel = false;
        var inThinking = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            // セクション判定（インデントなし）
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                inSkill = trimmed.StartsWith("skill:");
                inScreenshot = trimmed.StartsWith("screenshot:");
                inLogging = trimmed.StartsWith("logging:");
                inAgents = trimmed.StartsWith("agents:");
                
                if (trimmed.StartsWith("ashigaru_count:"))
                    s.AshigaruCount = int.TryParse(ExtractYamlValue(trimmed.Substring(15)), out var n) ? n : 8;
                if (trimmed.StartsWith("karo_permission_mode:"))
                    s.KaroExecutionPermissionMode = ExtractYamlValue(trimmed.Substring(21));
                if (trimmed.StartsWith("document_root:"))
                    s.DocumentRoot = ExtractYamlValue(trimmed.Substring(14));
                
                inModel = inThinking = false;
                continue;
            }

            // セクション内（インデントあり）
            if (inSkill)
            {
                if (trimmed.StartsWith("save_path:")) s.SkillSavePath = ExtractYamlValue(trimmed.Substring(10));
                if (trimmed.StartsWith("local_path:")) s.SkillLocalPath = ExtractYamlValue(trimmed.Substring(11));
            }
            else if (inScreenshot)
            {
                if (trimmed.StartsWith("windows_path:") || trimmed.StartsWith("path:"))
                {
                    var v = ExtractYamlValue(trimmed.Substring(trimmed.IndexOf(':') + 1));
                    if (!string.IsNullOrEmpty(v)) s.ScreenshotPath = v;
                }
            }
            else if (inLogging)
            {
                if (trimmed.StartsWith("level:")) s.LogLevel = ExtractYamlValue(trimmed.Substring(6));
                if (trimmed.StartsWith("path:")) s.LogPath = ExtractYamlValue(trimmed.Substring(5));
            }
            else if (inAgents)
            {
                if (trimmed.StartsWith("model:")) { inModel = true; inThinking = false; continue; }
                if (trimmed.StartsWith("thinking:")) { inThinking = true; inModel = false; continue; }

                if (inModel)
                {
                    if (trimmed.StartsWith("shogun:")) s.ModelShogun = ExtractYamlValue(trimmed.Substring(7));
                    if (trimmed.StartsWith("karo:")) s.ModelKaro = ExtractYamlValue(trimmed.Substring(5));
                    if (trimmed.StartsWith("ashigaru:")) s.ModelAshigaru = ExtractYamlValue(trimmed.Substring(9));
                }
                else if (inThinking)
                {
                    if (trimmed.StartsWith("shogun:")) s.ThinkingShogun = ExtractYamlValue(trimmed.Substring(7)).Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (trimmed.StartsWith("karo:")) s.ThinkingKaro = ExtractYamlValue(trimmed.Substring(5)).Equals("true", StringComparison.OrdinalIgnoreCase);
                    if (trimmed.StartsWith("ashigaru:")) s.ThinkingAshigaru = ExtractYamlValue(trimmed.Substring(9)).Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        if (string.IsNullOrEmpty(s.DocumentRoot)) s.DocumentRoot = GetDefaultDocumentRoot();
        return s;
    }

    private void WriteYamlAll(AppSettings s)
    {
        var quote = (string val) => "\"" + (val ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        
        var sb = new StringBuilder();
        sb.AppendLine("# multi-agent-shogun 設定ファイル");
        sb.AppendLine();
        sb.AppendLine($"ashigaru_count: {s.AshigaruCount}");
        sb.AppendLine($"karo_permission_mode: {s.KaroExecutionPermissionMode}");
        sb.AppendLine($"document_root: {quote(s.DocumentRoot)}");
        sb.AppendLine();
        sb.AppendLine("language: ja");
        sb.AppendLine();
        sb.AppendLine("skill:");
        sb.AppendLine($"  save_path: {quote(s.SkillSavePath)}");
        sb.AppendLine($"  local_path: {quote(s.SkillLocalPath)}");
        sb.AppendLine();
        sb.AppendLine("screenshot:");
        sb.AppendLine($"  windows_path: {quote(s.ScreenshotPath)}");
        sb.AppendLine("  path: \"\"");
        sb.AppendLine();
        sb.AppendLine("logging:");
        sb.AppendLine($"  level: {s.LogLevel}");
        sb.AppendLine($"  path: {quote(s.LogPath)}");
        sb.AppendLine();
        sb.AppendLine("agents:");
        sb.AppendLine("  model:");
        sb.AppendLine($"    shogun: {quote(s.ModelShogun)}");
        sb.AppendLine($"    karo: {quote(s.ModelKaro)}");
        sb.AppendLine($"    ashigaru: {quote(s.ModelAshigaru)}");
        sb.AppendLine("  thinking:");
        sb.AppendLine($"    shogun: {s.ThinkingShogun.ToString().ToLower()}");
        sb.AppendLine($"    karo: {s.ThinkingKaro.ToString().ToLower()}");
        sb.AppendLine($"    ashigaru: {s.ThinkingAshigaru.ToString().ToLower()}");

        var dir = Path.GetDirectoryName(_yamlPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(_yamlPath, sb.ToString());
    }

    private static string ExtractYamlValue(string part)
    {
        var v = part.Trim();
        if (v.StartsWith('"') && v.Length >= 2)
        {
            v = v.Substring(1, v.Length - 2);
            return v.Replace("\\\\", "\\").Replace("\\\"", "\"");
        }
        if (v.StartsWith('\'') && v.Length >= 2)
            return v.Substring(1, v.Length - 2);
        return v;
    }

    private static string GetDefaultDocumentRoot()
    {
        if (IsWindows) return "%USERPROFILE%\\Documents\\Shogun";
        if (IsMacOs) return "~/Documents/Shogun";
        return "~/Documents/Shogun";
    }

    public static string GetDefaultConfigDirectory()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(userProfile) && IsWindows)
            userProfile = Environment.ExpandEnvironmentVariables("%USERPROFILE%");
        if (string.IsNullOrEmpty(userProfile))
            userProfile = "~";
        return Path.Combine(userProfile, "Documents", "Shogun", "config");
    }

    private static string GetDefaultSettingsYamlPath()
    {
        return Path.Combine(GetDefaultConfigDirectory(), "settings.yaml");
    }

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static bool IsMacOs => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private void EnsureYamlDefaults()
    {
        if (File.Exists(_yamlPath)) return;
        
        var s = new AppSettings();
        if (IsWindows)
        {
            s.SkillSavePath = "%USERPROFILE%\\.claude\\skills\\shogun";
            s.SkillLocalPath = "%USERPROFILE%\\Documents\\shogun\\skills";
            s.ScreenshotPath = "%USERPROFILE%\\Pictures\\shogun";
            s.LogPath = "%USERPROFILE%\\Documents\\shogun\\logs";
        }
        else
        {
            s.SkillSavePath = "~/.claude/skills/shogun";
            s.SkillLocalPath = "~/Documents/shogun/skills";
            s.ScreenshotPath = "~/Pictures/shogun";
            s.LogPath = "~/Documents/shogun/logs";
        }
        WriteYamlAll(s);
    }
}
