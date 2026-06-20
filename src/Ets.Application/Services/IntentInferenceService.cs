using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ets.Application.Services;

/// <summary>
/// Flex Message 按鈕語意（§5.2.1）。
/// </summary>
public enum ButtonIntent
{
    /// <summary>
    /// 會抵達 — 後續自動 inviteTeamMember + inviteChatMember。
    /// 範例按鈕：15 分鐘內、30 分鐘內、已在現場等。
    /// </summary>
    WillArrive,

    /// <summary>
    /// 無法抵達 — 僅記錄回覆，不加入交談室。
    /// 目前唯一觸發值：「無法返回院區」。
    /// </summary>
    CannotArrive
}

/// <summary>
/// 單一按鈕的語意對應記錄。
/// </summary>
public sealed record ButtonIntentEntry(
    [property: JsonPropertyName("text")]   string Text,
    [property: JsonPropertyName("intent")] string Intent  // "WillArrive" | "CannotArrive"
);

/// <summary>
/// Flex Message 按鈕語意自動推導服務（§5.2.1）。
///
/// 設計原則：
///   - ETS 端固定規則推導，不依賴 HIS 傳入 mapping
///   - 唯一 CannotArrive 觸發值：「無法返回院區」（精確比對，去頭尾空白）
///   - 新增 CannotArrive 類按鈕只需擴充本類別，HIS 端介接規格不變
///   - 推導結果序列化為 JSON 存入 EmergencyEvents.FlexMsgIntentMapJson
/// </summary>
public static class IntentInferenceService
{
    /// <summary>固定觸發 CannotArrive 的按鈕文字集合</summary>
    private static readonly HashSet<string> CannotArriveTexts = new()
    {
        "無法返回院區"
        // 未來擴充：例如 "請假中", "外派中" 等，直接加入此集合即可
    };

    /// <summary>
    /// 推導單一按鈕的語意。
    /// </summary>
    public static ButtonIntent InferIntent(string buttonText)
        => CannotArriveTexts.Contains(buttonText.Trim())
            ? ButtonIntent.CannotArrive
            : ButtonIntent.WillArrive;

    /// <summary>
    /// 將 Flex Message 按鈕 JSON 陣列字串解析並推導 intent map。
    /// </summary>
    /// <param name="flexMsgItemsJson">如 "[\"15 分鐘內\",\"30 分鐘內\",\"無法返回院區\"]"</param>
    /// <returns>按鈕與語意對應清單；解析失敗回傳空清單</returns>
    public static List<ButtonIntentEntry> BuildIntentMap(string flexMsgItemsJson)
    {
        if (string.IsNullOrWhiteSpace(flexMsgItemsJson))
            return new();

        try
        {
            var buttons = JsonSerializer.Deserialize<List<string>>(flexMsgItemsJson)
                       ?? new();

            return buttons
                .Select(text => new ButtonIntentEntry(
                    text,
                    InferIntent(text).ToString()))
                .ToList();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// 將 intent map 序列化為 JSON 字串，存入 DB。
    /// </summary>
    public static string SerializeIntentMap(List<ButtonIntentEntry> intentMap)
        => JsonSerializer.Serialize(intentMap);

    /// <summary>
    /// 從 DB 儲存的 FlexMsgIntentMapJson 查詢指定按鈕文字的 intent。
    /// 用於 §7 Webhook 處理時快速查詢，不需重新計算。
    /// </summary>
    public static ButtonIntent? LookupIntent(string? intentMapJson, string buttonText)
    {
        if (string.IsNullOrWhiteSpace(intentMapJson))
            return null;

        try
        {
            var entries = JsonSerializer.Deserialize<List<ButtonIntentEntry>>(intentMapJson)
                       ?? new();

            var match = entries.FirstOrDefault(e =>
                string.Equals(e.Text, buttonText.Trim(), StringComparison.Ordinal));

            return match is null
                ? null
                : Enum.Parse<ButtonIntent>(match.Intent);
        }
        catch
        {
            return null;
        }
    }
}
