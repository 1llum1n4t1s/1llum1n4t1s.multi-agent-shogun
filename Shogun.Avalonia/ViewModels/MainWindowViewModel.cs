using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
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
    private readonly IAgentWorkerService _agentWorkerService;
    private readonly IClaudeModelsService _claudeModelsService;
    private bool _claudeCodeEnvInitialized;

    /// <summary>設定画面でモデル一覧取得に使うサービス（Claude Code CLI 経由。API キーは使わない）。</summary>
    internal IClaudeModelsService ClaudeModelsService => _claudeModelsService;

    /// <summary>環境セットアップサービス（設定画面に渡す用）。</summary>
    internal IClaudeCodeSetupService ClaudeCodeSetupService => _claudeCodeSetupService;

    /// <summary>起動時に最後に取得したモデル一覧（ID と表示名）。設定画面のドロップダウン初期表示に渡す。</summary>
    internal IReadOnlyList<(string Id, string Name)>? LastFetchedModels { get; private set; }

    /// <summary>各エージェントのペイン（将軍・家老・足軽1～8）。多カラム表示用。</summary>
    public ObservableCollection<AgentPaneViewModel> AgentPanes { get; } = new();

    private void UpdateAgentPaneModelInfos()
    {
        if (AgentPanes.Count < 2) return;
        AgentPanes[0].ModelInfo = ModelInfoShogun;
        AgentPanes[1].ModelInfo = ModelInfoKaro;
        for (int i = 2; i < AgentPanes.Count; i++)
        {
            AgentPanes[i].ModelInfo = ModelInfoAshigaru;
        }
    }

    /// <summary>左ペイン用（将軍・家老）。</summary>
    public AgentPaneViewModel? LeftPane0 => AgentPanes.Count > 0 ? AgentPanes[0] : null;
    public AgentPaneViewModel? LeftPane1 => AgentPanes.Count > 1 ? AgentPanes[1] : null;

    /// <summary>中央ペイン用（足軽1～N/2）。</summary>
    public AgentPaneViewModel? CenterPane0 => AgentPanes.Count > 2 ? AgentPanes[2] : null;
    public AgentPaneViewModel? CenterPane1 => AgentPanes.Count > 3 ? AgentPanes[3] : null;
    public AgentPaneViewModel? CenterPane2 => AgentPanes.Count > 4 ? AgentPanes[4] : null;
    public AgentPaneViewModel? CenterPane3 => AgentPanes.Count > 5 ? AgentPanes[5] : null;

    /// <summary>右ペイン用（足軽N/2+1～N）。</summary>
    public AgentPaneViewModel? RightPane0
    {
        get
        {
            var rightStart = 2 + (TotalAshigaru + 1) / 2;
            return AgentPanes.Count > rightStart ? AgentPanes[rightStart] : null;
        }
    }
    public AgentPaneViewModel? RightPane1
    {
        get
        {
            var rightStart = 2 + (TotalAshigaru + 1) / 2;
            return AgentPanes.Count > rightStart + 1 ? AgentPanes[rightStart + 1] : null;
        }
    }
    public AgentPaneViewModel? RightPane2
    {
        get
        {
            var rightStart = 2 + (TotalAshigaru + 1) / 2;
            return AgentPanes.Count > rightStart + 2 ? AgentPanes[rightStart + 2] : null;
        }
    }
    public AgentPaneViewModel? RightPane3
    {
        get
        {
            var rightStart = 2 + (TotalAshigaru + 1) / 2;
            return AgentPanes.Count > rightStart + 3 ? AgentPanes[rightStart + 3] : null;
        }
    }

    private void NotifyPanePropertiesChanged()
    {
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
    }

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

    /// <summary>足軽の総人数。ペイン配置の計算に使用。</summary>
    [ObservableProperty]
    private int _totalAshigaru = 8;

    /// <summary>家老のコード改修権限モード（AlwaysAllow / AlwaysReject / PromptUser）。メイン画面から切り替え可能。</summary>
    [ObservableProperty]
    private string _karoExecutionPermissionMode = "PromptUser";

    /// <summary>家老権限モードの選択肢。XAML バインディング用。</summary>
    public IReadOnlyList<string> KaroPermissionModeOptions { get; } = new[] { "AlwaysAllow", "AlwaysReject", "PromptUser" };

    /// <summary>実行中のモデル情報（将軍）。</summary>
    [ObservableProperty]
    private string _modelInfoShogun = string.Empty;

    /// <summary>実行中のモデル情報（家老）。</summary>
    [ObservableProperty]
    private string _modelInfoKaro = string.Empty;

    /// <summary>実行中のモデル情報（足軽）。</summary>
    [ObservableProperty]
    private string _modelInfoAshigaru = string.Empty;

    /// <summary>起動準備中（Node.js / Claude Code の確認・インストール）か。</summary>
    [ObservableProperty]
    private bool _isLoading = true;

    /// <summary>起動準備のメッセージ（ユーザーに表示）。</summary>
    [ObservableProperty]
    private string _loadingMessage = "起動準備中...";

    public MainWindowViewModel(IProjectService? projectService = null, IAiService? aiService = null, IShogunQueueService? queueService = null, IAgentOrchestrator? orchestrator = null, ISettingsService? settingsService = null, IClaudeCodeSetupService? claudeCodeSetupService = null, IClaudeCodeRunService? claudeCodeRunService = null, IAgentWorkerService? agentWorkerService = null, IClaudeModelsService? claudeModelsService = null)
    {
        _projectService = projectService ?? new ProjectService();
        _settingsService = settingsService ?? new SettingsService();
        _claudeCodeSetupService = claudeCodeSetupService ?? new ClaudeCodeSetupService();
        _queueService = queueService ?? new ShogunQueueService(_settingsService, _projectService);
        var instructionsLoader = new InstructionsLoader(_queueService);
        var processHost = new ClaudeCodeProcessHost(_claudeCodeSetupService, _queueService);
        _claudeCodeRunService = claudeCodeRunService ?? new ClaudeCodeRunService(processHost, _claudeCodeSetupService, _queueService, instructionsLoader);
        _agentWorkerService = agentWorkerService ?? new AgentWorkerService(_claudeCodeRunService, _queueService, processHost);
        if (_agentWorkerService is AgentWorkerService aws)
            aws.SetSettingsService(_settingsService);
        _claudeModelsService = claudeModelsService ?? new ClaudeCodeModelsService();
        _aiService = aiService ?? new AiService();
        _orchestrator = orchestrator ?? new AgentOrchestrator(_queueService, _aiService, instructionsLoader);
        
        // 設定から初期値を読み込む
        var s = _settingsService.Get();
        KaroExecutionPermissionMode = s.KaroExecutionPermissionMode;
        UpdateModelInfos(s);

        LoadProjects();
        InitializeDummyData();
        RefreshDashboard();
    }

    partial void OnKaroExecutionPermissionModeChanged(string value)
    {
        var s = _settingsService.Get();
        if (s.KaroExecutionPermissionMode != value)
        {
            s.KaroExecutionPermissionMode = value;
            _settingsService.Save(s);
            Logger.Log($"家老の権限モードをメイン画面から変更し、即座に保存しました: {value}", LogLevel.Info);
        }
    }

    /// <summary>アプリ終了時に呼ぶ。常駐プロセス・ワーカーを終了する。</summary>
    public void OnAppShutdown()
    {
        _agentWorkerService.StopAll();
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

            LoadingMessage = "ログイン状態を確認しています...";
            var isLoggedIn = await _claudeCodeSetupService.IsLoggedInAsync().ConfigureAwait(true);
            if (!isLoggedIn)
            {
                Logger.Log("Claude Code にログインしていません。ブラウザを起動してログインを促します。", LogLevel.Info);
                LoadingMessage = "ログインのためブラウザを起動します。認証を完了してください...";
                var loginStarted = await _claudeCodeSetupService.RunLoginAsync(progress).ConfigureAwait(true);
                if (!loginStarted)
                {
                    LoadingMessage = "ログインプロセスの起動に失敗しました。設定画面から手動でログインしてください。";
                    return;
                }
                
                // ログイン完了を待機（ポーリング）
                var retryCount = 0;
                while (retryCount < 60) // 最大10分程度待機
                {
                    await Task.Delay(10000).ConfigureAwait(true); // 10秒おきに確認
                    if (await _claudeCodeSetupService.IsLoggedInAsync().ConfigureAwait(true))
                    {
                        isLoggedIn = true;
                        break;
                    }
                    retryCount++;
                    LoadingMessage = $"ログイン待機中 ({retryCount * 10}秒経過)... ブラウザで承認してください";
                }

                if (!isLoggedIn)
                {
                    LoadingMessage = "ログインが確認できませんでした。アプリを再起動するか、設定画面から再度試してください。";
                    return;
                }
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
            LoadingMessage = "エージェントワーカーを起動しています...";
            await _agentWorkerService.StartAllAsync(
                onProcessReady: async (roleLabel, message) =>
                {
                    // UI スレッドに投げて各ペインを更新
                    Dispatcher.UIThread.Post(() =>
                    {
                        AgentPaneViewModel? pane = null;
                        if (roleLabel == "将軍")
                            pane = LeftPane0;
                        else if (roleLabel == "家老")
                            pane = LeftPane1;
                        else if (roleLabel.StartsWith("足軽", StringComparison.Ordinal))
                        {
                            if (int.TryParse(roleLabel.Substring(2), out var idx))
                            {
                                var leftCount = (TotalAshigaru + 1) / 2;
                                if (idx <= leftCount)
                                {
                                    pane = (idx - 1) switch
                                    {
                                        0 => CenterPane0,
                                        1 => CenterPane1,
                                        2 => CenterPane2,
                                        3 => CenterPane3,
                                        _ => null
                                    };
                                }
                                else
                                {
                                    pane = (idx - leftCount - 1) switch
                                    {
                                        0 => RightPane0,
                                        1 => RightPane1,
                                        2 => RightPane2,
                                        3 => RightPane3,
                                        _ => null
                                    };
                                }
                            }
                        }
                        if (pane != null)
                            pane.Blocks.Add(new PaneBlock { Content = message, Timestamp = DateTime.Now });
                    });
                    await Task.CompletedTask.ConfigureAwait(false);
                }
            ).ConfigureAwait(true);
            if (modelsObtained)
                LoadingMessage = "準備完了";
        }
        catch (Exception ex)
        {
            Logger.LogException("起動時環境初期化で例外が発生しました。", ex);
            LoadingMessage = $"準備エラー: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            Logger.Log("起動時環境初期化が終了し、画面が表示されます。", LogLevel.Info);
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
        var newSettings = new AppSettings
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
        };
        _settingsService.Save(newSettings);
        UpdateModelInfos(newSettings);
        return true;
    }

    /// <summary>dashboard.md を読み込み表示を更新する。</summary>
    [RelayCommand]
    private void RefreshDashboard()
    {
        DashboardContent = _queueService.ReadDashboardMd();
        if (string.IsNullOrEmpty(DashboardContent))
        {
            var repoRoot = _queueService.GetRepoRoot();
            DashboardContent = $"（dashboard.md がありません。設定でワークスペースルートを指定してください。現在のルート: {repoRoot}）";
        }
        RefreshAgentPanesFromQueue();
    }

    /// <summary>queue/tasks と queue/reports から各ペインの表示を更新する。ブロックが空の場合のみ追加。</summary>
    private void RefreshAgentPanesFromQueue()
    {
        if (AgentPanes.Count < 2)
            return;
        
        // 家老のペイン：既に「指示待ち」以外のブロックがあれば更新しない
        if (AgentPanes[1].Blocks.Count <= 1)
        {
            var commands = _queueService.ReadShogunToKaro().Take(3).ToList();
            if (commands.Any())
            {
                var karoContent = "queue/shogun_to_karo.yaml の最新: " + string.Join("; ", commands.Select(c => c.Id + " " + c.Command));
                AgentPanes[1].Blocks.Add(new PaneBlock { Content = karoContent, Timestamp = DateTime.Now });
            }
        }
        
        var ashigaruCount = _queueService.GetAshigaruCount();
        var leftCount = (ashigaruCount + 1) / 2;
        for (var i = 1; i <= ashigaruCount; i++)
        {
            AgentPaneViewModel? pane = null;
            if (i <= leftCount)
            {
                pane = (i - 1) switch
                {
                    0 => CenterPane0,
                    1 => CenterPane1,
                    2 => CenterPane2,
                    3 => CenterPane3,
                    _ => null
                };
            }
            else
            {
                pane = (i - leftCount - 1) switch
                {
                    0 => RightPane0,
                    1 => RightPane1,
                    2 => RightPane2,
                    3 => RightPane3,
                    _ => null
                };
            }

            if (pane == null) continue;

            // 足軽ペイン：既に「指示待ち」以外のブロックがあればスキップ
            if (pane.Blocks.Count > 1)
                continue;
                
            var task = _queueService.ReadTaskYaml(i);
            var report = _queueService.ReadReportYaml(i);
            
            if (!string.IsNullOrEmpty(task))
                pane.Blocks.Add(new PaneBlock { Content = "任務: " + task.Trim().Replace("\r\n", " ").Replace("\n", " "), Timestamp = DateTime.Now });
            if (!string.IsNullOrEmpty(report))
                pane.Blocks.Add(new PaneBlock { Content = "報告: " + report.Trim().Replace("\r\n", " ").Replace("\n", " "), Timestamp = DateTime.Now });
        }
        NotifyPanePropertiesChanged();
    }

    /// <summary>設定からモデル表示情報を更新する。</summary>
    private void UpdateModelInfos(AppSettings s)
    {
        ModelInfoShogun = GetModelDisplayName(s.ModelShogun, s.ThinkingShogun);
        ModelInfoKaro = GetModelDisplayName(s.ModelKaro, s.ThinkingKaro);
        ModelInfoAshigaru = GetModelDisplayName(s.ModelAshigaru, s.ThinkingAshigaru);
        UpdateAgentPaneModelInfos();
    }

    private string GetModelDisplayName(string id, bool thinking)
    {
        if (string.IsNullOrWhiteSpace(id)) return "未設定";
        var name = LastFetchedModels?.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)).Name;
        if (string.IsNullOrEmpty(name))
        {
            name = id.Split('/').Last();
        }
        return thinking ? $"{name} (Thinking)" : name;
    }

    /// <summary>AIサービスを再初期化する（設定変更後）。</summary>
    public void RefreshAiService()
    {
        _aiService = new AiService();
        UpdateModelInfos(_settingsService.Get());
    }

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
        var ashigaruCount = _queueService.GetAshigaruCount();
        TotalAshigaru = ashigaruCount;

        var paneNames = new List<string> { "将軍", "家老" };
        for (var i = 1; i <= ashigaruCount; i++)
            paneNames.Add($"足軽{i}");

        AgentPanes.Clear();
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
            AgentPanes.Add(pane);
        }
        UpdateAgentPaneModelInfos();

        NotifyPanePropertiesChanged();

        var welcomeMessage = new ChatMessage
        {
            Sender = "system",
            Content = "Shogun.Avalonia にようこそ",
            ProjectId = ""
        };
        ChatMessages.Add(welcomeMessage);

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
            Logger.Log($"SendMessageAsync 開始: Input='{inputCopy}', ProjectId='{projectId}'", LogLevel.Info);
            
            Logger.Log("Claude Code CLI 連携モードで実行を開始します。", LogLevel.Info);

            string resultMessage;
            if (_claudeCodeSetupService.IsClaudeCodeInstalled())
            {
                Logger.Log("Claude Code CLI がインストールされています。ジョブを投入します。", LogLevel.Debug);
                var shogunProgress = new Progress<string>(msg =>
                {
                    Logger.Log($"[将軍] {msg}", LogLevel.Info);
                    Dispatcher.UIThread.Post(() =>
                    {
                        var m = new ChatMessage { Sender = "system", Content = msg, ProjectId = projectId, Timestamp = DateTime.Now };
                        ChatMessages.Add(m);
                        if (AgentPanes.Count > 0)
                            AgentPanes[0].Blocks.Add(new PaneBlock { Content = msg, Timestamp = DateTime.Now });
                    });
                });
                var karoProgress = new Progress<string>(msg =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (AgentPanes.Count > 1)
                            AgentPanes[1].Blocks.Add(new PaneBlock { Content = msg, Timestamp = DateTime.Now });
                    });
                });
                var reportProgress = new Progress<string>(msg =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (AgentPanes.Count > 1)
                            AgentPanes[1].Blocks.Add(new PaneBlock { Content = "[集約] " + msg, Timestamp = DateTime.Now });
                    });
                });
                IProgress<string> AshigaruProgressFor(int n)
                {
                    return new Progress<string>(msg =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            var leftCount = (TotalAshigaru + 1) / 2;
                            AgentPaneViewModel? pane = null;
                            if (n <= leftCount)
                            {
                                pane = (n - 1) switch
                                {
                                    0 => CenterPane0,
                                    1 => CenterPane1,
                                    2 => CenterPane2,
                                    3 => CenterPane3,
                                    _ => null
                                };
                            }
                            else
                            {
                                pane = (n - leftCount - 1) switch
                                {
                                    0 => RightPane0,
                                    1 => RightPane1,
                                    2 => RightPane2,
                                    3 => RightPane3,
                                    _ => null
                                };
                            }
                            if (pane != null)
                                pane.Blocks.Add(new PaneBlock { Content = msg, Timestamp = DateTime.Now });
                        });
                    });
                }
                resultMessage = await _agentWorkerService.SubmitMessageAsync(
                    inputCopy,
                    string.IsNullOrEmpty(projectId) ? null : projectId,
                    shogunProgress,
                    karoProgress,
                    reportProgress,
                    AshigaruProgressFor,
                    default).ConfigureAwait(true);
            }
            else
            {
                resultMessage = "Claude Code CLI が未インストールのため実行できません。設定でインストールしてください。";
                Logger.Log("Claude Code CLI が未インストールです。", LogLevel.Warning);
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

    /// <summary>ブロックID から PaneBlock を検索する。</summary>
    public PaneBlock? GetPaneBlockById(string blockId)
    {
        foreach (var pane in AgentPanes)
        {
            foreach (var block in pane.Blocks)
            {
                if (block.BlockId == blockId)
                    return block;
            }
        }
        return null;
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
