using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Shogun.Avalonia.Models;
using Shogun.Avalonia.Util;

namespace Shogun.Avalonia.Services;

/// <summary>
/// アプリ内の Claude Code CLI を常駐プロセス経由で実行する。プロセスはアプリ起動時に起動し、アプリ終了時まで終了しない。
/// </summary>
public class ClaudeCodeRunService : IClaudeCodeRunService
{
    private const string KaroUserPrompt = @"queue/shogun_to_karo.yaml に新しい指示がある。確認して、以下のYAML形式で足軽タスク情報を出力せよ。

重要: あなたの現在の作業ディレクトリ（CWD）はプロジェクトのルートディレクトリである。
queue/shogun_to_karo.yaml は、カレントディレクトリからの相対パスでアクセスせよ。

最重要: status が pending のコマンドのみを処理せよ。status が done のコマンドは既に処理済みなので無視すること。

```yaml
tasks:
  - ashigaru_id: 1
    description: ""タスクの説明""
    target_path: ""対象ファイルパス（オプション）""
```

注意: 複数の独立したタスクなら複数足軽に分散して並列実行させよ。YAMLのみ出力し、余計な説明は不要。";
    
    private const string KaroExecutionPrompt = @"足軽からの報告書をすべて読んだ。確認せよ: queue/reports/ashigaru*_report.yaml

重要: あなたの現在の作業ディレクトリ（CWD）は queue/config の基準（ドキュメントルート）である。
queue/reports/ は CWD からの相対パスで読める。プロジェクトの実体パスは config/projects.yaml の path に記載されている。
足軽の報告内のファイルパスは各プロジェクトルートからの相対パスのことがある。config/projects.yaml で project_id に対応する path を確認し、その path を基準に絶対パスで Edit ツールを実行せよ。

報告内容をまとめ、必要に応じて自分でコードを改修せよ。
1. 報告ファイルをすべて読む
2. 改修が必要なファイルを特定する（絶対パスは config/projects.yaml の path から組み立てる）
3. 必要に応じてファイルを Edit ツールで絶対パス指定で改修する
4. ビルドが成功することを確認する（プロジェクトルートでビルドコマンドを実行）
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

重要: あなたの現在の作業ディレクトリ（CWD）は queue/config の基準（ドキュメントルート）である。
queue/reports/ および dashboard.md は、CWD からの相対パスでアクセスせよ。
もし queue/reports/ 配下に報告ファイル（ashigaru*_report.yaml）が一つも存在しない場合は、その旨を dashboard.md に記載せよ。";

    private const string KaroReportCheckPrompt = @"queue/reports/ の報告をすべて読み、dashboard.md の「戦果」を更新せよ。

重要: あなたの現在の作業ディレクトリ（CWD）は queue/config の基準（ドキュメントルート）である。
queue/reports/ および dashboard.md は CWD からの相対パスでアクセスせよ。

そのうえで、足軽に追加で割り当てるタスク（フォローアップ作業）がある場合のみ、以下の YAML 形式で出力せよ。追加タスクがなければ「終了」のみ出力せよ。

```yaml
tasks:
  - ashigaru_id: 1
    description: ""タスクの説明""
    target_path: ""対象ファイルパス（オプション）""
```

注意: 追加タスクがあるときは YAML のみ出力。ないときは「終了」のみ。余計な説明は不要。";

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
    public async Task<(bool Success, bool HasMoreTasks)> RunKaroReportCheckAndMaybeAssignAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var karoInstructions = _instructionsLoader.LoadKaroInstructions() ?? string.Empty;
        var result = await RunClaudeAsync(KaroReportCheckPrompt, karoInstructions, progress, "家老（報告確認）", cancellationToken).ConfigureAwait(false);
        if (!result.Success)
            return (false, false);
        if (string.IsNullOrWhiteSpace(result.Output))
            return (true, false);
        if (!HasTaskAssignmentsInKaroOutput(result.Output))
            return (true, false);
        await GenerateAshigaruTasksFromKaroOutputAsync(result.Output, cancellationToken).ConfigureAwait(false);
        return (true, true);
    }

    /// <inheritdoc />
    public async Task<bool> RunKaroExecutionAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var karoInstructions = _instructionsLoader.LoadKaroInstructions() ?? string.Empty;
        var result = await RunClaudeAsync(KaroExecutionPrompt, karoInstructions, progress, "家老（実行フェーズ）", cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<bool> SendClearToAshigaruAsync(int ashigaruIndex, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var max = _queueService.GetAshigaruCount();
        if (ashigaruIndex < 1 || ashigaruIndex > max)
            return false;
        var ashigaruInstructions = _instructionsLoader.LoadAshigaruInstructions() ?? string.Empty;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            progress?.Report($"足軽{ashigaruIndex} に /clear を送信中…");
            var result = await RunClaudeAsync("/clear", ashigaruInstructions, progress, $"足軽{ashigaruIndex}", cts.Token).ConfigureAwait(false);
            return result.Success;
        }
        catch (OperationCanceledException)
        {
            progress?.Report($"足軽{ashigaruIndex} /clear タイムアウト（次タスクへ進行）");
            return true;
        }
    }

    /// <inheritdoc />
    public async Task<bool> RunAshigaruAsync(int ashigaruIndex, IProgress<string>? progress = null, CancellationToken cancellationToken = default, string? projectId = null)
    {
        var max = _queueService.GetAshigaruCount();
        if (ashigaruIndex < 1 || ashigaruIndex > max)
        {
            progress?.Report($"足軽番号は 1～{max} の範囲で指定してください。");
            return false;
        }
        var repoRoot = _queueService.GetRepoRoot();
        if (!string.IsNullOrEmpty(repoRoot))
        {
            var reportsDir = Path.Combine(repoRoot, "queue", "reports");
            try { Directory.CreateDirectory(reportsDir); } catch { /* 作成失敗時は続行 */ }
        }
        var reportPath = string.IsNullOrEmpty(repoRoot) ? $"queue/reports/ashigaru{ashigaruIndex}_report.yaml" : Path.Combine(repoRoot, "queue", "reports", $"ashigaru{ashigaruIndex}_report.yaml");
        var reportPathForPrompt = reportPath.Replace('\\', '/');
        var ashigaruInstructions = _instructionsLoader.LoadAshigaruInstructions() ?? string.Empty;
        var taskContent = _queueService.ReadTaskYaml(ashigaruIndex);
        var effectiveProjectId = TryInferProjectIdFromTaskContent(taskContent) ?? projectId;
        var projectRoot = _queueService.GetProjectRoot(effectiveProjectId);
        var taskTargetIsShogunRepo = IsTaskTargetUnderShogunRepo(taskContent);
        string userPrompt;
        string? cwdOverride = null;
        if (!taskTargetIsShogunRepo && !string.IsNullOrEmpty(projectRoot) && Directory.Exists(projectRoot) && !string.IsNullOrWhiteSpace(taskContent))
        {
            var contentForCwd = RewriteTaskTargetPathForProjectRoot(taskContent, effectiveProjectId, projectRoot);
            userPrompt = $@"以下の任務（YAML）を実行せよ。作業ディレクトリ（CWD）はプロジェクトルートに設定されている。

```yaml
{contentForCwd}
```

重要: あなたの現在の作業ディレクトリ（CWD）はプロジェクトルートである。ファイルの読み書き・編集はすべてこのディレクトリ配下で行うこと。target_path は CWD からの相対パスである。

報告: 任務完了時、報告を必ず次の絶対パスに YAML で書き込むこと: {reportPathForPrompt} （CWD がプロジェクトルートでも、報告ファイルは常にこのドキュメントルート基準のパスに書くこと。ファイルが存在しない場合は新規作成せよ。）

補足: 実装・最適化タスクで前提となる施策ドキュメント等がまだ存在しない場合は、自己分析で方針を立てて進めてよい。";
            cwdOverride = projectRoot;
        }
        else
        {
            userPrompt = $@"queue/tasks/ashigaru{ashigaruIndex}.yaml に任務がある。確認して実行せよ。

重要: あなたの現在の作業ディレクトリ（CWD）は queue/config の基準パスである。
queue/tasks/ashigaru{ashigaruIndex}.yaml は、カレントディレクトリからの相対パスでアクセスせよ。

報告: 任務完了時、報告を必ず次のパスに YAML で書き込むこと: queue/reports/ashigaru{ashigaruIndex}_report.yaml （CWD からの相対パス。ファイルが存在しない場合は新規作成せよ。）

補足: 実装・最適化タスクで前提となる施策ドキュメント等がまだ存在しない場合は、自己分析で方針を立てて進めてよい。";
        }
        var ashigaruModelOverride = ExtractModelOverrideFromTaskContent(taskContent);
        var result = await RunClaudeAsync(userPrompt, ashigaruInstructions, progress, $"足軽{ashigaruIndex}", cancellationToken, cwdOverride, ashigaruModelOverride).ConfigureAwait(false);
        if (result.Success)
        {
            WriteReportFromAshigaruResult(ashigaruIndex, taskContent, result.Output);
        }
        return result.Success;
    }

    /// <summary>タスクの target_path が Shogun リポジトリ配下（queue/, config/, settings.yaml 等）かどうかを判定する。該当する場合は CWD を repo root にして queue/reports に報告を書けるようにする。</summary>
    private static bool IsTaskTargetUnderShogunRepo(string? taskContent)
    {
        if (string.IsNullOrWhiteSpace(taskContent))
            return true;
        var tp = ExtractTargetPathFromTaskContent(taskContent);
        if (string.IsNullOrWhiteSpace(tp))
            return true;
        tp = tp.Replace('\\', '/').Trim();
        if (tp.StartsWith("queue/", StringComparison.Ordinal) || tp.StartsWith("config/", StringComparison.Ordinal))
            return true;
        var fileName = Path.GetFileName(tp);
        if (string.Equals(fileName, "settings.yaml", StringComparison.OrdinalIgnoreCase) ||
            (fileName?.StartsWith("dashboard", StringComparison.OrdinalIgnoreCase) == true) ||
            (fileName?.StartsWith("ashigaru", StringComparison.OrdinalIgnoreCase) == true && fileName?.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) == true))
            return true;
        return false;
    }

    /// <summary>タスク YAML 文字列から target_path の値を抽出する。</summary>
    private static string? ExtractTargetPathFromTaskContent(string taskContent)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(taskContent);
            var wrapper = YamlHelper.Deserialize<TaskWrapper>(bytes);
            return wrapper?.Task?.TargetPath?.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>タスク YAML 文字列から model_override（本家: 昇格/降格）の値を抽出する。</summary>
    private static string? ExtractModelOverrideFromTaskContent(string? taskContent)
    {
        if (string.IsNullOrWhiteSpace(taskContent))
            return null;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(taskContent);
            var wrapper = YamlHelper.Deserialize<TaskWrapper>(bytes);
            var v = wrapper?.Task?.ModelOverride?.Trim();
            return string.IsNullOrEmpty(v) ? null : v;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>報告 result 文字列の最大長。YAML の literal scalar 制限を超えないよう抑える。</summary>
    private const int MaxReportResultLength = 4096;

    /// <summary>1行あたりの最大文字数。YAML の literal 行制限を超えないよう抑える。</summary>
    private const int MaxReportResultLineLength = 2000;

    /// <summary>YAML の literal scalar で問題になりうる制御文字を除去する。</summary>
    private static string SanitizeResultForYaml(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '\n' || c == '\r' || c == '\t' || (c >= ' ' && c <= '\uFFFD'))
                sb.Append(c);
            else if (char.IsSurrogate(c))
                continue;
            else if (c < ' ')
                sb.Append(' ');
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>報告文字列の各行を最大長で打ち切り、全体を MaxReportResultLength 以内に収める。</summary>
    private static string TruncateReportResult(string s)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        var lines = s.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var sb = new StringBuilder();
        var total = 0;
        foreach (var line in lines)
        {
            if (total >= MaxReportResultLength)
                break;
            var chunk = line.Length > MaxReportResultLineLength ? line.Substring(0, MaxReportResultLineLength) + "..." : line;
            if (total + chunk.Length + 1 > MaxReportResultLength)
            {
                var remain = MaxReportResultLength - total - 8;
                if (remain > 0)
                    sb.Append(chunk.AsSpan(0, Math.Min(remain, chunk.Length)));
                sb.Append("\n...(省略)");
                break;
            }
            if (sb.Length > 0)
                sb.Append('\n');
            sb.Append(chunk);
            total += chunk.Length + 1;
        }
        return sb.Length > 0 ? sb.ToString() : (s.Length > MaxReportResultLength ? s.Substring(0, MaxReportResultLength) + "\n...(省略)" : s);
    }

    /// <summary>足軽ジョブ完了時に stdout とタスク内容から報告 YAML を queue/reports に書き込む。エージェントがファイルに書けなくてもアプリ側で必ず保存する。</summary>
    private void WriteReportFromAshigaruResult(int ashigaruIndex, string? taskContent, string? output)
    {
        var taskId = "unknown";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        if (!string.IsNullOrWhiteSpace(taskContent))
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(taskContent);
                var wrapper = YamlHelper.Deserialize<TaskWrapper>(bytes);
                if (!string.IsNullOrWhiteSpace(wrapper?.Task?.TaskId))
                    taskId = wrapper.Task.TaskId;
                if (!string.IsNullOrWhiteSpace(wrapper?.Task?.Timestamp))
                    timestamp = wrapper.Task.Timestamp;
            }
            catch { /* パース失敗時は上記デフォルトのまま */ }
        }
        var result = (output ?? "").Trim();
        result = SanitizeResultForYaml(result);
        result = TruncateReportResult(result);
        try
        {
            _queueService.WriteReportYaml(ashigaruIndex, taskId, timestamp, "done", result);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Logger.Log($"足軽{ashigaruIndex}報告のYAML出力で長さ制限超過の可能性: {ex.Message}. 要約のみ保存します。", LogLevel.Warning);
            const string fallback = "任務完了。出力が長いため要約のみ保存しました。";
            _queueService.WriteReportYaml(ashigaruIndex, taskId, timestamp, "done", fallback);
        }
        catch (Exception ex)
        {
            Logger.Log($"足軽{ashigaruIndex}報告の保存に失敗: {ex.GetType().Name} {ex.Message}", LogLevel.Error);
            var fallback = SanitizeResultForYaml($"報告保存時にエラー: {ex.Message}");
            if (fallback.Length > 200)
                fallback = fallback.Substring(0, 200) + "...";
            _queueService.WriteReportYaml(ashigaruIndex, taskId, timestamp, "error", fallback);
        }
    }

    /// <summary>タスク YAML から target_path や description に含まれるプロジェクト ID を推定する。家老が足軽ごとに別プロジェクトを割り当てている場合に使用。</summary>
    private string? TryInferProjectIdFromTaskContent(string? taskContent)
    {
        if (string.IsNullOrWhiteSpace(taskContent))
            return null;
        var knownIds = _queueService.GetProjectIds();
        foreach (var id in knownIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (taskContent.Contains("projects/" + id + "/", StringComparison.Ordinal) || taskContent.Contains("projects/" + id + "\r", StringComparison.Ordinal) || taskContent.Contains("projects/" + id + "\n", StringComparison.Ordinal))
                return id;
            if (taskContent.Contains("target_path:" + " " + id + "\r", StringComparison.Ordinal) || taskContent.Contains("target_path:" + " " + id + "\n", StringComparison.Ordinal))
                return id;
            if (taskContent.Contains("target_path: " + id + "\r", StringComparison.Ordinal) || taskContent.Contains("target_path: " + id + "\n", StringComparison.Ordinal))
                return id;
            if (taskContent.Contains("project " + id, StringComparison.Ordinal) || taskContent.Contains("プロジェクト " + id, StringComparison.Ordinal))
                return id;
        }
        return null;
    }

    /// <summary>CWD がプロジェクトルートのとき、タスク YAML 内の target_path をプロジェクトルート相対に書き換える。</summary>
    private static string RewriteTaskTargetPathForProjectRoot(string taskContent, string? projectId, string? projectRoot)
    {
        var targetPathLabel = "target_path:";
        var c = taskContent;
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var prefixWithSlash = "projects/" + projectId + "/";
            var prefixOnly = "projects/" + projectId;
            c = c.Replace(targetPathLabel + " " + prefixWithSlash, targetPathLabel + " ");
            c = c.Replace(targetPathLabel + " " + prefixOnly + "\r\n", targetPathLabel + " .\r\n");
            c = c.Replace(targetPathLabel + " " + prefixOnly + "\r", targetPathLabel + " .\r");
            c = c.Replace(targetPathLabel + " " + prefixOnly + "\n", targetPathLabel + " .\n");
            c = c.Replace(targetPathLabel + " " + prefixOnly + "\"", targetPathLabel + " \".\"");
            if (c.Contains(targetPathLabel + " " + prefixOnly))
                c = c.Replace(targetPathLabel + " " + prefixOnly, targetPathLabel + " .");
            c = c.Replace(targetPathLabel + " " + projectId + "\r\n", targetPathLabel + " .\r\n");
            c = c.Replace(targetPathLabel + " " + projectId + "\r", targetPathLabel + " .\r");
            c = c.Replace(targetPathLabel + " " + projectId + "\n", targetPathLabel + " .\n");
            if (c.Contains(targetPathLabel + " " + projectId))
                c = c.Replace(targetPathLabel + " " + projectId, targetPathLabel + " .");
        }
        if (!string.IsNullOrWhiteSpace(projectRoot))
        {
            var normRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, '/').Replace('/', Path.DirectorySeparatorChar);
            c = c.Replace(targetPathLabel + " " + normRoot + "\r\n", targetPathLabel + " .\r\n");
            c = c.Replace(targetPathLabel + " " + normRoot + "\r", targetPathLabel + " .\r");
            c = c.Replace(targetPathLabel + " " + normRoot + "\n", targetPathLabel + " .\n");
            c = c.Replace(targetPathLabel + " \"" + normRoot + "\"\r\n", targetPathLabel + " \".\"\r\n");
            c = c.Replace(targetPathLabel + " \"" + normRoot + "\"\r", targetPathLabel + " \".\"\r");
            c = c.Replace(targetPathLabel + " \"" + normRoot + "\"\n", targetPathLabel + " \".\"\n");
        }
        return c;
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
    /// <param name="cwdOverride">ジョブの作業ディレクトリ（プロジェクトルート等）。null のときは RUNNER_CWD（queue/config の基準）を使用。</param>
    /// <param name="modelOverride">足軽用のモデル上書き（本家の model_override: opus / sonnet）。null のときは設定のデフォルトを使用。</param>
    private async Task<(bool Success, string Output)> RunClaudeAsync(string userPrompt, string systemPromptContent, IProgress<string>? progress, string roleLabel, CancellationToken cancellationToken, string? cwdOverride = null, string? modelOverride = null)
    {
        var repoRoot = _queueService.GetRepoRoot();
        if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
        {
            progress?.Report("queue/config の基準（ドキュメントルート）が無効です。設定の document_root を確認してください。");
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
            modelId = !string.IsNullOrWhiteSpace(modelOverride) ? modelOverride!.Trim() : settings.ModelAshigaru;
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
            var processKey = GetProcessKeyForRole(roleLabel);
            var (success, outputStr) = await _processHost.RunJobAsync(processKey, userPrompt, promptFile, modelId, thinking, progress, cancellationToken, cwdOverride).ConfigureAwait(false);
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

    /// <summary>ロール表示名に対応する常駐プロセスキーを返す。家老の3フェーズは同一プロセス「家老」を使用する。</summary>
    private static string GetProcessKeyForRole(string roleLabel)
    {
        if (roleLabel == "家老（実行フェーズ）" || roleLabel == "家老（報告集約）" || roleLabel == "家老（報告確認）")
            return "家老";
        return roleLabel;
    }

    /// <summary>家老の出力文字列から YAML コンテンツを抽出する。</summary>
    /// <param name="karoYaml">家老の生出力。</param>
    /// <returns>抽出した YAML 文字列。見つからなければ null。</returns>
    private static string? ExtractYamlFromKaroOutput(string karoYaml)
    {
        if (string.IsNullOrWhiteSpace(karoYaml))
            return null;
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
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            var lines = karoYaml.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var yamlLines = new List<string>();
            var foundStart = false;
            foreach (var line in lines)
            {
                if (!foundStart && (line.TrimStart().StartsWith("tasks:", StringComparison.OrdinalIgnoreCase) || line.TrimStart().StartsWith("---")))
                    foundStart = true;
                if (foundStart)
                    yamlLines.Add(line);
            }
            yamlText = yamlLines.Count > 0 ? string.Join("\n", yamlLines) : karoYaml.Trim();
        }
        else
        {
            yamlText = yamlText.Trim();
        }
        return string.IsNullOrWhiteSpace(yamlText) ? null : yamlText;
    }

    /// <summary>家老の出力にタスク割り当て YAML が含まれるか判定する。</summary>
    /// <param name="output">家老の生出力。</param>
    /// <returns>タスク割り当てが 1 件以上あれば true。</returns>
    private static bool HasTaskAssignmentsInKaroOutput(string output)
    {
        var yamlText = ExtractYamlFromKaroOutput(output);
        if (string.IsNullOrWhiteSpace(yamlText))
            return false;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(yamlText);
            var wrapper = YamlHelper.Deserialize<TaskAssignmentYaml>(bytes);
            return wrapper?.Assignments != null && wrapper.Assignments.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>家老の YAML 出力から足軽タスクを生成する。</summary>
    private async Task GenerateAshigaruTasksFromKaroOutputAsync(string karoYaml, CancellationToken cancellationToken)
    {
        try
        {
            var yamlText = ExtractYamlFromKaroOutput(karoYaml);
            if (string.IsNullOrWhiteSpace(yamlText))
            {
                Logger.Log("家老の出力から YAML コンテンツを抽出できませんでした。", LogLevel.Warning);
                return;
            }
            var repoRoot = _queueService.GetRepoRoot();
            try
            {
                var bytes = Encoding.UTF8.GetBytes(yamlText);
                var wrapper = YamlHelper.Deserialize<TaskAssignmentYaml>(bytes);
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
