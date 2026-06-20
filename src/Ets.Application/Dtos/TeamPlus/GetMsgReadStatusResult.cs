// src/Ets.Application/Dtos/TeamPlus/GetMsgReadStatusResult.cs
namespace Ets.Application.Dtos.TeamPlus;

/// <summary>
/// 查詢訊息已讀狀態回應 DTO（§6.8）
/// </summary>
/// <param name="ReadCount">已讀人數（對應 Dashboard 「✓✓ 25/32」之分子）</param>
/// <param name="ReadDetailList">個別已讀明細（帳號 + 已讀時間）</param>
public record GetMsgReadStatusResult(
    int ReadCount,
    IReadOnlyList<ReadDetailItem> ReadDetailList);

/// <summary>個別已讀明細</summary>
/// <param name="Account">已讀帳號</param>
/// <param name="ReadTime">已讀時間（ISO 8601）</param>
public record ReadDetailItem(
    string Account,
    string ReadTime);
