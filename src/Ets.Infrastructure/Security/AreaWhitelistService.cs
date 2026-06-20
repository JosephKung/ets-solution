using Ets.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ets.Infrastructure.Security;

/// <summary>
/// event_area 白名單服務。
/// 實作 IHostedService：程式啟動時從加密檔解密載入白名單，全程駐留記憶體。
/// 實作 IAreaWhitelistService：供 Controller 注入呼叫 IsAllowed()。
///
/// 對應規格書 §5.3.2 載入與快取策略：
///   - 金鑰來源：環境變數 ETS_AREA_WHITELIST_KEY（Base64，32 byte）
///   - 檔案不存在 → 警告 log + 不限制模式（向下相容）
///   - 解密失敗   → 拋出例外，ETS 服務拒絕啟動（fail-fast）
///   - 空白名單   → 不限制模式（任意 event_area 均通過）
/// </summary>
public sealed class AreaWhitelistService : IAreaWhitelistService, IHostedService
{
    private const string EnvKeyName = "ETS_AREA_WHITELIST_KEY";

    private readonly IOptions<AreaWhitelistOptions> _options;
    private readonly ILogger<AreaWhitelistService> _logger;

    // 以 volatile 確保多執行緒讀取一致性（白名單只在啟動時寫一次，之後唯讀）
    private volatile HashSet<string> _whitelist = new();
    private volatile bool _unrestricted = true;

    public AreaWhitelistService(
        IOptions<AreaWhitelistOptions> options,
        ILogger<AreaWhitelistService> logger)
    {
        _options = options;
        _logger  = logger;
    }

    // ── IAreaWhitelistService ─────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsAllowed(string? eventArea)
    {
        if (_unrestricted)
            return true;

        if (string.IsNullOrWhiteSpace(eventArea))
            return false;

        return _whitelist.Contains(eventArea);
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> CurrentWhitelist => _whitelist.ToList();

    /// <inheritdoc/>
    public bool IsUnrestricted => _unrestricted;

    // ── IHostedService ────────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var path = _options.Value.EncFilePath;

        // ── 情況 1：路徑未設定或檔案不存在 → 不限制模式 ─────────────────────
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning(
                "Security:AreaWhitelist:EncFilePath 未設定。" +
                "目前為不限制模式，所有 event_area 均可接受。" +
                "Production 環境請務必設定白名單檔案路徑。");
            _unrestricted = true;
            return Task.CompletedTask;
        }

        if (!File.Exists(path))
        {
            _logger.LogWarning(
                "Area whitelist 檔案不存在：{Path}。" +
                "目前為不限制模式。Production 環境請確認路徑正確。", path);
            _unrestricted = true;
            return Task.CompletedTask;
        }

        // ── 情況 2：讀取並解密 ────────────────────────────────────────────────
        var keyBase64 = Environment.GetEnvironmentVariable(EnvKeyName);
        if (string.IsNullOrWhiteSpace(keyBase64))
        {
            throw new InvalidOperationException(
                $"環境變數 {EnvKeyName} 未設定。" +
                "此環境變數為解密 area whitelist 的必要金鑰，ETS 服務無法啟動。");
        }

        byte[] key;
        try
        {
            key = AesGcmHelper.ParseKeyFromBase64(keyBase64);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"環境變數 {EnvKeyName} 格式錯誤：{ex.Message}", ex);
        }

        byte[] encryptedBytes;
        try
        {
            encryptedBytes = File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"無法讀取 area whitelist 檔案：{path}", ex);
        }

        string json;
        try
        {
            json = AesGcmHelper.Decrypt(encryptedBytes, key);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"Area whitelist 解密失敗（金鑰不符或檔案被篡改）：{path}", ex);
        }

        AreaWhitelistFileDto dto;
        try
        {
            dto = JsonSerializer.Deserialize<AreaWhitelistFileDto>(json, JsonOptions)
               ?? throw new InvalidOperationException("whitelist JSON 解析結果為 null");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Area whitelist JSON 格式錯誤：{ex.Message}", ex);
        }

        if (dto.EventAreaList is null)
        {
            throw new InvalidOperationException(
                "Area whitelist 缺少必要欄位 'event_areaList'");
        }

        // ── 情況 3a：空白名單 → 不限制模式 ──────────────────────────────────
        if (dto.EventAreaList.Count == 0)
        {
            _unrestricted = true;
            _logger.LogInformation(
                "Area whitelist 載入完成（不限制模式）。" +
                "Version={Version}, GeneratedBy={By}, GeneratedAt={At}",
                dto.Version, dto.GeneratedBy, dto.GeneratedAt);
        }
        else
        {
            // ── 情況 3b：有白名單 → 限制模式 ──────────────────────────────────
            _unrestricted = false;
            _whitelist    = new HashSet<string>(dto.EventAreaList, StringComparer.Ordinal);

            _logger.LogInformation(
                "Area whitelist 載入完成（限制模式）。" +
                "Count={Count}, Version={Version}, GeneratedBy={By}, GeneratedAt={At}",
                _whitelist.Count, dto.Version, dto.GeneratedBy, dto.GeneratedAt);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                foreach (var area in _whitelist)
                    _logger.LogDebug("  Whitelist entry: {Area}", area);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;

    // ── 私有 DTO ──────────────────────────────────────────────────────────────

    private sealed record AreaWhitelistFileDto(
        [property: JsonPropertyName("event_areaList")] List<string>? EventAreaList,
        [property: JsonPropertyName("generated_at")]   string? GeneratedAt,
        [property: JsonPropertyName("generated_by")]   string? GeneratedBy,
        [property: JsonPropertyName("version")]        int Version);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
