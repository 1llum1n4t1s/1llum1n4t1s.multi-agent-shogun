using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shogun.Avalonia.Models;
using Shogun.Avalonia.Services;
using Shogun.Avalonia.Util;

namespace Shogun.Avalonia.ViewModels;

/// <summary>
/// 設定画面の ViewModel。
/// モデル一覧は models.dev API から取得する。API キーは扱わない。
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IClaudeModelsService _claudeModelsService;
    private readonly IClaudeCodeSetupService _claudeCodeSetupService;
    private readonly Action? _onClose;

    /// <summary>ログレベルの選択肢（logging.level）。XAML バインディング用。</summary>
    public IReadOnlyList<string> LogLevelOptions { get; } = new[] { "debug", "info", "warn", "error" };

    /// <summary>足軽人数の選択肢（1～20）。XAML バインディング用。</summary>
    public IReadOnlyList<int> AshigaruCountOptions { get; } = Enumerable.Range(1, 20).ToList();

    /// <summary>家老権限モードの選択肢。XAML バインディング用。</summary>
    public IReadOnlyList<string> KaroPermissionModeOptions { get; } = new[] { "AlwaysAllow", "AlwaysReject", "PromptUser" };

    /// <summary>モデル選択肢の全件（models.dev から取得。取得前は空）。</summary>
    public ObservableCollection<ModelOption> AllModelOptions { get; } = new();

    /// <summary>モデル一覧の読み込み中か。</summary>
    [ObservableProperty]
    private bool _isLoadingModels = false;

    /// <summary>将軍用モデルの選択肢（ドロップダウンで選択した MODEL）。</summary>
    [ObservableProperty]
    private ModelOption? _selectedModelShogun;

    /// <summary>家老用モデルの選択肢。</summary>
    [ObservableProperty]
    private ModelOption? _selectedModelKaro;

    /// <summary>足軽用モデルの選択肢。</summary>
    [ObservableProperty]
    private ModelOption? _selectedModelAshigaru;

    [ObservableProperty]
    private int _ashigaruCount = 8;

    [ObservableProperty]
    private string _skillLocalPath = string.Empty;

    [ObservableProperty]
    private string _screenshotPath = string.Empty;

    [ObservableProperty]
    private string _logLevel = "info";

    [ObservableProperty]
    private string _logPath = string.Empty;

    [ObservableProperty]
    private string _modelShogun = string.Empty;

    [ObservableProperty]
    private string _modelKaro = string.Empty;

    [ObservableProperty]
    private string _modelAshigaru = string.Empty;

    /// <summary>将軍用モデルで Thinking を使うか。</summary>
    [ObservableProperty]
    private bool _thinkingShogun;

    /// <summary>家老用モデルで Thinking を使うか。</summary>
    [ObservableProperty]
    private bool _thinkingKaro;

    /// <summary>足軽用モデルで Thinking を使うか。</summary>
    [ObservableProperty]
    private bool _thinkingAshigaru;

    /// <summary>家老のコード改修権限モード（AlwaysAllow / AlwaysReject / PromptUser）。</summary>
    [ObservableProperty]
    private string _karoExecutionPermissionMode = "PromptUser";

    [ObservableProperty]
    private string _documentRoot = string.Empty;

    /// <summary>Claude Code CLI のパーミッションをスキップするか（--dangerously-skip-permissions）。自己責任。</summary>
    [ObservableProperty]
    private bool _dangerouslySkipPermissions;

    /// <summary>ViewModel を生成する。</summary>
    /// <param name="settingsService">設定サービス。</param>
    /// <param name="claudeModelsService">models.dev でモデル一覧を取得するサービス。null のときは新規作成。</param>
    /// <param name="onClose">ウィンドウを閉じる際のコールバック。</param>
    public SettingsViewModel(ISettingsService settingsService, IClaudeModelsService? claudeModelsService = null, Action? onClose = null, IClaudeCodeSetupService? claudeCodeSetupService = null)
    {
        _settingsService = settingsService;
        _claudeModelsService = claudeModelsService ?? new ClaudeCodeModelsService();
        _claudeCodeSetupService = claudeCodeSetupService ?? new ClaudeCodeSetupService();
        _onClose = onClose;
        LoadFromService();
    }

    /// <summary>設定サービスから現在の設定を読み込む。パスは環境変数を展開して表示する。モデルは保存値のまま（空のときは空）。</summary>
    private void LoadFromService()
    {
        var s = _settingsService.Get();
        AshigaruCount = s.AshigaruCount;
        SkillLocalPath = ExpandPath(s.SkillLocalPath);
        ScreenshotPath = ExpandPath(s.ScreenshotPath);
        LogLevel = s.LogLevel;
        LogPath = ExpandPath(s.LogPath);
        ModelShogun = NormalizeModelId(s.ModelShogun);
        ModelKaro = NormalizeModelId(s.ModelKaro);
        ModelAshigaru = NormalizeModelId(s.ModelAshigaru);
        ThinkingShogun = s.ThinkingShogun;
        ThinkingKaro = s.ThinkingKaro;
        ThinkingAshigaru = s.ThinkingAshigaru;
        KaroExecutionPermissionMode = s.KaroExecutionPermissionMode;
        DocumentRoot = ExpandPath(s.DocumentRoot);
        DangerouslySkipPermissions = s.DangerouslySkipPermissions;
    }

    /// <summary>保存済み ID（ModelShogun/Karo/Ashigaru）から SelectedModel* を同期する。AllModelOptions 設定後に呼ぶ。</summary>
    private void SyncSelectedFromIds()
    {
        SyncOne(ModelShogun, o => SelectedModelShogun = o);
        SyncOne(ModelKaro, o => SelectedModelKaro = o);
        SyncOne(ModelAshigaru, o => SelectedModelAshigaru = o);
    }

    /// <summary>ID に対応する ModelOption を AllModelOptions から探し setSelected で設定する。</summary>
    private void SyncOne(string id, Action<ModelOption?> setSelected)
    {
        if (ClaudeCodeModelsService.IsInvalidModelId(id))
        {
            setSelected(null);
            return;
        }
        var opt = AllModelOptions.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        setSelected(opt);
    }

    /// <summary>「claude-code」は無効なため空として扱う。</summary>
    private static string NormalizeModelId(string? id) =>
        ClaudeCodeModelsService.IsInvalidModelId(id) ? string.Empty : (id ?? string.Empty);

    /// <summary>ModelShogun/Karo/Ashigaru が空のとき、将軍・家老＝Sonnet 最大、足軽＝Haiku 最大で補う。設定画面表示時の未選択解消用。</summary>
    private void ApplyDefaultModelsIfEmpty()
    {
        if (AllModelOptions.Count == 0) return;
        var ids = AllModelOptions.Select(x => x.Id).Where(id => !ClaudeCodeModelsService.IsInvalidModelId(id)).ToList();
        if (ids.Count == 0) return;
        if (string.IsNullOrWhiteSpace(ModelShogun))
            ModelShogun = ModelFamilyHelper.GetLatestIdInFamilyBySort(ids, "sonnet") ?? ids.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ModelKaro))
            ModelKaro = ModelFamilyHelper.GetLatestIdInFamilyBySort(ids, "sonnet") ?? ids.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ModelAshigaru))
            ModelAshigaru = ModelFamilyHelper.GetLatestIdInFamilyBySort(ids, "haiku") ?? ids.FirstOrDefault() ?? string.Empty;
    }

    /// <summary>AllModelOptions を MODEL（DisplayName）昇順でソートする。
    /// Clear() による SelectedItem null 化を防ぐため、呼び出し元で ID 退避・復元を行うこと。</summary>
    private void SortAllModelOptionsByDisplayName()
    {
        var sorted = AllModelOptions.OrderBy(x => x.DisplayName, StringComparer.Ordinal).ToList();
        // 呼び出し元で ModelShogun/Karo/Ashigaru の退避・復元を行っているため、
        // ここでの Clear() による SelectedModel* null 化は問題ない。
        AllModelOptions.Clear();
        foreach (var o in sorted)
            AllModelOptions.Add(o);
    }

    partial void OnSelectedModelShogunChanged(ModelOption? value) => ModelShogun = value?.Id ?? string.Empty;
    partial void OnSelectedModelKaroChanged(ModelOption? value) => ModelKaro = value?.Id ?? string.Empty;
    partial void OnSelectedModelAshigaruChanged(ModelOption? value) => ModelAshigaru = value?.Id ?? string.Empty;

    private static string ExpandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        return Environment.ExpandEnvironmentVariables(path);
    }

    /// <summary>保存してウィンドウを閉じる。</summary>
    [RelayCommand]
    public void SaveAndClose()
    {
        var current = _settingsService.Get();
        var count = AshigaruCount;
        if (count < 1) count = 8;
        if (count > 20) count = 20;
        var modelShogun = ResolveModelFromList(ModelShogun);
        var modelKaro = ResolveModelFromList(ModelKaro);
        var modelAshigaru = ResolveModelFromList(ModelAshigaru);
        _settingsService.Save(new AppSettings
        {
            AshigaruCount = count,
            SkillSavePath = current.SkillSavePath,
            SkillLocalPath = SkillLocalPath,
            ScreenshotPath = ScreenshotPath,
            LogLevel = LogLevel,
            LogPath = LogPath,
            ModelName = current.ModelName,
            ModelShogun = modelShogun,
            ModelKaro = modelKaro,
            ModelAshigaru = modelAshigaru,
            ThinkingShogun = ThinkingShogun,
            ThinkingKaro = ThinkingKaro,
            ThinkingAshigaru = ThinkingAshigaru,
            ApiEndpoint = current.ApiEndpoint,
            RepoRoot = current.RepoRoot,
            KaroExecutionPermissionMode = KaroExecutionPermissionMode,
            DocumentRoot = DocumentRoot,
            DangerouslySkipPermissions = DangerouslySkipPermissions
        });
        _onClose?.Invoke();
    }

    /// <summary>モデルが空のとき、一覧の先頭（haiku があれば haiku）で解決する。「claude-code」は無効のため空を返す。</summary>
    private string ResolveModelFromList(string model)
    {
        if (ClaudeCodeModelsService.IsInvalidModelId(model))
            return string.Empty;
        
        return model ?? string.Empty;
    }

    /// <summary>保存せずにウィンドウを閉じる。</summary>
    [RelayCommand]
    private void Cancel()
    {
        _onClose?.Invoke();
    }

    /// <summary>渡されたモデル一覧（ID と表示名）で AllModelOptions を初期表示する。設定画面を開くときに起動時取得分を渡す。未選択のときは将軍・家老＝Sonnet 最大、足軽＝Haiku 最大で補う。</summary>
    /// <param name="models">表示するモデル一覧。null または空のときは何もしない。</param>
    public void SetInitialModels(IReadOnlyList<(string Id, string Name)>? models)
    {
        if (models == null || models.Count == 0) return;

        // Clear() で SelectedModel* → null → ModelShogun/Karo/Ashigaru が空になる問題を回避
        var savedShogun = ModelShogun;
        var savedKaro = ModelKaro;
        var savedAshigaru = ModelAshigaru;

        AllModelOptions.Clear();
        foreach (var m in models)
            AllModelOptions.Add(new ModelOption(m.Id, m.Name));
        SortAllModelOptionsByDisplayName();

        ModelShogun = savedShogun;
        ModelKaro = savedKaro;
        ModelAshigaru = savedAshigaru;
        SyncSelectedFromIds();
    }

    /// <summary>models.dev API でモデル一覧を取得し AllModelOptions を更新する。コレクション更新は UI スレッドで行う。</summary>
    public async Task LoadModelOptionsAsync()
    {
        if (IsLoadingModels)
            return;
        IsLoadingModels = true;
        try
        {
            var models = await _claudeModelsService.GetModelsAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Clear() すると SelectedModel* が null に飛び、OnSelectedModel*Changed 経由で
                // ModelShogun/Karo/Ashigaru が空文字に上書きされてしまう。
                // 事前に保存済み ID を退避し、Clear→再構築後に復元する。
                var savedShogun = ModelShogun;
                var savedKaro = ModelKaro;
                var savedAshigaru = ModelAshigaru;

                AllModelOptions.Clear();
                if (models.Count > 0)
                {
                    foreach (var m in models)
                        AllModelOptions.Add(new ModelOption(m.Id, m.Name));
                    SortAllModelOptionsByDisplayName();
                }

                // 退避した ID を復元してから同期する
                ModelShogun = savedShogun;
                ModelKaro = savedKaro;
                ModelAshigaru = savedAshigaru;
                SyncSelectedFromIds();
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoadingModels = false);
        }
    }

    /// <summary>一覧取得後に未設定のモデルを、一覧の先頭（haiku があれば haiku）で埋める。</summary>
    public void ResolveEmptyModelsFromList()
    {
        if (AllModelOptions.Count == 0)
            return;
        var first = FirstHaikuOrFirst(AllModelOptions);
        if (first == null) return;
        if (string.IsNullOrWhiteSpace(ModelShogun)) { ModelShogun = first.Id; SelectedModelShogun = first; }
        if (string.IsNullOrWhiteSpace(ModelKaro)) { ModelKaro = first.Id; SelectedModelKaro = first; }
        if (string.IsNullOrWhiteSpace(ModelAshigaru)) { ModelAshigaru = first.Id; SelectedModelAshigaru = first; }
    }

    /// <summary>保存済みモデルを同一ファミリ（Haiku/Sonnet/Opus）の最新に更新する。一覧は models.dev から取得した値のみ使用。</summary>
    public void UpgradeModelsToLatestInFamily()
    {
        if (AllModelOptions.Count == 0)
            return;
        var list = AllModelOptions.Select(x => x.Id).ToList();
        var newShogun = ModelFamilyHelper.UpgradeToLatestInFamily(ModelShogun, list);
        var newKaro = ModelFamilyHelper.UpgradeToLatestInFamily(ModelKaro, list);
        var newAshigaru = ModelFamilyHelper.UpgradeToLatestInFamily(ModelAshigaru, list);
        
        if (newShogun != ModelShogun || newKaro != ModelKaro || newAshigaru != ModelAshigaru)
        {
            ModelShogun = newShogun;
            ModelKaro = newKaro;
            ModelAshigaru = newAshigaru;
            SyncSelectedFromIds();
        }
    }

    /// <summary>同一ファミリ最新へ更新したモデル設定を永続化する（設定画面を開いたときに自動保存）。</summary>
    public void PersistUpgradedModels()
    {
        var current = _settingsService.Get();
        _settingsService.Save(new AppSettings
        {
            AshigaruCount = current.AshigaruCount,
            SkillSavePath = current.SkillSavePath,
            SkillLocalPath = current.SkillLocalPath,
            ScreenshotPath = current.ScreenshotPath,
            LogLevel = current.LogLevel,
            LogPath = current.LogPath,
            ModelName = current.ModelName,
            ModelShogun = ModelShogun,
            ModelKaro = ModelKaro,
            ModelAshigaru = ModelAshigaru,
            ThinkingShogun = ThinkingShogun,
            ThinkingKaro = ThinkingKaro,
            ThinkingAshigaru = ThinkingAshigaru,
            ApiEndpoint = current.ApiEndpoint,
            RepoRoot = current.RepoRoot,
            KaroExecutionPermissionMode = current.KaroExecutionPermissionMode,
            DocumentRoot = current.DocumentRoot,
            DangerouslySkipPermissions = current.DangerouslySkipPermissions
        });
    }

    private static ModelOption? FirstHaikuOrFirst(IEnumerable<ModelOption> options)
    {
        var valid = options.Where(o => !ClaudeCodeModelsService.IsInvalidModelId(o.Id));
        var haiku = valid.FirstOrDefault(o => o.Id.Contains("haiku", StringComparison.OrdinalIgnoreCase));
        return haiku ?? valid.FirstOrDefault();
    }
}
