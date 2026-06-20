// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusChannelClient.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ets.Application.Dtos.TeamPlus;
using Ets.Application.Interfaces.External;
using Ets.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;

namespace Ets.Infrastructure.ExternalClients.TeamPlus;

/// <summary>
/// team+ Channel API 客戶端實作（Channel Access Token Bearer 認證，JSON Body）
/// 使用 Polly ResiliencePipeline（ResiliencePipelineKeys.TeamPlus）
/// 規格書參照：§6.1.3 / §6.4 / §6.8 / §6.9
/// </summary>
public sealed class TeamPlusChannelClient : ITeamPlusChannelClient
{
    private readonly HttpClient _http;
    private readonly TeamPlusChannelsOptions _channelsOptions;
    private readonly TeamPlusSystemOptions _systemOptions;
    private readonly ResiliencePipelineProvider<string> _polly;
    private readonly ILogger<TeamPlusChannelClient> _logger;

    /// <summary>Channel API 固定 endpoint（§6.4 / §6.8 / §6.9 均使用此路徑）</summary>
    private const string MessageFeedPath = "/API/MessageFeedService.ashx";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        PropertyNamingPolicy = null,   // 保留原始大小寫（team+ 欄位為 PascalCase）
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TeamPlusChannelClient(
        HttpClient http,
        IOptions<TeamPlusChannelsOptions> channelsOptions,
        IOptions<TeamPlusSystemOptions> systemOptions,
        ResiliencePipelineProvider<string> polly,
        ILogger<TeamPlusChannelClient> logger)
    {
        _http            = http;
        _channelsOptions = channelsOptions.Value;
        _systemOptions   = systemOptions.Value;
        _polly           = polly;
        _logger          = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // §6.4 廣播 Flex Message
    // ─────────────────────────────────────────────────────────────
    public async Task<BroadcastFlexMessageResult> BroadcastFlexMessageAsync(
        BroadcastFlexMessageRequest request,
        CancellationToken ct = default)
    {
        var accessToken = GetAccessToken(request.EventType);

        // 組裝 JSON Body（§6.4 規格）
        var body = new
        {
            ask           = "broadcastMessageByLoginNameList",
            recipientList = request.RecipientList,
            message       = new
            {
                type     = "flex",
                contents = request.FlexContents
            }
        };

        var raw = await ExecuteWithPollyAsync(accessToken, body, ct);
        var dto = Deserialize<BroadcastResponseDto>(raw, "broadcastMessageByLoginNameList");

        _logger.LogInformation(
            "broadcastMessageByLoginNameList 完成：EventType={EventType}, MessageSN={MessageSN}, Recipients={Count}",
            request.EventType, dto.MessageSN, request.RecipientList.Count);

        return new BroadcastFlexMessageResult(
            IsSuccess: dto.MessageSN > 0,
            MessageSN: dto.MessageSN);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.8 查詢訊息已讀狀態
    // ─────────────────────────────────────────────────────────────
    public async Task<GetMsgReadStatusResult> GetMsgReadStatusAsync(
        GetMsgReadStatusRequest request,
        CancellationToken ct = default)
    {
        var accessToken = GetAccessToken(request.EventType);

        var body = new
        {
            ask       = "getMsgReadStatus",
            messageSN = request.MessageSN
        };

        var raw = await ExecuteWithPollyAsync(accessToken, body, ct);
        var dto = Deserialize<ReadStatusResponseDto>(raw, "getMsgReadStatus");

        _logger.LogDebug(
            "getMsgReadStatus 完成：MessageSN={MessageSN}, ReadCount={ReadCount}",
            request.MessageSN, dto.ReadCount);

        return new GetMsgReadStatusResult(
            ReadCount: dto.ReadCount,
            ReadDetailList: dto.ReadDetailList?
                .Select(d => new ReadDetailItem(d.Account, d.ReadTime))
                .ToList()
                ?? []);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.9 動態更新 Flex Footer
    // ─────────────────────────────────────────────────────────────
    public async Task<TeamPlusBaseResult> UpdateFlexFooterAsync(
        UpdateFlexFooterRequest request,
        CancellationToken ct = default)
    {
        var accessToken = GetAccessToken(request.EventType);

        // 依規格書 §6.9：fontColor 紅色 #E53935（WillArrive）或灰色 #888888（CannotArrive）
        var body = new
        {
            ask        = "updateFlexMessageFooter",
            messageSN  = request.MessageSN,
            recipient  = request.Recipient,
            flexFooter = new
            {
                type      = "text",
                text      = request.FooterText,
                fontColor = request.FontColor,
                align     = "center",
                fontSize  = 14
            }
        };

        var raw = await ExecuteWithPollyAsync(accessToken, body, ct);
        var dto = Deserialize<BaseResponseDto>(raw, "updateFlexMessageFooter");

        _logger.LogInformation(
            "updateFlexMessageFooter 完成：MessageSN={MessageSN}, Recipient={Recipient}, IsSuccess={IsSuccess}",
            request.MessageSN, request.Recipient, dto.IsSuccess);

        return new TeamPlusBaseResult(
            IsSuccess:   dto.IsSuccess,
            Description: dto.Description ?? string.Empty,
            ErrorCode:   dto.ErrorCode);
    }

    // ═════════════════════════════════════════════════════════════
    // 私有輔助方法
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// 依 event_type 取得對應服務頻道之 AccessToken（§6.1.4 多頻道設定）
    /// </summary>
    private string GetAccessToken(string eventType)
    {
        if (!_channelsOptions.Channels.TryGetValue(eventType, out var channel))
            throw new InvalidOperationException(
                $"找不到 event_type='{eventType}' 之服務頻道設定，請檢查 appsettings.json TeamPlusChannels");

        if (string.IsNullOrWhiteSpace(channel.AccessToken))
            throw new InvalidOperationException(
                $"event_type='{eventType}' 之 AccessToken 未設定");

        return channel.AccessToken;
    }

    /// <summary>
    /// 透過 Polly Pipeline 執行 Channel API HTTP POST（JSON Body + Bearer Token）
    /// </summary>
    private async Task<string> ExecuteWithPollyAsync(
        string accessToken,
        object body,
        CancellationToken ct)
    {
        var pipeline = _polly.GetPipeline(ResiliencePipelineKeys.TeamPlus);
        var url = $"{_systemOptions.BaseUrl.TrimEnd('/')}{MessageFeedPath}";
        var json = JsonSerializer.Serialize(body, JsonWriteOptions);

        return await pipeline.ExecuteAsync(async token =>
        {
            using var content  = new StringContent(json, Encoding.UTF8, "application/json");
            using var request  = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

            // Channel API 使用 Bearer Token（§6.1.3）
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await _http.SendAsync(request, token);

            // 若 401（AccessToken 失效），記錄警告供運維人員追查（§12.4 建議）
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(
                    "team+ Channel API 回傳 401 Unauthorized，" +
                    "AccessToken 可能已失效，請至 team+ 服務頻道更新並同步 appsettings");
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token);
        }, ct);
    }

    /// <summary>反序列化 team+ 回應，失敗時記錄 raw JSON 並拋出</summary>
    private T Deserialize<T>(string raw, string operation)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOptions)
                ?? throw new InvalidOperationException($"{operation} 回傳空物件");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "{Operation} 反序列化失敗，原始回應：{Raw}", operation, raw);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // 私有 DTO（僅用於反序列化 team+ 回應，不暴露至 Application 層）
    // ─────────────────────────────────────────────────────────────

    private sealed record BroadcastResponseDto(
        int MessageSN);

    private sealed record ReadStatusResponseDto(
        int ReadCount,
        List<ReadDetailDto>? ReadDetailList);

    private sealed record ReadDetailDto(
        string Account,
        string ReadTime);

    private sealed record BaseResponseDto(
        bool IsSuccess,
        string? Description,
        int ErrorCode);
}
