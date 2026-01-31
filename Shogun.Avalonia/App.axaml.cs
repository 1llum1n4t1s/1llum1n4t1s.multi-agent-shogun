using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Shogun.Avalonia.Services;
using Shogun.Avalonia.ViewModels;

using VYaml.Serialization;

namespace Shogun.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private static IAgentWorkerService? _agentWorkerServiceForShutdown;

    public override void OnFrameworkInitializationCompleted()
    {
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsService = new SettingsService();
            var projectService = new ProjectService();
            var aiService = new AiService();
            var queueService = new ShogunQueueService(settingsService);
            var instructionsLoader = new InstructionsLoader(queueService);
            var orchestrator = new AgentOrchestrator(queueService, aiService, instructionsLoader, settingsService);
            var claudeCodeSetupService = new ClaudeCodeSetupService();
            var claudeModelsService = new ClaudeCodeModelsService();
            var processHost = new ClaudeCodeProcessHost(claudeCodeSetupService, queueService);
            var claudeCodeRunService = new ClaudeCodeRunService(processHost, claudeCodeSetupService, queueService, instructionsLoader);
            var agentWorkerService = new AgentWorkerService(claudeCodeRunService, queueService, processHost);
            _agentWorkerServiceForShutdown = agentWorkerService;
            
            var vm = new MainWindowViewModel(projectService, aiService, queueService, orchestrator, settingsService, claudeCodeSetupService, claudeCodeRunService, agentWorkerService, claudeModelsService);
            
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            desktop.Exit += (s, e) =>
            {
                vm.OnAppShutdown();
                _agentWorkerServiceForShutdown = null;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            _agentWorkerServiceForShutdown?.StopAll();
        }
        catch { /* プロセス終了中は無視 */ }
    }
}
