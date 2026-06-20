// src/Ets.Infrastructure/Outbox/Handlers/FlexMessageBuilder.cs
using System.Linq;
using System.Text.Json;
using Ets.Application.Interfaces;
using Ets.Domain.Entities;

namespace Ets.Infrastructure.Outbox.Handlers;

/// <summary>
/// Flex Message contents 組裝器實作（§6.4）
/// 依照規格書 Figma 圖 e0001 組裝 body + footer
///
/// Body 區塊（固定順序）：
///   ① 標題：⚠️ 醫院緊急應變通報
///   ② 分隔線
///   ③ 時間
///   ④ 事件摘要
///   ⑤ 任務概述（選填，空值時略過）
///   ⑥ 指揮官
///
/// Footer（有按鈕版）：
///   PostbackButton × N（依 FlexMsgItemsJson 動態產生）
///   「無法」類使用 style=secondary，其餘 style=primary
/// </summary>
public sealed class FlexMessageBuilder : IFlexMessageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public object BuildContentsWithButtons(EmergencyEvent ev)
    {
        var buttons = ParseButtons(ev);
        return new
        {
            body = BuildBody(ev),
            footer = new[]
            {
                new
                {
                    type     = "footercontainer",
                    contents = buttons.Select(item => new
                    {
                        type        = "postbackbutton",
                        text        = item,
                        style       = IsCannotArrive(item) ? "secondary" : "primary",
                        displayText = item,
                        data        = $"id={ev.EventId}&feedback={Uri.EscapeDataString(item)}"
                    }).Cast<object>().ToArray()
                }
            }
        };
    }

    public object BuildContentsWithoutButtons(EmergencyEvent ev)
    {
        // observer 版本：只有 body，無 footer（§6.4.2）
        return new
        {
            body = BuildBody(ev, isObserver: true)
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 私有輔助方法
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 組裝 body containers（§6.4 規格 + Figma e0001）
    /// </summary>
    private static object[] BuildBody(EmergencyEvent ev, bool isObserver = false)
    {
        var title = isObserver
            ? "⚠️ 醫院緊急應變通報（僅通知）"
            : "⚠️ 醫院緊急應變通報";

        var containers = new List<object>
        {
            // ① 標題
            new
            {
                type     = "bodycontainer",
                contents = new[]
                {
                    new { type = "text", text = title,
                          align = "center", fontWeight = 600, fontSize = 16 }
                }
            },
            // ② 分隔線
            new { type = "separator", height = 1 },
            // ③ 時間
            BuildRow("時間：", ev.EventTime.ToString("yyyy-MM-dd HH:mm:ss")),
            // ④ 事件摘要
            BuildRow("事件摘要：", ev.EventSummary)
        };

        // ⑤ 任務概述（選填）
        if (!string.IsNullOrWhiteSpace(ev.EventDescription))
            containers.Add(BuildRow("任務概述：", ev.EventDescription));

        // ⑥ 指揮官（從 Responders 中取 role=commander 者，若無則略過）
        // 注意：此處簡化為從 FlexMsgIntentMapJson 取，
        // 完整版本由呼叫方傳入指揮官名稱
        // 目前保留欄位但不渲染，以免 null 問題，後續可由 Handler 擴充傳入
        return containers.ToArray();
    }

    /// <summary>組裝標籤 + 值的 bodycontainer row</summary>
    private static object BuildRow(string label, string value) =>
        new
        {
            type = "bodycontainer",
            contents = new object[]
            {
                new { type = "text", text = label, align = "right", fontSize = 12 },
                new { type = "text", text = value, flex  = 3,       fontSize = 12 }
            }
        };

    /// <summary>從 FlexMsgItemsJson 解析按鈕清單</summary>
    private static List<string> ParseButtons(EmergencyEvent ev)
    {
        if (string.IsNullOrWhiteSpace(ev.FlexMsgItemsJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<string>>(ev.FlexMsgItemsJson, JsonOptions)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 判斷按鈕是否為「無法/取消」類（使用 secondary 樣式）
    /// 依 §7.2.1 InferIntent 規則：固定「無法返回院區」為 CannotArrive
    /// </summary>
    private static bool IsCannotArrive(string buttonText) =>
        buttonText.Trim() == "無法返回院區";
}
