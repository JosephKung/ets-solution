using System.ComponentModel.DataAnnotations;

namespace Ets.Application.Dtos.CheckIn;

/// <summary>
/// QR Code 掃碼報到請求 DTO（對應 POST /api/v1/checkin）。
/// 由 team+ App 攔截 Universal Link 後組裝並送出。
/// </summary>
public class CheckInRequest
{
    /// <summary>事件識別碼（如 E20240101120000A001）</summary>
    [Required]
    public string EventId { get; set; } = string.Empty;

    /// <summary>team+ 帳號（由 App 本地端取得已登入帳號）</summary>
    [Required]
    public string Account { get; set; } = string.Empty;

    /// <summary>QR Code 中之 64 字元隨機 Nonce</summary>
    [Required]
    public string Nonce { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 短簽章（前 8 字元）</summary>
    [Required]
    public string Sig { get; set; } = string.Empty;
}
