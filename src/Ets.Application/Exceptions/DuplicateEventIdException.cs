namespace Ets.Application.Exceptions;

/// <summary>
/// 事件 ID 重複例外。
/// 由 Repository 實作層（Infrastructure）捕捉 DB 唯一鍵違反後拋出，
/// Application 層 Handler 捕捉此 domain exception 即可，不需依賴 EF Core。
/// </summary>
public sealed class DuplicateEventIdException : Exception
{
    public string EventId { get; }

    public DuplicateEventIdException(string eventId)
        : base($"event_ID '{eventId}' 已存在")
    {
        EventId = eventId;
    }
}
