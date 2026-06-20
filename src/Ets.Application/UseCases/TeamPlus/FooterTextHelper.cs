// src/Ets.Application/UseCases/TeamPlus/FooterTextHelper.cs
namespace Ets.Application.UseCases.TeamPlus;

/// <summary>
/// Flex Footer 文字與顏色決策輔助類別（§6.9 規格表）
/// 由 Postback Webhook Handler 呼叫，決定寫入 Outbox 的 FooterText / FontColor
/// </summary>
public static class FooterTextHelper
{
    // §6.9 顏色常數
    public const string ColorRed  = "#E53935";
    public const string ColorGray = "#888888";

    /// <summary>
    /// 依 ReplyStatus 決定 Footer 文字與顏色
    /// </summary>
    /// <param name="replyStatus">EventResponders.ReplyStatus 值</param>
    public static (string FooterText, string FontColor) GetFooter(string replyStatus) =>
        replyStatus switch
        {
            "VoiceConfirmed"   => ("語音已送達!請開 team+ 回覆", ColorGray),
            "VoiceUnreachable" => ("語音通報未接通",              ColorGray),
            "無法返回院區"     => ("已送出！您已表示無法返回。",  ColorGray),
            _                  => ("已送出！",                    ColorRed)
            // 其他 WillArrive 類按鈕文字一律顯示「已送出！」紅字
        };
}
