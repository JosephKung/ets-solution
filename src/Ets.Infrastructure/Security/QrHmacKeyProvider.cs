using Ets.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Ets.Infrastructure.Security;

/// <summary>
/// QR HMAC 金鑰提供者（從設定取得）。
/// 金鑰來源：appsettings.json > QrToken:HmacKey（Production 由 DPAPI 加密保護）。
/// </summary>
public class QrHmacKeyProvider : IQrHmacKeyProvider
{
    private readonly string _key;

    public QrHmacKeyProvider(IConfiguration configuration)
    {
        _key = configuration["QrToken:HmacKey"]
               ?? throw new InvalidOperationException("QrToken:HmacKey 設定不可為空");
    }

    /// <inheritdoc />
    public string GetKey() => _key;
}
