using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Shogun.Avalonia.Services;
using Shogun.Avalonia.ViewModels;

namespace Shogun.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(projectService, aiService, queueService, orchestrator, settingsService, claudeCodeSetupService, null, claudeModelsService)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
