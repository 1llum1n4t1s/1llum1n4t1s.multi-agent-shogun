using System;
using System.IO;
using System.Linq;
using System.Reflection;
using log4net;
using log4net.Config;
using log4net.Appender;
using log4net.Repository.Hierarchy;

namespace Shogun.Avalonia.Util;

/// <summary>ログレベルを表す列挙型。</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

/// <summary>log4net を使用したログ出力を提供するクラス。</summary>
public static class Logger
{
    private static readonly ILog _log = LogManager.GetLogger(typeof(Logger));
    private static bool _isConfigured;

    /// <summary>最小ログレベル（これ以上のレベルのログのみ出力）。</summary>
    private static readonly LogLevel MinLogLevel =
#if DEBUG
        LogLevel.Debug;
#else
        LogLevel.Info;
#endif

    /// <summary>ログ出力先ディレクトリ（exe と同じ BaseDirectory）。</summary>
    private static string LogDirectory => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>log4net を初期化する。</summary>
    public static void Initialize()
    {
        if (_isConfigured)
            return;

        var logFilePath = Path.Combine(LogDirectory, "Shogun.Avalonia.log");

        var entryAssembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var logRepository = LogManager.GetRepository(entryAssembly);
        var configFile = new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log4net.config"));

        if (configFile.Exists)
        {
            XmlConfigurator.Configure(logRepository, configFile);
            var hierarchy = (Hierarchy)logRepository;
            var appenders = hierarchy.Root.Appenders.OfType<FileAppender>();
            foreach (var appender in appenders)
            {
                appender.File = logFilePath;
                appender.ActivateOptions();
            }
        }
        else
        {
            BasicConfigurator.Configure(logRepository);
        }

        _isConfigured = true;
    }

    /// <summary>ログを出力する。</summary>
    /// <param name="message">ログメッセージ。</param>
    /// <param name="level">ログレベル（既定: Info）。</param>
    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (level < MinLogLevel)
            return;
        Initialize();
        switch (level)
        {
            case LogLevel.Debug:
                _log.Debug(message);
                break;
            case LogLevel.Info:
                _log.Info(message);
                break;
            case LogLevel.Warning:
                _log.Warn(message);
                break;
            case LogLevel.Error:
                _log.Error(message);
                break;
        }
    }

    /// <summary>例外情報を含むログを出力する（常に Error レベル）。</summary>
    /// <param name="message">ログメッセージ。</param>
    /// <param name="exception">例外オブジェクト。</param>
    public static void LogException(string message, Exception exception)
    {
        Initialize();
        _log.Error(message, exception);
    }

    /// <summary>アプリケーション起動時のログを出力する（Debug レベル）。</summary>
    /// <param name="args">コマンドライン引数。</param>
    public static void LogStartup(string[] args)
    {
        Initialize();
        _log.Debug("=== Shogun.Avalonia 起動ログ ===");
        _log.Debug($"起動時刻: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        _log.Debug($"実行ファイル: {Environment.ProcessPath}");
        _log.Debug($"コマンドライン引数: {args.Length}");
        for (var i = 0; i < args.Length; i++)
            _log.Debug($"  [{i}]: {args[i]}");
    }
}
