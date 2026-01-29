using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Shogun.Avalonia.Models;

namespace Shogun.Avalonia.Services;

/// <summary>
/// 設定サービス。パス等は config/settings.yaml、API キー等は AppData の JSON で永続化する。
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _yamlPath;
    private readonly string _jsonPath;
    private AppSettings? _current;

    /// <summary>設定ファイルのパスを指定してインスタンスを生成する。</summary>
    /// <param name="yamlPath">config/settings.yaml のパス。null のとき既定の config ディレクトリを使用。</param>
    /// <param name="jsonPath">API キー等を保存する JSON のパス。null のとき AppData を使用。</param>
    public SettingsService(string? yamlPath = null, string? jsonPath = null)
    {
        _yamlPath = yamlPath ?? GetDefaultSettingsYamlPath();
        _jsonPath = jsonPath ?? GetDefaultSettingsJsonPath();
    }

    /// <inheritdoc />
    public AppSettings Get()
    {
        if (_current != null)
            return _current;

        EnsureYamlDefaults();
        var yaml = ReadYamlPaths();
        var yamlAgents = ReadYamlAgentsModel();
        var api = ReadJsonApi();
        var ashigaruCount = yaml.AshigaruCount > 0 ? yaml.AshigaruCount : api.ashigaruCount;
        if (ashigaruCount < 1) ashigaruCount = 8;
        if (ashigaruCount > 20) ashigaruCount = 20;
        var modelName = string.IsNullOrWhiteSpace(api.modelName) ? string.Empty : api.modelName;
        var modelShogun = !string.IsNullOrWhiteSpace(api.modelShogun) ? api.modelShogun : (yamlAgents.modelShogun ?? string.Empty);
        var modelKaro = !string.IsNullOrWhiteSpace(api.modelKaro) ? api.modelKaro : (yamlAgents.modelKaro ?? string.Empty);
        var modelAshigaru = !string.IsNullOrWhiteSpace(api.modelAshigaru) ? api.modelAshigaru : (yamlAgents.modelAshigaru ?? string.Empty);
        _current = new AppSettings
        {
            AshigaruCount = ashigaruCount,
            SkillSavePath = yaml.SkillSavePath,
            SkillLocalPath = yaml.SkillLocalPath,
            ScreenshotPath = yaml.ScreenshotPath,
            LogLevel = yaml.LogLevel,
            LogPath = yaml.LogPath,
            ModelName = modelName,
            ModelShogun = modelShogun,
            ModelKaro = modelKaro,
            ModelAshigaru = modelAshigaru,
            ThinkingShogun = api.thinkingShogun,
            ThinkingKaro = api.thinkingKaro,
            ThinkingAshigaru = api.thinkingAshigaru,
            ApiEndpoint = api.apiEndpoint,
            RepoRoot = api.repoRoot
        };
        return _current;
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        _current = settings;
        WriteYamlPaths(settings.SkillSavePath, settings.SkillLocalPath, settings.ScreenshotPath, settings.LogLevel, settings.LogPath, settings.AshigaruCount, settings.ModelShogun, settings.ModelKaro, settings.ModelAshigaru);
        WriteJsonApi(settings.ModelName, settings.ModelShogun, settings.ModelKaro, settings.ModelAshigaru, settings.AshigaruCount, settings.ApiEndpoint, settings.RepoRoot, settings.ThinkingShogun, settings.ThinkingKaro, settings.ThinkingAshigaru);
    }

    /// <summary>config ディレクトリの既定パス。Windows: %USERPROFILE%\\Documents\\Shogun\\config、mac: ~/Documents/Shogun/config。ビルド・アップデートで消えないようユーザー Documents に作成。</summary>
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

    private static string GetDefaultSettingsJsonPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "Shogun.Avalonia");
        return Path.Combine(folder, "settings.json");
    }

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    private static bool IsMacOs => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    /// <summary>空の設定を補うときの初期パス（Windows: %USERPROFILE%、macOS: ~ のリテラルで YAML に保存）。</summary>
    private static (string SkillSavePath, string SkillLocalPath, string ScreenshotPath, string LogPath) GetDefaultPaths()
    {
        if (IsWindows)
        {
            return (
                "%USERPROFILE%\\.claude\\skills\\shogun",
                "%USERPROFILE%\\Documents\\shogun\\skills",
                "%USERPROFILE%\\Pictures\\shogun",
                "%USERPROFILE%\\Documents\\shogun\\logs"
            );
        }
        if (IsMacOs)
        {
            return (
                "~/.claude/skills/shogun",
                "~/Documents/shogun/skills",
                "~/Pictures/shogun",
                "~/Documents/shogun/logs"
            );
        }
        var fallbackHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(fallbackHome))
            fallbackHome = "~";
        return (
            $"{fallbackHome}/.claude/skills/shogun",
            $"{fallbackHome}/Documents/shogun/skills",
            $"{fallbackHome}/Pictures/shogun",
            $"{fallbackHome}/Documents/shogun/logs"
        );
    }

    private void EnsureYamlDefaults()
    {
        var dir = Path.GetDirectoryName(_yamlPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (!File.Exists(_yamlPath))
        {
            if (IsWindows)
            {
                WriteFullDefaultYaml(
                    "%USERPROFILE%\\.claude\\skills\\shogun",
                    "%USERPROFILE%\\Documents\\shogun\\skills",
                    "%USERPROFILE%\\Pictures\\shogun",
                    "%USERPROFILE%\\Documents\\shogun\\logs"
                );
            }
            else
            {
                WriteFullDefaultYaml(
                    "~/.claude/skills/shogun",
                    "~/Documents/shogun/skills",
                    "~/Pictures/shogun",
                    "~/Documents/shogun/logs"
                );
            }
            return;
        }

        var yaml = ReadYamlPaths();
        var needSave = string.IsNullOrWhiteSpace(yaml.SkillSavePath) || string.IsNullOrWhiteSpace(yaml.SkillLocalPath)
                      || string.IsNullOrWhiteSpace(yaml.ScreenshotPath) || string.IsNullOrWhiteSpace(yaml.LogPath);
        if (!needSave)
            return;

        var defaults = GetDefaultPaths();
        var newSkillSave = string.IsNullOrWhiteSpace(yaml.SkillSavePath) ? defaults.SkillSavePath : yaml.SkillSavePath;
        var newSkillLocal = string.IsNullOrWhiteSpace(yaml.SkillLocalPath) ? defaults.SkillLocalPath : yaml.SkillLocalPath;
        var newScreenshot = string.IsNullOrWhiteSpace(yaml.ScreenshotPath) ? defaults.ScreenshotPath : yaml.ScreenshotPath;
        var newLogPath = string.IsNullOrWhiteSpace(yaml.LogPath) ? defaults.LogPath : yaml.LogPath;
        var (mShogun, mKaro, mAshigaru) = ReadYamlAgentsModel();
        WriteYamlPaths(newSkillSave, newSkillLocal, newScreenshot, yaml.LogLevel, newLogPath, yaml.AshigaruCount, mShogun ?? string.Empty, mKaro ?? string.Empty, mAshigaru ?? string.Empty);
    }

    private void WriteFullDefaultYaml(string skillSave, string skillLocal, string screenshot, string logPath, string modelShogun = "", string modelKaro = "", string modelAshigaru = "")
    {
        var quote = (string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        var content = $@"# multi-agent-shogun 設定ファイル

# 足軽の人数（1～20）
ashigaru_count: 8

# 言語設定
language: ja

# スキル設定
skill:
  save_path: {quote(skillSave)}
  local_path: {quote(skillLocal)}

# スクリーンショット設定
screenshot:
  windows_path: {quote(screenshot)}
  path: """"

# ログ設定
logging:
  level: info
  path: {quote(logPath)}

# エージェント用モデル（将軍・家老・足軽）
agents:
  model:
    shogun: {quote(modelShogun)}
    karo: {quote(modelKaro)}
    ashigaru: {quote(modelAshigaru)}
";
        File.WriteAllText(_yamlPath, content);
    }

    private (string SkillSavePath, string SkillLocalPath, string ScreenshotPath, string LogLevel, string LogPath, int AshigaruCount) ReadYamlPaths()
    {
        var skillSave = string.Empty;
        var skillLocal = string.Empty;
        var screenshotPath = string.Empty;
        var logLevel = "info";
        var logPath = string.Empty;
        var ashigaruCount = 0;

        if (!File.Exists(_yamlPath))
            return (skillSave, skillLocal, screenshotPath, logLevel, logPath, ashigaruCount);

        var lines = File.ReadAllLines(_yamlPath);
        var inSkill = false;
        var inScreenshot = false;
        var inLogging = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                inSkill = inScreenshot = inLogging = false;
                continue;
            }

            if (trimmed.StartsWith("ashigaru_count:"))
            {
                var v = ExtractYamlValue(trimmed.Substring("ashigaru_count:".Length));
                if (int.TryParse(v, out var n) && n >= 1 && n <= 20)
                    ashigaruCount = n;
                inSkill = inScreenshot = inLogging = false;
                continue;
            }
            if (trimmed.StartsWith("skill:"))
            {
                inSkill = true;
                inScreenshot = inLogging = false;
                continue;
            }
            if (trimmed.StartsWith("screenshot:"))
            {
                inScreenshot = true;
                inSkill = inLogging = false;
                continue;
            }
            if (trimmed.StartsWith("logging:"))
            {
                inLogging = true;
                inSkill = inScreenshot = false;
                continue;
            }

            if (inSkill && trimmed.StartsWith("save_path:"))
            {
                skillSave = ExtractYamlValue(trimmed.Substring("save_path:".Length));
                continue;
            }
            if (inSkill && trimmed.StartsWith("local_path:"))
            {
                skillLocal = ExtractYamlValue(trimmed.Substring("local_path:".Length));
                continue;
            }
            if (inScreenshot && (trimmed.StartsWith("windows_path:") || trimmed.StartsWith("path:")))
            {
                if (trimmed.StartsWith("windows_path:"))
                    screenshotPath = ExtractYamlValue(trimmed.Substring("windows_path:".Length));
                else if (string.IsNullOrEmpty(screenshotPath))
                    screenshotPath = ExtractYamlValue(trimmed.Substring("path:".Length));
                continue;
            }
            if (inLogging && trimmed.StartsWith("level:"))
            {
                logLevel = ExtractYamlValue(trimmed.Substring("level:".Length));
                if (string.IsNullOrWhiteSpace(logLevel))
                    logLevel = "info";
                continue;
            }
            if (inLogging && trimmed.StartsWith("path:"))
            {
                logPath = ExtractYamlValue(trimmed.Substring("path:".Length));
                continue;
            }

            if (trimmed.StartsWith("-") || (trimmed.Length > 0 && char.IsLetter(trimmed[0]) && trimmed.Contains(':')))
                inSkill = inScreenshot = inLogging = false;
        }

        return (skillSave, skillLocal, screenshotPath, logLevel, logPath, ashigaruCount);
    }

    /// <summary>config/settings.yaml の agents.model（shogun, karo, ashigaru）を読み取る。JSON で未設定時のフォールバックに使用。</summary>
    private (string? modelShogun, string? modelKaro, string? modelAshigaru) ReadYamlAgentsModel()
    {
        string? modelShogun = null;
        string? modelKaro = null;
        string? modelAshigaru = null;
        if (!File.Exists(_yamlPath))
            return (modelShogun, modelKaro, modelAshigaru);
        var lines = File.ReadAllLines(_yamlPath);
        var inAgents = false;
        var inModel = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
            {
                if (trimmed.StartsWith("#") && trimmed.Length > 1 && char.IsLetter(trimmed[1]))
                    inAgents = inModel = false;
                continue;
            }
            if (trimmed.Equals("agents:", StringComparison.Ordinal))
            {
                inAgents = true;
                inModel = false;
                continue;
            }
            if (!inAgents)
                continue;
            if (trimmed.StartsWith("model:", StringComparison.Ordinal))
            {
                inModel = true;
                continue;
            }
            if (!inModel)
                continue;
            if (trimmed.StartsWith("shogun:", StringComparison.Ordinal))
            {
                var v = ExtractYamlValue(trimmed.Substring("shogun:".Length));
                if (!string.IsNullOrWhiteSpace(v)) modelShogun = v;
                continue;
            }
            if (trimmed.StartsWith("karo:", StringComparison.Ordinal))
            {
                var v = ExtractYamlValue(trimmed.Substring("karo:".Length));
                if (!string.IsNullOrWhiteSpace(v)) modelKaro = v;
                continue;
            }
            if (trimmed.StartsWith("ashigaru:", StringComparison.Ordinal))
            {
                var v = ExtractYamlValue(trimmed.Substring("ashigaru:".Length));
                if (!string.IsNullOrWhiteSpace(v)) modelAshigaru = v;
                continue;
            }
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
                inAgents = inModel = false;
        }
        return (modelShogun, modelKaro, modelAshigaru);
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

    private void WriteYamlPaths(string skillSavePath, string skillLocalPath, string screenshotPath, string logLevel, string logPath, int ashigaruCount = 0, string modelShogun = "", string modelKaro = "", string modelAshigaru = "")
    {
        var dir = Path.GetDirectoryName(_yamlPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var lines = File.Exists(_yamlPath) ? File.ReadAllLines(_yamlPath) : Array.Empty<string>();
        var inSkill = false;
        var inScreenshot = false;
        var inLogging = false;
        var result = new System.Collections.Generic.List<string>();
        var quote = (string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("skill:"))
            {
                inSkill = true;
                inScreenshot = inLogging = false;
                result.Add(line);
                continue;
            }
            if (trimmed.StartsWith("screenshot:"))
            {
                inScreenshot = true;
                inSkill = inLogging = false;
                result.Add(line);
                continue;
            }
            if (trimmed.StartsWith("logging:"))
            {
                inLogging = true;
                inSkill = inScreenshot = false;
                result.Add(line);
                continue;
            }

            if (inSkill && trimmed.StartsWith("save_path:"))
            {
                result.Add($"  save_path: {quote(skillSavePath)}");
                continue;
            }
            if (inSkill && trimmed.StartsWith("local_path:"))
            {
                result.Add($"  local_path: {quote(skillLocalPath)}");
                continue;
            }
            if (inScreenshot && trimmed.StartsWith("windows_path:"))
            {
                result.Add($"  windows_path: {quote(screenshotPath)}");
                continue;
            }
            if (inScreenshot && trimmed.StartsWith("path:"))
            {
                result.Add(line);
                continue;
            }
            if (inLogging && trimmed.StartsWith("level:"))
            {
                result.Add($"  level: {logLevel}");
                continue;
            }
            if (inLogging && trimmed.StartsWith("path:"))
            {
                result.Add($"  path: {quote(logPath)}");
                continue;
            }

            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#") || (trimmed.Length > 0 && char.IsLetter(trimmed[0]) && trimmed.Contains(':')))
                inSkill = inScreenshot = inLogging = false;
            result.Add(line);
        }

        if (!File.Exists(_yamlPath) || result.Count == 0)
        {
            WriteFullDefaultYaml(skillSavePath, skillLocalPath, screenshotPath, logPath, modelShogun, modelKaro, modelAshigaru);
            return;
        }

        var hasAshigaruCount = result.Any(l => l.TrimStart().StartsWith("ashigaru_count:", StringComparison.Ordinal));
        if (ashigaruCount >= 1 && ashigaruCount <= 20)
        {
            if (hasAshigaruCount)
            {
                for (var i = 0; i < result.Count; i++)
                {
                    if (result[i].TrimStart().StartsWith("ashigaru_count:", StringComparison.Ordinal))
                    {
                        result[i] = "ashigaru_count: " + ashigaruCount;
                        break;
                    }
                }
            }
            else
            {
                var insertIdx = 0;
                for (var i = 0; i < result.Count; i++)
                {
                    if (result[i].TrimStart().StartsWith("#", StringComparison.Ordinal) && result[i].Contains("設定", StringComparison.Ordinal))
                    {
                        insertIdx = i + 1;
                        break;
                    }
                }
                result.Insert(insertIdx, "ashigaru_count: " + ashigaruCount);
                result.Insert(insertIdx, "");
            }
        }

        var inAgents = false;
        var inAgentsModel = false;
        var agentsModelStart = -1;
        var agentsModelEnd = -1;
        for (var i = 0; i < result.Count; i++)
        {
            var t = result[i].Trim();
            if (t.Equals("agents:", StringComparison.Ordinal)) { inAgents = true; inAgentsModel = false; continue; }
            if (!inAgents) continue;
            if (t.StartsWith("model:", StringComparison.Ordinal)) { inAgentsModel = true; agentsModelStart = i; agentsModelEnd = i; continue; }
            if (!inAgentsModel) continue;
            if (t.StartsWith("shogun:")) { result[i] = "    shogun: " + quote(modelShogun); agentsModelEnd = i; continue; }
            if (t.StartsWith("karo:")) { result[i] = "    karo: " + quote(modelKaro); agentsModelEnd = i; continue; }
            if (t.StartsWith("ashigaru:")) { result[i] = "    ashigaru: " + quote(modelAshigaru); agentsModelEnd = i; continue; }
            if (t.Length > 0 && !char.IsWhiteSpace(result[i][0])) { inAgents = inAgentsModel = false; }
        }
        if (agentsModelStart < 0)
        {
            result.Add("");
            result.Add("# エージェント用モデル（将軍・家老・足軽）");
            result.Add("agents:");
            result.Add("  model:");
            result.Add("    shogun: " + quote(modelShogun));
            result.Add("    karo: " + quote(modelKaro));
            result.Add("    ashigaru: " + quote(modelAshigaru));
        }

        File.WriteAllLines(_yamlPath, result);
    }

    private (string modelName, string? modelShogun, string? modelKaro, string? modelAshigaru, int ashigaruCount, string apiEndpoint, string repoRoot, bool thinkingShogun, bool thinkingKaro, bool thinkingAshigaru) ReadJsonApi()
    {
        if (!File.Exists(_jsonPath))
            return (string.Empty, null, null, null, 0, string.Empty, string.Empty, false, false, false);
        try
        {
            var json = File.ReadAllText(_jsonPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var modelName = root.TryGetProperty("modelName", out var mn) ? mn.GetString() ?? string.Empty : string.Empty;
            var modelShogun = root.TryGetProperty("modelShogun", out var ms) ? ms.GetString() : null;
            var modelKaro = root.TryGetProperty("modelKaro", out var mk) ? mk.GetString() : null;
            var modelAshigaru = root.TryGetProperty("modelAshigaru", out var ma) ? ma.GetString() : null;
            var ashigaruCount = 0;
            if (root.TryGetProperty("ashigaruCount", out var ac))
            {
                if (ac.ValueKind == JsonValueKind.Number && ac.TryGetInt32(out var n))
                    ashigaruCount = n;
            }
            var apiEndpoint = root.TryGetProperty("apiEndpoint", out var ae) ? ae.GetString() ?? string.Empty : string.Empty;
            var repoRoot = root.TryGetProperty("repoRoot", out var rr) ? rr.GetString() ?? string.Empty : string.Empty;
            var thinkingShogun = root.TryGetProperty("thinkingShogun", out var ts) && ts.ValueKind == JsonValueKind.True;
            var thinkingKaro = root.TryGetProperty("thinkingKaro", out var tk) && tk.ValueKind == JsonValueKind.True;
            var thinkingAshigaru = root.TryGetProperty("thinkingAshigaru", out var ta) && ta.ValueKind == JsonValueKind.True;
            return (modelName ?? string.Empty, modelShogun, modelKaro, modelAshigaru, ashigaruCount, apiEndpoint ?? string.Empty, repoRoot ?? string.Empty, thinkingShogun, thinkingKaro, thinkingAshigaru);
        }
        catch
        {
            return (string.Empty, null, null, null, 0, string.Empty, string.Empty, false, false, false);
        }
    }

    private void WriteJsonApi(string modelName, string modelShogun, string modelKaro, string modelAshigaru, int ashigaruCount, string apiEndpoint, string repoRoot, bool thinkingShogun, bool thinkingKaro, bool thinkingAshigaru)
    {
        var dir = Path.GetDirectoryName(_jsonPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var o = new { modelName, modelShogun, modelKaro, modelAshigaru, ashigaruCount, apiEndpoint, repoRoot, thinkingShogun, thinkingKaro, thinkingAshigaru };
        var json = JsonSerializer.Serialize(o, JsonOptions);
        File.WriteAllText(_jsonPath, json);
    }
}
