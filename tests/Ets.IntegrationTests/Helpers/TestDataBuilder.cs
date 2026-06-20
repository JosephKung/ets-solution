// tests/Ets.IntegrationTests/Helpers/TestDataBuilder.cs
using Ets.Application.Dtos.TeamPlus;
using Ets.Domain.Entities;
using Ets.Domain.Enums;
using Ets.Infrastructure.Persistence;
using Ets.Application.Interfaces.External;
using NSubstitute;

namespace Ets.IntegrationTests.Helpers;

/// <summary>
/// 整合測試用資料建構輔助
/// </summary>
public static class TestDataBuilder
{
    /// <summary>標準事件 ID</summary>
    public const string DefaultEventId = "E20240101120000A001";

    /// <summary>在 DB 插入標準測試事件（含 Responders + Groups）</summary>
    public static async Task SeedStandardEventAsync(AppDbContext db)
    {
        db.EmergencyEvents.Add(new EmergencyEvent
        {
            EventId           = DefaultEventId,
            EventType         = "a",
            EventSummary      = "XX醫療大樓火警警報",
            EventDescription  = "台北市XX醫療大樓發生火警",
            EventTime         = new DateTime(2024, 1, 1, 12, 0, 0),
            FlexMsgItemsJson  = "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]",
            TeamPlusBigTeamSn = 99823104,
            Status            = EventStatus.Processing,
            CreatedAt         = DateTime.UtcNow,
            UpdatedAt         = DateTime.UtcNow
        });

        db.EventGroups.Add(new EventGroup
        {
            GroupId        = 1,
            EventId        = DefaultEventId,
            ChatGp         = "(A0021)消防組",
            TeamPlusChatSn = 88723611,
            CreatorAccount = "joseph",
            CreatedAt      = DateTime.UtcNow
        });

        db.EventResponders.AddRange(
            new EventResponder
            {
                EventId       = DefaultEventId,
                Account       = "joseph",
                Role          = "commander",
                ChatGp        = "(A0021)消防組",
                ReplyStatus   = "Pending",
                FlexMessageSn = 9453,
                JoinedTeam    = false,
                JoinedChatRoom = false,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            },
            new EventResponder
            {
                EventId       = DefaultEventId,
                Account       = "marry",
                Role          = "normal",
                ChatGp        = "(A0021)消防組",
                ReplyStatus   = "Pending",
                FlexMessageSn = 9453,
                JoinedTeam    = false,
                JoinedChatRoom = false,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            },
            new EventResponder
            {
                EventId       = DefaultEventId,
                Account       = "bob",
                Role          = "observer",
                ChatGp        = "(A0021)消防組",
                ReplyStatus   = "Pending",
                FlexMessageSn = 9453,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow
            });

        await db.SaveChangesAsync();
    }

    /// <summary>設定 MockSystemClient 標準回應</summary>
    public static void SetupMockSystemClientDefaults(
        ITeamPlusSystemClient mock)
    {
        mock.CreateTeamAsync(Arg.Any<CreateTeamRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateTeamResult(
                IsSuccess: true, Description: "Success", ErrorCode: 0,
                TeamSN: 99823104L,
                IgnoredMemberList: [],
                IgnoredManagerList: []));

        mock.CreateChatAsync(Arg.Any<CreateChatRequest>(), Arg.Any<CancellationToken>())
            .Returns(new CreateChatResult(
                IsSuccess: true, Description: "Success", ErrorCode: 0,
                ChatSN: 88723611L,
                IgnoredMemberList: [],
                IgnoredManagerList: []));

        mock.InviteTeamMemberAsync(Arg.Any<InviteTeamMemberRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));

        mock.InviteChatMemberAsync(Arg.Any<InviteChatMemberRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));

        mock.AssignTeamManagerAsync(Arg.Any<AssignManagerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));

        mock.AssignChatManagerAsync(Arg.Any<AssignManagerRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));
    }

    /// <summary>設定 MockChannelClient 標準回應</summary>
    public static void SetupMockChannelClientDefaults(
        ITeamPlusChannelClient mock)
    {
        mock.BroadcastFlexMessageAsync(Arg.Any<BroadcastFlexMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(new BroadcastFlexMessageResult(IsSuccess: true, MessageSN: 9453));

        mock.GetMsgReadStatusAsync(Arg.Any<GetMsgReadStatusRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetMsgReadStatusResult(ReadCount: 0, ReadDetailList: []));

        mock.UpdateFlexFooterAsync(Arg.Any<UpdateFlexFooterRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TeamPlusBaseResult(true, "Success", 0));
    }
}
