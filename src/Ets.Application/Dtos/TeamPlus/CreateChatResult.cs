// src/Ets.Application/Dtos/TeamPlus/CreateChatResult.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 建立分組交談室回應 DTO（§6.3）
/// </summary>
/// <param name="ChatSN">team+ 回傳之交談室編號，需回填 EventGroups.TeamPlusChatSN</param>
public record CreateChatResult(
    bool IsSuccess,
    string Description,
    int ErrorCode,
    long ChatSN,
    IReadOnlyList<string> IgnoredMemberList,
    IReadOnlyList<string> IgnoredManagerList)
    : TeamPlusBaseResult(IsSuccess, Description, ErrorCode);
