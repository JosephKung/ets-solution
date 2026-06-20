// src/Ets.Infrastructure/Security/TeamPlusSignatureVerifier.cs
using System.Security.Cryptography;
using System.Text;
using Ets.Application.Interfaces;
using Ets.Infrastructure.ExternalClients.TeamPlus;
using Microsoft.Extensions.Options;

namespace Ets.Infrastructure.Security;

/// <summary>
/// team+ Webhook HMAC 簽章驗證實作（§7.1）
/// 依官方規格：HMAC-SHA256(ChannelSecret, body) → Base64 → 與 X-TeamPlus-Signature 比對
/// 使用 CryptographicOperations.FixedTimeEquals 防止 timing attack
/// </summary>
public sealed class TeamPlusSignatureVerifier : ITeamPlusSignatureVerifier
{
    private readonly TeamPlusChannelsOptions _options;

    public TeamPlusSignatureVerifier(IOptions<TeamPlusChannelsOptions> options)
    {
        _options = options.Value;
    }

    public bool Verify(string eventType, string requestBody, string signatureHeader)
    {
        if (!_options.Channels.TryGetValue(eventType, out var channel))
            return false;

        byte[] secret     = Encoding.UTF8.GetBytes(channel.ChannelSecret);
        byte[] bodyBytes  = Encoding.UTF8.GetBytes(requestBody);
        byte[] hash       = new HMACSHA256(secret).ComputeHash(bodyBytes);
        string computed   = Convert.ToBase64String(hash);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed),
            Encoding.UTF8.GetBytes(signatureHeader));
    }
}
