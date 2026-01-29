using System;
using System.Threading;
using System.Threading.Tasks;

namespace Shogun.Avalonia.Services;

/// <summary>
/// アプリ内で Node.js と Claude Code CLI を導入するサービス（環境を汚さない）。
/// </summary>
public interface IClaudeCodeSetupService
{
    /// <summary>アプリ用 Node.js のルートディレクトリ（未インストール時は空）。</summary>
    string GetAppLocalNodeDir();

    /// <summary>npm のグローバル prefix（Claude Code のインストール先）。</summary>
    string GetAppLocalNpmPrefix();

    /// <summary>アプリ内に Node.js がインストール済みか。</summary>
    bool IsNodeInstalled();

    /// <summary>アプリ内に Claude Code CLI がインストール済みか。</summary>
    bool IsClaudeCodeInstalled();

    /// <summary>Node.js をアプリ用ディレクトリにダウンロード・展開する。</summary>
    Task<bool> InstallNodeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>npm install -g @anthropic-ai/claude-code を prefix 付きで実行する。</summary>
    Task<bool> InstallClaudeCodeAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Claude Code CLI の実行ファイルパス（未インストール時は空）。</summary>
    string GetClaudeExecutablePath();

    /// <summary>claude login を実行し、ブラウザで認証・承認できるようにする。認証方法の選択がある場合は「Claude Pro/Max アカウント」を選択する。</summary>
    /// <param name="progress">進捗メッセージ（任意）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>ログインプロセスを開始できたら true。CLI 未インストール等で開始できなければ false。</returns>
    Task<bool> RunLoginAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>テストコマンド（claude -p "..."）でログイン済みか判定する。CLI 未インストールの場合は false。</summary>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>ログイン済みなら true。</returns>
    Task<bool> IsLoggedInAsync(CancellationToken cancellationToken = default);

    /// <summary>claude コマンドが実行できるか疎通確認する（ログイン不要。--version 等で判定）。</summary>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    /// <returns>疎通できれば true。</returns>
    Task<bool> VerifyClaudeCodeConnectivityAsync(CancellationToken cancellationToken = default);

    /// <summary>Node.js 未インストールなら自動インストール、Claude Code 未インストールなら自動インストールする。ユーザー案内は行わない。</summary>
    /// <param name="progress">進捗（null のときは非表示で実行）。</param>
    /// <param name="cancellationToken">キャンセルトークン。</param>
    Task EnsureClaudeCodeEnvironmentAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
