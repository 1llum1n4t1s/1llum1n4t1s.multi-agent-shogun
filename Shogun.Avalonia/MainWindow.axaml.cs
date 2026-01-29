using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Shogun.Avalonia.Services;
using Shogun.Avalonia.ViewModels;

namespace Shogun.Avalonia;

public partial class MainWindow : Window
{
    private readonly ISettingsService _settingsService = new SettingsService();
    private readonly IProjectService _projectService = new ProjectService();

    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    /// <summary>ウィンドウ表示後、Claude Code 環境の準備を開始する（RealTimeTranslator の OnStartup → InitializeModelsAsync と同様）。</summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;
        _ = vm.InitializeClaudeCodeEnvironmentAsync().ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                var ex = t.Exception.GetBaseException();
                global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    vm.LoadingMessage = $"準備エラー: {ex.Message}";
                    vm.IsLoading = false;
                });
            }
        }, TaskScheduler.Default);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var w = new SettingsWindow();
        var mainVm = DataContext as MainWindowViewModel;
        var vm = new SettingsViewModel(_settingsService, mainVm?.ClaudeModelsService, () =>
        {
            w.Close();
            if (DataContext is MainWindowViewModel m)
                m.RefreshAiService();
        });
        vm.SetInitialModels(mainVm?.LastFetchedModels);
        w.DataContext = vm;
        await w.ShowDialog(this);
    }

    private async void OnProjectSettingsClick(object? sender, RoutedEventArgs e)
    {
        var w = new ProjectSettingsWindow();
        w.DataContext = new ProjectSettingsViewModel(_projectService, () =>
        {
            w.Close();
            if (DataContext is MainWindowViewModel vm)
            {
                vm.LoadProjects();
            }
        });
        await w.ShowDialog(this);
    }

    private async void OnChatInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainWindowViewModel vm && !vm.IsAiProcessing)
        {
            await vm.SendMessageAsync();
            e.Handled = true;
        }
    }

    private async void OnSendButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && !vm.IsAiProcessing)
        {
            await vm.SendMessageAsync();
        }
    }
}
