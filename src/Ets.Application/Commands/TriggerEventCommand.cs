using Ets.Application.Dtos;
using MediatR;

namespace Ets.Application.Commands;

/// <summary>
/// HIS 事件觸發 MediatR Command。
/// 攜帶驗證後的 DTO，由 Application 層 Handler 執行業務邏輯。
/// </summary>
public sealed record TriggerEventCommand(HisEventTriggerDto Dto) : IRequest<TriggerEventResult>;

/// <summary>
/// 事件觸發結果。
/// </summary>
public sealed record TriggerEventResult
{
    /// <summary>是否成功</summary>
    public bool Success { get; init; }

    /// <summary>接受的事件 ID</summary>
    public string? EventId { get; init; }

    /// <summary>接受時間（UTC）</summary>
    public DateTimeOffset? AcceptedAt { get; init; }

    /// <summary>錯誤碼（失敗時）</summary>
    public string? ErrorCode { get; init; }

    /// <summary>錯誤訊息（失敗時）</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>HTTP 狀態碼建議（失敗時）</summary>
    public int? SuggestedHttpStatus { get; init; }

    // ── 靜態工廠方法 ──────────────────────────────────────────────────────

    public static TriggerEventResult Ok(string eventId) => new()
    {
        Success    = true,
        EventId    = eventId,
        AcceptedAt = DateTimeOffset.UtcNow
    };

    public static TriggerEventResult Conflict(string eventId) => new()
    {
        Success             = false,
        ErrorCode           = "E3006",
        ErrorMessage        = $"event_ID '{eventId}' 已存在，請勿重複觸發相同事件",
        SuggestedHttpStatus = 409
    };

    public static TriggerEventResult AreaNotAllowed(string? area) => new()
    {
        Success             = false,
        ErrorCode           = "E3011",
        ErrorMessage        = $"event_area '{area}' 不在白名單內",
        SuggestedHttpStatus = 400
    };

    public static TriggerEventResult ApiKeyMismatch(string eventType) => new()
    {
        Success             = false,
        ErrorCode           = "E3010",
        ErrorMessage        = $"X-ETS-API-Key 與 event_type='{eventType}' 對應的 ChannelSecret 不符",
        SuggestedHttpStatus = 401
    };

    public static TriggerEventResult InvalidEventType(string eventType) => new()
    {
        Success             = false,
        ErrorCode           = "E3003",
        ErrorMessage        = $"未知的 event_type: '{eventType}'",
        SuggestedHttpStatus = 400
    };
}
