using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Shogun.Avalonia.Models;

/// <summary>
/// エージェントペイン内の1ブロック（指示・報告・ステータス等）。
/// </summary>
public partial class PaneBlock : ObservableObject
{
    /// <summary>表示テキスト（本文）。</summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>ステータス行（例: * Baked for 1m 50s, * Doing..., * Crunched for 1m 25s）。</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    /// <summary>タイムスタンプ（任意）。</summary>
    [ObservableProperty]
    private DateTime? _timestamp;

    /// <summary>確認ボタンを表示するかどうか（家老のコード改修確認用）。</summary>
    [ObservableProperty]
    private bool _showApprovalButtons;

    /// <summary>ブロックの一意な ID（確認ボタン用に必要）。</summary>
    [ObservableProperty]
    private string _blockId = Guid.NewGuid().ToString();

    /// <summary>確認完了フラグ。</summary>
    [ObservableProperty]
    private bool _approvalCompleted;

    /// <summary>確認結果を待機するための TaskCompletionSource。</summary>
    private TaskCompletionSource<bool>? _approvalCompletionSource;

    /// <summary>
    /// 確認を要求し、ユーザーの応答を非同期に待機する。
    /// </summary>
    public Task<bool> RequestApprovalAsync()
    {
        _approvalCompletionSource = new TaskCompletionSource<bool>();
        ShowApprovalButtons = true;
        return _approvalCompletionSource.Task;
    }

    /// <summary>
    /// ユーザーが「許可」ボタンを押した。
    /// </summary>
    public void ApproveExecution()
    {
        _approvalCompletionSource?.TrySetResult(true);
        ShowApprovalButtons = false;
        ApprovalCompleted = true;
    }

    /// <summary>
    /// ユーザーが「却下」ボタンを押した。
    /// </summary>
    public void RejectExecution()
    {
        _approvalCompletionSource?.TrySetResult(false);
        ShowApprovalButtons = false;
        ApprovalCompleted = true;
    }
}
