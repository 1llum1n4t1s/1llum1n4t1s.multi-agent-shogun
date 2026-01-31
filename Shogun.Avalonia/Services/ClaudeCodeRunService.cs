using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shogun.Avalonia.Models;
using Shogun.Avalonia.Util;
using VYaml.Serialization;

namespace Shogun.Avalonia.Services;

/// <summary>
/// アプリ内の Claude Code CLI を常駐プロセス経由で実行する。プロセスはアプリ起動時に起動し、アプリ終了時まで終了しない。
/// </summary>
public class ClaudeCodeRunService : IClaudeCodeRunService
{
    private const string KaroUserPrompt = @"queue/shogun_to_karo.yaml に新しい指示がある。確認して、以下のYAML形式で足軽タスク情報を出力せよ。

重要: あなたの現在の作業ディレクトリ（CWD）はプロジェクトのルートディレクトリである。
queue/shogun_to_karo.yaml は、カレントディレクトリからの相対パスでアクセスせよ。

```yaml
tasks:
  - ashigaru_id: 1
    description: ""タスクの説明""
    target_path: ""対象ファイルパス（オプション）""
```

注意: 複数の独立したタスクなら複数足軽に分散して並列実行させよ。YAMLのみ出力し、余計な説明は不要。";
    
    private const string KaroExecutionPrompt = @"足軽からの報告書をすべて読んだ。確認せよ: queue/reports/ashigaru*_report.yaml

重要: あなたの現在の作業ディレクトリ（CWD）はプロジェクトのルートディレクトリである。
queue/reports/ は、カレントディレクトリからの相対パスでアクセスせよ。

報告内容をまとめ、必要に応じて自分でコードを改修せよ。
1. 報告ファイルをすべて読む
2. 改修が必要なファイルを特定する
3. 必要に応じてファイルを Edit ツールで改修する
4. ビルドが成功することを確認する
5. 最終的なサマリーを出力する

改修内容を含めた最終報告を、以下のYAML形式で出力せよ:

---
modifications:
  - file: ""ファイルパス""
    description: ""改修内容""
result: ""成功/失敗""
summary: ""処理サマリー""
---";
    
    private const string KaroReportUserPrompt = @"queue/reports/ の報告を確認し、dashboard.md の「戦果」を更新せよ。

重要: あなたの現在の作業ディレクトリ（CWD）はプロジェクトのルートディレクトリである。
queue/reports/ および dashboard.md は、カレントディレクトリからの相対パスでアクセスせよ。";

    private readonly IClaudeCodeProcessHost _processHost;
    private readonly IClaudeCodeSetupService _setupService;
    private readonly IShogunQueueService _queueService;
    private readonly IInstructionsLoader _instructionsLoader;

    /// <summary>サービスを生成する。</summary>
    public ClaudeCodeRunService(IClaudeCodeProcessHost processHost, IClaudeCodeSetupService setupService, IShogunQueueService queueService, IInstructionsLoader instructionsLoader)
    {
        _processHost = processHost;
        _setupService = setupService;
        _queueService = queueService;
        _instructionsLoader = instructionsLoader;
    }

    /// <inheritdoc />
    public async Task<bool> RunKaroAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var karoInstructions = _instructionsLoader.LoadKaroInstructions() ?? string.Empty;
        var result = await RunClaudeAsync(KaroUserPrompt, karoInstructions, progress, "家老", cancellationToken).ConfigureAwait(false);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
        {
            // 家老の出力（JSON）を解析して足軽タスクファイルを生成
            await GenerateAshigaruTasksFromKaroOutputAsync(result.Output, cancellationToken).ConfigureAwait(false);
        }
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> RunKaroExecutionAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var karoInstructions = _instructionsLoader.LoadKaroInstructions() ?? string.Empty;
        var result = await RunClaudeAsync(KaroExecutionPrompt, karoInstructions, progress, "家老（実行フェーズ）", cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> RunAshigaruAsync(int ashigaruIndex, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var max = _queueService.GetAshigaruCount();
        if (ashigaruIndex < 1 || ashigaruIndex > max)
        {
            progress?.Report($"足軽番号は 1～{max} の範囲で指定してください。");
            return false;
        }
        var ashigaruInstructions = _instructionsLoader.LoadAshigaruInstructions() ?? string.Empty;
        var userPrompt = $@"queue/tasks/ashigaru{ashigaruIndex}.yaml に任務がある。確認して実行せよ。

重要: あなたの現在の作業ディレクトリ（CWD）はプロジェクトのルートディレクトリである。
queue/tasks/ashigaru{ashigaruIndex}.yaml は、カレントディレクトリからの相対パスでアクセスせよ。";
        var result = await RunClaudeAsync(userPrompt, ashigaruInstructions, progress, $"足軽{ashigaruIndex}", cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> RunKaroReportAggregationAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var karoInstructions = _instructionsLoader.LoadKaroInstructions() ?? string.Empty;
        var result = await RunClaudeAsync(KaroReportUserPrompt, karoInstructions, progress, "家老（報告集約）", cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<string> ResolveShogunCommandAsync(string userInput, string? projectId, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var shogunInstructions = _instructionsLoader.LoadShogunInstructions() ?? string.Empty;
        var userPrompt = $"殿の指示: {userInput}\nプロジェクトID: {projectId ?? "未指定"}\n\n上記を解析し、家老への具体的な指示文（1つのテキストブロック）を生成せよ。";
        
        var result = await RunClaudeAsync(userPrompt, shogunInstructions, progress, "将軍", cancellationToken).ConfigureAwait(false);
        if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
        {
            return result.Output;
        }
        return userInput;
    }

    /// <summary>常駐プロセスにジョブを送り、結果を返す。プロセスは終了しない。</summary>
    private async Task<(bool Success, string Output)> RunClaudeAsync(string userPrompt, string systemPromptContent, IProgress<string>? progress, string roleLabel, CancellationToken cancellationToken)
    {
        var repoRoot = _queueService.GetRepoRoot();
        if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
        {
            progress?.Report("ワークスペースルートが設定されていません。設定で指定してください。");
            return (false, string.Empty);
        }

        var settings = _queueService.GetSettings();
        string? modelId = null;
        bool thinking = false;

        if (roleLabel == "将軍")
        {
            modelId = settings.ModelShogun;
            thinking = settings.ThinkingShogun;
        }
        else if (roleLabel.StartsWith("家老"))
        {
            modelId = settings.ModelKaro;
            thinking = settings.ThinkingKaro;
        }
        else if (roleLabel.StartsWith("足軽"))
        {
            modelId = settings.ModelAshigaru;
            thinking = settings.ThinkingAshigaru;
        }

        string? promptFile = null;
        try
        {
            progress?.Report($"{roleLabel}（常駐プロセス）にジョブを送信中…");
            Logger.Log($"{roleLabel} のジョブを送信します。UserPrompt='{userPrompt}', Model='{modelId}', Thinking={thinking}", LogLevel.Info);
            promptFile = Path.Combine(Path.GetTempPath(), "shogun-prompt-" + Guid.NewGuid().ToString("N")[..8] + ".md");
            await File.WriteAllTextAsync(promptFile, systemPromptContent, cancellationToken).ConfigureAwait(false);
            Logger.Log($"システムプロンプトファイルを生成しました: {promptFile}", LogLevel.Debug);
            var (success, outputStr) = await _processHost.RunJobAsync(roleLabel, userPrompt, promptFile, modelId, thinking, progress, cancellationToken).ConfigureAwait(false);
            Logger.Log($"{roleLabel} のジョブが完了しました。Success={success}", LogLevel.Info);
            if (!success)
                progress?.Report($"{roleLabel}の実行が失敗しました。");
            else
                progress?.Report($"{roleLabel}の実行が完了しました。");
            return (success, outputStr);
        }
        catch (Exception ex)
        {
            progress?.Report($"エラー: {ex.Message}");
            return (false, string.Empty);
        }
        finally
        {
            if (!string.IsNullOrEmpty(promptFile) && File.Exists(promptFile))
            {
                try { File.Delete(promptFile); } catch { /* ignore */ }
            }
        }
    }

    /// <summary>家老の YAML 出力から足軽タスクを生成する。</summary>
    private async Task GenerateAshigaruTasksFromKaroOutputAsync(string karoYaml, CancellationToken cancellationToken)
    {
        try
        {
            var repoRoot = _queueService.GetRepoRoot();
            
            // markdown コードブロック（```yaml ... ```）を抽出
            var yamlText = string.Empty;
            if (karoYaml.Contains("```yaml"))
            {
                var start = karoYaml.IndexOf("```yaml", StringComparison.Ordinal) + 7;
                var end = karoYaml.IndexOf("```", start, StringComparison.Ordinal);
                if (end > start) yamlText = karoYaml.Substring(start, end - start);
            }
            else if (karoYaml.Contains("```"))
            {
                var start = karoYaml.IndexOf("```", StringComparison.Ordinal) + 3;
                var end = karoYaml.IndexOf("```", start, StringComparison.Ordinal);
                if (end > start) yamlText = karoYaml.Substring(start, end - start);
            }
            
            // コードブロックが見つからない場合は、全体が YAML であると期待してパースを試みるが、
            // その前に YAML らしい開始部分を探す（VYaml の MappingStart エラー対策）
            if (string.IsNullOrWhiteSpace(yamlText))
            {
                var lines = karoYaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                var yamlLines = new List<string>();
                bool foundStart = false;
                foreach (var line in lines)
                {
                    if (!foundStart && (line.TrimStart().StartsWith("tasks:", StringComparison.OrdinalIgnoreCase) || line.TrimStart().StartsWith("---")))
                    {
                        foundStart = true;
                    }
                    if (foundStart)
                    {
                        yamlLines.Add(line);
                    }
                }
                if (yamlLines.Count > 0)
                {
                    yamlText = string.Join("\n", yamlLines);
                }
                else
                {
                    yamlText = karoYaml.Trim();
                }
            }
            else
            {
                yamlText = yamlText.Trim();
            }

            if (string.IsNullOrWhiteSpace(yamlText))
            {
                Logger.Log("家老の出力から YAML コンテンツを抽出できませんでした。", LogLevel.Warning);
                return;
            }
            
            try
            {
                var bytes = Encoding.UTF8.GetBytes(yamlText);
                var wrapper = YamlSerializer.Deserialize<TaskAssignmentYaml>(bytes);
                if (wrapper?.Assignments == null || wrapper.Assignments.Count == 0)
                {
                    Logger.Log("家老の YAML 出力にタスク割り当てが含まれていません。", LogLevel.Debug);
                    return;
                }
                
                foreach (var task in wrapper.Assignments)
                {
                    var ashigaruId = task.Ashigaru;
                    var description = task.Description ?? "";
                    var targetPath = task.TargetPath ?? "";
                    var yaml = $"""
task:
  task_id: task_{ashigaruId}_{DateTime.Now:HHmmss}
  description: "{description}"
  {(string.IsNullOrEmpty(targetPath) ? "" : $"target_path: {targetPath}")}
  status: assigned
  timestamp: "{DateTime.UtcNow:O}"
""";
                    var taskFilePath = Path.Combine(repoRoot, "queue", "tasks", $"ashigaru{ashigaruId}.yaml");
                    var taskDir = Path.GetDirectoryName(taskFilePath);
                    if (!string.IsNullOrEmpty(taskDir))
                    {
                        Directory.CreateDirectory(taskDir);
                    }
                    await File.WriteAllTextAsync(taskFilePath, yaml, cancellationToken).ConfigureAwait(false);
                    Logger.Log($"足軽{ashigaruId}タスクファイルを生成しました: {taskFilePath}", LogLevel.Debug);
                }
            }
            catch (Exception parseEx)
            {
                Logger.Log($"家老の出力が YAML 形式ではありません（エラーメッセージ等の可能性）: {parseEx.GetType().Name}", LogLevel.Info);
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"足軽タスク生成エラー: {ex.Message}", LogLevel.Warning);
            Logger.Log($"解析対象 YAML 文字列:\n{karoYaml}", LogLevel.Debug);
        }
    }
}
