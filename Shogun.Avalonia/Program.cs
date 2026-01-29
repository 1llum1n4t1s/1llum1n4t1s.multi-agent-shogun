using System;
using Avalonia;
using Shogun.Avalonia.Util;
using Velopack;
using Velopack.Sources;

namespace Shogun.Avalonia;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Logger.Initialize();
        Logger.LogStartup(args);
        VelopackApp.Build().Run();
        CheckForUpdatesAndRestartIfNeeded();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>GitHub リリースに最新版があればダウンロード・適用して再起動する。なければ何もしない。</summary>
    private static void CheckForUpdatesAndRestartIfNeeded()
    {
        try
        {
            var repoUrl = AppConstants.VelopackGitHubRepoUrl;
            if (string.IsNullOrWhiteSpace(repoUrl))
                return;
            var source = new GithubSource(repoUrl, "", false, null);
            var mgr = new UpdateManager(source);
            if (!mgr.IsInstalled)
                return;
            var updateInfo = mgr.CheckForUpdatesAsync().GetAwaiter().GetResult();
            if (updateInfo == null)
                return;
            mgr.DownloadUpdatesAsync(updateInfo).GetAwaiter().GetResult();
            mgr.ApplyUpdatesAndRestart(updateInfo);
        }
        catch
        {
            // 更新チェック失敗時はそのまま起動
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
