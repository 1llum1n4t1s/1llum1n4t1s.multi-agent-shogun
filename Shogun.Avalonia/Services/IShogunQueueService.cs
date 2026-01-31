using System;
using System.Collections.Generic;
using Shogun.Avalonia.Models;

namespace Shogun.Avalonia.Services;

/// <summary>
/// フォーク元の queue / dashboard を読み書きするサービス（将軍→家老の指示・dashboard 読み取り）。
/// </summary>
public interface IShogunQueueService
{
    /// <summary>ワークスペースルート（queue/dashboard/instructions の親フォルダ）。空のときは config の親を使用。</summary>
    string GetRepoRoot();

    /// <summary>足軽の人数（1～20）。設定に従う。</summary>
    int GetAshigaruCount();

    /// <summary>queue/shogun_to_karo.yaml の queue を読み取る。</summary>
    IReadOnlyList<ShogunCommand> ReadShogunToKaro();

    /// <summary>新規指示をキューに追加して書き込み、cmd_xxx を返す。</summary>
    /// <param name="command">指示内容。</param>
    /// <param name="projectId">対象プロジェクト ID（任意）。</param>
    /// <param name="priority">優先度（任意）。</param>
    string AppendCommand(string command, string? projectId = null, string? priority = null);

    /// <summary>dashboard.md の内容を読み取る。</summary>
    string ReadDashboardMd();

    /// <summary>queue/tasks/ashigaru{N}.yaml の内容を読み取る。N は 1～GetAshigaruCount()。</summary>
    string ReadTaskYaml(int ashigaruIndex);

    /// <summary>queue/reports/ashigaru{N}_report.yaml の内容を読み取る。N は 1～GetAshigaruCount()。</summary>
    string ReadReportYaml(int ashigaruIndex);

    /// <summary>queue/tasks/ashigaru{N}.yaml を書き込む（フォーク元形式）。</summary>
    void WriteTaskYaml(int ashigaruIndex, string taskId, string parentCmd, string description, string? targetPath, string status, string timestamp);

    /// <summary>queue/reports/ashigaru{N}_report.yaml を書き込む（フォーク元形式）。skill_candidate は足軽報告の必須項目。</summary>
    void WriteReportYaml(int ashigaruIndex, string taskId, string timestamp, string status, string result, bool skillCandidateFound = false, string? skillCandidateName = null, string? skillCandidateDescription = null, string? skillCandidateReason = null);

    /// <summary>dashboard.md を上書きする。</summary>
    void WriteDashboardMd(string content);

    /// <summary>指定したコマンド ID の status を更新する。</summary>
    void UpdateCommandStatus(string commandId, string status);

    /// <summary>status/master_status.yaml を書き込む（フォーク元: 全体進捗。shutsujin_departure.sh と同形式）。</summary>
    /// <param name="updated">最終更新時刻。</param>
    /// <param name="currentTask">現在のコマンド ID（null のときは null 表記）。</param>
    /// <param name="taskStatus">idle / in_progress / done / failed。</param>
    /// <param name="taskDescription">指示内容（任意）。</param>
    /// <param name="assignments">完了時の割り当て一覧（進行中・完了時にどの足軽が使われたか）。</param>
    void WriteMasterStatus(DateTime updated, string? currentTask, string taskStatus, string? taskDescription, IReadOnlyList<TaskAssignmentItem>? assignments);

    /// <summary>現在の設定を取得する。</summary>
    AppSettings GetSettings();

    /// <summary>現在のプロジェクトに基づいたドキュメント出力先パスを取得する。</summary>
    /// <param name="projectId">プロジェクト ID（任意）。</param>
    string GetDocumentOutputPath(string? projectId = null);
}
