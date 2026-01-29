using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Shogun.Avalonia;

/// <summary>
/// プロジェクト設定画面のコードビハインド。
/// プロジェクトパスは参照ボタンでフォルダ選択ダイアログから指定する。
/// </summary>
public partial class ProjectSettingsWindow : Window
{
    public ProjectSettingsWindow()
    {
        InitializeComponent();
    }

    /// <summary>プロジェクトパス参照ボタン押下時、フォルダ選択ダイアログを表示して選択パスを設定する。</summary>
    private async void OnBrowseProjectPath(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.ProjectSettingsViewModel vm && vm.SelectedProject != null)
            await PickFolderAndSet(p => vm.SelectedProject!.Path = p, "プロジェクトのフォルダを選択").ConfigureAwait(true);
    }

    /// <summary>フォルダ選択ダイアログを表示し、選択されたパスを指定アクションに渡す。</summary>
    private async System.Threading.Tasks.Task PickFolderAndSet(Action<string> setPath, string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title });
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            setPath(path);
    }
}
