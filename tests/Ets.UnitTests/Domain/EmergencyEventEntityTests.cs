using Ets.Domain.Entities;
using Ets.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Ets.UnitTests.Domain;

/// <summary>
/// EmergencyEvent Entity 單元測試。
/// 驗證初始狀態、欄位預設值，以及 EventID 格式規則。
/// </summary>
public class EmergencyEventEntityTests
{
    [Fact(DisplayName = "新建 EmergencyEvent 預設狀態應為 Processing")]
    public void NewEmergencyEvent_DefaultStatus_ShouldBeProcessing()
    {
        // Arrange & Act
        var ev = new EmergencyEvent();

        // Assert
        ev.Status.Should().Be(EventStatus.Processing,
            because: "事件初始狀態應為 Processing，待 OutboxDispatcher 完成派送後才轉為其他狀態");
    }

    [Fact(DisplayName = "新建 EmergencyEvent 導航屬性集合不應為 null")]
    public void NewEmergencyEvent_NavigationCollections_ShouldNotBeNull()
    {
        // Arrange & Act
        var ev = new EmergencyEvent();

        // Assert
        ev.Groups.Should().NotBeNull(because: "Groups 集合應初始化為空集合，避免 NullReferenceException");
        ev.Responders.Should().NotBeNull(because: "Responders 集合應初始化為空集合");
        ev.OutboxMessages.Should().NotBeNull(because: "OutboxMessages 集合應初始化為空集合");
    }

    [Theory(DisplayName = "EventID 格式驗證：應符合 E+YYYYMMDDHHMMSS+eventType(1碼)+3碼流水號")]
    [InlineData("E20240101120000A001", true)]   // 合法：火災(A) 事件
    [InlineData("E20240101120000b099", true)]   // 合法：小寫 event_type 亦接受
    [InlineData("E20241231235959e999", true)]   // 合法：最大流水號
    [InlineData("E2024010112000A001",  false)]  // 不合法：時間戳少一碼
    [InlineData("20240101120000A001",  false)]  // 不合法：缺少 E 前綴
    [InlineData("",                    false)]  // 不合法：空字串
    public void EventId_Format_ShouldMatchSpecification(string eventId, bool isValid)
    {
        // 格式規則：E + 14 碼時間 + 1 碼 event_type + 3 碼流水號 = 19 碼
        // 使用簡單 regex 驗證（實際業務驗證在 FluentValidation，此處測試格式規則本身）
        var isMatch = System.Text.RegularExpressions.Regex.IsMatch(
            eventId,
            @"^E\d{14}[a-zA-Z]\d{3}$");

        isMatch.Should().Be(isValid,
            because: $"EventID '{eventId}' 的格式驗證結果應為 {isValid}");
    }

    [Fact(DisplayName = "EventResponder 初始 ReplyStatus 應為 Pending")]
    public void NewEventResponder_DefaultReplyStatus_ShouldBePending()
    {
        // Arrange & Act
        var responder = new EventResponder();

        // Assert
        responder.ReplyStatus.Should().Be("Pending",
            because: "SSOT 原則：尚未透過 Flex Message 點選回覆前，狀態為 Pending");
        responder.ReplyChannel.Should().Be("None",
            because: "尚未回覆時，回覆通道應為 None");
        responder.CheckInStatus.Should().BeFalse(
            because: "尚未掃碼報到前，CheckInStatus 應為 false");
        responder.IsAdHoc.Should().BeFalse(
            because: "預設為正式名單成員，非臨時支援");
    }

    [Fact(DisplayName = "OutboxMessage 初始狀態應為 Pending")]
    public void NewOutboxMessage_DefaultStatus_ShouldBePending()
    {
        // Arrange & Act
        var msg = new OutboxMessage();

        // Assert
        msg.Status.Should().Be(OutboxMessageStatus.Pending,
            because: "新建 OutboxMessage 尚未被 Worker 取用，狀態應為 Pending");
        msg.RetryCount.Should().Be(0,
            because: "新建訊息尚未重試");
        msg.ProcessedAt.Should().BeNull(
            because: "新建訊息尚未被處理完成");
    }
}
