using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Shogun.Avalonia.Models;

namespace Shogun.Avalonia.ViewModels;

/// <summary>
/// 1エージェント分のカラム（将軍・家老・足軽1～8）。
/// </summary>
public partial class AgentPaneViewModel : ObservableObject
{
    /// <summary>表示名（将軍, 家老, 足軽1 等）。</summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>モデル情報。</summary>
    [ObservableProperty]
    private string _modelInfo = string.Empty;

    /// <summary>ペイン内のブロック一覧（指示・報告・ステータス等）。</summary>
    public ObservableCollection<PaneBlock> Blocks { get; } = new();
}
