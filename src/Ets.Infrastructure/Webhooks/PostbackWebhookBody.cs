// src/Ets.Application/UseCases/Webhooks/PostbackWebhookBody.cs
using System.Text.Json.Serialization;

namespace Ets.Application.UseCases.Webhooks;

/// <summary>
/// team+ Postback Webhook Request Body（§7.1）
/// </summary>
public sealed class PostbackWebhookBody
{
    /// <summary>服務頻道代碼（Channel ID），對應 event_type</summary>
    [JsonPropertyName("destination")]
    public string Destination { get; init; } = string.Empty;

    /// <summary>事件陣列（一次可能多筆）</summary>
    [JsonPropertyName("events")]
    public List<WebhookEvent> Events { get; init; } = [];
}

public sealed class WebhookEvent
{
    /// <summary>事件類型：postback / message / modal</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>觸發時間（毫秒 Unix Timestamp）</summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    [JsonPropertyName("source")]
    public WebhookEventSource Source { get; init; } = new();

    [JsonPropertyName("postback")]
    public WebhookPostback? Postback { get; init; }
}

public sealed class WebhookEventSource
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    /// <summary>點擊按鈕的使用者帳號</summary>
    [JsonPropertyName("userId")]
    public string UserId { get; init; } = string.Empty;
}

public sealed class WebhookPostback
{
    /// <summary>query string 格式：id={eventId}&feedback={buttonText}</summary>
    [JsonPropertyName("data")]
    public string Data { get; init; } = string.Empty;
}
