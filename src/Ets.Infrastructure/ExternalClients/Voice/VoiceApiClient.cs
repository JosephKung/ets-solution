// src/Ets.Infrastructure/ExternalClients/Voice/VoiceApiClient.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ets.Application.Dtos.Voice;
using Ets.Application.Interfaces;
using Ets.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.Registry;


namespace Ets.Infrastructure.ExternalClients.Voice;

/// <summary>
/// Voice API 外撥客戶端實作（§9.3）
/// 認證：Authorization: Bearer {VoiceApiToken}
/// 傳輸格式：application/json
/// </summary>
public sealed class VoiceApiClient : IVoiceApiClient
{
    private readonly HttpClient _http;
    private readonly VoiceNotifyOptions _options;
    private readonly ResiliencePipelineProvider<string> _polly;
    private readonly ILogger<VoiceApiClient> _logger;
    private readonly IVoiceQueueMetrics _metrics;

    private const string VoiceCallPath = "/api/v1/voice-call";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    public VoiceApiClient(
        HttpClient http,
        IOptions<VoiceNotifyOptions> options,
        ResiliencePipelineProvider<string> polly,
		IVoiceQueueMetrics metrics,         
        ILogger<VoiceApiClient> logger)
    {
        _http    = http;
        _options = options.Value;
        _polly   = polly;
        _logger  = logger;
		_metrics = metrics;
    }

    public async Task<VoiceCallResult> CallAsync(VoiceCallRequest request, CancellationToken ct = default)
    {
        // 組裝 JSON Body（§9.3 Request 欄位）
        var body = new
        {
            event_ID       = request.EventId,
            callee_account = request.CalleeAccount,  // team+ UserNo 字串（§9.2.2 v3.2）
            AudioContent   = request.AudioContent,
            callback_url   = request.CallbackUrl,
            RetryCount     = request.RetryCount
        };

        var json     = JsonSerializer.Serialize(body);
        var pipeline = _polly.GetPipeline(ResiliencePipelineKeys.VoiceApi);
        var url      = $"{_options.VoiceApiBaseUrl.TrimEnd('/')}{VoiceCallPath}";

        var raw = await pipeline.ExecuteAsync(async token =>
        {
            using var content  = new StringContent(json, Encoding.UTF8, "application/json");
            using var req      = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.VoiceApiToken);

            using var resp = await _http.SendAsync(req, token);

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(token);
                _logger.LogError(
                    "Voice API 外撥失敗：StatusCode={StatusCode}, Body={Body}",
                    resp.StatusCode, err);
                resp.EnsureSuccessStatusCode();  // 拋出，由 Polly retry 處理
            }

            return await resp.Content.ReadAsStringAsync(token);
        }, ct);

        VoiceCallResponseDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<VoiceCallResponseDto>(raw, JsonOptions)
                  ?? throw new InvalidOperationException("Voice API 回傳空物件");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Voice API 回應反序列化失敗：{Raw}", raw);
            throw;
        }

        _logger.LogInformation(
            "Voice API 外撥成功：ExternalCallId={ExternalCallId}, Status={Status}, " +
            "Queue={InUse}/{Max}（等待={Waiting}）",
            dto.ExternalCallId, dto.Status,
            dto.Queue?.InUse ?? 0, dto.Queue?.Max ?? 0, dto.Queue?.Waiting ?? 0);
		// 1.4.3：佇列指標
		_metrics.RecordQueueStatus(
			dto.Queue?.InUse    ?? 0,
			dto.Queue?.Max       ?? 0,
			dto.Queue?.Waiting   ?? 0);
        // 佇列壅塞警告（§9.3 佇列觀察建議）
        if (dto.Queue is { Waiting: > 0 } ||
            (dto.Queue is not null && dto.Queue.Max > 0 &&
             dto.Queue.InUse >= dto.Queue.Max * 0.8))
        {
            _logger.LogWarning(
                "Voice API 佇列接近壅塞：InUse={InUse}, Max={Max}, Waiting={Waiting}",
                dto.Queue!.InUse, dto.Queue.Max, dto.Queue.Waiting);
        }

        return new VoiceCallResult(
            IsSuccess:      true,
            ExternalCallId: dto.ExternalCallId ?? string.Empty,
            Status:         dto.Status         ?? string.Empty,
            QueueInUse:     dto.Queue?.InUse    ?? 0,
            QueueMax:       dto.Queue?.Max       ?? 0,
            QueueWaiting:   dto.Queue?.Waiting   ?? 0);
    }

    // ─── 私有 DTO ──────────────────────────────────────────────────
    private sealed record VoiceCallResponseDto(
        [property: JsonPropertyName("external_call_id")] string? ExternalCallId,
        [property: JsonPropertyName("status")]           string? Status,
        [property: JsonPropertyName("queue")]            QueueDto? Queue,
        [property: JsonPropertyName("created_at")]       string? CreatedAt);

    private sealed record QueueDto(
        [property: JsonPropertyName("in_use")]  int InUse,
        [property: JsonPropertyName("max")]     int Max,
        [property: JsonPropertyName("waiting")] int Waiting);
}
