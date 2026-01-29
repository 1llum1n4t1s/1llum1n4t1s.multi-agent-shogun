namespace Shogun.Avalonia.Models;

/// <summary>ドロップダウン用のモデル選択肢（ID と表示名）。</summary>
/// <param name="Id">保存・API 用のモデル ID。</param>
/// <param name="DisplayName">ドロップダウンに表示する MODEL 名。</param>
public record ModelOption(string Id, string DisplayName);
