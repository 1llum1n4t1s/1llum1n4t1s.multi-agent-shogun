using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Shogun.Avalonia.Util;

namespace Shogun.Avalonia.Services;

/// <summary>
/// 将軍・家老・足軽の Claude Code CLI を常駐プロセスで起動し、ジョブごとに終了させない。
/// プロセスの終了は <see cref="StopAll"/> 呼び出し（アプリ終了時）のみ。
/// </summary>
public class ClaudeCodeProcessHost : IClaudeCodeProcessHost
{
    private static readonly string RunnerScriptContent = GetRunnerScriptContent();

    private readonly IClaudeCodeSetupService _setupService;
    private readonly IShogunQueueService _queueService;
    private readonly Dictionary<string, ProcessEntry> _processes = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _started;

    /// <summary>サービスを生成する。</summary>
    public ClaudeCodeProcessHost(IClaudeCodeSetupService setupService, IShogunQueueService queueService)
    {
        _setupService = setupService;
        _queueService = queueService;
    }

    /// <inheritdoc />
    public async Task StartAllAsync(Func<string, string, Task>? onProcessReady = null, CancellationToken cancellationToken = default)
    {
        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_started)
                return;
            var nodeDir = _setupService.GetAppLocalNodeDir();
            if (string.IsNullOrEmpty(nodeDir))
            {
                Logger.Log("ClaudeCodeProcessHost: Node.js がインストールされていません。", LogLevel.Error);
                return;
            }
            var nodeExe = Path.Combine(nodeDir, "node.exe");
            if (!File.Exists(nodeExe))
            {
                Logger.Log($"ClaudeCodeProcessHost: node.exe が見つかりません: {nodeExe}", LogLevel.Error);
                return;
            }
            var cliJs = Path.Combine(_setupService.GetAppLocalNpmPrefix(), "node_modules", "@anthropic-ai", "claude-code", "cli.js");
            if (!File.Exists(cliJs))
            {
                Logger.Log($"ClaudeCodeProcessHost: cli.js が見つかりません: {cliJs}", LogLevel.Error);
                return;
            }
            var repoRoot = _queueService.GetRepoRoot();
            if (string.IsNullOrEmpty(repoRoot) || !Directory.Exists(repoRoot))
            {
                Logger.Log("ClaudeCodeProcessHost: queue/config の基準パスが無効です。", LogLevel.Error);
                return;
            }
            Logger.Log($"ClaudeCodeProcessHost: RUNNER_CWD（作業ディレクトリ）={repoRoot}", LogLevel.Info);
            var queueDir = Path.Combine(repoRoot, "queue");
            if (!Directory.Exists(queueDir))
                Logger.Log($"ClaudeCodeProcessHost: queue/ が基準パス配下にありません。足軽のタスク・報告はこのパスを基準にします: {queueDir}", LogLevel.Warning);
            var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shogun.Avalonia");
            Directory.CreateDirectory(baseDir);
            var runnerPath = Path.Combine(baseDir, "agent-runner.js");
            await File.WriteAllTextAsync(runnerPath, RunnerScriptContent, cancellationToken).ConfigureAwait(false);

            var roles = new List<string> { "将軍", "家老" };
            var ashigaruCount = _queueService.GetAshigaruCount();
            for (var i = 1; i <= ashigaruCount; i++)
                roles.Add($"足軽{i}");

            var settings = _queueService.GetSettings();
            Logger.Log($"ClaudeCodeProcessHost: DangerouslySkipPermissions={settings.DangerouslySkipPermissions}（config/settings.yaml より読み取り）", LogLevel.Info);
            var env = new Dictionary<string, string>
            {
                ["RUNNER_NODE_EXE"] = nodeExe,
                ["RUNNER_CLI_JS"] = cliJs,
                ["RUNNER_CWD"] = repoRoot,
                ["RUNNER_ADD_DIR"] = repoRoot,
                ["RUNNER_SKIP_PERMISSIONS"] = settings.DangerouslySkipPermissions ? "true" : "false",
                ["CI"] = "true",
                ["NO_COLOR"] = "true",
                ["TERM"] = "dumb",
                ["FORCE_COLOR"] = "0",
                ["NODE_OPTIONS"] = "--no-deprecation",
                ["DEBIAN_FRONTEND"] = "noninteractive",
                ["GITHUB_ACTIONS"] = "true",
                ["PATH"] = nodeDir + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? "")
            };

            foreach (var role in roles)
            {
                var roleEnv = new Dictionary<string, string>(env)
                {
                    ["RUNNER_ROLE"] = role
                };
                var entry = StartRunnerProcess(nodeExe, runnerPath, roleEnv);
                if (entry != null)
                {
                    _processes[role] = entry;
                    if (onProcessReady != null)
                        await onProcessReady(role, "✓ プロセス起動完了").ConfigureAwait(false);
                }
            }
            _started = true;
            Logger.Log($"ClaudeCodeProcessHost: 常駐プロセスを {_processes.Count} ロールで起動しました。", LogLevel.Info);
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<(bool Success, string Output)> RunJobAsync(
        string roleLabel,
        string userPrompt,
        string systemPromptPath,
        string? modelId = null,
        bool thinking = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        string? cwdOverride = null)
    {
        if (!_processes.TryGetValue(roleLabel, out var entry))
        {
            progress?.Report($"{roleLabel} の常駐プロセスがありません。");
            return (false, string.Empty);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"prompt: {EscapeYaml(userPrompt)}");
        sb.AppendLine($"systemPromptFile: {EscapeYaml(systemPromptPath)}");
        if (!string.IsNullOrEmpty(modelId)) sb.AppendLine($"modelId: {EscapeYaml(modelId)}");
        sb.AppendLine($"thinking: {thinking.ToString().ToLower()}");
        if (!string.IsNullOrEmpty(cwdOverride)) sb.AppendLine($"cwd: {EscapeYaml(cwdOverride)}");
        sb.AppendLine("---");
        var line = sb.ToString();
        var tcs = new TaskCompletionSource<(bool Success, string Output)>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (entry.Lock)
        {
            if (entry.CurrentTcs != null)
            {
                progress?.Report($"{roleLabel} は別ジョブ実行中です。");
                return (false, string.Empty);
            }
            entry.CurrentTcs = tcs;
            entry.CurrentProgress = progress;
        }

        try
        {
            entry.StdinWriter.Write(line);
            await entry.StdinWriter.FlushAsync(cancellationToken).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(600)); // 600秒（足軽の分析・実装完了を待つため）
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return (false, "タイムアウト");
        }
        finally
        {
            lock (entry.Lock)
            {
                entry.CurrentTcs = null;
                entry.CurrentProgress = null;
            }
        }
    }

    private static string EscapeYaml(string s)
    {
        if (string.IsNullOrEmpty(s)) return "\"\"";
        if (s.Contains(':') || s.Contains('"') || s.Contains('#') || s.Contains('\n') || s.Contains('\r'))
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
        return s;
    }

    /// <inheritdoc />
    public void StopAll()
    {
        _initLock.Wait();
        try
        {
            foreach (var kv in _processes)
            {
                try
                {
                    var entry = kv.Value;
                    if (entry.Process.HasExited)
                        continue;
                    try
                    {
                        entry.StdinWriter?.Close();
                    }
                    catch { /* stdin 閉鎖は失敗しても続行 */ }
                }
                catch (Exception ex)
                {
                    Logger.Log($"ClaudeCodeProcessHost: stdin 閉鎖時に例外: {kv.Key}, {ex.Message}", LogLevel.Debug);
                }
            }
            var waitMs = 2000;
            var deadline = DateTime.UtcNow.AddMilliseconds(waitMs);
            while (DateTime.UtcNow < deadline)
            {
                var allExited = true;
                foreach (var kv in _processes)
                {
                    if (!kv.Value.Process.HasExited)
                    {
                        allExited = false;
                        break;
                    }
                }
                if (allExited) break;
                Thread.Sleep(100);
            }
            foreach (var kv in _processes)
            {
                try
                {
                    var proc = kv.Value.Process;
                    if (proc.HasExited)
                        continue;
                    var pid = proc.Id;
                    if (OperatingSystem.IsWindows())
                    {
                        try
                        {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "taskkill",
                                Arguments = $"/T /F /PID {pid}",
                                CreateNoWindow = true,
                                UseShellExecute = false
                            };
                            using (var p = Process.Start(psi))
                            {
                                p?.WaitForExit(3000);
                            }
                        }
                        catch
                        {
                            proc.Kill(entireProcessTree: true);
                        }
                    }
                    else
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    if (!proc.HasExited && !proc.WaitForExit(2000))
                        Logger.Log($"ClaudeCodeProcessHost: プロセス {kv.Key} (PID {pid}) が終了しませんでした。", LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    Logger.Log($"ClaudeCodeProcessHost: プロセス終了時に例外: {kv.Key}, {ex.Message}", LogLevel.Warning);
                }
            }
            _processes.Clear();
            _started = false;
            Logger.Log("ClaudeCodeProcessHost: 全常駐プロセスを終了しました。", LogLevel.Info);
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static ProcessEntry? StartRunnerProcess(string nodeExe, string runnerPath, Dictionary<string, string> env)
    {
        try
        {
            var proc = new Process();
            proc.StartInfo.FileName = nodeExe;
            proc.StartInfo.Arguments = "\"" + runnerPath.Replace("\"", "\\\"") + "\"";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.CreateNoWindow = true;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.RedirectStandardError = true;
            proc.StartInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            proc.StartInfo.StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            proc.StartInfo.StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            foreach (var kv in env)
                proc.StartInfo.Environment[kv.Key] = kv.Value;
            proc.Start();
            var stdinWriter = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = false };
            var stdout = proc.StandardOutput;
            var stderr = proc.StandardError;
            var entry = new ProcessEntry(proc, stdinWriter);
            entry.StdoutReaderTask = Task.Run(() => ReadStdoutLoopAsync(entry, stdout));
            _ = Task.Run(() => ReadStderrLoopAsync(stderr));
            return entry;
        }
        catch (Exception ex)
        {
            Logger.LogException("ClaudeCodeProcessHost: ランプロセス起動失敗", ex);
            return null;
        }
    }

    private static async Task ReadStdoutLoopAsync(ProcessEntry entry, StreamReader stdout)
    {
        try
        {
            var buf = new StringBuilder();
            var charBuf = new char[4096];
            while (!entry.Process.HasExited)
            {
                int read = await stdout.ReadAsync(charBuf, 0, charBuf.Length).ConfigureAwait(false);
                if (read == 0) break;

                for (int i = 0; i < read; i++)
                {
                    char c = charBuf[i];
                    if (c == '\n')
                    {
                        string line = buf.ToString().TrimEnd('\r');
                        buf.Clear();
                        ProcessOutputLine(entry, line);
                    }
                    else
                    {
                        buf.Append(c);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch (Exception ex)
        {
            Logger.Log($"ClaudeCodeProcessHost: stdout 読み取り例外: {ex.Message}", LogLevel.Warning);
            lock (entry.Lock)
            {
                entry.CurrentTcs?.TrySetResult((false, ex.Message));
            }
        }
    }

    private static void ProcessOutputLine(ProcessEntry entry, string line)
    {
        Logger.Log($"[Runner stdout] {line}", LogLevel.Debug);
        TaskCompletionSource<(bool Success, string Output)>? tcs;
        IProgress<string>? progress;
        lock (entry.Lock)
        {
            tcs = entry.CurrentTcs;
            progress = entry.CurrentProgress;
        }

        if (line.StartsWith("OUT:", StringComparison.Ordinal))
        {
            var msg = line.Length > 4 ? line.Substring(4) : "";
            progress?.Report(msg);
        }
        else if (line.StartsWith("RESULT:", StringComparison.Ordinal))
        {
            var rest = line.Length > 7 ? line.Substring(7) : "";
            bool success = false;
            string output = "";
            try
            {
                var exitCode = 1;
                var outputIdx = rest.IndexOf("output:", StringComparison.OrdinalIgnoreCase);
                if (outputIdx >= 0)
                {
                    var exitStr = rest[..outputIdx].Trim();
                    var exitCodePrefix = "exitCode:";
                    var exitCodeIdx = exitStr.IndexOf(exitCodePrefix, StringComparison.OrdinalIgnoreCase);
                    if (exitCodeIdx >= 0)
                    {
                        var numStart = exitCodeIdx + exitCodePrefix.Length;
                        var commaIdx = exitStr.IndexOf(',', numStart);
                        var numStr = commaIdx >= 0 ? exitStr.Substring(numStart, commaIdx - numStart).Trim() : exitStr.Substring(numStart).Trim();
                        int.TryParse(numStr, out exitCode);
                    }
                    var valueStart = rest.IndexOf('"', outputIdx + 7);
                    if (valueStart >= 0)
                    {
                        output = ExtractQuotedValue(rest, valueStart);
                        success = (exitCode == 0);
                    }
                }
                if (string.IsNullOrEmpty(output))
                    output = rest;
            }
            catch
            {
                output = rest;
                success = false;
            }
            tcs?.TrySetResult((success, output));
        }
    }

    /// <summary>output: "..." の引用符内の値を取り出し、\n \r \t \" \\ を復元する。</summary>
    private static string ExtractQuotedValue(string line, int quoteStartIndex)
    {
        if (quoteStartIndex < 0 || quoteStartIndex >= line.Length || line[quoteStartIndex] != '"')
            return string.Empty;
        var sb = new StringBuilder();
        for (var i = quoteStartIndex + 1; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
                return sb.ToString();
            if (c == '\\' && i + 1 < line.Length)
            {
                var next = line[i + 1];
                if (next == 'n') { sb.Append('\n'); i++; continue; }
                if (next == 'r') { sb.Append('\r'); i++; continue; }
                if (next == 't') { sb.Append('\t'); i++; continue; }
                if (next == '"') { sb.Append('"'); i++; continue; }
                if (next == '\\') { sb.Append('\\'); i++; continue; }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static async Task ReadStderrLoopAsync(StreamReader stderr)
    {
        try
        {
            string? line;
            while ((line = await stderr.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                Logger.Log($"[Runner stderr] {line}", LogLevel.Debug);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"ClaudeCodeProcessHost: stderr 読み取り例外: {ex.Message}", LogLevel.Warning);
        }
    }

    private static string GetRunnerScriptContent()
    {
        return """
const { spawn } = require('child_process');
const readline = require('readline');
const fs = require('fs');

const nodeExe = process.env.RUNNER_NODE_EXE;
const cliJs = process.env.RUNNER_CLI_JS;
const cwd = process.env.RUNNER_CWD;
const addDir = process.env.RUNNER_ADD_DIR || '';
const skipPermissions = process.env.RUNNER_SKIP_PERMISSIONS === 'true';
const isResident = process.env.RUNNER_RESIDENT === 'true';
const role = process.env.RUNNER_ROLE || '';

if (!nodeExe || !cliJs || !cwd) {
  const msg = `RUNNER env missing: nodeExe=${nodeExe}, cliJs=${cliJs}, cwd=${cwd}`;
  process.stdout.write('RESULT: exitCode: 1, output: "' + msg + '"\n');
  process.exit(1);
}

process.stderr.write(`[RUNNER] Started role=${role}, resident=${isResident}, cwd=${cwd}\n`);

let residentChild = null;
let residentStdin = null;
let residentChunks = [];
let residentTimer = null;
const RESIDENT_INACTIVITY_MS = 30000;

function clearResidentTimer() {
  if (residentTimer) {
    clearTimeout(residentTimer);
    residentTimer = null;
  }
}

function flushResidentResult(exitCode) {
  clearResidentTimer();
  const fullOutput = residentChunks.join('\n').replace(/\\\\/g, '\\\\\\\\').replace(/"/g, '\\\\"').replace(/\\n/g, '\\\\n');
  process.stdout.write(`RESULT: exitCode: ${exitCode || 0}, output: "${fullOutput}"\n`);
  residentChunks = [];
}

function runResidentKaroJob(job, jobCwd) {
  const prompt = job.prompt || '';
  const systemPromptFile = job.systemPromptFile || '';
  const modelId = job.modelId;
  const thinking = job.thinking === 'true';

  if (!residentChild) {
    let appendPrompt = '';
    if (systemPromptFile && fs.existsSync(systemPromptFile)) {
      try {
        appendPrompt = fs.readFileSync(systemPromptFile, 'utf8');
      } catch (e) {
        process.stderr.write(`[RUNNER] Resident: failed to read system prompt file: ${e.message}\n`);
      }
    }
    const args = [cliJs];
    if (appendPrompt) args.push('--append-system-prompt', appendPrompt);
    const addDirsR = new Set();
    if (addDir.trim()) addDirsR.add(addDir.trim());
    if (jobCwd && jobCwd !== cwd) addDirsR.add(jobCwd);
    for (const d of addDirsR) {
      args.push('--add-dir', d);
    }
    if (!skipPermissions && addDirsR.size > 0) {
      const allowedPaths = [...addDirsR].map(d => d.replace(/\\\\/g, '/'));
      const editArgs = allowedPaths.flatMap(p => ['Edit(' + p + '/*)', 'Edit(' + p + '/**)']);
      const writeArgs = allowedPaths.flatMap(p => ['Write(' + p + '/*)', 'Write(' + p + '/**)']);
      args.push('--allowedTools', 'Read', 'Bash', ...editArgs, ...writeArgs);
    }
    if (skipPermissions) args.push('--dangerously-skip-permissions');
    if (modelId) args.push('--model', modelId);
    if (thinking) args.push('--thinking');

    process.stderr.write(`[RUNNER] Resident Karo: spawning long-lived child (no -p)\n`);
    try {
      const child = spawn(nodeExe, args, {
        cwd: jobCwd,
        stdio: ['pipe', 'pipe', 'pipe'],
        timeout: 600000
      });
      residentChild = child;
      residentStdin = child.stdin;

      child.stdout.setEncoding('utf8');
      child.stdout.on('data', (data) => {
        const str = data.toString();
        let idx;
        let buf = str;
        const nl = '\n';
        while ((idx = buf.indexOf(nl)) !== -1) {
          const line2 = buf.slice(0, idx);
          buf = buf.slice(idx + 1);
          residentChunks.push(line2);
          process.stdout.write('OUT:' + line2 + nl);
        }
        if (buf.length > 0) residentChunks.push(buf);
        clearResidentTimer();
        residentTimer = setTimeout(() => flushResidentResult(0), RESIDENT_INACTIVITY_MS);
      });

      child.stderr.on('data', (data) => {
        const errStr = data.toString().trim();
        if (errStr) {
          process.stderr.write(`[RUNNER] Resident child stderr: ${errStr}\n`);
          process.stdout.write('OUT:[stderr] ' + errStr + '\\n');
        }
      });

      child.on('close', (code) => {
        process.stderr.write(`[RUNNER] Resident child closed with code: ${code}\n`);
        residentChild = null;
        residentStdin = null;
        if (residentChunks.length > 0) flushResidentResult(code || 0);
      });

      child.on('error', (err) => {
        process.stderr.write(`[RUNNER] Resident child error: ${err.message}\n`);
        residentChild = null;
        residentStdin = null;
        flushResidentResult(1);
      });
    } catch (spawnErr) {
      process.stderr.write(`[RUNNER] Resident spawn error: ${spawnErr.message}\n`);
      process.stdout.write('RESULT: exitCode: 1, output: "' + spawnErr.message.replace(/"/g, '\\\\"') + '"\n');
      return;
    }
  }

  residentChunks = [];
  clearResidentTimer();
  residentTimer = setTimeout(() => flushResidentResult(0), RESIDENT_INACTIVITY_MS);
  if (residentStdin && !residentStdin.destroyed) {
    residentStdin.write(prompt + '\n');
    residentStdin.write('\n');
  } else {
    flushResidentResult(1);
  }
}

const rl = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });

let currentJobLines = [];

rl.on('line', (line) => {
  if (line.trim() === '---') {
    if (currentJobLines.length === 0) return;

    const jobText = currentJobLines.join('\n');
    currentJobLines = [];

    process.stderr.write(`[RUNNER] Received job (${jobText.length} chars)\n`);

    const job = {};
    jobText.split('\n').forEach(l => {
      const parts = l.split(':');
      if (parts.length >= 2) {
        const key = parts[0].trim();
        let val = parts.slice(1).join(':').trim();
        if (val.startsWith('"') && val.endsWith('"')) {
          val = val.substring(1, val.length - 1).replace(/\\\\\\\\/g, '\\\\').replace(/\\\\"/g, '"').replace(/\\\\r/g, '\\r').replace(/\\\\n/g, '\\n');
        }
        job[key] = val;
      }
    });

    const prompt = job.prompt || '';
    const systemPromptFile = job.systemPromptFile || '';
    const modelId = job.modelId;
    const thinking = job.thinking === 'true';
    const jobCwd = (job.cwd && String(job.cwd).trim()) || cwd;

    if (isResident && role === '家老') {
      runResidentKaroJob(job, jobCwd);
      return;
    }

    const args = [cliJs, '-p', prompt, '--append-system-prompt-file', systemPromptFile];
    const addDirs = new Set();
    if (addDir.trim()) addDirs.add(addDir.trim());
    if (jobCwd && jobCwd !== cwd) addDirs.add(jobCwd);
    for (const d of addDirs) {
      args.push('--add-dir', d);
    }
    if (!skipPermissions && addDirs.size > 0) {
      const allowedPaths = [...addDirs].map(d => d.replace(/\\\\/g, '/'));
      const editArgs = allowedPaths.flatMap(p => ['Edit(' + p + '/*)', 'Edit(' + p + '/**)']);
      const writeArgs = allowedPaths.flatMap(p => ['Write(' + p + '/*)', 'Write(' + p + '/**)']);
      args.push('--allowedTools', 'Read', 'Bash', ...editArgs, ...writeArgs);
    }
    if (skipPermissions) {
      args.push('--dangerously-skip-permissions');
    }
    if (modelId) {
      args.push('--model', modelId);
    }
    if (thinking) {
      args.push('--thinking');
    }

    process.stderr.write(`[RUNNER] Spawning CLI: ${nodeExe} ${args.join(' ')} (cwd=${jobCwd})\n`);

    let stderrOutput = '';
    try {
      const child = spawn(nodeExe, args, {
        cwd: jobCwd,
        stdio: ['pipe', 'pipe', 'pipe'],
        timeout: 600000
      });

      process.stderr.write(`[RUNNER] Child process spawned\n`);

      child.stdin?.end();

      const chunks = [];
      let buf = '';

      child.stdout.setEncoding('utf8');
      child.stdout.on('data', (data) => {
        buf += data.toString();
        let idx;
        while ((idx = buf.indexOf('\n')) !== -1) {
          const line2 = buf.slice(0, idx);
          buf = buf.slice(idx + 1);
          if (line2.length > 0) chunks.push(line2);
          process.stdout.write('OUT:' + line2 + '\n');
        }
      });

      child.stderr.on('data', (data) => {
        const errStr = data.toString();
        if (errStr.trim()) {
          stderrOutput += errStr;
          process.stderr.write(`[RUNNER] Child stderr: ${errStr.trim()}\n`);
          process.stdout.write('OUT:[stderr] ' + errStr.trim() + '\n');
        }
      });

      child.on('error', (err) => {
        const errMsg = `Child spawn error: ${err.message}`;
        process.stderr.write(`[RUNNER] Spawn error: ${errMsg}\n`);
        process.stdout.write('RESULT: exitCode: 1, output: "' + errMsg.replace(/"/g, '\\"') + '"\n');
      });

      child.on('close', (code) => {
        process.stderr.write(`[RUNNER] Child process closed with code: ${code}\n`);
        if (buf.length > 0) {
          chunks.push(buf);
          process.stdout.write('OUT:' + buf + '\n');
        }
        const fullOutput = chunks.join('\n').replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n');
        if (code !== 0 && stderrOutput) {
          const errOut = stderrOutput.replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, '\\n');
          process.stdout.write(`RESULT: exitCode: ${code || 1}, output: "STDERR: ${errOut}\\nSTDOUT: ${fullOutput}"\n`);
        } else {
          process.stdout.write(`RESULT: exitCode: ${code || 0}, output: "${fullOutput}"\n`);
        }
      });
    } catch (spawnErr) {
      const errMsg = `Spawn error: ${spawnErr.message}`;
      process.stderr.write(`[RUNNER] Exception: ${errMsg}\n`);
      process.stdout.write('RESULT: exitCode: 1, output: "' + errMsg.replace(/"/g, '\\"') + '"\n');
    }
  } else {
    currentJobLines.push(line);
  }
});

rl.on('close', () => {
  process.stderr.write('[RUNNER] stdin closed\n');
  process.exit(0);
});
""";
    }

    private sealed class ProcessEntry
    {
        internal Process Process { get; }
        internal StreamWriter StdinWriter { get; }
        internal Task? StdoutReaderTask { get; set; }
        internal readonly object Lock = new();
        internal TaskCompletionSource<(bool Success, string Output)>? CurrentTcs;
        internal IProgress<string>? CurrentProgress;

        internal ProcessEntry(Process process, StreamWriter stdinWriter)
        {
            Process = process;
            StdinWriter = stdinWriter;
        }
    }
}
