using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AvaloniaEdit.Document;
using Shogun.Avalonia.Models;
using Shogun.Avalonia.Services;
using Shogun.Avalonia.Util;

namespace Shogun.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IProjectService _projectService;
    private IAiService _aiService;
    private readonly IShogunQueueService _queueService;
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ISettingsService _settingsService;
    private readonly IClaudeCodeSetupService _claudeCodeSetupService;
    private readonly IClaudeCodeRunService _claudeCodeRunService;
    private readonly IClaudeModelsService _claudeModelsService;
    private bool _claudeCodeEnvInitialized;

    /// <summary>設定画面でモデル一覧取得に使うサービス（Claude Code CLI 経由。API キーは使わない）。</summary>
    internal IClaudeModelsService ClaudeModelsService => _claudeModelsService;

    /// <summary>起動時に最後に取得したモデル一覧（ID と表示名）。設定画面のドロップダウン初期表示に渡す。</summary>
    internal IReadOnlyList<(string Id, string Name)>? LastFetchedModels { get; private set; }

    /// <summary>各エージェントのペイン（将軍・家老・足軽1～8）。多カラム表示用。</summary>
    public ObservableCollection<AgentPaneViewModel> AgentPanes { get; } = new();

    /// <summary>左ペイン用（将軍・家老）。</summary>
    public AgentPaneViewModel? LeftPane0 => AgentPanes.Count > 0 ? AgentPanes[0] : null;
    public AgentPaneViewModel? LeftPane1 => AgentPanes.Count > 1 ? AgentPanes[1] : null;
    /// <summary>中央ペイン用（足軽1～4）。</summary>
    public AgentPaneViewModel? CenterPane0 => AgentPanes.Count > 2 ? AgentPanes[2] : null;
    public AgentPaneViewModel? CenterPane1 => AgentPanes.Count > 3 ? AgentPanes[3] : null;
    public AgentPaneViewModel? CenterPane2 => AgentPanes.Count > 4 ? AgentPanes[4] : null;
    public AgentPaneViewModel? CenterPane3 => AgentPanes.Count > 5 ? AgentPanes[5] : null;
    /// <summary>右ペイン用（足軽5～8）。</summary>
    public AgentPaneViewModel? RightPane0 => AgentPanes.Count > 6 ? AgentPanes[6] : null;
    public AgentPaneViewModel? RightPane1 => AgentPanes.Count > 7 ? AgentPanes[7] : null;
    public AgentPaneViewModel? RightPane2 => AgentPanes.Count > 8 ? AgentPanes[8] : null;
    public AgentPaneViewModel? RightPane3 => AgentPanes.Count > 9 ? AgentPanes[9] : null;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _chatMessages = new();

    [ObservableProperty]
    private ObservableCollection<TaskItem> _allTasks = new();

    /// <summary>表示用のタスク一覧（選択中のプロジェクトのタスクのみ）。</summary>
    public ObservableCollection<TaskItem> Tasks
    {
        get
        {
            var filtered = new ObservableCollection<TaskItem>();
            var projectId = SelectedProject?.Id ?? "";
            foreach (var task in AllTasks.Where(t => t.ProjectId == projectId || string.IsNullOrEmpty(projectId)))
            {
                filtered.Add(task);
            }
            return filtered;
        }
    }

    [ObservableProperty]
    private string _chatInput = string.Empty;

    [ObservableProperty]
    private TextDocument? _codeDocument;

    [ObservableProperty]
    private ObservableCollection<Project> _projects = new();

    [ObservableProperty]
    private Project? _selectedProject;

    partial void OnSelectedProjectChanged(Project? value)
    {
        OnPropertyChanged(nameof(Tasks));
    }

    [ObservableProperty]
    private bool _isAiProcessing = false;

    [ObservableProperty]
    private string _dashboardContent = string.Empty;

    /// <summary>起動準備中（Node.js / Claude Code の確認・インストール）か。</summary>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>起動準備のメッセージ（ユーザーに表示）。</summary>
    [ObservableProperty]
    private string _loadingMessage = "起動準備中...";

    public MainWindowViewModel(IProjectService? projectService = null, IAiService? aiService = null, IShogunQueueService? queueService = null, IAgentOrchestrator? orchestrator = null, ISettingsService? settingsService = null, IClaudeCodeSetupService? claudeCodeSetupService = null, IClaudeCodeRunService? claudeCodeRunService = null, IClaudeModelsService? claudeModelsService = null)
    {
        _projectService = projectService ?? new ProjectService();
        _settingsService = settingsService ?? new SettingsService();
        _claudeCodeSetupService = claudeCodeSetupService ?? new ClaudeCodeSetupService();
        _queueService = queueService ?? new ShogunQueueService(_settingsService);
        _claudeCodeRunService = claudeCodeRunService ?? new ClaudeCodeRunService(_claudeCodeSetupService, _queueService, new InstructionsLoader(_queueService));
        _claudeModelsService = claudeModelsService ?? new ClaudeCodeModelsService();
        _aiService = aiService ?? new AiService();
        _orchestrator = orchestrator ?? new AgentOrchestrator(_queueService, _aiService, new InstructionsLoader(_queueService));
        LoadProjects();
        InitializeDummyData();
        RefreshDashboard();
    }

    /// <summary>起動時: Node.js / Claude Code の確認・自動インストールとログイン確認を行う。RealTimeTranslator の InitializeModelsAsync と同様に UI で進捗を表示する。</summary>
    public async Task InitializeClaudeCodeEnvironmentAsync()
    {
        if (_claudeCodeEnvInitialized)
            return;
        _claudeCodeEnvInitialized = true;
        Logger.Log("起動時環境初期化を開始します。", LogLevel.Info);
        try
        {
            var progress = new Progress<string>(msg => LoadingMessage = msg);
            await _claudeCodeSetupService.EnsureClaudeCodeEnvironmentAsync(progress).ConfigureAwait(true);

            if (!_claudeCodeSetupService.IsNodeInstalled() || !_claudeCodeSetupService.IsClaudeCodeInstalled())
            {
                Logger.Log("Node.js または Claude Code のインストールに失敗しました。", LogLevel.Error);
                LoadingMessage = "Node.js または Claude Code のインストールに失敗しました。設定画面から手動でインストールしてください。";
                return;
            }

            LoadingMessage = "Claude Code の疎通確認をしています...";
            var connectivityOk = await _claudeCodeSetupService.VerifyClaudeCodeConnectivityAsync().ConfigureAwait(true);
            if (!connectivityOk)
            {
                Logger.Log("Claude Code の疎通確認に失敗しました。", LogLevel.Error);
                LoadingMessage = "Claude Code の疎通確認に失敗しました。設定画面から再インストールしてください。";
                return;
            }

            LoadingMessage = "モデル一覧を取得しています...";
            var modelsObtained = await UpgradeSettingsModelsToLatestInFamilyAsync().ConfigureAwait(true);
            if (modelsObtained)
            {
                Logger.Log("起動時環境初期化が完了しました（準備完了）。", LogLevel.Info);
                LoadingMessage = "準備完了";
            }
            else
            {
                Logger.Log("起動時環境初期化が完了しました（モデル一覧は取得できませんでした）。", LogLevel.Warning);
            }
        }
        catch (Exception ex)
        {
            Logger.LogException("起動時環境初期化で例外が発生しました。", ex);
            LoadingMessage = $"準備エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>models.dev でモデル一覧を取得し、保存済みの将軍/家老/足軽モデルを同一ファミリ（Haiku/Sonnet/Opus）の最新に更新して保存する。一覧取得に成功すれば LastFetchedModels に格納し設定画面で利用する。</summary>
    /// <returns>モデル一覧を取得できた場合は true、0件または失敗の場合は false。</returns>
    private async Task<bool> UpgradeSettingsModelsToLatestInFamilyAsync()
    {
        var models = await _claudeModelsService.GetModelsAsync().ConfigureAwait(true);
        if (models.Count == 0)
        {
            LoadingMessage = "モデル一覧を取得できませんでした（未ログインの場合は設定でログインしてください）";
            return false;
        }
        LastFetchedModels = models;
        var ids = models.Select(m => m.Id).Where(id => !ClaudeCodeModelsService.IsInvalidModelId(id)).ToList();
        if (ids.Count == 0)
        {
            LoadingMessage = "モデル一覧を取得できませんでした（有効なモデルがありません）";
            return false;
        }
        var current = _settingsService.Get();
        var curShogun = ClaudeCodeModelsService.IsInvalidModelId(current.ModelShogun) ? string.Empty : (current.ModelShogun ?? string.Empty);
        var curKaro = ClaudeCodeModelsService.IsInvalidModelId(current.ModelKaro) ? string.Empty : (current.ModelKaro ?? string.Empty);
        var curAshigaru = ClaudeCodeModelsService.IsInvalidModelId(current.ModelAshigaru) ? string.Empty : (current.ModelAshigaru ?? string.Empty);
        var modelShogun = string.IsNullOrWhiteSpace(curShogun)
            ? (ModelFamilyHelper.GetLatestIdInFamilyBySort(ids, "sonnet") ?? ids.FirstOrDefault() ?? string.Empty)
            : ModelFamilyHelper.UpgradeToLatestInFamily(curShogun, ids);
        var modelKaro = string.IsNullOrWhiteSpace(curKaro)
            ? (ModelFamilyHelper.GetLatestIdInFamilyBySort(ids, "sonnet") ?? ids.FirstOrDefault() ?? string.Empty)
            : ModelFamilyHelper.UpgradeToLatestInFamily(curKaro, ids);
        var modelAshigaru = string.IsNullOrWhiteSpace(curAshigaru)
            ? (ModelFamilyHelper.GetLatestIdInFamilyBySort(ids, "haiku") ?? ids.FirstOrDefault() ?? string.Empty)
            : ModelFamilyHelper.UpgradeToLatestInFamily(curAshigaru, ids);
        if (modelShogun == current.ModelShogun && modelKaro == current.ModelKaro && modelAshigaru == current.ModelAshigaru)
            return true;
        _settingsService.Save(new AppSettings
        {
            AshigaruCount = current.AshigaruCount,
            SkillSavePath = current.SkillSavePath,
            SkillLocalPath = current.SkillLocalPath,
            ScreenshotPath = current.ScreenshotPath,
            LogLevel = current.LogLevel,
            LogPath = current.LogPath,
            ModelName = current.ModelName,
            ModelShogun = modelShogun,
            ModelKaro = modelKaro,
            ModelAshigaru = modelAshigaru,
            ThinkingShogun = current.ThinkingShogun,
            ThinkingKaro = current.ThinkingKaro,
            ThinkingAshigaru = current.ThinkingAshigaru,
            ApiEndpoint = current.ApiEndpoint,
            RepoRoot = current.RepoRoot
        });
        return true;
    }

    /// <summary>dashboard.md を読み込み表示を更新する。</summary>
    [RelayCommand]
    private void RefreshDashboard()
    {
        DashboardContent = _queueService.ReadDashboardMd();
        if (string.IsNullOrEmpty(DashboardContent))
            DashboardContent = "（dashboard.md がありません。設定でワークスペースルートを指定してください）";
        RefreshAgentPanesFromQueue();
    }

    /// <summary>queue/tasks と queue/reports から各ペインの表示を更新する。</summary>
    private void RefreshAgentPanesFromQueue()
    {
        if (AgentPanes.Count < 2)
            return;
        var karoContent = "queue/shogun_to_karo.yaml の最新: " + string.Join("; ", _queueService.ReadShogunToKaro().Take(3).Select(c => c.Id + " " + c.Command));
        AgentPanes[1].Blocks.Clear();
        AgentPanes[1].Blocks.Add(new PaneBlock { Content = karoContent, Timestamp = DateTime.Now });
        var ashigaruCount = _queueService.GetAshigaruCount();
        for (var i = 1; i <= ashigaruCount && i + 1 < AgentPanes.Count; i++)
        {
            var task = _queueService.ReadTaskYaml(i);
            var report = _queueService.ReadReportYaml(i);
            AgentPanes[i + 1].Blocks.Clear();
            if (!string.IsNullOrEmpty(task))
                AgentPanes[i + 1].Blocks.Add(new PaneBlock { Content = "任務: " + task.Trim().Replace("\r\n", " ").Replace("\n", " "), Timestamp = DateTime.Now });
            if (!string.IsNullOrEmpty(report))
                AgentPanes[i + 1].Blocks.Add(new PaneBlock { Content = "報告: " + report.Trim().Replace("\r\n", " ").Replace("\n", " "), Timestamp = DateTime.Now });
        }
    }

    /// <summary>AIサービスを再初期化する（設定変更後）。</summary>
    public void RefreshAiService()
    {
        _aiService = new AiService();
        OnPropertyChanged(nameof(IsAiAvailable));
    }

    /// <summary>AIサービスが利用可能か。</summary>
    public bool IsAiAvailable => _aiService.IsAvailable;

    /// <summary>プロジェクト一覧を読み込む。</summary>
    public void LoadProjects()
    {
        var projects = _projectService.GetProjects();
        var currentSelectedId = SelectedProject?.Id;
        Projects.Clear();
        foreach (var project in projects)
        {
            Projects.Add(project);
        }
        SelectedProject = Projects.FirstOrDefault(p => p.Id == currentSelectedId) ?? Projects.FirstOrDefault();
        OnPropertyChanged(nameof(Tasks));
    }

    private void InitializeDummyData()
    {
        var paneNames = new List<string> { "将軍", "家老" };
        var ashigaruCount = _queueService.GetAshigaruCount();
        for (var i = 1; i <= ashigaruCount; i++)
            paneNames.Add($"足軽{i}");
        foreach (var name in paneNames)
        {
            var pane = new AgentPaneViewModel { DisplayName = name };
            pane.Blocks.Add(new PaneBlock
            {
                Content = name == "将軍"
                    ? "Shogun.Avalonia にようこそ。下の入力欄から指示を送れ。"
                    : "次の指示をお待ち申し上げる。",
                Timestamp = DateTime.Now
            });
            if (name == "将軍" && IsAiAvailable)
            {
                pane.Blocks.Add(new PaneBlock
                {
                    Content = "何かお手伝いできることはありますか？",
                    Timestamp = DateTime.Now
                });
            }
            else if (name == "将軍" && !IsAiAvailable)
            {
                pane.Blocks.Add(new PaneBlock
                {
                    Content = "AI機能を使用するには、設定画面でAPIキーとモデル名を設定してください。",
                    Timestamp = DateTime.Now
                });
            }
            AgentPanes.Add(pane);
        }

        OnPropertyChanged(nameof(LeftPane0));
        OnPropertyChanged(nameof(LeftPane1));
        OnPropertyChanged(nameof(CenterPane0));
        OnPropertyChanged(nameof(CenterPane1));
        OnPropertyChanged(nameof(CenterPane2));
        OnPropertyChanged(nameof(CenterPane3));
        OnPropertyChanged(nameof(RightPane0));
        OnPropertyChanged(nameof(RightPane1));
        OnPropertyChanged(nameof(RightPane2));
        OnPropertyChanged(nameof(RightPane3));

        var welcomeMessage = new ChatMessage
        {
            Sender = "system",
            Content = "Shogun.Avalonia にようこそ",
            ProjectId = ""
        };
        ChatMessages.Add(welcomeMessage);

        if (IsAiAvailable)
        {
            var aiMessage = new ChatMessage
            {
                Sender = "ai",
                Content = "何かお手伝いできることはありますか？",
                ProjectId = ""
            };
            ChatMessages.Add(aiMessage);
        }
        else
        {
            var aiMessage = new ChatMessage
            {
                Sender = "system",
                Content = "AI機能を使用するには、設定画面でAPIキーとモデル名を設定してください。",
                ProjectId = ""
            };
            ChatMessages.Add(aiMessage);
        }

        CodeDocument = new TextDocument(string.Empty);
    }

    /// <summary>メッセージを送信する（将軍AIで家老への指示文を生成→queue 書き込み→家老・足軽実行）。</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    public async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(ChatInput) || IsAiProcessing)
            return;

        var projectId = SelectedProject?.Id ?? "";
        var inputCopy = ChatInput;
        ChatInput = string.Empty;
        IsAiProcessing = true;

        var userMessage = new ChatMessage
        {
            Sender = "user",
            Content = inputCopy,
            ProjectId = projectId,
            Timestamp = DateTime.Now
        };
        ChatMessages.Add(userMessage);

        if (AgentPanes.Count > 0)
            AgentPanes[0].Blocks.Add(new PaneBlock { Content = inputCopy, Timestamp = DateTime.Now });

        try
        {
            string resultMessage;
            if (_aiService.IsAvailable)
            {
                var commandForKaro = await _orchestrator.ResolveShogunCommandAsync(inputCopy, string.IsNullOrEmpty(projectId) ? null : projectId);
                var id = _queueService.AppendCommand(commandForKaro, string.IsNullOrEmpty(projectId) ? null : projectId, "medium");
                resultMessage = $"将軍が指示文を生成し、キューに追加しました（{id}）。家老・足軽を実行中…";
                var progressMessage = new ChatMessage
                {
                    Sender = "system",
                    Content = resultMessage,
                    ProjectId = projectId,
                    Timestamp = DateTime.Now
                };
                ChatMessages.Add(progressMessage);
                if (AgentPanes.Count > 0)
                    AgentPanes[0].Blocks.Add(new PaneBlock { Content = resultMessage, Timestamp = DateTime.Now });
                var runResult = await _orchestrator.RunAsync(id);
                resultMessage = runResult;
            }
            else
            {
                var id = _queueService.AppendCommand(inputCopy, string.IsNullOrEmpty(projectId) ? null : projectId, "medium");
                if (_claudeCodeSetupService.IsClaudeCodeInstalled())
                {
                    var progress = new Progress<string>(msg =>
                    {
                        var m = new ChatMessage { Sender = "system", Content = msg, ProjectId = projectId, Timestamp = DateTime.Now };
                        ChatMessages.Add(m);
                        if (AgentPanes.Count > 0)
                            AgentPanes[0].Blocks.Add(new PaneBlock { Content = msg, Timestamp = DateTime.Now });
                    });
                    var karoOk = await _claudeCodeRunService.RunKaroAsync(progress).ConfigureAwait(true);
                    if (!karoOk)
                    {
                        resultMessage = $"指示をキューに追加しました（{id}）。家老の実行に失敗しました。ダッシュボードで確認してください。";
                    }
                    else
                    {
                        var ashigaruCount = _queueService.GetAshigaruCount();
                        var assigned = new List<int>();
                        for (var i = 1; i <= ashigaruCount; i++)
                        {
                            var taskContent = _queueService.ReadTaskYaml(i);
                            if (!string.IsNullOrWhiteSpace(taskContent) && (taskContent.Contains("task:", StringComparison.Ordinal) || taskContent.Contains("status:", StringComparison.Ordinal)))
                                assigned.Add(i);
                        }
                        if (assigned.Count > 0)
                        {
                            var ashigaruTasks = assigned.Select(n => _claudeCodeRunService.RunAshigaruAsync(n, progress)).ToArray();
                            await Task.WhenAll(ashigaruTasks).ConfigureAwait(true);
                            var reportOk = await _claudeCodeRunService.RunKaroReportAggregationAsync(progress).ConfigureAwait(true);
                            resultMessage = reportOk
                                ? $"指示をキューに追加しました（{id}）。家老・足軽{assigned.Count}名・家老（報告集約）の実行が完了しました。"
                                : $"指示をキューに追加しました（{id}）。家老・足軽の実行は完了しましたが、家老（報告集約）に失敗しました。ダッシュボードで確認してください。";
                        }
                        else
                        {
                            resultMessage = $"指示をキューに追加しました（{id}）。家老（Claude Code）の実行が完了しました。（割り当てられた足軽なし）";
                        }
                    }
                }
                else
                {
                    resultMessage = $"指示をキューに追加しました（{id}）。Claude Code CLI が未インストールのため家老は実行されません。設定でインストールしてください。";
                }
            }
            var sysMessage = new ChatMessage
            {
                Sender = "system",
                Content = resultMessage,
                ProjectId = projectId,
                Timestamp = DateTime.Now
            };
            ChatMessages.Add(sysMessage);
            if (AgentPanes.Count > 0)
                AgentPanes[0].Blocks.Add(new PaneBlock { Content = resultMessage, Timestamp = DateTime.Now });
            RefreshDashboard();
        }
        catch (Exception ex)
        {
            var errorMessage = new ChatMessage
            {
                Sender = "system",
                Content = $"エラー: {ex.Message}",
                ProjectId = projectId,
                Timestamp = DateTime.Now
            };
            ChatMessages.Add(errorMessage);
            if (AgentPanes.Count > 0)
                AgentPanes[0].Blocks.Add(new PaneBlock { Content = $"エラー: {ex.Message}", Timestamp = DateTime.Now });
        }
        finally
        {
            IsAiProcessing = false;
        }

        await Task.CompletedTask;
    }

    /// <summary>AI応答を処理してタスクやコードを抽出する。</summary>
    private async Task ProcessAiResponseAsync(string aiResponse, string projectId)
    {
        if (string.IsNullOrWhiteSpace(aiResponse))
            return;

        var lines = aiResponse.Split('\n', StringSplitOptions.None);
        var codeBlocks = new List<string>();
        var inCodeBlock = false;
        var currentCodeBlock = new List<string>();
        var codeBlockLanguage = "";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    var code = string.Join("\n", currentCodeBlock);
                    if (!string.IsNullOrWhiteSpace(code))
                    {
                        codeBlocks.Add(code);
                    }
                    currentCodeBlock.Clear();
                    codeBlockLanguage = "";
                }
                else
                {
                    codeBlockLanguage = trimmed.Substring(3).Trim();
                }
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                currentCodeBlock.Add(line);
            }
            else
            {
                var taskDesc = ExtractTaskDescription(line);
                if (!string.IsNullOrWhiteSpace(taskDesc) && taskDesc.Length > 3)
                {
                    var existingTask = AllTasks.FirstOrDefault(t => 
                        t.Description.Equals(taskDesc, StringComparison.OrdinalIgnoreCase) && 
                        t.ProjectId == projectId);
                    if (existingTask == null)
                    {
                        var newTask = new TaskItem
                        {
                            Id = $"task_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid():N}",
                            Description = taskDesc,
                            Status = "pending",
                            Priority = DeterminePriority(taskDesc),
                            ProjectId = projectId
                        };
                        AllTasks.Add(newTask);
                        OnPropertyChanged(nameof(Tasks));
                    }
                }
            }
        }

        if (codeBlocks.Count > 0)
        {
            var latestCode = codeBlocks.Last();
            CodeDocument = new TextDocument(latestCode);
        }
    }

    /// <summary>行からタスク説明を抽出する。</summary>
    private static string ExtractTaskDescription(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith("- ") || trimmed.StartsWith("* ") || trimmed.StartsWith("• "))
        {
            return trimmed.Substring(2).Trim();
        }
        if (trimmed.StartsWith("-") || trimmed.StartsWith("*") || trimmed.StartsWith("•"))
        {
            return trimmed.Substring(1).Trim();
        }
        if (trimmed.StartsWith("1. ") || trimmed.StartsWith("2. ") || trimmed.StartsWith("3. ") ||
            trimmed.StartsWith("4. ") || trimmed.StartsWith("5. ") || trimmed.StartsWith("6. ") ||
            trimmed.StartsWith("7. ") || trimmed.StartsWith("8. ") || trimmed.StartsWith("9. "))
        {
            var dotIndex = trimmed.IndexOf(". ", StringComparison.Ordinal);
            if (dotIndex > 0)
            {
                return trimmed.Substring(dotIndex + 2).Trim();
            }
        }
        return string.Empty;
    }

    /// <summary>タスク説明から優先度を判定する。</summary>
    private static string DeterminePriority(string description)
    {
        var lower = description.ToLowerInvariant();
        if (lower.Contains("重要") || lower.Contains("緊急") || lower.Contains("urgent") || lower.Contains("critical"))
            return "high";
        if (lower.Contains("低") || lower.Contains("low") || lower.Contains("optional"))
            return "low";
        return "medium";
    }
}
