// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusSsoClient.cs
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
/// team+ SSO 客戶端實作（system_sn + api_key 認證，form-urlencoded）
/// 使用 Polly ResiliencePipeline（ResiliencePipelineKeys.TeamPlus）
/// 規格書參照：§10.1 / §10.1.4 / §10.1.5
/// </summary>
public sealed class TeamPlusSsoClient : ITeamPlusSsoClient
{
    private readonly HttpClient _http;
    private readonly TeamPlusSsoOptions _options;
    private readonly ResiliencePipelineProvider<string> _polly;
    private readonly ILogger<TeamPlusSsoClient> _logger;

    /// <summary>SSO endpoint（§10.1 規格，固定 query string ask=getUserAccount）</summary>
    private const string SsoPath = "/API/SystemSSOService.ashx?ask=getUserAccount";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TeamPlusSsoClient(
        HttpClient http,
        IOptions<TeamPlusSsoOptions> options,
        ResiliencePipelineProvider<string> polly,
        ILogger<TeamPlusSsoClient> logger)
    {
        _http    = http;
        _options = options.Value;

        _polly   = polly;
        _logger  = logger;
    }

    public async Task<SsoUserAccountResult> GetUserAccountAsync(
        string sessionKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionKey))
            throw new ArgumentException("session_key 不可為空", nameof(sessionKey));

        var pipeline = _polly.GetPipeline(ResiliencePipelineKeys.TeamPlus);
        var url      = $"{_options.BaseUrl.TrimEnd('/')}{SsoPath}";

        // form-urlencoded（§10.1.4：system_sn + api_key + session_key）
        var formData = new[]
        {
            new KeyValuePair<string, string>("system_sn",   _options.SystemSn),
            new KeyValuePair<string, string>("api_key",     _options.ApiKey),
            new KeyValuePair<string, string>("session_key", sessionKey)
        };

        var raw = await pipeline.ExecuteAsync(async token =>
        {
            using var content  = new FormUrlEncodedContent(formData);
            using var response = await _http.PostAsync(url, content, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token);
        }, ct);

        SsoResponseDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<SsoResponseDto>(raw, JsonOptions)
                  ?? throw new InvalidOperationException("getUserAccount 回傳空物件");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "getUserAccount 反序列化失敗，原始回應：{Raw}", raw);
            throw;
        }

        // 依 §10.1.5 錯誤碼記錄對應 log level
        if (!dto.IsSuccess)
        {
            var logLevel = dto.ErrorCode switch
            {
                -3  => Microsoft.Extensions.Logging.LogLevel.Warning,  // 權限不足（設定問題）
                -4  => Microsoft.Extensions.Logging.LogLevel.Error,    // 功能未開放（環境問題）
                _   => Microsoft.Extensions.Logging.LogLevel.Information
            };
            _logger.Log(logLevel,
                "SSO 驗證失敗：ErrorCode={ErrorCode}, Description={Description}",
                dto.ErrorCode, dto.Description);
        }
        else
        {
            _logger.LogInformation(
                "SSO 驗證成功：UserAccount={UserAccount}", dto.UserAccount);
        }

        return new SsoUserAccountResult(
            IsSuccess:   dto.IsSuccess,
            Description: dto.Description ?? string.Empty,
            ErrorCode:   dto.ErrorCode,
            UserAccount: dto.UserAccount ?? string.Empty);
    }

    // ─── 私有 DTO ─────────────────────────────────────────────────
    private sealed record SsoResponseDto(
        bool IsSuccess,
        string? Description,
        int ErrorCode,
        string? UserAccount);
}
