using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Shogun.Avalonia.Util;

namespace Shogun.Avalonia.Services;

/// <summary>
/// アプリ内で Node.js と Claude Code CLI を導入する（環境を汚さない）。
/// </summary>
public class ClaudeCodeSetupService : IClaudeCodeSetupService
{
    private const string NodeVersion = "v20.20.0";
    private const string NodeZipName = "node-v20.20.0-win-x64.zip";
    private static readonly string NodeDownloadUrl = $"https://nodejs.org/download/release/latest-v20.x/{NodeZipName}";

    private static string BaseDir
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appData, "Shogun.Avalonia");
        }
    }

    /// <inheritdoc />
    public string GetAppLocalNodeDir()
    {
        var nodeDir = Path.Combine(BaseDir, "nodejs");
        return Directory.Exists(nodeDir) && File.Exists(Path.Combine(nodeDir, "node.exe")) ? nodeDir : string.Empty;
    }

    /// <inheritdoc />
    public string GetAppLocalNpmPrefix() => Path.Combine(BaseDir, "npm");

    /// <inheritdoc />
    public bool IsNodeInstalled() => !string.IsNullOrEmpty(GetAppLocalNodeDir());

    /// <inheritdoc />
    public bool IsClaudeCodeInstalled()
    {
        if (!IsNodeInstalled())
            return false;
        var path = GetClaudeExecutablePath();
        return !string.IsNullOrEmpty(path) && File.Exists(path);
    }

    /// <inheritdoc />
    public async Task<bool> InstallNodeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            progress?.Report("Windows のみ対応しています。");
            return false;
        }
        var baseDir = BaseDir;
        var extractDir = Path.Combine(baseDir, "nodejs_extract");
        var nodeDir = Path.Combine(baseDir, "nodejs");
        var zipPath = Path.Combine(baseDir, NodeZipName);
        try
        {
            Logger.Log("Node.js のインストールを開始します。", LogLevel.Info);
            progress?.Report("Node.js をダウンロード中…");
            Directory.CreateDirectory(baseDir);
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                var bytes = await client.GetByteArrayAsync(NodeDownloadUrl, cancellationToken).ConfigureAwait(false);
                await File.WriteAllBytesAsync(zipPath, bytes, cancellationToken).ConfigureAwait(false);
            }
            progress?.Report("Node.js を展開中…");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            if (Directory.Exists(nodeDir))
                Directory.Delete(nodeDir, true);
            var innerDir = Directory.GetDirectories(extractDir).FirstOrDefault(d => Path.GetFileName(d).StartsWith("node-", StringComparison.Ordinal) && Path.GetFileName(d).Contains("-win-"));
            if (string.IsNullOrEmpty(innerDir))
            {
                progress?.Report("展開後のフォルダが見つかりません。");
                return false;
            }
            Directory.Move(innerDir, nodeDir);
            Directory.Delete(extractDir, false);
            try { File.Delete(zipPath); } catch { /* ignore */ }
            Logger.Log("Node.js のインストールが完了しました。", LogLevel.Info);
            progress?.Report("Node.js のインストールが完了しました。");
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogException("Node.js のインストールに失敗しました。", ex);
            progress?.Report($"エラー: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> InstallClaudeCodeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var nodeDir = GetAppLocalNodeDir();
        if (string.IsNullOrEmpty(nodeDir))
        {
            progress?.Report("先に Node.js をインストールしてください。");
            return false;
        }
        var npmPrefix = GetAppLocalNpmPrefix();
        Directory.CreateDirectory(npmPrefix);
        var npmPath = Path.Combine(nodeDir, "npm.cmd");
        if (!File.Exists(npmPath))
        {
            progress?.Report("npm が見つかりません。");
            return false;
        }
        try
        {
            Logger.Log("Claude Code CLI のインストールを開始します。", LogLevel.Info);
            progress?.Report("Claude Code CLI をインストール中…");
            var startInfo = new ProcessStartInfo
            {
                FileName = npmPath,
                Arguments = "install -g @anthropic-ai/claude-code",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = nodeDir
            };
            startInfo.Environment["NPM_CONFIG_PREFIX"] = npmPrefix;
            startInfo.Environment["PATH"] = nodeDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
            using var proc = Process.Start(startInfo);
            if (proc == null)
            {
                progress?.Report("プロセスを開始できませんでした。");
                return false;
            }
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            if (proc.ExitCode != 0)
            {
                Logger.Log($"npm が終了コード {proc.ExitCode} で終了しました。", LogLevel.Warning);
                progress?.Report($"npm が終了コード {proc.ExitCode} で終了しました。");
                return false;
            }
            Logger.Log("Claude Code CLI のインストールが完了しました。", LogLevel.Info);
            progress?.Report("Claude Code CLI のインストールが完了しました。");
            return IsClaudeCodeInstalled();
        }
        catch (Exception ex)
        {
            Logger.LogException("Claude Code CLI のインストールに失敗しました。", ex);
            progress?.Report($"エラー: {ex.Message}");
            return false;
        }
    }

    /// <inheritdoc />
    public string GetClaudeExecutablePath()
    {
        var prefix = GetAppLocalNpmPrefix();
        var claudeCmd = Path.Combine(prefix, "claude.cmd");
        if (File.Exists(claudeCmd))
            return claudeCmd;
        var binClaude = Path.Combine(prefix, "node_modules", ".bin", "claude.cmd");
        return File.Exists(binClaude) ? binClaude : string.Empty;
    }

    /// <inheritdoc />
    public async Task<bool> RunLoginAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var claudePath = GetClaudeExecutablePath();
        if (string.IsNullOrEmpty(claudePath) || !File.Exists(claudePath))
        {
            progress?.Report("Claude Code CLI がインストールされていません。先に「Claude Code をインストール」を実行してください。");
            return false;
        }
        try
        {
            progress?.Report("ログイン用のウィンドウを開いています。認証方法で「Claude Pro/Max アカウント」を選択し、ブラウザで認証・承認してください。");
            var startInfo = new ProcessStartInfo
            {
                FileName = claudePath,
                Arguments = "login",
                UseShellExecute = false,
                CreateNoWindow = false,
                RedirectStandardInput = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            using var proc = new Process { StartInfo = startInfo };
            proc.Start();
            await Task.Delay(2500, cancellationToken).ConfigureAwait(false);
            try
            {
                await proc.StandardInput.WriteLineAsync("2").ConfigureAwait(false);
                proc.StandardInput.Close();
            }
            catch
            {
                // 既にプロンプトが過ぎている場合などは無視
            }
            progress?.Report("ログイン用ウィンドウが開きました。ブラウザで認証を完了してください。");
            return true;
        }
        catch (Exception ex)
        {
            progress?.Report($"エラー: {ex.Message}");
            return false;
        }
    }

    /// <summary>テスト用の最小プロンプト（ログイン確認に使用）。</summary>
    private const string LoginTestPrompt = "reply with exactly: ok";

    /// <inheritdoc />
    public async Task<bool> VerifyClaudeCodeConnectivityAsync(CancellationToken cancellationToken = default)
    {
        var claudePath = GetClaudeExecutablePath();
        if (string.IsNullOrEmpty(claudePath) || !File.Exists(claudePath))
        {
            Logger.Log("Claude Code 疎通確認: 実行ファイルが見つかりません。", LogLevel.Warning);
            return false;
        }
        var nodeDir = GetAppLocalNodeDir();
        try
        {
            Logger.Log("Claude Code の疎通確認を実行します（claude --version）。", LogLevel.Debug);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var proc = new Process();
            proc.StartInfo.FileName = claudePath;
            proc.StartInfo.Arguments = "--version";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(nodeDir))
                proc.StartInfo.Environment["PATH"] = nodeDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
            proc.Start();
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var ok = proc.ExitCode == 0;
            Logger.Log(ok ? "Claude Code の疎通確認に成功しました。" : $"Claude Code の疎通確認に失敗しました（終了コード: {proc.ExitCode}）。", ok ? LogLevel.Info : LogLevel.Warning);
            return ok;
        }
        catch (OperationCanceledException)
        {
            Logger.Log("Claude Code の疎通確認がタイムアウトしました。", LogLevel.Warning);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogException("Claude Code の疎通確認で例外が発生しました。", ex);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default)
    {
        var claudePath = GetClaudeExecutablePath();
        if (string.IsNullOrEmpty(claudePath) || !File.Exists(claudePath))
        {
            Logger.Log("ログイン確認: Claude Code 実行ファイルが見つかりません。", LogLevel.Warning);
            return false;
        }
        var nodeDir = GetAppLocalNodeDir();
        try
        {
            Logger.Log("Claude Code のログイン確認を実行します。", LogLevel.Debug);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            using var proc = new Process();
            proc.StartInfo.FileName = claudePath;
            proc.StartInfo.Arguments = $"-p \"{LoginTestPrompt}\"";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(nodeDir))
                proc.StartInfo.Environment["PATH"] = nodeDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process) ?? "";
            proc.Start();
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            var ok = proc.ExitCode == 0;
            Logger.Log(ok ? "Claude Code のログイン確認に成功しました。" : $"Claude Code のログイン確認に失敗しました（終了コード: {proc.ExitCode}）。", ok ? LogLevel.Info : LogLevel.Warning);
            return ok;
        }
        catch (OperationCanceledException)
        {
            Logger.Log("Claude Code のログイン確認がタイムアウトしました。", LogLevel.Warning);
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogException("Claude Code のログイン確認で例外が発生しました。", ex);
            return false;
        }
    }

    /// <inheritdoc />
    /// <remarks>順序は依存関係に基づく: (1) Node.js → (2) Claude Code → 呼び出し元で (3) ログイン確認。Claude Code は npm で入れるため Node が先。ログイン確認は claude コマンドが必要なため Claude Code が先。</remarks>
    public async Task EnsureClaudeCodeEnvironmentAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Logger.Log("Claude Code 環境の確保を開始します。", LogLevel.Info);
        // (1) Node.js: 基盤。未導入ならインストール
        progress?.Report("Node.js を確認しています...");
        var nodeWasMissing = !IsNodeInstalled();
        if (nodeWasMissing)
        {
            Logger.Log("Node.js が未導入のためインストールを実行します。", LogLevel.Info);
            progress?.Report("Node.js をインストールしています...");
            await InstallNodeAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Logger.Log("Node.js はインストール済みです。", LogLevel.Debug);
            progress?.Report("Node.js はインストール済みです。");
        }

        // (2) Claude Code: Node の上に npm で導入。未導入、または今回 Node を入れた場合はインストール
        progress?.Report("Claude Code を確認しています...");
        var needClaude = !IsClaudeCodeInstalled() || nodeWasMissing;
        if (needClaude)
        {
            Logger.Log("Claude Code が未導入または再インストールが必要なためインストールを実行します。", LogLevel.Info);
            progress?.Report("Claude Code をインストールしています...");
            await InstallClaudeCodeAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Logger.Log("Claude Code はインストール済みです。", LogLevel.Debug);
            progress?.Report("Claude Code はインストール済みです。");
        }
        Logger.Log("Claude Code 環境の確保が完了しました。", LogLevel.Info);
    }
}
