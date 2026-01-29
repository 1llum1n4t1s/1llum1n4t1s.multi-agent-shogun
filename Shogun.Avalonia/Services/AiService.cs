using System.Threading;
using System.Threading.Tasks;

namespace Shogun.Avalonia.Services;

/// <summary>
/// AI チャットサービス。当アプリでは API キーを使用しないため、常に利用不可。
/// 実際の AI 呼び出しは Claude Code CLI（upstream）側で行う。
/// </summary>
public class AiService : IAiService
{
    /// <summary>サービスが利用可能か。当アプリでは API を使わないため常に false。</summary>
    public bool IsAvailable => false;

    /// <inheritdoc />
    public Task<string> SendChatMessageAsync(string message, string? projectContext = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("エラー: 当アプリでは API 呼び出しは行いません。家老・足軽の実行は upstream の Claude Code CLI 等で行ってください。");
    }

    /// <inheritdoc />
    public Task<string> SendWithSystemAsync(string systemPrompt, string userMessage, string? modelOverride = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("エラー: 当アプリでは API 呼び出しは行いません。");
    }
}
