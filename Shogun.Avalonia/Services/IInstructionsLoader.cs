namespace Shogun.Avalonia.Services;

/// <summary>
/// フォーク元の instructions/*.md と memory/global_context.md をワークスペースルートから読み込むサービス。
/// </summary>
public interface IInstructionsLoader
{
    /// <summary>instructions/shogun.md の内容。見つからない場合は null または埋め込み用の最小テキスト。</summary>
    string? LoadShogunInstructions();

    /// <summary>instructions/karo.md の内容。見つからない場合は null または埋め込み用の最小テキスト。</summary>
    string? LoadKaroInstructions();

    /// <summary>instructions/ashigaru.md の内容。見つからない場合は null または埋め込み用の最小テキスト。</summary>
    string? LoadAshigaruInstructions();

    /// <summary>memory/global_context.md の内容。存在しない場合は null（フォーク元: システム全体の設定・殿の好み）。</summary>
    string? LoadGlobalContext();
}
