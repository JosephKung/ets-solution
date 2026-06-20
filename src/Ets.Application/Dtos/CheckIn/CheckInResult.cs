namespace Ets.Application.Dtos.CheckIn;

/// <summary>
/// 報到結果 DTO。
/// </summary>
public class CheckInResult
{
    /// <summary>報到成功時間（UTC）</summary>
    public DateTime CheckedInAt { get; set; }

    /// <summary>是否於此次報到時觸發補位（加入團隊）</summary>
    public bool AddedToTeam { get; set; }

    /// <summary>是否於此次報到時觸發補位（加入交談室）</summary>
    public bool AddedToChatRoom { get; set; }

    /// <summary>已加入之交談室名稱（補位時顯示）</summary>
    public string? ChatRoomName { get; set; }
}
