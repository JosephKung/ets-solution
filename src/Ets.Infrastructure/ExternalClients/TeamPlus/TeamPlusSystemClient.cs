// src/Ets.Infrastructure/ExternalClients/TeamPlus/TeamPlusSystemClient.cs
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
/// team+ System API 客戶端實作（system_sn + api_key 認證，form-urlencoded）
/// 使用已建立之 Polly ResiliencePipeline（ResiliencePipelineKeys.TeamPlus）
/// 規格書參照：§6.1 / §6.2 / §6.3 / §6.5 / §6.6
/// </summary>
public sealed class TeamPlusSystemClient : ITeamPlusSystemClient
{
    private readonly HttpClient _http;
    private readonly TeamPlusSystemOptions _options;
    private readonly ResiliencePipelineProvider<string> _polly;
    private readonly ILogger<TeamPlusSystemClient> _logger;

    /// <summary>team+ System API endpoint 固定路徑</summary>
    private const string SystemServicePath = "/API/SystemService.ashx";

    /// <summary>team+ TeamService endpoint（虛擬帳號貼文用，§6.6.4）</summary>
    private const string TeamServicePath = "/API/TeamService.ashx";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public TeamPlusSystemClient(
        HttpClient http,
        IOptions<TeamPlusSystemOptions> options,
        ResiliencePipelineProvider<string> polly,
        ILogger<TeamPlusSystemClient> logger)
    {
        _http    = http;
        _options = options.Value;
        _polly   = polly;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // §6.2 建立大團隊
    // ─────────────────────────────────────────────────────────────
    public async Task<CreateTeamResult> CreateTeamAsync(
        CreateTeamRequest request,
        CancellationToken ct = default)
    {
        var form = BuildBaseForm("createTeam");
        form.Add(KV("owner",                request.Owner));
        form.Add(KV("name",                 Truncate(request.Name, 50)));
        form.Add(KV("team_type",            "1"));
        form.Add(KV("subject",              Truncate(request.Subject, 50)));
        form.Add(KV("description",          Truncate(request.Description, 300)));
        form.Add(KV("icon",                 string.Empty));
        form.Add(KV("member_list",          SerializeList(request.MemberList)));
        form.Add(KV("manager_list",         SerializeList(request.ManagerList)));
        form.Add(KV("enable_external_user", "0"));

        var raw = await ExecuteWithPollyAsync(form, ct);
        var dto = Deserialize<CreateTeamResponseDto>(raw, "createTeam");

        _logger.LogInformation(
            "createTeam 完成：IsSuccess={IsSuccess}, TeamSN={TeamSN}, IgnoredMembers={IgnoredMembers}",
            dto.IsSuccess, dto.TeamSN, dto.IgnoredMemberList?.Count ?? 0);

        return new CreateTeamResult(
            IsSuccess:          dto.IsSuccess,
            Description:        dto.Description ?? string.Empty,
            ErrorCode:          dto.ErrorCode,
            TeamSN:             dto.TeamSN,
            IgnoredMemberList:  dto.IgnoredMemberList ?? [],
            IgnoredManagerList: dto.IgnoredManagerList ?? []);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.3 建立分組交談室
    // ─────────────────────────────────────────────────────────────
    public async Task<CreateChatResult> CreateChatAsync(
        CreateChatRequest request,
        CancellationToken ct = default)
    {
        var form = BuildBaseForm("createChat");
        form.Add(KV("creator_account",      request.CreatorAccount));
        form.Add(KV("chat_name",            Truncate(request.ChatName, 20)));
        form.Add(KV("chat_icon",            string.Empty));
        form.Add(KV("member_list",          SerializeList(request.MemberList)));
        form.Add(KV("manager_list",         SerializeList(request.ManagerList)));
        form.Add(KV("enable_external_user", "0"));

        var raw = await ExecuteWithPollyAsync(form, ct);
        var dto = Deserialize<CreateChatResponseDto>(raw, "createChat");

        _logger.LogInformation(
            "createChat 完成：IsSuccess={IsSuccess}, ChatSN={ChatSN}",
            dto.IsSuccess, dto.ChatSN);

        return new CreateChatResult(
            IsSuccess:          dto.IsSuccess,
            Description:        dto.Description ?? string.Empty,
            ErrorCode:          dto.ErrorCode,
            ChatSN:             dto.ChatSN,
            IgnoredMemberList:  dto.IgnoredMemberList ?? [],
            IgnoredManagerList: dto.IgnoredManagerList ?? []);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.5.1 邀請加入大團隊
    // ─────────────────────────────────────────────────────────────
    public async Task<TeamPlusBaseResult> InviteTeamMemberAsync(
        InviteTeamMemberRequest request,
        CancellationToken ct = default)
    {
        var form = BuildBaseForm("inviteTeamMember");
        form.Add(KV("team_sn",     request.TeamSN.ToString()));
        form.Add(KV("operator",    request.OperatorAccount));
        form.Add(KV("member_list", SerializeList(request.MemberList)));

        var raw = await ExecuteWithPollyAsync(form, ct);
        var dto = Deserialize<TeamPlusBaseResponseDto>(raw, "inviteTeamMember");

        _logger.LogInformation(
            "inviteTeamMember 完成：IsSuccess={IsSuccess}, TeamSN={TeamSN}",
            dto.IsSuccess, request.TeamSN);

        return ToBaseResult(dto);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.5.2 邀請加入分組交談室
    // ─────────────────────────────────────────────────────────────
    public async Task<TeamPlusBaseResult> InviteChatMemberAsync(
        InviteChatMemberRequest request,
        CancellationToken ct = default)
    {
        var form = BuildBaseForm("inviteChatMember");
        form.Add(KV("chat_sn",     request.ChatSN.ToString()));
        form.Add(KV("operator",    request.OperatorAccount));
        form.Add(KV("member_list", SerializeList(request.MemberList)));

        var raw = await ExecuteWithPollyAsync(form, ct);
        var dto = Deserialize<TeamPlusBaseResponseDto>(raw, "inviteChatMember");

        _logger.LogInformation(
            "inviteChatMember 完成：IsSuccess={IsSuccess}, ChatSN={ChatSN}",
            dto.IsSuccess, request.ChatSN);

        return ToBaseResult(dto);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.5.3 指派大團隊管理員
    // ─────────────────────────────────────────────────────────────
    public async Task<TeamPlusBaseResult> AssignTeamManagerAsync(
        AssignManagerRequest request,
        CancellationToken ct = default)
    {
        var form = BuildBaseForm("assignTeamManager");
        form.Add(KV("team_sn",      request.TeamOrChatSN.ToString()));
        form.Add(KV("operator",     request.OperatorAccount));
        form.Add(KV("manager_list", SerializeList(request.ManagerList)));

        var raw = await ExecuteWithPollyAsync(form, ct);
        var dto = Deserialize<TeamPlusBaseResponseDto>(raw, "assignTeamManager");

        _logger.LogInformation(
            "assignTeamManager 完成：IsSuccess={IsSuccess}, TeamSN={TeamSN}",
            dto.IsSuccess, request.TeamOrChatSN);

        return ToBaseResult(dto);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.5.3 指派交談室管理員
    // ─────────────────────────────────────────────────────────────
    public async Task<TeamPlusBaseResult> AssignChatManagerAsync(
        AssignManagerRequest request,
        CancellationToken ct = default)
    {
        var form = BuildBaseForm("assignChatManager");
        form.Add(KV("chat_sn",      request.TeamOrChatSN.ToString()));
        form.Add(KV("operator",     request.OperatorAccount));
        form.Add(KV("manager_list", SerializeList(request.ManagerList)));

        var raw = await ExecuteWithPollyAsync(form, ct);
        var dto = Deserialize<TeamPlusBaseResponseDto>(raw, "assignChatManager");

        _logger.LogInformation(
            "assignChatManager 完成：IsSuccess={IsSuccess}, ChatSN={ChatSN}",
            dto.IsSuccess, request.TeamOrChatSN);

        return ToBaseResult(dto);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.6.3 建立團隊虛擬帳號
    // ─────────────────────────────────────────────────────────────
    public async Task<CreateTeamApiAccountResult> CreateTeamApiAccountAsync(
        long teamSn,
        string ownerAccount,
        string accountName,
        CancellationToken ct = default)
    {
        var form = BuildBaseForm("createTeamAPIAccount");
        form.Add(KV("team_sn", teamSn.ToString()));
        form.Add(KV("owner",   ownerAccount));
        form.Add(KV("name",    accountName));
        form.Add(KV("icon",    string.Empty));

        var raw = await ExecuteWithPollyAsync(form, ct);
        var dto = Deserialize<CreateTeamApiAccountResponseDto>(raw, "createTeamAPIAccount");

        _logger.LogInformation(
            "createTeamAPIAccount 完成：IsSuccess={IsSuccess}, ApiAccount={ApiAccount}",
            dto.IsSuccess, dto.APIAccount?.APIAccount);

        return new CreateTeamApiAccountResult(
            IsSuccess:   dto.IsSuccess,
            Description: dto.Description ?? string.Empty,
            ErrorCode:   dto.ErrorCode,
            ApiAccount:  dto.APIAccount?.APIAccount ?? string.Empty,
            ApiKey:      dto.APIAccount?.APIKey     ?? string.Empty);
    }

    // ─────────────────────────────────────────────────────────────
    // §6.6.4 虛擬帳號發佈大團隊貼文（TeamService.ashx）
    // ─────────────────────────────────────────────────────────────
    public async Task<PostTeamMessageResult> PostTeamMessageAsync(
        string virtualAccount,
        string virtualApiKey,
        long teamSn,
        string textContent,
        string subject,
        CancellationToken ct = default)
    {
        // postMessage 走 TeamService.ashx，認證為虛擬帳號（非 system_sn）
        var teamServiceUrl = $"{_options.BaseUrl.TrimEnd('/')}{TeamServicePath}";

        var formData = new List<KeyValuePair<string, string>>
        {
            KV("ask",          "postMessage"),
            KV("account",      virtualAccount),
            KV("api_key",      virtualApiKey),
            KV("team_sn",      teamSn.ToString()),
            KV("content_type", "1"),
            KV("text_content", textContent),
            KV("subject",      subject)
        };

        var pipeline = _polly.GetPipeline(ResiliencePipelineKeys.TeamPlus);

        var raw = await pipeline.ExecuteAsync(async token =>
        {
            using var content  = new FormUrlEncodedContent(formData);
            using var response = await _http.PostAsync(teamServiceUrl, content, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token);
        }, ct);

        var dto = Deserialize<PostTeamMessageResponseDto>(raw, "postMessage");

        _logger.LogInformation(
            "postMessage 完成：IsSuccess={IsSuccess}, BatchId={BatchId}",
            dto.IsSuccess, dto.BatchId);

        return new PostTeamMessageResult(
            IsSuccess:   dto.IsSuccess,
            Description: dto.Description ?? string.Empty,
            ErrorCode:   dto.ErrorCode,
            BatchId:     dto.BatchId ?? string.Empty);
    }

    // ═════════════════════════════════════════════════════════════
    // 私有輔助方法
    // ═════════════════════════════════════════════════════════════

    private List<KeyValuePair<string, string>> BuildBaseForm(string ask) =>
    [
        KV("system_sn", _options.SystemSn),
        KV("api_key",   _options.ApiKey),
        KV("ask",       ask)
    ];

    private static KeyValuePair<string, string> KV(string key, string value) =>
        new(key, value);

    private async Task<string> ExecuteWithPollyAsync(
        List<KeyValuePair<string, string>> formData,
        CancellationToken ct)
    {
        var pipeline = _polly.GetPipeline(ResiliencePipelineKeys.TeamPlus);
        var url = $"{_options.BaseUrl.TrimEnd('/')}{SystemServicePath}";

        return await pipeline.ExecuteAsync(async token =>
        {
            using var content  = new FormUrlEncodedContent(formData);
            using var response = await _http.PostAsync(url, content, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token);
        }, ct);
    }

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

// ─────────────────────────────────────────────────────────────────────
// §9.2.2 批次解析 LoginName → UserNo（v3.2 新增）
// ─────────────────────────────────────────────────────────────────────
public async Task<Dictionary<string, int>> GetUserNosAsync(
    IReadOnlyList<string> loginNames,
    CancellationToken ct = default)
{
    var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var batchSize = 50;  // §9.2.2 單次最多 50 個

    foreach (var batch in loginNames.Chunk(batchSize))
    {
        var uidListJson = JsonSerializer.Serialize(batch);
        var form = new List<KeyValuePair<string, string>>
        {
            KV("ask",       "getUserInfoList"),
            KV("system_sn", _options.SystemSn),
            KV("api_key",   _options.ApiKey),
            KV("root_code", string.Empty),
            KV("uid_type",  "0"),
            KV("uid_list",  uidListJson)
        };

        var pipeline = _polly.GetPipeline(ResiliencePipelineKeys.TeamPlus);
        var url = $"{_options.BaseUrl.TrimEnd('/')}/API/SystemServiceExt01.ashx?ask=getUserInfoList";

        var raw = await pipeline.ExecuteAsync(async token =>
        {
            using var content  = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(url, content, token);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token);
        }, ct);

        var dto = Deserialize<GetUserInfoListResponseDto>(raw, "getUserInfoList");

        if (!dto.IsSuccess)
        {
            _logger.LogWarning("getUserInfoList 失敗：{Desc}", dto.Description);
            continue;  // Skip 模式（Halt 模式由 caller 判斷）
        }

        foreach (var u in dto.UserInfo ?? [])
            result[u.LoginName] = u.UserNo;

        // 找出未回傳的 LoginName，寫 Warning
        var notFound = batch.Except(result.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        if (notFound.Count > 0)
            _logger.LogWarning(
                "getUserInfoList 查無帳號（{Count} 人）：{List}",
                notFound.Count, string.Join(",", notFound));
    }

    return result;
}

//─── 私有 DTO（加入私有 DTO 區段）──────────────────────────────────────
private sealed record GetUserInfoListResponseDto(
    bool IsSuccess,
    string? Description,
    int ErrorCode,
    UserInfoDto[]? UserInfo);

private sealed record UserInfoDto(
    int UserNo,
    string LoginName,
    string DeptCode,
    string UserName,
    string Lang,
    int Status,
    string MVPN);

    private static string SerializeList(IReadOnlyList<string> list) =>
        JsonSerializer.Serialize(list);

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static TeamPlusBaseResult ToBaseResult(TeamPlusBaseResponseDto dto) =>
        new(dto.IsSuccess, dto.Description ?? string.Empty, dto.ErrorCode);

    // ─────────────────────────────────────────────────────────────
    // 私有 DTO
    // ─────────────────────────────────────────────────────────────

    private sealed record TeamPlusBaseResponseDto(
        bool IsSuccess,
        string? Description,
        int ErrorCode);

    private sealed record CreateTeamResponseDto(
        bool IsSuccess,
        string? Description,
        int ErrorCode,
        long TeamSN,
        List<string>? IgnoredMemberList,
        List<string>? IgnoredManagerList);

    private sealed record CreateChatResponseDto(
        bool IsSuccess,
        string? Description,
        int ErrorCode,
        long ChatSN,
        List<string>? IgnoredMemberList,
        List<string>? IgnoredManagerList);

    private sealed record CreateTeamApiAccountResponseDto(
        bool IsSuccess,
        string? Description,
        int ErrorCode,
        ApiAccountDto? APIAccount);

    private sealed record ApiAccountDto(
        string APIAccount,
        string APIKey,
        string? Name,
        bool IsEnable);

    private sealed record PostTeamMessageResponseDto(
        bool IsSuccess,
        string? Description,
        int ErrorCode,
        string? BatchId);
}
